// 비행용 바다(넓은 평면 하나 — 정점 4개 쿼드여도 된다). 모든 무늬를 월드 XZ로 깔아 평면 크기와 상관없다.
//  - 물결: 노멀 세 배율(가까이 _WaveScale1 / 중간 _WaveScale2 / 아주 크게 _WaveScale3)을 서로 다른 방향으로 흘린다 — 고공에서 바둑판 반복이 안 보이게
//    큰 배율 거품 텍스처 G(큰 얼룩)로 물결 세기·색을 넓게 흔든다. 가까운 잔물결은 _DetailFadeStart → _DetailFadeEnd에서 줄인다
//  - 반사·햇빛 반짝임: Standard 조명(리플렉션 프로브/스카이박스 반사 + 해 하이라이트). 멀수록 거칠게(_FarSmoothness) — 반짝임 깜빡임 방지
//  - 색: 내려다보면 깊은 색, 비스듬히 보면 수평선 색(반사가 더해진다). 물결 마루에 흰 거품(_FoamAmount)
//  - (선택) 깊이 텍스처(_UseDepth): 섬 둘레 얕은 물 색·투명도·해안 거품. VRChat에선 그림자를 켠 실시간 방향광이 있어야 깊이 텍스처가 생긴다 — 없으면 끈다
//  - (선택) 가까운 너울(정점 이동): 카메라를 따라다니는 촘촘한 원판 메시(_ACO_SWELL_MESH 머티리얼)가 반경 _SwellRadius 안에서 게르스트너 파도 4개로 출렁이고,
//    평평한 바다(_ACO_SWELL_HOLE 머티리얼)는 같은 원 안을 비운다. 원 가장자리에선 너울이 0으로 잦아들어 두 면이 이음매 없이 이어진다.
//    카메라가 _SwellFadeHeight보다 높으면 너울이 줄어든다(고공에선 안 보인다). 원판은 셰이더가 카메라 위치(VR이면 두 눈 가운데)로 옮긴다 — 스크립트 없음.
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
        _BlotchScale ("Large blotch tile (m)", Float) = 6000

        [Header(Color)]
        _DeepColor ("Deep (looking down)", Color) = (0.015, 0.075, 0.13, 1)
        _HorizonColor ("Grazing", Color) = (0.08, 0.2, 0.28, 1)
        _ShallowColor ("Shallow (depth mode)", Color) = (0.06, 0.38, 0.42, 1)
        _BlotchColor ("Color blotch amount", Range(0, 1)) = 0.3
        _Smoothness ("Smoothness near", Range(0, 1)) = 0.95
        _FarSmoothness ("Smoothness far", Range(0, 1)) = 0.82
        _FarDistance ("Far distance (m)", Float) = 8000

        [Header(Foam)]
        _FoamColor ("Foam", Color) = (0.9, 0.93, 0.95, 1)
        _FoamAmount ("Whitecaps", Range(0, 1)) = 0.25
        _FoamScale ("Foam tile (m)", Float) = 45

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
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 300
        ZWrite On

        CGPROGRAM
        #pragma surface surf Standard vertex:vert alpha:premul nolightmap nodynlightmap nodirlightmap nometa noshadowmask
        #pragma target 3.0
        #pragma shader_feature_local _ACO_DEPTH
        #pragma shader_feature_local _SWELLMODE_OFF _SWELLMODE_HOLE _SWELLMODE_MESH

        sampler2D _NormalTex, _FoamTex;
        float _WaveScale1, _WaveScale2, _WaveScale3, _WaveSpeed, _WindDir, _WaveStrength, _SwellStrength;
        float _DetailFadeStart, _DetailFadeEnd, _BlotchScale;
        fixed4 _DeepColor, _HorizonColor, _ShallowColor, _FoamColor;
        float _BlotchColor, _Smoothness, _FarSmoothness, _FarDistance;
        float _FoamAmount, _FoamScale;
        float _ShallowDepth, _ClearDepth, _ShoreFoamWidth;
        float _SeaY, _SwellRadius, _SwellSnap, _SwellScale, _SwellFadeHeight;
        float4 _SwellWave1, _SwellWave2, _SwellWave3, _SwellWave4;
        #ifdef _ACO_DEPTH
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        #endif

        struct Input
        {
            float3 worldPos;
            float4 screenPos;
        };

        float2 Rot(float2 d, float deg)
        {
            float a = radians(deg);
            float s = sin(a), c = cos(a);
            return float2(d.x * c - d.y * s, d.x * s + d.y * c);
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

            // 큰 얼룩: 물결 세기·색을 넓게 흔든다(고공에서 균일한 판처럼 보이지 않게)
            float4 blotch = tex2D(_FoamTex, xz / _BlotchScale + t * 0.0004 * wind);
            float rough = lerp(0.6, 1.4, blotch.g);

            // 물결 노멀 세 배율 — 서로 다른 방향·속도로 흘린다(파도는 바람 쪽으로, 빠를수록 짧은 물결)
            float2 uv1 = Rot(xz / _WaveScale1, 7) + wind * t * 0.045;
            float2 uv2 = Rot(xz / _WaveScale2, -31) + Rot(wind, 20) * t * 0.012;
            float2 uv3 = Rot(xz / _WaveScale3, 53) + Rot(wind, -15) * t * 0.0022;
            float3 n1 = UnpackNormal(tex2D(_NormalTex, uv1));
            float3 n2 = UnpackNormal(tex2D(_NormalTex, uv2));
            float3 n3 = UnpackNormal(tex2D(_NormalTex, uv3));
            float2 bump = (n1.xy * nearW + n2.xy) * (_WaveStrength * rough) + n3.xy * _SwellStrength;
            o.Normal = normalize(float3(bump, 1));

            // 색: 내려다보면 깊은 색, 비스듬하면 수평선 색
            float3 v = normalize(_WorldSpaceCameraPos - wp);
            float facing = saturate(v.y);
            float3 col = lerp(_HorizonColor.rgb, _DeepColor.rgb, sqrt(facing));
            col *= lerp(1, 0.75 + 0.5 * blotch.g, _BlotchColor);

            // 거품: 마루(물결 기울기가 큰 곳) × 거품 무늬 × 큰 얼룩. 너울 원판에선 너울 마루(해수면보다 높은 곳)에 조금 더
            float crest = saturate(length(bump) * 1.6);
            #if defined(_SWELLMODE_MESH)
            crest = saturate(crest + saturate((wp.y - _SeaY) / max(_SwellWave1.z * _SwellScale, 0.1)) * 0.35);
            #endif
            float2 fuv = xz / _FoamScale + wind * t * 0.03;
            float fpat = tex2D(_FoamTex, fuv).r * tex2D(_FoamTex, fuv * 0.37 + 0.5).r * 1.6;
            float foam = smoothstep(1 - _FoamAmount, 1 - _FoamAmount * 0.5, fpat * crest * blotch.g * 1.4) * lerp(1, 0.5, far);
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
            o.Albedo = col;
            o.Smoothness = lerp(lerp(_Smoothness, _FarSmoothness, far), 0.3, foam);
            o.Metallic = 0;
            o.Alpha = alpha;
        }
        ENDCG
    }
    FallBack Off
}
