// 레일건 레이저·포선. 안개와 구름에 가려지면 안 되는 경고라 안개를 받지 않고, 안개·구름을 그린 뒤에 그린다.
//  - 지형과 건물에는 가려진다(ZTest LEqual). 구름 층 판은 깊이를 쓰므로 판 품질에서는 층 뒤쪽이 가려질 수 있다.
//  - 가운데가 밝고 가장자리가 부드러운 띠(LineRenderer의 폭 방향 UV).
Shader "ACO/Laser"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.05, 0.03, 1)
        _Core ("Core brightness", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue" = "Overlay-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Core;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float across = 1 - abs(i.uv.y * 2 - 1);   // 가장자리 0, 가운데 1
                fixed4 c = i.color;
                c.rgb = lerp(c.rgb, 1, _Core * smoothstep(0.55, 1, across));
                c.a *= smoothstep(0, 0.35, across);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
