// 물속 물빛 벽: 머리를 감싸는 큰 구의 안쪽 면을 지금 안개 색(unity_FogColor) 한 색으로 칠한다 — 안개가 덮지 못하는 하늘(스카이박스)을 가린다.
// 구는 안개가 거의 다 덮는 거리에 두므로 그 앞의 물체·너울은 안개에 섞여 벽 색으로 자연스럽게 사라진다. 불투명(깊이 씀) — 스카이박스는 깊이가 빈 곳에만 그려진다.
Shader "ACO/UnderwaterBackdrop"
{
    SubShader
    {
        Tags { "Queue" = "Geometry+100" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Front
        ZWrite On

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
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return fixed4(unity_FogColor.rgb, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
