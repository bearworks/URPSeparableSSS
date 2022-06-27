using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering.Universal
{
    public class SeparableSubsurfaceScatterPassRenderFeature : ScriptableRendererFeature
    {
        SeparableSubsurfaceScatterPass sssssPass;
        
        public Shader sssssPS;

        public override void Create()
        {
            if(sssssPS == null)
               sssssPS = Shader.Find("Hidden/Universal Render Pipeline/SeparableSubsurfaceScatter");

            sssssPass = new SeparableSubsurfaceScatterPass(RenderPassEvent.AfterRenderingTransparents, sssssPS);

        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            sssssPass.Setup(renderer.cameraColorTarget, renderer);
            renderer.EnqueuePass(sssssPass);
        }
       
    }
}
