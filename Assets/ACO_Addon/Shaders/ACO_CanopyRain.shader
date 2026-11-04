// 캐노피 빗방울(ACO_Rain이 조종 중일 때만 켠다): 머리를 감싸는 구(기체 자세에 맞춰 돈다 — 방울이 캐노피에 붙어 있는 것처럼)의 앞쪽 반구에 물방울을 그린다.
//  - 좌표 = 기체 기준 방위(좌우 각) × 고도(위아래 각) — 앞 가운데에 특이점이 없다. 칸마다 무작위로 방울 하나(밀도 _DropDensity), 두 겹.
//  - 느릴 땐 방울이 맺혔다 사라지며 천천히 흘러내리고, 빠를수록(_Speed / _StreakSpeed) 위로 길게 늘어나 빠르게 넘어간다(풍압에 캐노피 위로 밀려 올라가는 물줄기).
//  - _Intensity(스크립트) = 비 세기(지붕 아래·구름층 위면 0으로 마른다). 색은 안개 색 쪽으로 섞어 날씨에 어울리게.
//  - 깊이 검사(구 반경 0.9m보다 가까운 조종석 프레임·계기는 덮지 않는다), HUD보다 먼저.
Shader "ACO/CanopyRain"
{
    Properties
    {
        _Intensity ("Intensity (script)", Range(0, 1)) = 0
        _Speed ("Airspeed m/s (script)", Float) = 0
        _StreakSpeed ("Full streak at (m/s)", Float) = 120
        _DropDensity ("Drop density", Range(0, 1)) = 0.45
        _DropSize ("Drop size", Range(0.1, 1)) = 0.55
        _Opacity ("Opacity", Range(0, 1)) = 0.5
        _Tint ("Tint", Color) = (0.85, 0.9, 0.95, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+60" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Front
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _Intensity, _Speed, _StreakSpeed, _DropDensity, _DropSize, _Opacity;
            fixed4 _Tint;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;   // 기체 기준 방향(구의 오브젝트 공간 — 스크립트가 구를 기체 자세로 돌린다)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            /// 방울 한 겹: 칸 크기(라디안당 칸 수), 흐름 속도, 겹 번호. 반환 = 방울 알파(테두리가 조금 더 진하다).
            float Layer(float2 ang, float cells, float flow, float speedK, float layer)
            {
                float2 g = float2(ang.x * cells, ang.y * cells - flow);
                float2 id = floor(g);
                float2 f = frac(g) - 0.5;
                float h = Hash(id + layer * 31.7);
                if (h > _DropDensity) { return 0; }
                float h2 = Hash(id + 7.3 + layer * 11.1);
                float h3 = Hash(id + 19.7 + layer * 5.3);
                f -= (float2(h2, h3) - 0.5) * 0.5;
                float stretch = lerp(1, 6, speedK);
                float2 q = float2(f.x, f.y / stretch);
                float size = _DropSize * 0.5 * (0.6 + 0.8 * h3);
                float r = length(q) / max(size, 0.01);
                // 느릴 땐 맺혔다 사라진다(칸마다 다른 때), 빠르면 늘 보인다(흐르는 물줄기)
                float life = frac(_Time.y * 0.12 + h * 7.1);
                float lifeA = lerp(smoothstep(0, 0.15, life) * smoothstep(1, 0.75, life), 1, speedK);
                float drop = smoothstep(1, 0.55, r);
                return drop * (0.55 + 0.45 * smoothstep(0.2, 0.9, r)) * lifeA;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float front = smoothstep(0.0, 0.35, d.z);   // 앞쪽 반구만
                if (front <= 0 || _Intensity <= 0.001) { return 0; }
                float2 ang = float2(atan2(d.x, d.z), asin(clamp(d.y, -1, 1)));   // 좌우·위아래 각(라디안)
                float speedK = saturate(_Speed / max(_StreakSpeed, 1));
                // 흐름: 느리면 천천히 아래로(중력), 빠르면 빠르게 위로(풍압)
                float flow = _Time.y * lerp(-0.08, 3.5, speedK);
                float a = max(Layer(ang, 16, flow, speedK, 0), Layer(ang, 27, flow * 1.3, speedK, 1));
                float3 col = lerp(_Tint.rgb, unity_FogColor.rgb, 0.4);
                return fixed4(col, a * _Opacity * _Intensity * front);
            }
            ENDCG
        }
    }
    FallBack Off
}
