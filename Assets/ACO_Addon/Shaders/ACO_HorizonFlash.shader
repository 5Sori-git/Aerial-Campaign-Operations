// 지평선 섬광(랜스 포대 착탄) — 파티클 판 하나. 가운데가 밝고 가장자리로 부드럽게 사라지는 더하기 혼합.
//  - 안개를 받지 않는다: 멀리 있는 빛이라 씬 안개에 묻히면 안 보인다.
//  - 깊이는 본다: 산 뒤의 섬광은 산에 가려져 능선 뒤에서 번쩍인다.
//  - 큐 Transparent+100: 하늘 안개 돔 뒤에 그린다.
Shader "ACO/HorizonFlash"
{
    Properties
    {
        _Falloff ("Edge falloff", Range(0.5, 6)) = 2.4
        _Intensity ("Intensity", Range(0, 8)) = 2.5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _Falloff, _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = saturate(1 - length(i.uv - 0.5) * 2);
                return fixed4(i.color.rgb * i.color.a * pow(d, _Falloff) * _Intensity, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
