// 육각 기둥 지형(ACO_HexTerrain)을 자연스럽게: 모든 색을 월드 좌표(높이·경사·위치)로 정한다 — 기둥마다 머티리얼이 갈리지 않아
// 육각 경계에서 색이 끊기지 않고, 높이대 경계는 노이즈로 들쭉날쭉하게 섞인다.
//  - 높이: 해안 모래(젖은 모래) → 풀(초원/마른 풀/숲, 매크로 노이즈로 분포) → 흙 → 바위 → 눈
//  - 경사: 수직 벽 = 절벽(지층 줄무늬), 경사 치마 = 약간 흙빛(_SlopeDirt)
//  - 디테일: 가까이(_DetailScale)와 멀리(_DetailScale2) 두 배율로 섞어 반복 무늬를 숨기고, 멀어지면 잔결을 줄인다
//  - 노멀: 디테일 노멀 두 배율 + 경사 치마를 위쪽으로 눕혀(_SlopeFlatten) 윗면과 치마 사이 음영 꺾임(육각 윤곽)을 줄인다
// 메시에 UV·탄젠트가 없어도 된다(정점 함수가 탄젠트를 만든다 — 기둥은 회전 없이 놓인다). GPU 인스턴싱 켬.
Shader "ACO/HexTerrain"
{
    Properties
    {
        [Header(Textures)]
        [NoScaleOffset] _DetailTex ("Detail (R grain, G blotch, B crack, A clump)", 2D) = "gray" {}
        [NoScaleOffset][Normal] _DetailNormal ("Detail Normal", 2D) = "bump" {}
        [NoScaleOffset] _MacroTex ("Macro (R bright, G dry, B forest, A edge)", 2D) = "gray" {}
        _DetailScale ("Detail tile size near (m)", Float) = 40
        _DetailScale2 ("Detail tile size far (m)", Float) = 420
        _MacroScale ("Macro tile size (m)", Float) = 9000
        _DetailFadeStart ("Fine detail fade start (m)", Float) = 400
        _DetailFadeEnd ("Fine detail fade end (m)", Float) = 3000

        [Header(Heights m above sea)]
        _SeaLevel ("Sea level (world Y)", Float) = 0
        _SandHeight ("Beach top", Float) = 30
        _DirtHeight ("Dirt start", Float) = 480
        _RockHeight ("Rock start", Float) = 700
        _SnowHeight ("Snow start", Float) = 1000
        _BlendNoise ("Boundary jitter (m)", Float) = 120
        _BlendWidth ("Boundary softness (m)", Float) = 60

        [Header(Biome)]
        _DryAmount ("Dry grass amount", Range(0, 1)) = 0.4
        _ForestAmount ("Forest amount", Range(0, 1)) = 0.45
        _SlopeDirt ("Dirt on slopes", Range(0, 1)) = 0.15
        _CliffSlope ("Cliff below normal.y", Range(0.1, 0.9)) = 0.5

        [Header(Colors)]
        _WetSandColor ("Wet sand", Color) = (0.55, 0.5, 0.38, 1)
        _SandColor ("Sand", Color) = (0.78, 0.72, 0.55, 1)
        _GrassColor ("Grass", Color) = (0.3, 0.42, 0.2, 1)
        _DryGrassColor ("Dry grass", Color) = (0.55, 0.52, 0.3, 1)
        _ForestColor ("Forest", Color) = (0.13, 0.22, 0.1, 1)
        _DirtColor ("Dirt", Color) = (0.42, 0.34, 0.24, 1)
        _RockColor ("Rock", Color) = (0.45, 0.43, 0.4, 1)
        _CliffColor ("Cliff", Color) = (0.4, 0.36, 0.32, 1)
        _SnowColor ("Snow", Color) = (0.93, 0.94, 0.97, 1)
        _StrataScale ("Cliff strata spacing (m)", Float) = 40

        [Header(Surface)]
        _DetailStrength ("Albedo detail", Range(0, 1)) = 0.6
        _NormalStrength ("Normal near", Range(0, 2)) = 0.7
        _NormalStrength2 ("Normal far", Range(0, 2)) = 0.5
        _SlopeFlatten ("Flatten slope shading (hide hex edges)", Range(0, 1)) = 0.6
        _Smoothness ("Smoothness", Range(0, 1)) = 0.05
        _SnowSmoothness ("Snow smoothness", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #pragma instancing_options assumeuniformscaling

        sampler2D _DetailTex, _DetailNormal, _MacroTex;
        float _DetailScale, _DetailScale2, _MacroScale, _DetailFadeStart, _DetailFadeEnd;
        float _SeaLevel, _SandHeight, _DirtHeight, _RockHeight, _SnowHeight, _BlendNoise, _BlendWidth;
        float _DryAmount, _ForestAmount, _SlopeDirt, _CliffSlope;
        fixed4 _WetSandColor, _SandColor, _GrassColor, _DryGrassColor, _ForestColor, _DirtColor, _RockColor, _CliffColor, _SnowColor;
        float _StrataScale;
        float _DetailStrength, _NormalStrength, _NormalStrength2, _SlopeFlatten, _Smoothness, _SnowSmoothness;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        // 윗면·치마(위를 보는 면) = XZ 평면 투영, 벽 = 수평 방향 × 높이 투영. 탄젠트(+U)·바이탄젠트(+V)를 그 투영 축에 맞춘다.
        void vert(inout appdata_full v)
        {
            float3 n = normalize(v.normal);
            float3 t;
            float w;
            if (n.y > 0.5)
            {
                t = float3(1, 0, 0);
                w = -1;   // 바이탄젠트 = cross(n, t) * w = +Z
            }
            else
            {
                t = normalize(cross(float3(0, 1, 0), n));
                w = 1;    // 바이탄젠트 = +Y
            }
            t = normalize(t - n * dot(t, n));
            v.tangent = float4(t, w);
        }

        float2 ProjectUV(float3 wp, float3 wn)
        {
            if (wn.y > 0.5) { return wp.xz; }
            float3 t = normalize(cross(float3(0, 1, 0), wn));
            return float2(dot(wp, t), wp.y);
        }

        float Band(float h, float edge, float width)
        {
            return smoothstep(edge - width, edge + width, h);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 wp = IN.worldPos;
            float3 wn = normalize(WorldNormalVector(IN, float3(0, 0, 1)));
            float h = wp.y - _SeaLevel;
            float dist = distance(_WorldSpaceCameraPos, wp);
            float fine = 1 - saturate((dist - _DetailFadeStart) / max(_DetailFadeEnd - _DetailFadeStart, 1));

            float2 uv = ProjectUV(wp, wn);
            float4 d1 = tex2D(_DetailTex, uv / _DetailScale);
            float4 d2 = tex2D(_DetailTex, uv / _DetailScale2 + 0.37);
            float4 m1 = tex2D(_MacroTex, wp.xz / _MacroScale);
            float4 m2 = tex2D(_MacroTex, wp.xz / (_MacroScale * 0.27) + 0.61);

            // 높이대 경계를 흔든다(큰 들쭉날쭉 + 작은 결)
            float hj = h + (m2.a - 0.5) * 2 * _BlendNoise + (d2.g - 0.5) * _BlendNoise * 0.5;
            float ny = wn.y;
            float cliff = 1 - smoothstep(_CliffSlope - 0.1, _CliffSlope + 0.1, ny);
            float slope = (1 - smoothstep(0.985, 0.999, ny)) * (1 - cliff);   // 경사 치마
            float flatTop = smoothstep(0.7, 0.9, ny);

            // 풀: 초원 ↔ 마른 풀(메마름 노이즈), 숲은 덩어리지게
            float dry = smoothstep(1 - _DryAmount - 0.15, 1 - _DryAmount + 0.15, m1.g + (d2.g - 0.5) * 0.25);
            float forest = smoothstep(1 - _ForestAmount - 0.08, 1 - _ForestAmount + 0.08, m1.b + (d2.a - 0.5) * 0.35)
                * (1 - Band(hj, _DirtHeight * 0.85, _BlendWidth)) * flatTop;
            float3 col = lerp(_GrassColor.rgb, _DryGrassColor.rgb, dry * 0.85);
            float3 forestCol = _ForestColor.rgb * lerp(1, 0.7 + 0.6 * d1.a, fine);
            col = lerp(col, forestCol, forest);

            // 흙 → 바위 → 모래 → 눈
            col = lerp(col, _DirtColor.rgb, saturate(Band(hj, _DirtHeight, _BlendWidth) * 0.7 + slope * _SlopeDirt));
            float3 rockCol = _RockColor.rgb * (0.8 + 0.4 * d2.b) * lerp(1, 0.85 + 0.3 * d1.b, fine);
            col = lerp(col, rockCol, Band(hj, _RockHeight, _BlendWidth));
            float beach = 1 - Band(h + (m2.a - 0.5) * 20 + (d1.g - 0.5) * 8, _SandHeight, 8);
            float3 sandCol = lerp(_WetSandColor.rgb, _SandColor.rgb, smoothstep(1, 6, h));
            col = lerp(col, sandCol, beach);
            float snow = Band(hj + (d1.r - 0.5) * 60, _SnowHeight, _BlendWidth) * smoothstep(0.6, 0.8, ny);
            col = lerp(col, _SnowColor.rgb, snow);

            // 절벽: 높이 줄무늬(지층) + 균열
            float strata = sin(wp.y / _StrataScale * 6.2832 + (m2.r - 0.5) * 8 + d2.b * 2) * 0.5 + 0.5;
            float3 cliffCol = _CliffColor.rgb * (0.75 + 0.35 * strata) * (0.8 + 0.35 * d2.b) * lerp(1, 0.85 + 0.3 * d1.b, fine);
            col = lerp(col, cliffCol, cliff);

            // 잔결·얼룩·큰 밝기 변화
            float grain = lerp(1, 0.75 + 0.5 * d1.r, _DetailStrength * fine);
            float blotch = lerp(1, 0.85 + 0.3 * d2.r, _DetailStrength);
            col *= grain * blotch * (0.88 + 0.24 * m1.r);
            // 물속(벽 아랫부분)은 어둡게
            col *= lerp(1, 0.45, saturate(-h / 60));

            // 노멀: 경사 치마를 위쪽으로 눕히고, 두 배율 디테일 노멀을 얹는다
            float3 tsUp = float3(dot(float3(0, 1, 0), WorldNormalVector(IN, float3(1, 0, 0))),
                                 dot(float3(0, 1, 0), WorldNormalVector(IN, float3(0, 1, 0))),
                                 ny);
            float3 baseN = normalize(lerp(float3(0, 0, 1), normalize(tsUp), _SlopeFlatten * slope));
            float3 n1 = UnpackNormal(tex2D(_DetailNormal, uv / _DetailScale));
            float3 n2 = UnpackNormal(tex2D(_DetailNormal, uv / _DetailScale2 + 0.37));
            float2 bump = n1.xy * (_NormalStrength * fine) + n2.xy * _NormalStrength2;
            bump *= 1 - snow * 0.6;
            o.Normal = normalize(float3(baseN.xy + bump, baseN.z));

            o.Albedo = saturate(col);
            float wet = beach * (1 - smoothstep(1, 6, h));
            o.Smoothness = max(max(_Smoothness, _SnowSmoothness * snow), 0.35 * wet);
            o.Metallic = 0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
