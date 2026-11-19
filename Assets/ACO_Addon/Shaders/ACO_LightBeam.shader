// CIC 빛기둥(6-275): 커서 · 찍은 지면을 표시하는 반투명 빛기둥. Unity 기본 원기둥(로컬 y −1 ~ 1, 반지름 0.5)에 쓴다.
//  - 반투명(미리 곱한 알파 + 약간의 더하기 빛 _Glow): 밝은 하늘 · 안개 위에서도 색이 남는다.
//  - 위로 갈수록 흐려진다: 높이 h(0 바닥 → 1 꼭대기)에 따라 (1 − h)^_FadePower.
//  - 바닥은 비운다: h < _BaseSoft 구간은 _BaseFloor(거의 투명)에서 차오른다 — 찍은 자리의 유닛 · 마커가 빛에 덮이지 않게(2026-11-18).
//  - 가장자리가 부드럽다: 보는 방향과 옆면 법선(수평 성분)이 마주 볼수록 진하고 테두리로 갈수록 0 → 빛 덩어리처럼 보인다. 가운데는 흰 심(_CoreColor).
//  - 앞 · 뒷면을 다 그린다(Cull Off) — 겹친 가운데가 더 밝다. 위 · 아래 뚜껑 면은 그리지 않는다(바닥에 원판이 안 보이게).
//  - 위에서 내려다볼수록 옅어진다(_TopFade — 시선의 수평 비율^p): 위(UAV)에서 볼 때 기둥이 찍은 자리를 덮지 않고, 빛무리 테두리가 딱딱하게 안 보인다.
//  - 위로 흐르는 옅은 띠(_BandAmount)와 약한 맥동(_Pulse).
//  - 안개는 받지 않는다(멀리서도 위치가 보여야 한다 — 표시용).
Shader "ACO/LightBeam"
{
    Properties
    {
        _Color ("Color (alpha = strength)", Color) = (1, 0.85, 0.2, 0.6)
        _CoreColor ("Core color", Color) = (1, 0.97, 0.85, 1)
        _Glow ("Additive glow", Range(0, 2)) = 0.6
        _FadePower ("Height fade power", Range(0.2, 6)) = 1.6
        _BaseSoft ("Clear base height (0-1)", Range(0, 0.3)) = 0.01
        _BaseFloor ("Alpha at base", Range(0, 1)) = 0.08
        _EdgePower ("Edge softness", Range(0.2, 8)) = 1.5
        _CorePower ("Core sharpness", Range(1, 32)) = 10
        _BandFreq ("Band count", Float) = 10
        _BandSpeed ("Band speed (heights/s)", Float) = 0.12
        _BandAmount ("Band amount", Range(0, 1)) = 0.2
        _Pulse ("Pulse amount", Range(0, 1)) = 0.1
        _TopFade ("Fade when seen from above", Range(0, 4)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
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

            fixed4 _Color, _CoreColor;
            float _Glow, _FadePower, _BaseSoft, _BaseFloor, _EdgePower, _CorePower, _BandFreq, _BandSpeed, _BandAmount, _Pulse, _TopFade;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float h : TEXCOORD0;        // 0 바닥 → 1 꼭대기
                float3 n : TEXCOORD1;       // 물체 공간 법선
                float3 v : TEXCOORD2;       // 물체 공간 시선(점 → 카메라)
                float3 wv : TEXCOORD3;      // 월드 시선(점 → 카메라) — 내려다보는 각도
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.h = v.vertex.y * 0.5 + 0.5;
                o.n = v.normal;
                o.v = ObjSpaceViewDir(v.vertex);   // x · z 배율이 같아 수평 방향은 그대로
                o.wv = _WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (abs(i.n.y) > 0.5) { discard; }   // 뚜껑 면
                float facing = abs(dot(normalize(i.n.xz), normalize(i.v.xz + 1e-5)));
                float h = saturate(i.h);
                float fade = pow(1 - h, _FadePower) * lerp(_BaseFloor, 1, smoothstep(0, max(_BaseSoft, 1e-4), h));
                float band = 1 - _BandAmount + _BandAmount * (0.5 + 0.5 * sin((h - _Time.y * _BandSpeed) * _BandFreq * 6.2832));
                float pulse = 1 - _Pulse * (0.5 + 0.5 * sin(_Time.y * 5.0));
                float side = length(i.wv.xz) / max(length(i.wv), 1e-4);   // 1 = 옆에서, 0 = 바로 위에서
                float a = saturate(_Color.a * pow(facing, _EdgePower) * fade * band * pulse * pow(max(side, 1e-3), _TopFade));
                fixed3 c = lerp(_Color.rgb, _CoreColor.rgb, pow(facing, _CorePower));
                return fixed4(c * a * (1 + _Glow), a);
            }
            ENDCG
        }
    }
}
