// 월드 공간 브러시 셰이더 (PaintSurfaceManager가 CommandBuffer.DrawMesh로 사용).
// 표면 메시를 UV 좌표 위치에 펼쳐서 RenderTexture에 그리므로, 각 픽셀이 메시 위 어느 월드 지점인지 알 수 있다.
//  - Pass 0 (브러시): 브러시 중심과의 '월드 거리'로 칠하기/지우기
//    → 메시 크기/스케일/UV 배치/타일링과 무관하게 항상 같은 실제 크기(미터)의 둥근 자국
//  - Pass 1 (면적 측정): 픽셀 하나가 덮는 월드 면적(m²)을 기록 → 칠해진 픽셀 수를 실제 면적으로 환산할 때 사용
// CommandBuffer 직접 실행이라 URP 파이프라인을 거치지 않으므로 UnityCG.cginc(CGPROGRAM) 기반으로 동작함.
Shader "Hidden/PaintBrush"
{
    Properties
    {
        _BrushPos ("Brush World Position", Vector) = (0, 0, 0, 0)
        _BrushRadius ("Brush Radius (m)", Float) = 0.2
        _BrushValue ("Brush Value (1=칠하기, 0=지우기)", Float) = 1
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    struct appdata
    {
        float4 vertex : POSITION;
        float2 uv     : TEXCOORD0;
    };

    struct v2f
    {
        float4 pos      : SV_POSITION;
        float3 worldPos : TEXCOORD0;
    };

    v2f vert(appdata v)
    {
        v2f o;
        // UV(0~1)를 클립 공간(-1~1)으로 펼친다.
        // 뷰/투영은 C#에서 단위 행렬로 설정하므로, UNITY_MATRIX_P에는 Unity가 넣어주는
        // 플랫폼별 보정(렌더 텍스처 상하 반전 등)만 남는다 → 직접 #if로 뒤집지 않아도 됨
        o.pos = mul(UNITY_MATRIX_P, float4(v.uv * 2.0 - 1.0, 0.0, 1.0));
        o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
        return o;
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        // UV 공간에서는 삼각형 방향이 제각각이므로 컬링 끔
        Cull Off ZWrite Off ZTest Always

        // Pass 0: 브러시
        Pass
        {
            // 결과 = 기존값 * (1 - mask) + 브러시값 * mask  (기존 lerp(existing, value, mask)와 동일)
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _BrushPos;
            float _BrushRadius;
            float _BrushValue;

            fixed4 frag(v2f i) : SV_Target
            {
                float dist = distance(i.worldPos, _BrushPos.xyz);
                // dist < radius*0.7 : mask=1 (브러시값 완전 적용, 중심부)
                // radius*0.7 ~ radius : mask가 1→0으로 페더링 (부드러운 경계)
                // dist > radius : mask=0 (기존값 유지)
                float mask = smoothstep(_BrushRadius, _BrushRadius * 0.7, dist);

                float value = _BrushValue * mask;
                return fixed4(value, value, value, mask);
            }
            ENDCG
        }

        // Pass 1: 픽셀당 월드 면적 측정
        Pass
        {
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragArea

            float4 fragArea(v2f i) : SV_Target
            {
                // 옆/위 픽셀로 한 칸 갈 때 월드 위치가 얼마나 변하는지 → 두 변으로 만든 평행사변형 넓이 = 픽셀 하나의 월드 면적
                float3 dx = ddx(i.worldPos);
                float3 dy = ddy(i.worldPos);
                float area = length(cross(dx, dy));
                return float4(area, 0, 0, 0);
            }
            ENDCG
        }
    }
}
