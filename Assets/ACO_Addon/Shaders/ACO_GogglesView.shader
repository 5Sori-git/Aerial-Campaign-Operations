// CIC 고글(6-275): 고글 카메라 화면(정사각형 렌더 텍스처)을 내 눈 화면 전체에 깐다.
//  - 정점 셰이더가 쿼드(-0.5~0.5)를 화면 전체(클립 공간)로 펼치고, 각 눈의 투영 · 뷰 행렬로 그 점의 시선 방향(월드)을 구한다.
//    고글 카메라는 "대상 자세 × 기준 · 내 머리 각도(_HeadInvQ의 역 — Udon이 카메라를 돌릴 때 쓴 머리 각도)"로 돌아 있으므로,
//    월드 시선을 그 머리 각도 기준으로 되돌리면 곧 고글 카메라 안의 방향 — 화각(_TanHalf)으로 텍스처 좌표를 구한다.
//    그리는 순간의 실제 눈 방향을 쓰므로(VR은 Udon이 읽은 뒤의 더 새 머리 각도로 그리고, 런타임이 남은 차이를 다시 돌려 맞춘다)
//    고개를 돌려도 화면이 늦게 따라오지 않는다(6-290 — 예전엔 눈 공간 방향을 그대로 써 머리에 붙은 화면이 밀렸다 따라왔다).
//  - 화각 밖은 검다. _Live 0이면 어둡게(NO SIGNAL 글자는 따로 위에 그린다).
//  - _Reticle 1이면 한가운데 조준선(UAV 시점 — 트리거로 이 점에 CIC 커서를 찍는다).
//  - 보통 시야에만 그린다: VRChat 손 카메라 · 스크린샷(_VRChatCameraMode ≠ 0) · 거울(_VRChatMirrorMode ≠ 0)에는 안 그린다.
//  - FX 레이어(관전 카메라 = 고글 카메라 자신은 그리지 않는다).
Shader "ACO/GogglesView"
{
    Properties
    {
        _MainTex ("Goggles view (render texture)", 2D) = "black" {}
        _TanHalf ("tan(half FOV)", Float) = 1.19
        _Live ("Live", Float) = 1
        _Reticle ("Center reticle", Float) = 0
        _HeadInvQ ("Inverse head rotation used by the goggles camera (quaternion xyzw)", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Overlay+80" "RenderType" = "Overlay" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _TanHalf, _Live, _Reticle;
            float4 _HeadInvQ;
            float _VRChatCameraMode;
            float _VRChatMirrorMode;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wdir : TEXCOORD0;   // 월드 시선(그리는 눈 기준)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                bool show = _VRChatCameraMode < 0.5 && _VRChatMirrorMode < 0.5;
                float2 p = show ? v.vertex.xy * 2 : float2(0, 0);
                o.pos = float4(p, UNITY_NEAR_CLIP_VALUE, 1);
                // 클립 p를 만드는 눈 공간 점(z = −1): p = (P00·x − P02, P11·y − P12) → x = (p.x + P02) / P00
                float4x4 P = UNITY_MATRIX_P;
                float2 e = float2((p.x + P[0][2]) / P[0][0], (p.y + P[1][2]) / P[1][1]);
                // 눈 공간(카메라는 −Z를 본다) → 월드: 그리는 순간의 눈 자세
                o.wdir = mul((float3x3)UNITY_MATRIX_I_V, float3(e.x, e.y, -1));
                return o;
            }

            float3 RotateQ(float4 q, float3 v)
            {
                float3 t = 2 * cross(q.xyz, v);
                return v + q.w * t + cross(q.xyz, t);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 월드 시선 → 고글 카메라 안의 방향(+Z 앞). 투영 행렬로 되돌린 값이라 플랫폼 뒤집힘이 이미 빠져 있다(위 = +y)
                float3 l = RotateQ(_HeadInvQ, normalize(i.wdir));
                float2 dir = l.xy / max(l.z, 1e-4);
                float2 uv = dir / (2 * _TanHalf) + 0.5;
                fixed3 c = (l.z <= 0 || uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) ? fixed3(0, 0, 0) : tex2D(_MainTex, uv).rgb;
                c *= lerp(0.08, 1, saturate(_Live));
                if (_Reticle > 0.5)
                {
                    // 조준선: 가운데 빈칸(0.016)에서 바깥(0.048)까지 가는 십자(가운데 점 없음 — 겨눈 표적이 가려지지 않게, 2026-11-18) — 시선 방향(tan) 단위라 VR · 데스크톱 모두 같은 각도
                    // 빈칸 0.008 → 0.016(2026-11-18 사용자 "크로스헤어 중간에 너무 모여 있어서 조금 거리를 벌려줘" — 팔 길이 0.032는 그대로)
                    // 밝은 하늘 · 안개에서도 보이게 어두운 테두리(굵게)를 먼저, 노란 심을 위에
                    float2 d = l.z > 0 ? abs(dir) : float2(1, 1);   // 고글 카메라 정중앙(트리거로 찍는 방향) 기준
                    float w = 0.0018, o = 0.0042;
                    bool arm = (d.x < w && d.y > 0.016 && d.y < 0.048) || (d.y < w && d.x > 0.016 && d.x < 0.048);
                    bool armO = (d.x < o && d.y > 0.0138 && d.y < 0.0502) || (d.y < o && d.x > 0.0138 && d.x < 0.0502);
                    if (armO) { c = fixed3(0.05, 0.05, 0.04); }
                    if (arm) { c = fixed3(1, 0.85, 0.25); }
                }
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
