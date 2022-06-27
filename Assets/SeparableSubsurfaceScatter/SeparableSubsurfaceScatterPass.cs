using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{

    public class SeparableSubsurfaceScatterPass : ScriptableRenderPass
    {
        SeparableSubsurfaceScatter m_SSSS = null;

        static readonly string k_RenderTag = "Separable SubsurfaceScatter";

        Material ssssMaterial = null;
        RenderTargetIdentifier currentTarget;
        ScriptableRenderer currentRenderer;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int TempTargetId = Shader.PropertyToID("Destination");

        const int nSamples = 11;
        private Vector4[] kernel = new Vector4[nSamples];

        private Vector3 Gaussian(float variance, float r, Vector3 falloff)
        {
		    /**
		     * We use a falloff to modulate the shape of the profile. Big falloffs
		     * spreads the shape making it wider, while small falloffs make it
		     * narrower.
		     */
            Vector3 g = Vector3.zero;

            for (int i = 0; i < 3; i++) {
                float rr = r / (0.001f + falloff[i]);
                g[i] = Mathf.Exp((-(rr * rr)) / (2.0f * variance)) / (2.0f * 3.14f * variance);
            }

            return g;
        }

        private Vector3 Profile(float r, Vector3 falloff)
        {
	    /**
	     * We used the red channel of the original skin profile defined in
	     * [d'Eon07] for all three channels. We noticed it can be used for green
	     * and blue channels (scaled using the falloff parameter) without
	     * introducing noticeable differences and allowing for total control over
	     * the profile. For example, it allows to create blue SSS gradients, which
	     * could be useful in case of rendering blue creatures.
	     */
            return  // 0.233f * gaussian(0.0064f, r) + /* We consider this one to be directly bounced light, accounted by the strength parameter (see @STRENGTH) */
               0.100f * Gaussian(0.0484f, r, falloff) +
               0.118f * Gaussian( 0.187f, r, falloff) +
               0.113f * Gaussian( 0.567f, r, falloff) +
               0.358f * Gaussian(  1.99f, r, falloff) +
               0.078f * Gaussian(  7.41f, r, falloff);
        }

        public void CalculateKernel(Vector3 strength, Vector3 falloff, Material material)
        {
            const float RANGE = nSamples > 20 ? 3.0f : 2.0f;
            const float EXPONENT = 2.0f;

            // Calculate the offsets:

            float step = 2.0f * RANGE / (nSamples - 1);
            for (int i = 0; i < nSamples; i++)
            {
                float o = -RANGE + i * step;
                float sign = o < 0.0f ? -1.0f : 1.0f;
                kernel[i].w = RANGE * sign * Mathf.Abs(Mathf.Pow(o, EXPONENT)) / Mathf.Pow(RANGE, EXPONENT);
            }

            // Calculate the weights:
            for (int i = 0; i < nSamples; i++)
            {
                float w0 = i > 0 ? Mathf.Abs(kernel[i].w - kernel[i - 1].w) : 0.0f;
                float w1 = i < nSamples - 1 ? Mathf.Abs(kernel[i].w - kernel[i + 1].w) : 0.0f;
                float area = (w0 + w1) / 2.0f;
                Vector3 tt = area * Profile(kernel[i].w, falloff);
                kernel[i].x = tt.x;
                kernel[i].y = tt.y;
                kernel[i].z = tt.z;
            }

            // We want the offset 0.0 to come first:
            Vector4 t = kernel[nSamples / 2];
            for (int i = nSamples / 2; i > 0; i--)
                kernel[i] = kernel[i - 1];
            kernel[0] = t;

             // Calculate the sum of the weights, we will need to normalize them below:
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < nSamples; i++)
            {
                sum.x += kernel[i].x;
                sum.y += kernel[i].y;
                sum.z += kernel[i].z;
            }

            // Normalize the weights:
            for (int i = 0; i < nSamples; i++)
            {
                kernel[i].x /= sum.x;
                kernel[i].y /= sum.y;
                kernel[i].z /= sum.z;
            }

            // Tweak them using the desired strength. The first one is:
            //     lerp(1.0, kernel[0].rgb, strength)
            kernel[0].x = (1.0f - strength.x) * 1.0f + strength.x * kernel[0].x;
            kernel[0].y = (1.0f - strength.y) * 1.0f + strength.y * kernel[0].y;
            kernel[0].z = (1.0f - strength.z) * 1.0f + strength.z * kernel[0].z;

            for (int i = 1; i < nSamples; i++)
            {
                kernel[i].x *= strength.x;
                kernel[i].y *= strength.y;
                kernel[i].z *= strength.z;
            }

            material.SetVectorArray("_Kernel", kernel);
        }

        public SeparableSubsurfaceScatterPass(RenderPassEvent evt, Shader ssssPS)
        {
            renderPassEvent = evt;
            if (ssssPS == null)
            {
                Debug.LogError("Shader not found.");
                return;
            }
            ssssMaterial = CoreUtils.CreateEngineMaterial(ssssPS);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!renderingData.cameraData.postProcessEnabled) return;

            var stack = VolumeManager.instance.stack;
            m_SSSS = stack.GetComponent<SeparableSubsurfaceScatter>();
            if (m_SSSS == null) { return; }
            if (!m_SSSS.IsActive()) { return; }

            if (ssssMaterial == null) return;

            var cmd = CommandBufferPool.Get(k_RenderTag);

            ref CameraData cameraData = ref renderingData.cameraData;

            RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;

            bool isSceneViewCamera = cameraData.isSceneViewCamera;

            {
                var material = ssssMaterial;

                var destination = currentTarget;

                cmd.SetGlobalTexture(MainTexId, destination);
                cmd.GetTemporaryRT(TempTargetId, desc.width, desc.height, 0, FilterMode.Bilinear, desc.colorFormat);

                cmd.Blit(destination, TempTargetId);

                Vector3 SSSColor = new Vector3(m_SSSS.SubsurfaceColor.value.r, m_SSSS.SubsurfaceColor.value.g, m_SSSS.SubsurfaceColor.value.b);
                Vector3 SSSFalloff = new Vector3(m_SSSS.SubsurfaceFalloff.value.r, m_SSSS.SubsurfaceFalloff.value.g, m_SSSS.SubsurfaceFalloff.value.b);
                CalculateKernel(SSSColor, SSSFalloff, material);

                material.SetFloat("_SSSSDepthFalloff", m_SSSS.SurfaceDepthFalloff.value);

                float distanceToProjectionWindow = 1.0F / Mathf.Tan(0.5F * Mathf.Deg2Rad * (renderingData.cameraData.camera.fieldOfView) * 0.333F);

                material.SetFloat("_DistanceToProjectionWindow", distanceToProjectionWindow);

                if(m_SSSS.FollowSurfaceDepth.value)
                    material.EnableKeyword("SSSS_FOLLOW_SURFACE");
                else
                    material.DisableKeyword("SSSS_FOLLOW_SURFACE");

                material.SetFloat("_RefValue", m_SSSS.RefValue.value);
                
                cmd.SetGlobalTexture(MainTexId, TempTargetId);

                cmd.SetGlobalVector("_SSSSDirection", new Vector4(m_SSSS.SubsurfaceWidth.value, 0f, 0f, 0f));

                if(isSceneViewCamera)
                { 
             	     cmd.Blit(TempTargetId, destination, material, 0);
                }
                else
                {
                    cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);

                    cmd.SetRenderTarget(currentTarget, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, currentRenderer.cameraDepthTarget, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

                    cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, material, 0, 0);
                }

                cmd.Blit(destination, TempTargetId);

                cmd.SetGlobalVector("_SSSSDirection", new Vector4(0f, m_SSSS.SubsurfaceWidth.value, 0f, 0f));

                cmd.SetGlobalTexture(MainTexId, TempTargetId);
                if (isSceneViewCamera)
                {
                   cmd.Blit(TempTargetId, destination, material, 0);
                }
                else
                {
                    cmd.SetRenderTarget(currentTarget, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, currentRenderer.cameraDepthTarget, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, material, 0, 0);

                }

                cmd.SetGlobalTexture(MainTexId, destination);

                cmd.ReleaseTemporaryRT(TempTargetId);

                context.ExecuteCommandBuffer(cmd);

                CommandBufferPool.Release(cmd);
            }
        }

        public void Setup(in RenderTargetIdentifier target, in ScriptableRenderer renderer)
        {
            currentTarget = target;
            currentRenderer = renderer;
        }

    }
}
