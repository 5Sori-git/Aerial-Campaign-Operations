// 구름 속 물안개 막: 머리를 감싸는 작은 구(ACO_CloudField가 머리로 옮긴다)에 시선 방향 몇 거리(가까이·중간·멀리)의 3D 노이즈를 겹쳐 그린다.
// 노이즈는 월드에 고정돼 있어, 날면 안개 줄기가 앞에서 다가와 스쳐 지나간다(VR에서도 거리감이 맞다). 색 = 지금 안개 색.
//  - _Amount(스크립트, 0~1): 구름 속 정도. 0이면 스크립트가 끈다.
//  - 큐 3980: 물속 물빛 구(3990)·HUD(4000 이상)보다 먼저 — HUD는 또렷하다. 깊이 검사를 해서 구 반경보다 가까운 조종석은 덮지 않는다.
Shader "ACO/CloudVeil"
{
    Properties
    {
        _Amount ("Amount (script)", Range(0, 1)) = 0
        _Opacity ("Max opacity", Range(0, 1)) = 0.55
        _WispScale ("Near wisp size (m)", Float) = 9
        _Brightness ("Brightness", Range(0.5, 1.5)) = 1.05
    }
    SubShader
    {
        Tags { "Queue" = "Overlay-20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Front
        ZWrite Off
        ZTest LEqual   // 구 반경(1.5m)보다 가까운 조종석은 덮지 않는다
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _Amount, _Opacity, _WispScale, _Brightness;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3 - 2 * f);
                float a = lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), u.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), u.x), u.y);
                float b = lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), u.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), u.x), u.y);
                return lerp(a, b, u.z);
            }

            float Wisp(float3 p)
            {
                return Noise(p) * 0.65 + Noise(p * 2.1 + 7.3) * 0.35;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
                o.dir = wp - _WorldSpaceCameraPos;
                o.pos = UnityWorldToClipPos(wp);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 cam = _WorldSpaceCameraPos;
                float s = max(_WispScale, 0.5);
                // 가까이·중간·멀리 — 멀수록 크게(멀리 있는 덩어리는 천천히, 가까운 줄기는 빠르게 스친다)
                float w = Wisp((cam + d * (s * 0.6)) / s) * 0.45
                        + Wisp((cam + d * (s * 1.6)) / (s * 2)) * 0.35
                        + Wisp((cam + d * (s * 4)) / (s * 4.5)) * 0.2;
                float a = _Amount * _Opacity * saturate(w * 1.5 - 0.15);
                return fixed4(unity_FogColor.rgb * _Brightness, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
