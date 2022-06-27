Shader "Hidden/Universal Render Pipeline/SeparableSubsurfaceScatter" {
    Properties 
    {
        _RefValue ("Ref Value", Float) = 2
    }
    
	HLSLINCLUDE
	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
	#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

	#define nSamples 11

	float _SSSSDepthFalloff;
	float _DistanceToProjectionWindow;
	float2 _SSSSDirection;
	float4 _Kernel[nSamples];
	float4 _CameraDepthTexture_TexelSize;

	TEXTURE2D_X(_MainTex);
	TEXTURE2D_X(_CameraDepthTexture);
	SAMPLER(sampler_CameraDepthTexture);

    #pragma target 3.0
    ENDHLSL

    SubShader 
	{
		ZTest Always ZWrite Off Cull Off
        Pass 
		{
            Name "Separable Pass"

            Stencil
            {
                Ref [_RefValue]
                Comp equal
            }

            HLSLPROGRAM
            
            #pragma multi_compile _ SSSS_FOLLOW_SURFACE
            #pragma vertex FullscreenVert
            #pragma fragment frag

            float4 frag(Varyings i) : SV_TARGET {

                float2 texcoord = i.uv.xy;
                float4 colorM = SAMPLE_TEXTURE2D_X(_MainTex, sampler_PointClamp, texcoord);

				float dSceneDepth = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, texcoord);
				float depthM = LinearEyeDepth(dSceneDepth, _ZBufferParams);

				float scale = _DistanceToProjectionWindow / depthM;
				float2 finalStep = _SSSSDirection.xy * scale * _CameraDepthTexture_TexelSize.xy;

				float4 colorBlurred = colorM;
				colorBlurred.rgb *= _Kernel[0].rgb;

				for (int i = 1; i < nSamples; i++) 
				{
					float2 offset = texcoord + _Kernel[i].a * finalStep;
					float4 color = SAMPLE_TEXTURE2D_X(_MainTex, sampler_PointClamp, offset);
			   #ifdef SSSS_FOLLOW_SURFACE
					float depth = LinearEyeDepth(SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, offset), _ZBufferParams);
				#if 1
					float s = 1 - exp(-_SSSSDepthFalloff * 10 * _DistanceToProjectionWindow * abs(depthM - depth));
				#else
					float s = saturate(_SSSSDepthFalloff * 10 * _DistanceToProjectionWindow * abs(depthM - depth));
				#endif
					color.rgb = lerp(color.rgb, colorM.rgb, s);
			   #endif
					colorBlurred.rgb += _Kernel[i].rgb * color.rgb;
				}

				return colorBlurred;
            }
            ENDHLSL
        } 
	
    }
}
