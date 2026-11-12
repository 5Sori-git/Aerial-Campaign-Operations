// 비행용 바다(넓은 평면 하나 — 정점 4개 쿼드여도 된다). 모든 무늬를 월드 XZ로 깔아 평면 크기와 상관없다.
//  - 물결: 노멀 세 배율(가까이 _WaveScale1 / 중간 _WaveScale2 / 아주 크게 _WaveScale3)을 서로 다른 방향으로 흘린다 — 고공에서 바둑판 반복이 안 보이게
//    큰 배율 거품 텍스처 G(큰 얼룩)로 물결 세기·색을 넓게 흔든다. 가까운 잔물결은 _DetailFadeStart → _DetailFadeEnd에서 줄인다
//  - 반복 무늬 없애기: 물결 UV를 큰 저주파 무늬로 살짝 비틀고(_WarpScale/_WarpAmount), 중간 물결은 멀어지면 끄고(_MidFadeStart → _MidFadeEnd),
//    먼 곳은 더 큰 배율(_LargeScale)로 넘기며, 큰 얼룩을 배율이 다른 두 겹으로 섞는다. 격자가 같은 배율로 반복되는 것이 하늘에서 보이지 않게.
//  - 먼 바다 빛남 제한: 멀수록(_FarGlowStart → _FarGlowEnd) 안개색 × _FarGlowLimit보다 밝아지지 않는다 — 스쳐 보는 각도의 반사가 수평선을 빛나게 하지 않는다.
//  - 안개: finalcolor를 쓰면 표면 셰이더 자동 안개를 믿을 수 없어 nofog로 끄고 FinalColor에서 직접 입힌다(날씨·구름 안개는 전역 안개라 그대로 따라온다).
//  - 반사·햇빛 반짝임: Standard 조명(리플렉션 프로브/스카이박스 반사 + 해 하이라이트). 멀수록 거칠게(_FarSmoothness) — 반짝임 깜빡임 방지
//  - 색: 내려다보면 깊은 색, 비스듬히 보면 수평선 색(반사가 더해진다). 물결 마루에 흰 거품(_FoamAmount)
//  - (선택) 깊이 텍스처(_UseDepth): 섬 둘레 얕은 물 색·투명도·해안 거품. VRChat에선 그림자를 켠 실시간 방향광이 있어야 깊이 텍스처가 생긴다 — 없으면 끈다
//  - (선택) 가까운 너울(정점 이동): 카메라를 따라다니는 촘촘한 원판 메시(_ACO_SWELL_MESH 머티리얼)가 반경 _SwellRadius 안에서 게르스트너 파도 4개로 출렁이고,
//    평평한 바다(_ACO_SWELL_HOLE 머티리얼)는 같은 원 안을 비운다. 원 가장자리에선 너울이 0으로 잦아들어 두 면이 이음매 없이 이어진다.
//    카메라가 _SwellFadeHeight보다 높으면 너울이 줄어든다(고공에선 안 보인다). 원판은 셰이더가 카메라 위치(VR이면 두 눈 가운데)로 옮긴다 — 스크립트 없음.
//  - 물속에서 본 수면: 평소엔 뒷면을 버리고(_Cull = Back), 물속 연출(ACO_Underwater)이 내 화면의 너울 머티리얼 복사본만 _Cull = Off로 바꾼다.
//    뒷면은 조명 없이 _UnderColor로 스스로 빛난다(바로 위일수록 밝게).
//  - 비(전역 _UdonACORain — ACO_Rain): 가까운 바다(300m)에 빗방울 파문(격자마다 퍼지는 고리 두 겹), 수면을 거칠게 해 해 반짝임을 흐린다(_RainDull).
// 그림자 투사 패스가 없다(깊이 텍스처에 바다 자신이 들어가지 않게). 큐 Transparent-100(불투명 다음, 깊이 씀).
Shader "ACO/Ocean"
{
    Properties
    {
        [Header(Textures)]
        [NoScaleOffset][Normal] _NormalTex ("Wave Normal", 2D) = "bump" {}
        [NoScaleOffset] _FoamTex ("Foam (R foam, G large blotch)", 2D) = "black" {}

        [Header(Waves)]
        _WaveScale1 ("Near wave tile (m)", Float) = 28
        _WaveScale2 ("Mid wave tile (m)", Float) = 140
        _WaveScale3 ("Large swell tile (m)", Float) = 1100
        _WaveSpeed ("Wave speed", Float) = 1
        _WindDir ("Wind direction (deg)", Range(0, 360)) = 25
        _WaveStrength ("Wave strength", Range(0, 2)) = 0.8
        _SwellStrength ("Swell strength", Range(0, 2)) = 0.5
        _DetailFadeStart ("Near waves fade start (m)", Float) = 250
        _DetailFadeEnd ("Near waves gone (m)", Float) = 2500
        _MidFadeStart ("Mid waves fade start (m)", Float) = 3000
        _MidFadeEnd ("Mid waves gone (m)", Float) = 12000
        _LargeScale ("Far swell tile (m)", Float) = 4300
        _WarpScale ("Anti-repeat warp tile (m)", Float) = 2300
        _WarpAmount ("Anti-repeat warp amount", Range(0, 1)) = 0.3
        _BlotchScale ("Large blotch tile (m)", Float) = 6000

        [Header(Color)]
        _DeepColor ("Deep (looking down)", Color) = (0.015, 0.075, 0.13, 1)
        _HorizonColor ("Grazing", Color) = (0.08, 0.2, 0.28, 1)
        _ShallowColor ("Shallow (depth mode)", Color) = (0.06, 0.38, 0.42, 1)
        _BlotchColor ("Color blotch amount", Range(0, 1)) = 0.3
        _Smoothness ("Smoothness near", Range(0, 1)) = 0.95
        _RainDull ("Rain: smoothness loss (dull sun glint)", Range(0, 1)) = 0.6
        _FarSmoothness ("Smoothness far", Range(0, 1)) = 0.82
        _FarDistance ("Far distance (m)", Float) = 8000
        _FarGlowStart ("Far brightness limit start (m)", Float) = 3000
        _FarGlowEnd ("Far brightness limit full (m)", Float) = 15000
        _FarGlowLimit ("Far brightness limit (x fog color)", Range(0.3, 2)) = 1

        [Header(Foam)]
        _FoamColor ("Foam", Color) = (0.9, 0.93, 0.95, 1)
        _FoamAmount ("Whitecaps", Range(0, 1)) = 0.25
        _FoamScale ("Foam tile (m)", Float) = 45
        _FoamFadeStart ("Foam fade start (m)", Float) = 1500
        _FoamFadeEnd ("Foam gone (m)", Float) = 5000

        [Header(Depth optional needs realtime shadows)]
        [Toggle(_ACO_DEPTH)] _UseDepth ("Use depth texture (shallow water, shore foam)", Float) = 0
        _ShallowDepth ("Shallow color depth (m)", Float) = 25
        _ClearDepth ("See-through depth (m)", Float) = 6
        _ShoreFoamWidth ("Shore foam width (m)", Float) = 4

        [Header(Near swell vertex waves set by ACO_OceanLook)]
        [KeywordEnum(Off, Hole, Mesh)] _SwellMode ("Swell role (Hole = flat sea, Mesh = near disk)", Float) = 0
        _SeaY ("Sea level (world Y)", Float) = 0
        _SwellRadius ("Swell radius (m)", Float) = 1200
        _SwellSnap ("Disk snap (m)", Float) = 4
        _SwellScale ("Swell height scale", Range(0, 3)) = 1
        _SwellFadeHeight ("Swell fades above camera height (m)", Float) = 900
        _SwellWave1 ("Swell 1 (dir deg, length m, height m, sharpness)", Vector) = (25, 180, 1.4, 0.7)
        _SwellWave2 ("Swell 2", Vector) = (5, 110, 0.8, 0.6)
        _SwellWave3 ("Swell 3", Vector) = (55, 68, 0.45, 0.5)
        _SwellWave4 ("Swell 4", Vector) = (-20, 37, 0.22, 0.4)

        [Header(Underside seen from below ACO_Underwater turns culling off while underwater)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _UnderColor ("Underside glow", Color) = (0.12, 0.42, 0.5, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 300
        ZWrite On
        Cull [_Cull]

        CGPROGRAM
        #pragma surface surf Standard vertex:vert finalcolor:FinalColor alpha:premul nofog nolightmap nodynlightmap nodirlightmap nometa noshadowmask
        #pragma multi_compile_fog
        #pragma target 3.0
        #pragma shader_feature_local _ACO_DEPTH
        #pragma shader_feature_local _SWELLMODE_OFF _SWELLMODE_HOLE _SWELLMODE_MESH

        sampler2D _NormalTex, _FoamTex;
        float _WaveScale1, _WaveScale2, _WaveScale3, _WaveSpeed, _WindDir, _WaveStrength, _SwellStrength;
        float _DetailFadeStart, _DetailFadeEnd, _BlotchScale;
        float _MidFadeStart, _MidFadeEnd, _LargeScale, _WarpScale, _WarpAmount;
        float _FoamFadeStart, _FoamFadeEnd;
        float _FarGlowStart, _FarGlowEnd, _FarGlowLimit;
        fixed4 _DeepColor, _HorizonColor, _ShallowColor, _FoamColor;
        float _BlotchColor, _Smoothness, _FarSmoothness, _FarDistance, _RainDull;
        float _FoamAmount, _FoamScale;
        float _ShallowDepth, _ClearDepth, _ShoreFoamWidth;
        float _SeaY, _SwellRadius, _SwellSnap, _SwellScale, _SwellFadeHeight;
        float4 _SwellWave1, _SwellWave2, _SwellWave3, _SwellWave4;
        fixed4 _UnderColor;
        // 비(ACO_Rain이 VRChat 셰이더 전역으로 넣는다 — 날씨 비 세기, 0 = 비 없음): 가까운 바다에 빗방울 파문
        float _UdonACORain;
        #ifdef _ACO_DEPTH
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        #endif

        struct Input
        {
            float3 worldPos;
            float4 screenPos;
            float facing : VFACE;   // 뒷면(물속에서 올려다본 수면)이면 음수 — 컬링을 끈 때만 온다
        };

        float2 Rot(float2 d, float deg)
        {
            float a = radians(deg);
            float s = sin(a), c = cos(a);
            return float2(d.x * c - d.y * s, d.x * s + d.y * c);
        }

        float RainHash(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        // 빗방울 파문 한 겹: cell(m) 격자마다 고리 하나가 무작위 때에 퍼져 나간다. 반환 = 노멀 기울기(xz)
        float2 RainRipple(float2 xz, float cell, float t)
        {
            float2 g = xz / cell;
            float2 id = floor(g);
            float h = RainHash(id);
            float2 c = (float2(h, RainHash(id + 3.7)) - 0.5) * 0.6;
            float2 dv = frac(g) - 0.5 - c;
            float r = length(dv);
            float age = frac(t * 0.9 + h * 5.3);            // 0 → 1: 고리가 퍼지며 사라진다
            float front = age * 0.45;
            float ring = sin((r - front) * 45) * smoothstep(0.12, 0, abs(r - front)) * (1 - age);
            return r > 0.001 ? dv / r * ring : float2(0, 0);
        }

        // 너울 원의 중심: 카메라(VR이면 두 눈 가운데 — 눈마다 원이 어긋나지 않게)를 _SwellSnap 격자에 맞춘 XZ
        float2 SwellCenter()
        {
            #if defined(USING_STEREO_MATRICES)
            float3 cam = (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
            #else
            float3 cam = _WorldSpaceCameraPos;
            #endif
            float snap = max(_SwellSnap, 0.01);
            return floor(cam.xz / snap) * snap;
        }

        #if defined(_SWELLMODE_MESH)
        // 게르스트너 파도 하나: w = (방향 도, 파장 m, 높이 m, 날카로움 0~1). 위치 이동과 노멀 기울기를 더한다.
        void Gerstner(float4 w, float2 xz, float t, inout float3 disp, inout float3 nrm)
        {
            float2 d = float2(cos(radians(w.x)), sin(radians(w.x)));
            float k = 6.2832 / max(w.y, 1);
            float c = sqrt(9.8 / k);                 // 깊은 바다 파도 속도
            float a = w.z * _SwellScale;
            float q = saturate(w.w) / max(k * a * 4, 0.0001);   // 날카로움 1 = 네 파도 합이 막 고리를 만들기 직전
            float f = k * (dot(d, xz) - c * t);
            float cf = cos(f), sf = sin(f);
            disp.xz += q * a * d * cf;
            disp.y += a * sf;
            nrm.xz -= d * (k * a * cf);
            nrm.y -= q * k * a * sf;
        }
        #endif

        void vert(inout appdata_full v)
        {
            #if defined(_SWELLMODE_MESH)
            // 원판 메시(로컬 XZ = 중심에서의 m)를 카메라 쪽 해수면으로 옮기고 출렁인다. 오브젝트 위치·회전과 상관없다.
            float2 c = SwellCenter();
            float3 wp = float3(v.vertex.x + c.x, _SeaY, v.vertex.z + c.y);
            #if defined(USING_STEREO_MATRICES)
            float camY = (unity_StereoWorldSpaceCameraPos[0].y + unity_StereoWorldSpaceCameraPos[1].y) * 0.5;
            #else
            float camY = _WorldSpaceCameraPos.y;
            #endif
            float r = length(v.vertex.xz) / max(_SwellRadius, 1);
            float fade = (1 - smoothstep(0.5, 0.92, r)) * (1 - smoothstep(_SwellFadeHeight * 0.6, _SwellFadeHeight, camY - _SeaY));
            float3 disp = 0;
            float3 nrm = float3(0, 1, 0);
            float t = _Time.y * _WaveSpeed;
            Gerstner(_SwellWave1, wp.xz, t, disp, nrm);
            Gerstner(_SwellWave2, wp.xz, t, disp, nrm);
            Gerstner(_SwellWave3, wp.xz, t, disp, nrm);
            Gerstner(_SwellWave4, wp.xz, t, disp, nrm);
            wp += disp * fade;
            nrm = normalize(lerp(float3(0, 1, 0), nrm, fade));
            v.vertex = mul(unity_WorldToObject, float4(wp, 1));
            v.normal = normalize(mul((float3x3)unity_WorldToObject, nrm));
            // 탄젠트 = 노멀에 직교한 월드 +X(노멀 맵 방향이 평평한 바다와 같게)
            float3 tw = normalize(float3(1, 0, 0) - nrm * nrm.x);
            v.tangent = float4(normalize(mul((float3x3)unity_WorldToObject, tw)), -1);
            #endif
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 wp = IN.worldPos;
            float2 xz = wp.xz;

            #if defined(_SWELLMODE_HOLE) || defined(_SWELLMODE_MESH)
            // 평평한 바다는 원 안을, 원판은 원 밖을 버린다 — 경계에서 정확히 맞물린다
            float rr = distance(xz, SwellCenter());
            #if defined(_SWELLMODE_HOLE)
            clip(rr - _SwellRadius);
            #else
            clip(_SwellRadius - rr);
            #endif
            #endif

            float t = _Time.y * _WaveSpeed;
            float2 wind = float2(cos(radians(_WindDir)), sin(radians(_WindDir)));
            float dist = distance(_WorldSpaceCameraPos, wp);
            float nearW = 1 - saturate((dist - _DetailFadeStart) / max(_DetailFadeEnd - _DetailFadeStart, 1));
            float far = saturate(dist / max(_FarDistance, 1));
            float midW = 1 - saturate((dist - _MidFadeStart) / max(_MidFadeEnd - _MidFadeStart, 1));
            float largeW = saturate((dist - _MidFadeStart) / max(_MidFadeEnd * 2 - _MidFadeStart, 1));

            // 큰 얼룩: 물결 세기·색을 넓게 흔든다(고공에서 균일한 판처럼 보이지 않게). 배율이 맞지 않는 두 겹을 섞어 얼룩 자체도 반복되지 않게
            float4 blotch = tex2D(_FoamTex, xz / _BlotchScale + t * 0.0004 * wind);
            float blotch2 = tex2D(_FoamTex, Rot(xz / (_BlotchScale * 2.7), 37)).g;
            float patch = lerp(blotch.g, blotch2, 0.45);
            float rough = lerp(0.6, 1.4, patch);
            // 물결 UV를 살짝 비틀어 타일 격자를 깬다
            float2 warp = float2(tex2D(_FoamTex, Rot(xz / _WarpScale, 71)).g, blotch2) - 0.5;

            // 물결 노멀 세 배율 — 서로 다른 방향·속도로 흘린다(파도는 바람 쪽으로, 빠를수록 짧은 물결)
            float2 uv1 = Rot(xz / _WaveScale1, 7) + wind * t * 0.045;
            float2 uv2 = Rot(xz / _WaveScale2, -31) + Rot(wind, 20) * t * 0.012 + warp * _WarpAmount;
            float2 uv3 = Rot(xz / _WaveScale3, 53) + Rot(wind, -15) * t * 0.0022 + warp * _WarpAmount * 0.6;
            float2 uv4 = Rot(xz / _LargeScale, 139) + Rot(wind, 10) * t * 0.0006 + warp * _WarpAmount * 0.4;
            float3 n1 = UnpackNormal(tex2D(_NormalTex, uv1));
            float3 n2 = UnpackNormal(tex2D(_NormalTex, uv2));
            float3 n3 = UnpackNormal(tex2D(_NormalTex, uv3));
            float3 n4 = UnpackNormal(tex2D(_NormalTex, uv4));
            float2 bump = (n1.xy * nearW + n2.xy * midW) * (_WaveStrength * rough) + lerp(n3.xy, n4.xy, largeW) * (_SwellStrength * lerp(0.55, 1.45, patch));
            // 비: 가까운 바다(300m 안)에 빗방울 파문 두 겹(크기 다르게)
            float rainNear = _UdonACORain * (1 - saturate(dist / 300));
            if (rainNear > 0.001)
            {
                float2 rr = RainRipple(xz, 1.7, _Time.y) + RainRipple(xz + 17.3, 2.9, _Time.y * 0.8 + 0.5) * 0.7;
                bump += rr * (0.35 * rainNear);
            }
            o.Normal = normalize(float3(bump, 1));

            // 색: 내려다보면 깊은 색, 비스듬하면 수평선 색
            float3 v = normalize(_WorldSpaceCameraPos - wp);
            float facing = saturate(v.y);
            float3 col = lerp(_HorizonColor.rgb, _DeepColor.rgb, sqrt(facing));
            col *= lerp(1, 0.75 + 0.5 * patch, _BlotchColor);

            // 거품: 마루(물결 기울기가 큰 곳) × 거품 무늬 × 큰 얼룩. 너울 원판에선 너울 마루(해수면보다 높은 곳)에 조금 더
            float crest = saturate(length(bump) * 1.6);
            #if defined(_SWELLMODE_MESH)
            crest = saturate(crest + saturate((wp.y - _SeaY) / max(_SwellWave1.z * _SwellScale, 0.1)) * 0.35);
            #endif
            float2 fuv = xz / _FoamScale + wind * t * 0.03;
            float fpat = tex2D(_FoamTex, fuv).r * tex2D(_FoamTex, fuv * 0.37 + 0.5).r * 1.6;
            // 멀어지면 끈다 — 거품 무늬가 뭉개지면 먼 바다 전체가 희게 뜬다
            float foamNear = 1 - saturate((dist - _FoamFadeStart) / max(_FoamFadeEnd - _FoamFadeStart, 1));
            float foam = smoothstep(1 - _FoamAmount, 1 - _FoamAmount * 0.5, fpat * crest * patch * 1.4) * foamNear;
            float alpha = 1;

            #ifdef _ACO_DEPTH
            // 물 밑 지형까지 거리(시선 방향 m): 얕을수록 얕은 물 색·투명, 해안선 둘레 거품
            float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(IN.screenPos)));
            float depthDiff = max(sceneZ - IN.screenPos.w, 0);
            float shallow = 1 - saturate(depthDiff / max(_ShallowDepth, 0.01));
            col = lerp(col, _ShallowColor.rgb, shallow * shallow);
            alpha = lerp(0.35, 1, saturate(depthDiff / max(_ClearDepth, 0.01)));
            float shore = 1 - saturate(depthDiff / max(_ShoreFoamWidth, 0.01));
            foam = max(foam, smoothstep(0.35, 0.7, shore * (0.6 + 0.8 * tex2D(_FoamTex, fuv * 2).r)));
            #endif

            col = lerp(col, _FoamColor.rgb, foam);

            if (IN.facing < 0)
            {
                // 물속에서 올려다본 수면: 조명·반사 없이 밝은 물빛으로 스스로 빛난다. 바로 위일수록 밝고, 물결·거품이 조금 비친다(안개는 그 위에 덮인다)
                float up = saturate(-v.y);
                o.Normal = float3(0, 0, -1);
                o.Albedo = 0;
                o.Emission = _UnderColor.rgb * lerp(0.55, 1.25, up) * (0.85 + 0.3 * saturate(length(bump))) + _FoamColor.rgb * foam * 0.25;
                o.Smoothness = 0;
                o.Metallic = 0;
                o.Alpha = 1;
                return;
            }

            o.Albedo = col;
            // 비가 오면 수면이 거칠어 해 반짝임이 흐려진다(빗방울 · 흐린 하늘 — 비 0.85 · 폭풍 1에서 _RainDull만큼, 예전엔 8%라 흐린 날에도 해 반사가 밝게 남았다)
            o.Smoothness = lerp(lerp(_Smoothness, _FarSmoothness, far), 0.3, foam) * (1 - _RainDull * _UdonACORain);
            o.Metallic = 0;
            o.Alpha = alpha;
        }

        // 먼 바다는 안개색보다 밝아지지 않는다(안개색을 따르므로 날씨가 어두워지면 함께 내려간다). 그 뒤에 안개를 입힌다
        void FinalColor(Input IN, SurfaceOutputStandard o, inout fixed4 color)
        {
            #ifndef UNITY_PASS_FORWARDADD
            float dist = distance(_WorldSpaceCameraPos, IN.worldPos);
            float k = saturate((dist - _FarGlowStart) / max(_FarGlowEnd - _FarGlowStart, 1));
            color.rgb = lerp(color.rgb, min(color.rgb, unity_FogColor.rgb * _FarGlowLimit), k);
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
            UNITY_CALC_FOG_FACTOR_RAW(dist);
            // 색이 알파를 곱한 값이라 안개색에도 알파를 곱한다
            color.rgb = lerp(unity_FogColor.rgb * color.a, color.rgb, saturate(unityFogFactor));
            #endif
            #endif
        }
        ENDCG
    }
    FallBack Off
}
