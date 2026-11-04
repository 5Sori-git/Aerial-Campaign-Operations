// 데칼(Unity 프로젝터용 — 기체 표면에 이미지를 비춘다, 기체 UV와 상관없다). 프로젝터는 직교(정사각 틀 = MaxSize), 크기·좌우 뒤집기는 머티리얼 값이라
// Udon이 프로젝터 컴포넌트를 건드리지 않고 위치(Transform)와 머티리얼만 바꾼다(ACO_VehicleDecal·ACO_DecalStudio).
//  - _Size(m): 데칼 한 변. 틀(_FrameSize = 프로젝터 orthographicSize × 2) 가운데에 그만큼만 그리고 밖은 버린다.
//  - _Flip: 좌우 뒤집기(대칭 복사한 반대쪽 데칼 — 밖에서 보면 글자가 바로 읽히게).
//  - 표면 방향: 프로젝터를 마주 보는 면만(비추는 방향 = 깊이 축 — 부호를 따로 맞출 필요 없다, _FacingSign은 1 그대로), 비스듬할수록 흐리게 → 날개 반대쪽으로 비쳐 나가거나 길게 늘어진 자국이 없다.
//  - 깊이: 프로젝터 가까운 면 ~ 먼 면(near/far, 기본 0.6m) 안만, 먼 쪽 끝에서 흐리게 → 갑판·다른 물체엔 묻지 않는다.
//  - 조명 없이 _Tint(조금 어둡게)를 곱한다. 알파 블렌드, 깊이 오프셋으로 표면 위에.
Shader "ACO/Decal"
{
    Properties
    {
        _MainTex ("Decal (script)", 2D) = "white" {}
        _Tint ("Tint", Color) = (0.85, 0.85, 0.85, 1)
        _Size ("Decal size m (script)", Float) = 1
        _FrameSize ("Projector frame m (orthographicSize x 2)", Float) = 6
        _Flip ("Flip horizontally (script)", Float) = 0
        _FacingSign ("Facing sign (1 or -1)", Float) = 1
        _FacingFade ("Grazing fade", Range(0, 1)) = 0.35
        _Visible ("Visible (script)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Tint;
            float _Size, _FrameSize, _Flip, _FacingSign, _FacingFade, _Visible;
            float4x4 unity_Projector;
            float4x4 unity_ProjectorClip;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 uvProj : TEXCOORD0;
                float4 uvClip : TEXCOORD1;
                float facing : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uvProj = mul(unity_Projector, v.vertex);
                o.uvClip = mul(unity_ProjectorClip, v.vertex);
                // 마주 보는 면: 프로젝터가 비추는 방향(오브젝트 공간) = 깊이(unity_ProjectorClip 첫 줄 — 가까운 면 0 → 먼 면 1)가 늘어나는 방향.
                // 노멀이 그 반대쪽이면 프로젝터를 본다. 투영 행렬의 z 부호(플랫폼·Unity 내부 규칙)에 기대지 않는다 — 처음엔 그 부호를 잘못 잡으면 모든 면이 버려져 데칼이 통째로 안 보였다.
                float3 projDir = normalize(unity_ProjectorClip[0].xyz);
                o.facing = -_FacingSign * dot(normalize(v.normal), projDir);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Visible < 0.5) { discard; }
                float2 uv = i.uvProj.xy / i.uvProj.w;
                // 틀(0~1) 가운데 _Size만큼을 데칼 0~1로
                float k = _FrameSize / max(_Size, 0.01);
                uv = (uv - 0.5) * k + 0.5;
                if (_Flip > 0.5) { uv.x = 1 - uv.x; }
                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) { discard; }
                if (i.facing <= 0) { discard; }
                float depth = i.uvClip.x;   // 0 = 가까운 면, 1 = 먼 면
                if (depth < 0 || depth > 1) { discard; }
                fixed4 c = tex2D(_MainTex, uv) * _Tint;
                c.a *= saturate(i.facing / max(_FacingFade, 0.01)) * saturate((1 - depth) * 4);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
