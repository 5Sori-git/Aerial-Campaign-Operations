// CIC 커서 받침(6-275): 빛기둥 발밑의 반투명 빛 원판. Unity 기본 원기둥(납작하게 — 반지름 0.5)의 윗면만 그린다.
//  - 가운데가 진하고 바깥으로 갈수록 옅어진다 + 가장자리 가까이 옅은 고리(표적 표시 느낌).
//  - 반투명(미리 곱한 알파 + 약한 더하기 빛), 빛기둥(ACO/LightBeam)과 같은 맥동.
//  - 옆면 · 아랫면은 그리지 않는다.
Shader "ACO/LightDisc"
{
    Properties
    {
        _Color ("Color (alpha = strength)", Color) = (1, 0.82, 0.25, 0.7)
        _Glow ("Additive glow", Range(0, 2)) = 0.6
        _FillPower ("Center falloff", Range(0.2, 6)) = 1.6
        _Fill ("Center fill", Range(0, 1)) = 0.55
        _RingPos ("Ring radius (0-1)", Range(0, 1)) = 0.82
        _RingWidth ("Ring width", Range(0.01, 0.5)) = 0.07
        _Pulse ("Pulse amount", Range(0, 1)) = 0.1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+9" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Glow, _FillPower, _Fill, _RingPos, _RingWidth, _Pulse;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 xz : TEXCOORD0;      // 물체 공간 가로 자리(반지름 0.5)
                float ny : TEXCOORD1;       // 물체 공간 법선 y
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.xz = v.vertex.xz;
                o.ny = v.normal.y;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (i.ny < 0.5) { discard; }   // 윗면만
                float r = saturate(length(i.xz) * 2);
                float fill = _Fill * pow(1 - r, _FillPower);
                float ring = exp(-pow((r - _RingPos) / _RingWidth, 2));
                float pulse = 1 - _Pulse * (0.5 + 0.5 * sin(_Time.y * 5.0));
                float a = saturate(_Color.a * (fill + ring) * pulse);
                return fixed4(_Color.rgb * a * (1 + _Glow), a);
            }
            ENDCG
        }
    }
}
