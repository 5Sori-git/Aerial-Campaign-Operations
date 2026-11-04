// 관전 화면을 VRChat 카메라(사진·스트림 손 카메라) 화면 전체에 깐다 — 스트림 카메라를 관전 스크린 가까이(_Radius 안) 두면 녹화 화면이 곧 관전 화면이 된다.
//  - VRChat이 넣는 전역 _VRChatCameraMode(0 = 보통 시야, 1 = VR 손 카메라, 2 = 데스크톱 손 카메라, 3 = 스크린샷)가 0이 아닐 때만 그린다 — 내 눈(VR·데스크톱 시야)에는 안 보인다.
//    거울(_VRChatMirrorMode)에는 안 그린다. 테스트(에디터 — 값이 늘 0)는 _AnyCamera를 켠다.
//  - 오브젝트 위치(관전 스크린)에서 카메라가 _Radius(m) 안일 때만. 정점 셰이더가 쿼드를 화면 전체(클립 공간)로 펼친다 — 메시 경계를 크게 구워 둬 어느 쪽을 봐도 잘리지 않는다.
//  - 화면 비율이 다르면 위아래·좌우를 검은 띠로 맞춘다(늘이지 않는다).
// 관전 카메라 자신에겐 FX 레이어(관전 카메라가 그리지 않는다)라 안 보인다.
Shader "ACO/SpectatorFullscreen"
{
    Properties
    {
        _MainTex ("Spectator view (render texture)", 2D) = "black" {}
        _Radius ("Camera within (m)", Float) = 4
        [Toggle] _AnyCamera ("Any camera (editor test)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Overlay+50" "RenderType" = "Overlay" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
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
            float4 _MainTex_TexelSize;
            float _Radius, _AnyCamera;
            float _VRChatCameraMode;
            float _VRChatMirrorMode;

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 center = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                bool show = (_VRChatCameraMode > 0.5 || _AnyCamera > 0.5) && _VRChatMirrorMode < 0.5
                    && distance(_WorldSpaceCameraPos, center) < _Radius;
                // 쿼드(-0.5~0.5)를 화면 전체로. 안 그릴 땐 한 점으로 접는다
                float2 p = show ? v.vertex.xy * 2 : float2(0, 0);
                o.pos = float4(p, UNITY_NEAR_CLIP_VALUE, 1);
                o.uv = v.uv;
                if (_ProjectionParams.x < 0) { o.uv.y = 1 - o.uv.y; }
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 비율 맞추기: 화면이 더 넓으면 좌우, 더 높으면 위아래에 검은 띠
                float texAspect = _MainTex_TexelSize.z / max(_MainTex_TexelSize.w, 1);
                float scrAspect = _ScreenParams.x / max(_ScreenParams.y, 1);
                float2 uv = i.uv - 0.5;
                if (scrAspect > texAspect) { uv.x *= scrAspect / texAspect; }
                else { uv.y *= texAspect / scrAspect; }
                uv += 0.5;
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) { return fixed4(0, 0, 0, 1); }
                return fixed4(tex2D(_MainTex, uv).rgb, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
