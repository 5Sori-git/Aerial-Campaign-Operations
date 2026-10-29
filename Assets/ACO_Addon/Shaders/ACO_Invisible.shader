// 아무것도 그리지 않는다: 색·깊이를 쓰지 않고, 정점을 한 점으로 모아 픽셀도 만들지 않는다. 그림자 패스가 없어 그림자도 없다.
// 렌더러를 끄지 않고 보이지만 않게 하고 싶을 때(콜라이더용 메시, 애니메이터가 켜고 끄는 렌더러 등) 쓴다.
Shader "ACO/Invisible"
{
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        ZWrite Off
        ColorMask 0
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = float4(0, 0, 0, 0);   // 모든 정점을 한 점으로 — 삼각형이 면적이 없어 픽셀이 생기지 않는다
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
    Fallback Off
}
