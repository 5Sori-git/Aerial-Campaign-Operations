// 경계 격자 벽(6-170): 플레이어 루프 경계(SAV RepeatingWorld — 축별 ±RepeatingWorldDistance 정사각형)의 네 면에 세운 판.
// 벽 전체가 보이지 않고, 눈(VR은 두 눈 가운데)에서 가까운 부분만 격자가 드러난다(_FadeNear 안은 또렷, _FadeFar에서 사라짐 — 그 밖 픽셀은 버린다).
//  - 격자: 월드 좌표(벽을 따라가는 축 × 높이)로 칸 _Cell(m), _MajorEvery칸마다 굵은 선. 선 굵기는 화면 기준(fwidth)이라 멀리서도 깨지지 않는다
//  - 연출: 위로 천천히 흐르는 밝기 띠(_BandSpacing · _BandSpeed), 가까울수록 밝다. 양면(안 · 밖 어디서 와도 보인다)
//  - 투명(가산 섞기), 깊이 쓰지 않음, 그림자 · 안개 없음(짙은 안개 · 구름 속에서도 가까운 벽은 보인다)
// 메시: ACO_BoundaryBuilder가 만드는 네 장의 판(노멀 = 바깥) — 판마다 벽을 따라가는 축을 노멀로 고른다.
Shader "ACO/BoundaryWall"
{
    Properties
    {
        _Color ("Line color", Color) = (0.35, 0.85, 1, 1)
        _FillColor ("Fill tint", Color) = (0.2, 0.55, 0.8, 0.08)
        _Intensity ("Intensity", Range(0, 4)) = 1.6
        _Cell ("Grid cell (m)", Float) = 200
        _MajorEvery ("Major line every N cells", Float) = 5
        _LineWidth ("Line width (pixels)", Range(0.5, 4)) = 1.2
        _MajorWidth ("Major line width (pixels)", Range(0.5, 6)) = 2.2
        _FadeNear ("Fully visible within (m)", Float) = 1500
        _FadeFar ("Gone beyond (m)", Float) = 3000
        _BandSpacing ("Rising band spacing (m)", Float) = 1200
        _BandSpeed ("Rising band speed (m/s)", Float) = 60
        _BandStrength ("Rising band strength", Range(0, 2)) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color, _FillColor;
            float _Intensity, _Cell, _MajorEvery, _LineWidth, _MajorWidth, _FadeNear, _FadeFar, _BandSpacing, _BandSpeed, _BandStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldN : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 CenterEye()
            {
            #if defined(USING_STEREO_MATRICES)
                return (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
            #else
                return _WorldSpaceCameraPos;
            #endif
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.pos = UnityWorldToClipPos(o.worldPos);
                return o;
            }

            // 화면 기준 굵기(width 픽셀)의 격자선 세기 0~1 — coord는 칸 단위
            float GridLine(float2 coord, float width)
            {
                float2 d = abs(frac(coord - 0.5) - 0.5) / max(fwidth(coord), 0.00001);
                return 1 - saturate(min(d.x, d.y) / width);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float dist = distance(CenterEye(), i.worldPos);
                float fade = 1 - smoothstep(_FadeNear, _FadeFar, dist);
                clip(fade - 0.002);

                // 벽을 따라가는 축: 노멀이 X쪽이면 Z, 아니면 X
                float along = abs(i.worldN.x) > 0.5 ? i.worldPos.z : i.worldPos.x;
                float2 coord = float2(along, i.worldPos.y) / max(_Cell, 1);
                float minor = GridLine(coord, _LineWidth);
                float major = GridLine(coord / max(_MajorEvery, 1), _MajorWidth);
                float lines = max(minor * 0.55, major);

                // 위로 흐르는 밝기 띠
                float band = frac((i.worldPos.y - _Time.y * _BandSpeed) / max(_BandSpacing, 1));
                band = pow(1 - abs(band * 2 - 1), 6) * _BandStrength;

                // 가까울수록 밝게(가장 가까운 곳 1.6배)
                float nearBoost = lerp(1.6, 1, saturate(dist / max(_FadeNear, 1)));
                float3 col = _Color.rgb * lines * (1 + band) * nearBoost + _FillColor.rgb * _FillColor.a * (1 + band);
                float a = saturate(max(lines, _FillColor.a) * fade);
                return fixed4(col * _Intensity, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
