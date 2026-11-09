// 폭발·연기·탄환 파티클 공용 셰이더. 플립북은 파티클의 Texture Sheet Animation이 UV를 넘겨 준다.
//  - 블렌드는 머티리얼 값: 알파(불덩이·연기) = SrcAlpha / OneMinusSrcAlpha, 가산(섬광·불티·탄환) = SrcAlpha / One.
//  - _Boost: 색 배수(HDR 카메라에서 더 밝게 번진다).
//  - 안개: 알파는 안개색으로, 가산은 검정으로 흐려진다(_FogToBlack = 1). 가산에 안개색을 더하면 먼 곳에서 흰 판이 보인다.
//  - 알파 재질(연기·불)은 안개만큼 투명해지기만 하고 색은 안개색으로 섞지 않는다 — 색만 바꾸면 안개색 덩어리로 윤곽이 남고,
//    둘 다 하면 안개가 두 번 걸려(안개 비율의 제곱) 지형보다 훨씬 먼저 사라진다(흐린 날 10km에서 지형 68% · 연기 46%).
//  - 체적 구름층은 파티클보다 먼저 그려져 덮지 못한다. 그래서 시선이 층을 지나는 거리만큼 스스로 투명해진다(ACO_CloudField가 전역값을 넣는다).
Shader "ACO/FxParticle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Boost ("Boost", Range(0, 8)) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        _FogToBlack ("Fog To Black (additive)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Boost, _FogToBlack;
            float _UdonACOFogBottom, _UdonACOFogTop, _UdonACOFogDensity, _UdonACOFogFade;

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
                float3 worldPos : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            /// 카메라에서 이 픽셀까지 체적 층을 지나고 남는 비율.
            float VolumeKeep(float3 worldPos)
            {
                if (_UdonACOFogFade <= 0) { return 1; }
                float3 ray = worldPos - _WorldSpaceCameraPos;
                float low = min(_WorldSpaceCameraPos.y, worldPos.y);
                float high = max(_WorldSpaceCameraPos.y, worldPos.y);
                float overlap = max(min(high, _UdonACOFogTop) - max(low, _UdonACOFogBottom), 0);
                float inside = length(ray) * overlap / max(high - low, 0.001);
                return 1 - (1 - exp(-_UdonACOFogDensity * inside)) * _UdonACOFogFade;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                c.rgb *= _Boost;
                float keep = VolumeKeep(i.worldPos);
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                {   // 아래 UNITY_APPLY_FOG_COLOR도 unityFogFactor를 선언한다 — 범위를 나눈다
                    UNITY_CALC_FOG_FACTOR_RAW(length(i.worldPos - _WorldSpaceCameraPos));
                    keep *= saturate(unityFogFactor);
                }
                #endif
                c.a *= lerp(keep, 1, _FogToBlack);   // 알파 재질: 안개만큼 투명해진다(색은 그대로 — 뒤 배경이 이미 안개에 흐려져 있다)
                fixed4 fogged = c;
                UNITY_APPLY_FOG_COLOR(i.fogCoord, fogged, fixed4(0, 0, 0, 0));   // 가산 재질: 안개가 검게 줄인다
                c.rgb = lerp(c.rgb, fogged.rgb, _FogToBlack);
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
