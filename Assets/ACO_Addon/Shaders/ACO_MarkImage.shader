// 기체에 붙이는 이미지 표시(라운델 — URL로 받은 PNG/JPG, 6-191). ACO/MarkText와 같은 판 방식: 조명 없이 _Tint를 곱하고,
//  - Z파이팅 방지: 정점을 카메라 쪽으로 끌어당긴다(시선 방향으로만 — 화면의 자리·크기는 그대로, 눈마다 계산해 VR에서도 맞다).
//    판은 편집기에서 기체 표면에 맞춰 구운 메시라(곡면을 따라간다) 당기는 양은 작게 둔다.
//  - 거리 페이드: _FadeStart → _FadeEnd에서 사라진다(관전 망원 1500 → 1800m).
//  - 이미지의 알파를 그대로 쓴다(투명 PNG면 라운델 모양만 보인다).
Shader "ACO/MarkImage"
{
    Properties
    {
        _MainTex ("Image", 2D) = "white" {}
        _Tint ("Tint", Color) = (0.85, 0.85, 0.85, 1)
        _PullPercent ("Pull toward camera (% of distance)", Range(0, 3)) = 0.3
        _PullMeters ("Pull toward camera (m, at any distance)", Range(0, 0.5)) = 0.02
        _FadeStart ("Fade start (m)", Float) = 1500
        _FadeEnd ("Fade end (m)", Float) = 1800
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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float fade : TEXCOORD1;
                UNITY_FOG_COORDS(2)
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
                o.fade = 1 - saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 1));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 c = tex2D(_MainTex, i.uv) * _Tint;
                c.a *= i.fade;
                if (c.a <= 0.003) { discard; }
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
