// 구름층 판(비구름층 아랫면·윗면, 낮은 안개층 윗면 — ACO_CloudField가 머리 아래·위로 따라 옮긴다). 큰 평면 하나, 무늬는 월드 XZ에 고정.
//  - 색 = _Color와 _ShadeColor를 큰 얼룩 노이즈(값 노이즈 4옥타브, 바람 따라 천천히 흐른다)로 섞는다. 멀수록 얼룩을 줄인다(깜빡임 방지) — 멀리는 안개가 덮는다.
//  - 한쪽 면만: 윗면은 위에서만(Cull Back), 아랫면은 아래에서만(Cull Front — 평면 법선이 위라 뒷면이 보인다).
//  - 거의 불투명(깊이 씀) — 아래에서 보면 하늘을 완전히 덮고, 위에서 보면 땅을 덮는다(층보다 높은 산봉우리는 그대로 솟는다).
//  - 부드러운 경계: 깊이 텍스처로 판 바로 뒤 물체(지형·뭉게구름·함선 등)까지 거리를 재서, _SoftDistance보다 가까우면 판을 투명하게 한다
//    — 판이 물체를 자르는 선이 안개 속으로 스며들 듯 흐려진다. 깊이 텍스처는 VRChat에서 실시간 그림자 태양이 있어야 생긴다:
//    없으면(그림자를 끈 플레이어) 예전처럼 딱딱한 경계. 깊이 텍스처에 안 그려지는 것(그림자 패스가 없는 나무·바다)은 흐려지지 않는다.
//  - 큐 Transparent-499: 불투명·뭉게구름·스카이박스를 다 그린 뒤(투명한 경계로 그 뒤가 비친다), 하늘 안개 돔·바다 전.
//  - _Fade(스크립트): 날씨가 바뀔 때 디더링으로 나타나고 사라진다(디더라 사라진 자리는 깊이도 안 써 바다가 그대로 보인다).
//  - 폭풍(스크립트): _Darken = 더 어두운 천장, _Flash·_FlashPos = 번개 — 번개 자리 둘레가 안쪽에서 빛나듯 밝아진다(조명을 켜지 않는다 — 값 하나).
Shader "ACO/CloudLayer"
{
    Properties
    {
        _SoftDistance ("Soft edge height (m from the layer)", Float) = 120
        _SoftRayDistance ("Cover things farther than (m along view, behind the layer)", Float) = 2500        _Color ("Main", Color) = (0.95, 0.96, 0.98, 1)
        _ShadeColor ("Blotch", Color) = (0.75, 0.78, 0.84, 1)
        _NoiseScale ("Blotch tile (m)", Float) = 1800
        _Contrast ("Blotch amount", Range(0, 1)) = 0.7
        _WindDir ("Drift direction (deg)", Range(0, 360)) = 25
        _WindSpeed ("Drift speed (m/s)", Float) = 6
        _DetailFade ("Blotches fade out by (m)", Float) = 25000
        _Fade ("Fade (script)", Range(0, 1)) = 1
        _Darken ("Storm darken (script)", Range(0, 1)) = 0
        _Flash ("Lightning flash (script)", Range(0, 2)) = 0
        _FlashPos ("Lightning XZ (script)", Vector) = (0, 0, 0, 0)
        _FlashRadius ("Lightning glow radius (m)", Float) = 3500
        _FlashColor ("Lightning glow", Color) = (0.85, 0.9, 1, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Back = seen from above, Front = from below)", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-499" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull [_Cull]
        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color, _ShadeColor;
            float _NoiseScale, _Contrast, _WindDir, _WindSpeed, _DetailFade, _Fade, _SoftDistance, _SoftRayDistance;            float _Darken, _Flash, _FlashRadius;
            float4 _FlashPos;
            fixed4 _FlashColor;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _CameraDepthTexture_TexelSize;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 screenPos : TEXCOORD2;
                UNITY_FOG_COORDS(1)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0, a = 0.5;
                for (int k = 0; k < 4; k++)
                {
                    v += a * Noise(p);
                    p = p * 2.03 + float2(17.1, 9.4);
                    a *= 0.5;
                }
                return v / 0.9375;
            }

            float Dither(float2 pix)
            {
                return frac(52.9829189 * frac(dot(pix, float2(0.06711056, 0.00583715))));
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPos = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
                o.pos = UnityWorldToClipPos(o.worldPos);
                o.screenPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.screenPos.z);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(_Fade - Dither(i.pos.xy) - 0.001);
                float2 wind = float2(cos(radians(_WindDir)), sin(radians(_WindDir)));
                float2 uv = (i.worldPos.xz - wind * _WindSpeed * _Time.y) / max(_NoiseScale, 1);
                float n = Fbm(uv);
                float dist = distance(_WorldSpaceCameraPos, i.worldPos);
                float amount = _Contrast * (1 - saturate(dist / max(_DetailFade, 1)));
                fixed4 col = fixed4(lerp(_Color.rgb, _ShadeColor.rgb, saturate((n - 0.3) * 1.8) * amount), 1);
                // 폭풍: 더 어둡게. 번개: 번개 자리 둘레(_FlashRadius)가 안쪽에서 빛나듯 밝아지고(얼룩 따라 고르지 않게) 판 전체도 조금
                col.rgb *= 1 - 0.45 * _Darken;
                float2 fd = i.worldPos.xz - _FlashPos.xz;
                float glow = exp(-dot(fd, fd) / max(_FlashRadius * _FlashRadius, 1)) * (0.6 + 0.8 * n) + 0.25;
                col.rgb += _FlashColor.rgb * (_Flash * glow);

                // 부드러운 경계: 판 바로 뒤 물체까지(시선 방향 m)가 가까울수록 투명 — 얼룩 노이즈로 폭을 흔들어 들쭉날쭉하게.
                // 깊이 텍스처가 없으면(VRChat에서 그림자를 끈 경우 — 크기가 작은 기본 텍스처가 묶인다) 불투명 그대로.
                // 높이로 잰다: 깊이로 판 뒤 물체의 월드 자리를 되살려 판과의 높이 차(m)를 쓴다 — 시선 방향 거리로 재면 비스듬히 볼 때(층 위를 수평으로)
                // 시선이 판을 지나 멀리 산에 닿아 차이가 금방 커져 경계가 딱딱하게 남는다. 시선 방향으로 _SoftRayDistance 넘게 떨어진 물체는 덮는다(멀리 낮은 언덕이 비치지 않게).
                if (_CameraDepthTexture_TexelSize.z > 16)
                {
                    float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                    float fragZ = max(i.screenPos.z, 0.01);
                    float3 scenePos = _WorldSpaceCameraPos + (i.worldPos - _WorldSpaceCameraPos) * (sceneZ / fragZ);
                    float soft = max(_SoftDistance, 0.01) * (0.6 + 0.8 * n);
                    float byHeight = abs(scenePos.y - i.worldPos.y) / soft;
                    float byRay = (sceneZ - fragZ) / max(_SoftRayDistance, 1);
                    float a = saturate(max(min(byHeight, 1), byRay));
                    col.a = a * a * (3 - 2 * a);
                    clip(col.a - 0.02);   // 거의 투명한 자리는 깊이도 안 쓴다(뒤에 그리는 바다·안개가 가려지지 않게)
                }
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}
