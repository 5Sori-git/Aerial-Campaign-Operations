// 소형 레일건 폭발 지역(피해 구)의 반투명 껍질 — 화구처럼 일렁이는 불빛 막. 메시는 Unity 내장 Sphere.fbx(반지름 1 — 스크립트가 피해 반경 ÷ ShellMeshRadius로 키운다).
//  - 가장자리(시선과 비스듬한 면)는 진하고 정면은 옅다 → 구의 경계가 고리처럼 보이고 안쪽이 비친다.
//  - 불꽃 일렁임: 물체 좌표 3D 값 노이즈 두 겹을 위로 흘린다(텍스처 없음).
//  - 안에서 보면(뒷면) 더 옅게(_InsideAlpha) — 들어간 조종사의 시야를 가리지 않는다.
//  - 가산 혼합. 안개에서는 검게 줄어든다(먼 곳에서 흰 판이 생기지 않게). _Fade(스크립트)로 나타나고 사라진다.
Shader "ACO/ZoneShell"
{
    Properties
    {
        _ColorEdge ("Edge color", Color) = (1, 0.62, 0.22, 1)
        _ColorCore ("Inner color", Color) = (1, 0.28, 0.06, 1)
        _Fade ("Fade (script)", Range(0, 1)) = 1
        _EdgePower ("Edge sharpness", Float) = 2.2
        _BaseAlpha ("Face-on alpha", Range(0, 1)) = 0.06
        _EdgeAlpha ("Edge alpha", Range(0, 1)) = 0.75
        _InsideAlpha ("Inside (back face) multiplier", Range(0, 1)) = 0.45
        _NoiseScale ("Flame noise scale", Float) = 4
        _NoiseSpeed ("Flame speed", Float) = 0.5
        _Boost ("Brightness", Float) = 1.5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _ColorEdge, _ColorCore;
            float _Fade, _EdgePower, _BaseAlpha, _EdgeAlpha, _InsideAlpha, _NoiseScale, _NoiseSpeed, _Boost;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 objPos : TEXCOORD2;
                UNITY_FOG_COORDS(3)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.objPos = v.vertex.xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i, fixed facing : VFACE) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 view = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = pow(1.0 - abs(dot(normalize(i.normal), view)), _EdgePower);
                float t = _Time.y * _NoiseSpeed;
                float3 q = i.objPos * _NoiseScale;
                float flame = Noise(q + float3(0, -t, 0)) * 0.6 + Noise(q * 2.3 + float3(t * 0.6, -t * 1.4, t * 0.3)) * 0.4;
                float a = lerp(_BaseAlpha, _EdgeAlpha, rim) * (0.5 + flame);
                if (facing < 0) { a *= _InsideAlpha; }
                a = saturate(a * _Fade);
                fixed4 c;
                c.rgb = lerp(_ColorCore.rgb, _ColorEdge.rgb, saturate(rim + (flame - 0.5) * 0.6)) * _Boost;
                c.a = a;
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0, 0, 0, 0));
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
