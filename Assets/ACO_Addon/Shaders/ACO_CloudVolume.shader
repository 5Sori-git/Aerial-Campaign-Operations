// 체적 구름층(비구름층·낮은 안개층). 판을 그리지 않고, 픽셀마다 시선이 층(_SlabBottom ~ _SlabTop)을 지나는 길이로 진하기를 계산한다(1 - exp(-밀도 × 길이)).
// 카메라가 층에 들어가도 값이 끊기지 않아 경계를 지날 때 튀지 않는다 — 아래에서는 천장, 안에서는 안개, 위에서는 구름 바다가 같은 식에서 나온다.
//  - 시선은 깊이 텍스처로 잰 물체, 바다(_SeaY)에서 멈춘다 — 층 앞의 기체·조종석은 덮지 않고, 층 속 산은 거리만큼 묻힌다.
//    깊이 텍스처에는 그림자 패스가 있는 물체만 들어간다 — 스크린(Unlit)·그림자를 끈 물체는 없는 것으로 보여 그 뒤의 거리만큼 안개가 덮인다.
//    그래서 구를 카메라에서 _NearRadius 떨어뜨리고 실제 깊이 버퍼로 검사한다: 그 안의 불투명한 물체(조종석·기체·격납고·스크린)는 무엇이든 덮이지 않는다.
//    깊이 텍스처가 없는 경우(VRChat에서 그림자를 끈 플레이어)도 같은 이유로 가까운 물체는 안전하다.
//    구 안쪽 경계가 보이지 않도록 진하기는 _NearRadius → _NearFade에서 0부터 올라간다.
//  - 윗면·아랫면 높이와 진하기, 색을 큰 노이즈로 흔든다(값 노이즈 3옥타브). 색은 카메라 높이로 아래 색 → 위 색.
//  - 전체 화면 패스라 층이 켜져 있으면 화면 전체 픽셀에서 노이즈를 계산한다. 층이 없는 날씨에는 스크립트가 끈다.
//  - 큐 Transparent-50: 바다(Transparent-100) 다음. 판 위치는 카메라에 붙인다(머티리얼 스케일과 상관없다).
Shader "ACO/CloudVolume"
{
    Properties
    {
        _SlabBottom ("Layer bottom (script)", Float) = 1300
        _SlabTop ("Layer top (script)", Float) = 1800
        _SeaY ("Sea level", Float) = 0
        _Density ("Density per m (script)", Float) = 0.005
        _Fade ("Fade (script)", Range(0, 1)) = 0
        _Bump ("Top and bottom height variation (m)", Float) = 120
        _TopColor ("Seen from above (script)", Color) = (0.95, 0.96, 0.98, 1)
        _BottomColor ("Seen from below (script)", Color) = (0.55, 0.58, 0.63, 1)
        _NoiseScale ("Blotch tile (m)", Float) = 1800
        _Contrast ("Blotch amount", Range(0, 1)) = 0.5
        _WindDir ("Drift direction (deg)", Range(0, 360)) = 25
        _WindSpeed ("Drift speed (m/s)", Float) = 6
        _MaxDistance ("Max distance (m)", Float) = 60000
        _NearRadius ("Never cover closer than (m)", Float) = 40
        _NearFade ("Full effect from (m)", Float) = 90
        _Darken ("Storm darken (script)", Range(0, 1)) = 0
        _Flash ("Lightning flash (script)", Range(0, 2)) = 0
        _FlashPos ("Lightning XZ (script)", Vector) = (0, 0, 0, 0)
        _FlashRadius ("Lightning glow radius (m)", Float) = 3500
        _FlashColor ("Lightning glow", Color) = (0.85, 0.9, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Front
        ZWrite Off
        ZTest LEqual
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

            float _SlabBottom, _SlabTop, _SeaY, _Density, _Fade, _Bump, _NoiseScale, _Contrast, _WindDir, _WindSpeed, _MaxDistance;
            float _Darken, _Flash, _FlashRadius, _NearRadius, _NearFade;
            float4 _FlashPos;
            fixed4 _TopColor, _BottomColor, _FlashColor;
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
                float3 dir : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
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
                for (int k = 0; k < 3; k++)
                {
                    v += a * Noise(p);
                    p = p * 2.03 + float2(17.1, 9.4);
                    a *= 0.5;
                }
                return v / 0.875;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.dir = normalize(v.vertex.xyz);
                o.pos = UnityWorldToClipPos(_WorldSpaceCameraPos + o.dir * _NearRadius);
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(i.dir);

                // 시선이 멈추는 곳: 물체, 바다
                float tEnd = _MaxDistance;
                if (_CameraDepthTexture_TexelSize.z > 16)
                {
                    float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                    float3 forward = -UNITY_MATRIX_V[2].xyz;
                    tEnd = min(tEnd, sceneZ / max(dot(rd, forward), 0.001));
                }
                if (rd.y < -0.0001)
                {
                    float tSea = (_SeaY - ro.y) / rd.y;
                    if (tSea > 0) { tEnd = min(tEnd, tSea); }
                }

                // 노이즈는 시선이 층에 닿는 자리(층 속이면 카메라 높이)에서 잰다
                float yRef = clamp(ro.y, _SlabBottom, _SlabTop);
                float tRef = abs(rd.y) > 0.0001 ? clamp((yRef - ro.y) / rd.y, 0, _MaxDistance) : 0;
                float2 wind = float2(cos(radians(_WindDir)), sin(radians(_WindDir)));
                float2 uv = ((ro + rd * tRef).xz - wind * _WindSpeed * _Time.y) / max(_NoiseScale, 1);
                float n = Fbm(uv);

                // 시선이 층 안에 있는 구간
                float bump = (n - 0.5) * _Bump;
                float y0 = _SlabBottom - bump;
                float y1 = _SlabTop + bump;
                float t0 = 0;
                float t1 = (ro.y > y0 && ro.y < y1) ? _MaxDistance : 0;
                if (abs(rd.y) > 0.0001)
                {
                    float ta = (y0 - ro.y) / rd.y;
                    float tb = (y1 - ro.y) / rd.y;
                    t0 = max(min(ta, tb), 0);
                    t1 = max(ta, tb);
                }
                t1 = min(t1, tEnd);
                float len = t1 - t0;
                if (len <= 0) { discard; }
                float alpha = (1 - exp(-_Density * len * lerp(0.7, 1.3, n))) * _Fade;
                alpha *= smoothstep(_NearRadius, max(_NearFade, _NearRadius + 1), tEnd);

                // 색: 층 아래 → 위. 폭풍은 아래쪽만 더 어둡게
                float h = saturate((ro.y - _SlabBottom) / max(_SlabTop - _SlabBottom, 1));
                h = h * h * (3 - 2 * h);
                float3 col = lerp(_BottomColor.rgb, _TopColor.rgb, h);
                col *= lerp(1, 0.82 + 0.36 * n, _Contrast);
                col *= 1 - 0.45 * _Darken * (1 - h);

                // 층까지의 공기: 지금 안개
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                UNITY_CALC_FOG_FACTOR_RAW(t0);
                col = lerp(unity_FogColor.rgb, col, saturate(unityFogFactor));
                #endif

                // 번개: 번개 자리 둘레가 안쪽에서 빛난다
                float2 fd = (ro + rd * t0).xz - _FlashPos.xz;
                col += _FlashColor.rgb * (_Flash * (exp(-dot(fd, fd) / max(_FlashRadius * _FlashRadius, 1)) * (0.6 + 0.8 * n) + 0.25));
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
