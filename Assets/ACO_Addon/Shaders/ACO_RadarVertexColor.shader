// 레이더(HUD 레이더·조종석 지도)용: 조명 없이 정점 색 × _Color, 반투명. 깊이 판정은 _ZTest — HUD 레이더는 Always(항상 맨 위), 조종석 지도는 LessEqual(조종석에 가려진다).
// 겹치는 순서(바탕 → 고리/지형 → 점)는 머티리얼 렌더 큐로 정한다(깊이를 쓰지 않는다).
Shader "ACO/RadarVertexColor"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }
    SubShader
    {
        Tags { "Queue" = "Overlay+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZTest [_ZTest]
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
