// 뭉게구름(ACO_CloudField의 구름 덩어리). 메시는 퍼프(구름 조각) 수십 개 — 퍼프마다 정점 4개가 모두 퍼프 중심에 겹쳐 있고,
// 정점 셰이더가 카메라를 향해 펼친다(구형 빌보드). 메시는 ACO_SkyBuilder가 만든다.
//  정점: POSITION = 퍼프 중심, NORMAL = 덩어리 바깥 방향, COLOR r = 차폐(AO) g = 아래쪽 정도,
//        UV0 = 모서리(-1~1), UV1 = (반지름 m, 회전각, 무작위, 아틀라스 칸)
//  - VR: 두 눈의 가운데를 향한다(눈마다 다르게 펼치면 좌우 형상이 달라 지글거린다)
//  - 판인데 둥글게: UV로 구 법선을 복원(_PuffRound) + 덩어리 바깥 방향과 섞어 덩어리 전체가 해 쪽이 밝다
//  - 조명: 감싸는 조명, 두꺼운 곳은 어둡게, 해를 등지면 얇은 가장자리가 빛남(은빛 테두리), 아래쪽·안쪽 어둡게(정점 색)
//  - 가장자리: 알파 투 커버리지(MSAA에서 부드럽게, 정렬 불필요, 깊이 기록) — MSAA가 없으면 알파 테스트처럼 그려진다
//  - 가까이(_NearFadeEnd → _NearFadeStart) 오면 디더링으로 사라진다(구름 속은 ACO_CloudField가 안개로 채운다)
//  - 멀리(_LodStart → _LodEnd)는 퍼프 일부를 접고(_LodKeep만 남김) 남은 퍼프를 키운다
//  - _Darken: 날씨(비·폭풍)로 어두운 회색 구름
// 부드러운 반투명으로 바꾸려면: _Soft 켬, Src Blend = SrcAlpha, Dst Blend = OneMinusSrcAlpha, ZWrite Off, Alpha To Coverage Off, Render Queue 3000
// (퍼프 정렬이 안 돼 겹친 순서가 틀릴 수 있고 구름 속에서 겹쳐 그리는 비용이 크다).
Shader "ACO/CloudPuffs"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Puff atlas (2x2, A = shape, R = thickness)", 2D) = "white" {}

        [Header(Lighting)]
        _LitColor ("Sunlit", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shadow side", Color) = (0.58, 0.63, 0.72, 1)
        _BottomColor ("Bottom", Color) = (0.56, 0.6, 0.68, 1)
        _Wrap ("Wrap lighting", Range(0, 1)) = 0.6
        _PuffRound ("Puff roundness", Range(0, 1)) = 0.35
        _ThickShade ("Thick parts darker", Range(0, 1)) = 0.3
        _Ambient ("Sky ambient", Range(0, 1)) = 0.25
        _Silver ("Silver lining", Range(0, 3)) = 1.2
        _SilverPower ("Silver lining focus", Range(1, 32)) = 8
        _Darken ("Weather darken", Range(0, 1)) = 0
        _StormColor ("Storm tint", Color) = (0.42, 0.44, 0.5, 1)

        [Header(Shape)]
        _SizeScale ("Puff size scale", Float) = 1
        _Wobble ("Wobble", Range(0, 0.2)) = 0.05
        _WobbleSpeed ("Wobble speed", Float) = 0.12
        _Cutoff ("Edge cutoff", Range(0.05, 0.95)) = 0.45

        [Header(Distance)]
        _NearFadeStart ("Gone closer than (m)", Float) = 40
        _NearFadeEnd ("Full from (m)", Float) = 160
        _LodStart ("LOD start (m)", Float) = 7000
        _LodEnd ("LOD end (m)", Float) = 22000
        _LodKeep ("LOD puffs kept at end", Range(0.1, 1)) = 0.4

        [Header(Blend mode)]
        [Toggle] _Soft ("Soft (alpha blend) edges", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(Off, 0, On, 1)] _AlphaToMask ("Alpha To Coverage", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest+20" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" "DisableBatching" = "True" }

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float _SizeScale, _Wobble, _WobbleSpeed, _Cutoff;
        float _NearFadeStart, _NearFadeEnd, _LodStart, _LodEnd, _LodKeep;

        struct appdata
        {
            float4 vertex : POSITION;    // 퍼프 중심(오브젝트)
            float3 normal : NORMAL;      // 덩어리 바깥 방향(오브젝트)
            float4 color : COLOR;        // r = 차폐, g = 아래쪽
            float2 uv0 : TEXCOORD0;      // 모서리 -1~1
            float4 uv1 : TEXCOORD1;      // 반지름, 회전각, 무작위, 칸
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 CenterEye()
        {
        #if defined(USING_STEREO_MATRICES)
            return (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
        #else
            return _WorldSpaceCameraPos;
        #endif
        }

        // 퍼프를 펼친다: forward 쪽을 향하는 축(right, up — 퍼프 회전 반영)과 월드 위치, 남길지(0/1), 가까운 페이드.
        void Billboard(appdata v, float3 viewer, bool towardLight, float3 lightDir,
            out float3 posWS, out float3 right, out float3 up, out float3 fwd, out float fade)
        {
            float3 c = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
            fwd = towardLight ? -lightDir : normalize(c - viewer);
            float3 upRef = abs(fwd.y) > 0.99 ? float3(0, 0, 1) : float3(0, 1, 0);
            float3 r0 = normalize(cross(upRef, fwd));
            float3 u0 = cross(fwd, r0);
            float s, co;
            sincos(v.uv1.y, s, co);
            right = r0 * co + u0 * s;
            up = -r0 * s + u0 * co;

            float3 scl = float3(length(unity_ObjectToWorld._m00_m10_m20), length(unity_ObjectToWorld._m01_m11_m21), length(unity_ObjectToWorld._m02_m12_m22));
            float size = v.uv1.x * _SizeScale * (scl.x + scl.y + scl.z) / 3;
            size *= 1 + _Wobble * sin(_Time.y * _WobbleSpeed * 6.2832 + v.uv1.z * 6.2832);

            float dist = distance(CenterEye(), c);
            // 거리 LOD: 무작위 값이 남길 비율보다 크면 접는다. 남은 퍼프는 빈자리를 덮도록 키운다
            float t = smoothstep(_LodStart, _LodEnd, dist);
            float keep = lerp(1, _LodKeep, t);
            size *= (v.uv1.z <= keep ? 1 : 0) * pow(keep, -0.45);
            // 가까우면 사라진다(완전히 사라지면 접어서 그리지도 않는다)
            fade = towardLight ? 1 : smoothstep(_NearFadeStart, _NearFadeEnd, dist);
            size *= fade > 0.001 ? 1 : 0;

            posWS = c + (right * v.uv0.x + up * v.uv0.y) * size;
        }

        float2 AtlasUV(float2 corner, float cell)
        {
            float k = floor(fmod(cell, 4));
            float2 o = float2(fmod(k, 2), floor(k / 2)) * 0.5;
            return o + (corner * 0.5 + 0.5) * 0.5;
        }

        // 화면 디더(Interleaved Gradient Noise)
        float Dither(float2 pix)
        {
            return frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
        }
        ENDCG

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Off
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            AlphaToMask [_AlphaToMask]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityLightingCommon.cginc"

            fixed4 _LitColor, _ShadeColor, _BottomColor, _StormColor;
            float _Wrap, _PuffRound, _ThickShade, _Ambient, _Silver, _SilverPower, _Darken, _Soft;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 uv : TEXCOORD0;        // xy = 아틀라스, zw = 모서리(-1~1)
                float3 right : TEXCOORD1;
                float3 up : TEXCOORD2;
                float3 fwd : TEXCOORD3;
                float3 clusterN : TEXCOORD4;
                float4 data : TEXCOORD5;      // x = 차폐, y = 아래쪽, z = 가까운 페이드
                float3 worldPos : TEXCOORD6;
                UNITY_FOG_COORDS(7)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 posWS, right, up, fwd;
                float fade;
                Billboard(v, CenterEye(), false, 0, posWS, right, up, fwd, fade);
                o.pos = UnityWorldToClipPos(posWS);
                o.uv = float4(AtlasUV(v.uv0, v.uv1.w), v.uv0);
                o.right = right;
                o.up = up;
                o.fwd = fwd;
                o.clusterN = UnityObjectToWorldNormal(v.normal);
                o.data = float4(v.color.r, v.color.g, fade, 0);
                o.worldPos = posWS;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv.xy);
                float2 c = i.uv.zw;
                float z = sqrt(saturate(1 - dot(c, c)));
                float3 puffN = normalize(i.right * c.x + i.up * c.y - i.fwd * z);
                float3 n = normalize(lerp(normalize(i.clusterN), puffN, _PuffRound));
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float thick = tex.r;

                // 감싸는 조명 + 두꺼운 곳 어둡게
                float lit = saturate((dot(n, L) + _Wrap) / (1 + _Wrap)) * (1 - _ThickShade * thick);
                float3 sun = _LitColor.rgb * _LightColor0.rgb;
                float3 col = lerp(_ShadeColor.rgb * (0.35 + 0.65 * saturate(_LightColor0.rgb)), sun, lit);
                // 하늘 환경광
                col += ShadeSH9(float4(n, 1)) * _Ambient;
                // 아래쪽·안쪽 어둡게(정점 색)
                col = lerp(col, _BottomColor.rgb * (0.4 + 0.6 * saturate(_LightColor0.rgb)), i.data.y * saturate(0.6 - n.y * 0.6));
                col *= lerp(0.7, 1, i.data.x);
                // 은빛 테두리: 해를 등지고 보면 얇은 가장자리가 빛난다
                float3 V = normalize(i.worldPos - CenterEye());
                col += _LightColor0.rgb * _Silver * pow(saturate(dot(V, L)), _SilverPower) * (1 - thick);
                // 날씨: 어두운 회색
                col = lerp(col, dot(col, float3(0.3, 0.5, 0.2)) * _StormColor.rgb * 1.6, _Darken);

                float fw = max(fwidth(tex.a), 0.0001);
                float a;
                if (_Soft > 0.5)
                {
                    a = smoothstep(_Cutoff - 0.3, _Cutoff + 0.15, tex.a) * i.data.z;
                    clip(a - 0.004);
                }
                else
                {
                    // 알파 투 커버리지용으로 가장자리를 날카롭게(픽셀 한두 칸 폭으로 안티에일리어싱) — MSAA가 없으면 clip이 알파 테스트가 된다
                    a = (tex.a - _Cutoff) / fw + 0.5;
                    clip(a);
                    // 가까운 페이드는 디더링(MSAA와 상관없이)
                    clip(i.data.z - Dither(i.pos.xy) - 0.001);
                }
                fixed4 o = fixed4(col, a);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }

        // 그림자(렌더러에서 Cast Shadows를 켤 때만): 빛 방향을 향해 펼친다. 카메라 깊이 패스에서는 카메라를 향한다.
        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            ZWrite On

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing
            #pragma target 3.0

            struct v2fS
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2fS vertShadow(appdata v)
            {
                v2fS o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2fS, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                bool shadow = unity_LightShadowBias.z != 0.0;
                float3 posWS, right, up, fwd;
                float fade;
                Billboard(v, CenterEye(), shadow, normalize(_WorldSpaceLightPos0.xyz), posWS, right, up, fwd, fade);
                o.pos = UnityApplyLinearShadowBias(UnityWorldToClipPos(posWS));
                o.uv = AtlasUV(v.uv0, v.uv1.w);
                return o;
            }

            fixed4 fragShadow(v2fS i) : SV_Target
            {
                clip(tex2D(_MainTex, i.uv).a - _Cutoff);
                return 0;
            }
            ENDCG
        }
    }
    FallBack Off
}
