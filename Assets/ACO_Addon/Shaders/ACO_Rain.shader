// 빗줄기(ACO_Rain). 메시는 빗방울 수천 개 — 방울마다 정점 4개가 모두 단위 상자(0~1) 안의 같은 자리에 겹쳐 있고(ACO_SkyBuilder가 만든다),
// 정점 셰이더가 카메라 둘레 _Box(m) 상자 안에서 반복(wrap)시켜 끝없는 비를 만든다 — Udon은 프레임마다 카메라 속도만 넘긴다.
//  - 방울은 월드에 붙어 떨어진다(_FallSpeed + _WindVel) — 기체가 빠르면 방울이 그만큼 빨리 스쳐 지나간다
//  - 빗줄기는 카메라 기준 상대 속도 방향으로 늘어난다(길이 = 상대 속도 × _StreakTime) — 빨리 날면 비가 앞에서 옆으로 날아온다
//  - _Intensity: 방울의 무작위 값이 이보다 작은 것만 그린다(0이면 하나도 안 그린다)
//  - 조종석 안(_NearHide m)과 상자 가장자리(반복 경계)는 흐리게 숨긴다
//  - VR: 두 눈의 가운데를 기준으로 반복·방향을 정한다
Shader "ACO/Rain"
{
    Properties
    {
        _Color ("Color (A = opacity)", Color) = (0.72, 0.76, 0.84, 0.32)
        _Intensity ("Intensity", Range(0, 1)) = 1
        _Box ("Box size (m)", Float) = 44
        _FallSpeed ("Fall speed (m/s)", Float) = 9
        _WindVel ("Wind (m/s, xyz)", Vector) = (2, 0, 1, 0)
        _CamVel ("Camera velocity (set by ACO_Rain)", Vector) = (0, 0, 0, 0)
        _StreakTime ("Streak length (s of motion)", Float) = 0.03
        _MinLength ("Min length (m)", Float) = 0.7
        _MaxLength ("Max length (m)", Float) = 7
        _Width ("Width (m)", Float) = 0.012
        _NearHide ("Hidden closer than (m)", Float) = 2.5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" "DisableBatching" = "True" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            fixed4 _Color;
            float _Intensity, _Box, _FallSpeed, _StreakTime, _MinLength, _MaxLength, _Width, _NearHide;
            float4 _WindVel, _CamVel;

            struct appdata
            {
                float4 vertex : POSITION;    // 단위 상자 안 자리
                float2 uv0 : TEXCOORD0;      // x = 폭 방향 -1~1, y = 0(앞) ~ 1(꼬리)
                float2 uv1 : TEXCOORD1;      // x = 무작위
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 data : TEXCOORD0;     // x = 폭 위치, y = 길이 위치, z = 페이드
                float3 tint : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            #if defined(USING_STEREO_MATRICES)
                float3 cam = (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
            #else
                float3 cam = _WorldSpaceCameraPos;
            #endif
                float3 fall = float3(0, -_FallSpeed, 0) + _WindVel.xyz;
                // 월드에 붙어 떨어지는 방울을 카메라 둘레 상자 안으로 반복시킨다
                float3 p = v.vertex.xyz * _Box + fall * _Time.y;
                float3 rel = (frac((p - cam) / _Box) - 0.5) * _Box;
                float3 drop = cam + rel;

                float3 relVel = fall - _CamVel.xyz;
                float spd = max(length(relVel), 0.001);
                float3 axis = relVel / spd;
                float len = clamp(spd * _StreakTime, _MinLength, _MaxLength);
                float3 toCam = normalize(cam - drop);
                float3 side = normalize(cross(axis, toCam) + float3(0.0001, 0, 0));
                float d = length(rel);
                float w = max(_Width, d * 0.0012);   // 멀어도 한 픽셀쯤은 남게

                float show = v.uv1.x < _Intensity ? 1 : 0;
                float fade = smoothstep(_NearHide, _NearHide + 2, d) * (1 - smoothstep(_Box * 0.3, _Box * 0.48, d));
                show *= fade > 0.001 ? 1 : 0;

                float3 posWS = drop - axis * len * v.uv0.y * show + side * w * v.uv0.x * show;
                o.pos = UnityWorldToClipPos(posWS);
                o.data = float3(v.uv0.x, v.uv0.y, fade);
                o.tint = _Color.rgb * (ShadeSH9(float4(0, 1, 0, 1)) * 0.9 + _LightColor0.rgb * 0.25);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float a = _Color.a * i.data.z * (1 - abs(i.data.x)) * (1 - i.data.y * 0.75);
                fixed4 o = fixed4(i.tint, a);
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }
    }
    FallBack Off
}
