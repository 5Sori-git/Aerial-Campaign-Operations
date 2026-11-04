// 낙뢰 줄기(ACO_Lightning이 번개 칠 때만 켠다): 엇갈린 리본 두 장(어느 방향에서 봐도 보인다)의 지그재그 메시를 더하기 블렌드로 빛나게 그린다.
//  - 메시 UV.x = 리본 폭(0~1, 가운데가 가장 밝다), UV.y = 줄기 위치(0 땅 ~ 1 구름). 정점 색 a = 가지(곁가지는 옅게).
//  - _Intensity(스크립트) = 번쩍임(2~3번 깜빡인다). 안개를 받지 않는다(빛이 안개를 뚫고 보인다) — 멀리는 _FadeDistance로 옅게.
Shader "ACO/Lightning"
{
    Properties
    {
        _Color ("Core", Color) = (0.85, 0.9, 1, 1)
        _GlowColor ("Glow", Color) = (0.45, 0.55, 1, 1)
        _Intensity ("Intensity (script)", Range(0, 3)) = 0
        _FadeDistance ("Fades out by (m)", Float) = 20000
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color, _GlowColor;
            float _Intensity, _FadeDistance;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fade : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
                o.pos = UnityWorldToClipPos(wp);
                o.uv = v.uv;
                o.fade = v.color.a * (1 - saturate(distance(_WorldSpaceCameraPos, wp) / max(_FadeDistance, 1)));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float x = abs(i.uv.x * 2 - 1);            // 0 = 가운데
                float core = pow(saturate(1 - x * 2.5), 2);
                float glow = pow(saturate(1 - x), 2);
                float3 c = _Color.rgb * core * 3 + _GlowColor.rgb * glow * 0.6;
                return fixed4(c * (_Intensity * i.fade), 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
