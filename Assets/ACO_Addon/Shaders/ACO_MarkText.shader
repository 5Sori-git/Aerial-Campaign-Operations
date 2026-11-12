// 기체에 붙이는 표시(헐넘버·콜사인 = TextMeshPro SDF, 킬마크 = 거리값 아이콘 아틀라스 — 알파 0.5가 경계). 조명 없이 _Tint를 곱한다.
//  - Z파이팅 방지: 정점을 카메라 쪽으로 끌어당긴다. 시선 방향으로만 움직여 화면의 자리·크기는 그대로이고, 눈마다 계산해 VR에서도 어긋나지 않는다.
//    _PullPercent = 거리의 %(멀수록 떨어지는 깊이 정밀도를 따라간다), _PullMeters = 거리와 상관없는 m(가까이서 곡면에 묻히는 것을 막는다). 둘 다 값이 클수록 많이 당긴다.
//  - 거리 페이드: _FadeStart → _FadeEnd에서 사라진다. 글자·킬마크 머티리얼 모두 관전 망원을 위해 1500 → 1800m.
// 아래 Hidden 값은 TextMeshPro가 머티리얼에서 읽는 값이다 — 이 셰이더는 쓰지 않는다.
Shader "ACO/MarkText"
{
    Properties
    {
        _MainTex ("Atlas (alpha = distance)", 2D) = "white" {}
        _Tint ("Tint", Color) = (0.85, 0.85, 0.85, 1)
        _PullPercent ("Pull toward camera (% of distance)", Range(0, 3)) = 0.5
        _PullMeters ("Pull toward camera (m, at any distance)", Range(0, 0.5)) = 0.05
        _FadeStart ("Fade start (m)", Float) = 150
        _FadeEnd ("Fade end (m)", Float) = 300

        [HideInInspector] _FaceColor ("Face", Color) = (1, 1, 1, 1)
        [HideInInspector] _FaceDilate ("Dilate", Float) = 0
        [HideInInspector] _OutlineWidth ("Outline", Float) = 0
        [HideInInspector] _OutlineSoftness ("Outline soft", Float) = 0
        [HideInInspector] _WeightNormal ("Weight normal", Float) = 0
        [HideInInspector] _WeightBold ("Weight bold", Float) = 0.5
        [HideInInspector] _ScaleRatioA ("Ratio A", Float) = 1
        [HideInInspector] _ScaleRatioB ("Ratio B", Float) = 1
        [HideInInspector] _ScaleRatioC ("Ratio C", Float) = 1
        [HideInInspector] _TextureWidth ("Width", Float) = 512
        [HideInInspector] _TextureHeight ("Height", Float) = 512
        [HideInInspector] _GradientScale ("Gradient", Float) = 5
        [HideInInspector] _ScaleX ("Scale X", Float) = 1
        [HideInInspector] _ScaleY ("Scale Y", Float) = 1
        [HideInInspector] _PerspectiveFilter ("Perspective", Float) = 0.875
        [HideInInspector] _Sharpness ("Sharpness", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-9" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Tint;
            float _PullPercent, _PullMeters, _FadeStart, _FadeEnd;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 view = UnityObjectToViewPos(v.vertex.xyz);
                float dist = length(view);
                float pull = saturate(_PullPercent * 0.01 + _PullMeters / max(dist, 0.1));
                o.pos = mul(UNITY_MATRIX_P, float4(view * (1 - min(pull, 0.5)), 1));
                o.uv = v.uv;
                o.color = v.color * _Tint;
                o.color.a *= 1 - saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 1));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = tex2D(_MainTex, i.uv).a;
                float w = max(fwidth(d) * 0.7, 0.0001);
                fixed4 c = i.color;
                c.a *= smoothstep(0.5 - w, 0.5 + w, d);
                if (c.a <= 0.003) { discard; }
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
