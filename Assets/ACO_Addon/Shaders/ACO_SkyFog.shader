// 하늘 안개 돔: Unity 안개는 물체만 칠하고 스카이박스는 그대로라, 짙은 안개·구름 속·물속에서도 틈새 하늘이 원래 색으로 보인다.
// 머리를 감싸는 구(ACO_CloudField가 머리로 옮긴다)를 가장 먼 깊이(원평면)에 그려 하늘 픽셀에만 덮는다 — 지형·구름·바다가 그려진 곳은 깊이에 막힌다.
//  - 색 = 지금 안개 색(unity_FogColor), 진하기 = 지금 안개 밀도(unity_FogParams, Exponential Squared)에서 맑은 날 밀도(_BaseDensity)를 뺀 만큼.
//    맑은 날엔 아무것도 덮지 않는다(스카이박스 그대로).
//  - 시선이 지나는 공기 길이 = _ScaleHeight / 올려다본 각의 sin(최대 _MaxPath) — 수평선 쪽은 진하고 머리 위는 옅다. 수평선 아래는 최대 길이.
// 큐 Transparent-400: 불투명·스카이박스 다음, 바다(Transparent-100) 전.
Shader "ACO/SkyFog"
{
    Properties
    {
        _BaseDensity ("Clear-day fog density (Bake = scene fog)", Float) = 0.000028
        _ScaleHeight ("Haze height (m)", Float) = 2000
        _MaxPath ("Max path (m)", Float) = 30000
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-400" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _BaseDensity, _ScaleHeight, _MaxPath;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
                o.dir = wp - _WorldSpaceCameraPos;
                o.pos = UnityWorldToClipPos(wp);
                // 원평면에 붙인다: 이미 그려진 것(깊이가 있는 곳)에는 가려지고 빈 하늘에만 그려진다
                #if defined(UNITY_REVERSED_Z)
                o.pos.z = o.pos.w * 0.000001;
                #else
                o.pos.z = o.pos.w * 0.999999;
                #endif
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float density = unity_FogParams.x * 0.83255;   // Exponential Squared: x = 밀도 / sqrt(ln 2)
                float extra = max(density - _BaseDensity, 0);
                float path = d.y > 0.0001 ? min(_ScaleHeight / d.y, _MaxPath) : _MaxPath;
                float k = extra * path;
                float a = 1 - exp(-k * k);
                return fixed4(unity_FogColor.rgb, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
