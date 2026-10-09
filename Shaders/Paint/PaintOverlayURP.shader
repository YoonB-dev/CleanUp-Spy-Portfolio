Shader "Custom/PaintOverlayURP"
{
    Properties
    {
        _PaintMask ("Paint Mask (R = 오염도)", 2D) = "black" {}
        _PaintColor ("Paint Color", Color) = (0.8, 0.05, 0.05, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        Pass
        {
            Name "PaintOverlay"
            Tags { "LightMode"="UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 텍스처 및 샘플러 선언
            TEXTURE2D(_PaintMask);
            SAMPLER(sampler_PaintMask);

            // SRP Batcher 호환을 위해 텍스처 변환 변수와 머테리얼 프로퍼티를 UnityPerMaterial CBUFFER에 둔다
            CBUFFER_START(UnityPerMaterial)
                float4 _PaintMask_ST; // UV Scale/Offset 사용 시 필요
                half4 _PaintColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                // 머테리얼의 Tiling/Offset 반영
                OUT.uv = TRANSFORM_TEX(IN.uv, _PaintMask); 
                return OUT;
            }

            half4 frag(Varyings IN, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                if (!frontFace)
                {
                    return half4(0, 0, 0, 0);
                }

                half mask = SAMPLE_TEXTURE2D(_PaintMask, sampler_PaintMask, IN.uv).r;

                return half4(_PaintColor.rgb, mask * _PaintColor.a);
            }
            ENDHLSL
        }
    }
}