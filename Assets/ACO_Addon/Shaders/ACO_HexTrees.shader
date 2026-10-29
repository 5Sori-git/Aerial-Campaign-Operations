// 육각 기둥 윗면의 나무 묶음(ACO_HexColumn 자식 "Trees"). 메시는 나무 수백 그루 자리(정점마다 그 나무의 밑동 = UV0)를 미리 깔아 두고,
// 정점 셰이더가 그루마다 정한다 — 지면 셰이더(ACO/HexTerrain)와 같은 숲 분포 노이즈로 숲이면 세우고 아니면 크기 0(사라짐).
//  - 무작위(크기·회전·위치 흔들기·종류)는 월드 위치 해시라 기둥마다 배치가 달라 보인다
//  - 종류: 높을수록 침엽수(_ConiferStart → _ConiferFull), 낮으면 활엽수 — 같은 정점 구조를 두 모양(침엽수 = 정점, 활엽수 = UV2/UV3) 사이에서 바꾼다
//  - 해안 모래·흙/바위 높이에서는 안 서고, 숲 가장자리는 작고 성기게, 숲 밖 풀밭엔 가끔 외딴 나무(_LoneTrees)
//  - 멀어지면(_FadeStart → _FadeEnd) 작아지며 사라진다(그 너머는 기둥 프리팹의 LOD Group이 아예 안 그린다)
// 지면 머티리얼과 같은 값(텍스처·배율·높이·숲 양)을 써야 숲 색과 나무 자리가 맞는다 — ACO_TerrainLook이 적용할 때 복사한다.
// 그림자는 렌더러에서 끈다(나무 수천 그루 그림자는 비싸다). 텍스처 없이 정점 색 + 색 속성만.
Shader "ACO/HexTrees"
{
    Properties
    {
        [Header(Shared with ACO HexTerrain material)]
        [NoScaleOffset] _DetailTex ("Detail", 2D) = "gray" {}
        [NoScaleOffset] _MacroTex ("Macro", 2D) = "gray" {}
        _DetailScale2 ("Detail tile size far (m)", Float) = 420
        _MacroScale ("Macro tile size (m)", Float) = 9000
        _SeaLevel ("Sea level (world Y)", Float) = 0
        _SandHeight ("Beach top", Float) = 30
        _DirtHeight ("Dirt start", Float) = 480
        _BlendNoise ("Boundary jitter (m)", Float) = 120
        _BlendWidth ("Boundary softness (m)", Float) = 60
        _ForestAmount ("Forest amount", Range(0, 1)) = 0.45
        _DryAmount ("Dry grass amount", Range(0, 1)) = 0.4

        [Header(Trees)]
        _ForestDensity ("Forest density (share of slots)", Range(0, 1)) = 0.9
        _LoneTrees ("Lone trees on grass", Range(0, 0.3)) = 0.04
        _ConiferHeight ("Conifer height (m)", Float) = 22
        _BroadleafHeight ("Broadleaf height (m)", Float) = 15
        _SizeJitter ("Size jitter", Range(0, 0.6)) = 0.3
        _Jitter ("Position jitter (m)", Float) = 14
        _ConiferStart ("Conifers start (m)", Float) = 150
        _ConiferFull ("All conifers above (m)", Float) = 420
        _FadeStart ("Shrink start (m from camera)", Float) = 1800
        _FadeEnd ("Gone at (m from camera)", Float) = 2600
        _Wind ("Wind sway", Range(0, 1)) = 0.3

        [Header(Colors)]
        _ConiferColor ("Conifer", Color) = (0.1, 0.2, 0.11, 1)
        _BroadleafColor ("Broadleaf", Color) = (0.2, 0.32, 0.12, 1)
        _DryLeafColor ("Dry leaves", Color) = (0.45, 0.42, 0.18, 1)
        _TrunkColor ("Trunk", Color) = (0.25, 0.18, 0.12, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert noforwardadd nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.0
        #pragma instancing_options assumeuniformscaling

        sampler2D _DetailTex, _MacroTex;
        float _DetailScale2, _MacroScale, _SeaLevel, _SandHeight, _DirtHeight, _BlendNoise, _BlendWidth, _ForestAmount, _DryAmount;
        float _ForestDensity, _LoneTrees, _ConiferHeight, _BroadleafHeight, _SizeJitter, _Jitter, _ConiferStart, _ConiferFull;
        float _FadeStart, _FadeEnd, _Wind;
        fixed4 _ConiferColor, _BroadleafColor, _DryLeafColor, _TrunkColor;

        struct Input
        {
            float4 tree;   // x = 침엽수(1)/활엽수(0), y = 밝기 무작위, z = 메마름 노이즈
            float4 vcol;   // r = 아래쪽 어둡게(AO), a = 잎(1)/줄기(0)
        };

        // 큰 좌표(수십 km)에서도 고른 해시(sin 해시는 정밀도가 떨어진다)
        float Hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 base = float3(v.texcoord.x, 0, v.texcoord.y);
            float3 local = v.vertex.xyz - base;   // 침엽수 모양(높이 1 단위)
            float3 wBase = mul(unity_ObjectToWorld, float4(base, 1)).xyz;
            float2 key = floor(wBase.xz * 4) * 0.25;   // 같은 나무의 정점은 같은 값
            float r1 = Hash12(key);
            float r2 = Hash12(key + 17.31);
            float r3 = Hash12(key + 41.97);
            float r4 = Hash12(key + 73.13);

            // 자리 흔들기(기둥마다 같은 격자로 보이지 않게)
            float2 jit = (float2(r1, r4) - 0.5) * _Jitter;
            base.xz += jit;
            wBase.xz += jit;

            // 지면 셰이더와 같은 숲 분포
            float h = wBase.y - _SeaLevel;
            float4 m1 = tex2Dlod(_MacroTex, float4(wBase.xz / _MacroScale, 0, 0));
            float4 m2 = tex2Dlod(_MacroTex, float4(wBase.xz / (_MacroScale * 0.27) + 0.61, 0, 0));
            float4 d2 = tex2Dlod(_DetailTex, float4(wBase.xz / _DetailScale2 + 0.37, 0, 0));
            float hj = h + (m2.a - 0.5) * 2 * _BlendNoise + (d2.g - 0.5) * _BlendNoise * 0.5;
            float forest = smoothstep(1 - _ForestAmount - 0.08, 1 - _ForestAmount + 0.08, m1.b + (d2.a - 0.5) * 0.35)
                * (1 - smoothstep(_DirtHeight * 0.85 - _BlendWidth, _DirtHeight * 0.85 + _BlendWidth, hj));
            float land = smoothstep(_SandHeight + 5, _SandHeight + 25, h + (m2.a - 0.5) * 20);
            float alpine = 1 - smoothstep(_DirtHeight - _BlendWidth, _DirtHeight + _BlendWidth, hj);

            // 숲: 짙을수록 많이(가장자리는 성기고 작게) / 숲 밖: 가끔 외딴 나무
            float inForest = step(r3, forest * _ForestDensity);
            float lone = step(1 - _LoneTrees, r3) * (1 - inForest);
            float size = (inForest * lerp(0.6, 1, saturate(forest * 2)) + lone) * land * alpine;
            float dist = distance(_WorldSpaceCameraPos, wBase);
            size *= 1 - smoothstep(_FadeStart, _FadeEnd, dist);

            float conifer = step(r2, smoothstep(_ConiferStart, _ConiferFull, hj));
            float H = lerp(_BroadleafHeight, _ConiferHeight, conifer) * (1 + (r1 - 0.5) * 2 * _SizeJitter) * size;

            float3 shape = lerp(v.texcoord2.xyz, local, conifer);
            float3 nrm = lerp(v.texcoord3.xyz, v.normal, conifer);
            float widen = 0.85 + 0.3 * r4;
            shape.xz *= widen;
            // 바람: 위로 갈수록 크게
            float sway = sin(_Time.y * 1.6 + r1 * 40) * _Wind * 0.03 * shape.y * shape.y;
            shape.x += sway;
            // 회전
            float a = r1 * 6.2832;
            float s = sin(a), c = cos(a);
            shape.xz = float2(shape.x * c - shape.z * s, shape.x * s + shape.z * c);
            nrm.xz = float2(nrm.x * c - nrm.z * s, nrm.x * s + nrm.z * c);

            v.vertex.xyz = base + shape * H;   // H = 0이면 모든 정점이 밑동에 모여 안 그려진다
            v.normal = normalize(nrm);
            o.tree = float4(conifer, r4, m1.g, 0);
            o.vcol = v.color;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 leaf = lerp(_BroadleafColor.rgb, _ConiferColor.rgb, IN.tree.x);
            float dry = smoothstep(1 - _DryAmount - 0.15, 1 - _DryAmount + 0.15, IN.tree.z);
            leaf = lerp(leaf, _DryLeafColor.rgb, dry * 0.45 * (1 - IN.tree.x));
            leaf *= 0.8 + 0.4 * IN.tree.y;
            o.Albedo = lerp(_TrunkColor.rgb, leaf, IN.vcol.a) * IN.vcol.r;
        }
        ENDCG
    }
    FallBack Off
}
