// 함내 격실 불투명 재질. 빛은 전부 구운 것을 쓴다.
//  - 라이트맵(벽)이나 라이트 프로브(소품, 시계 바늘) + 박스 투영 반사 프로브 + 자체 발광.
//  - 바닥과 천장은 Blender로 구운 빛 텍스처를 두 번째 UV로 읽는다(_BAKEDLIGHT, Tools/aco_interior_bake.py).
//  - 태양을 받지 않는다. 태양 그림자 세기가 0.8이라 Standard는 벽 안쪽에도 햇빛 20%가 비친다.
//  - 안개를 받지 않는다. 폭풍과 구름 속 안개는 야외 효과라 실내에 끼면 안 된다.
//  - 실시간 조명 패스가 없어 격실에 조명이 많아도 드로우콜이 늘지 않는다.
//  - 씬 라이트맵은 방향 정보가 없으므로(ACO_LightingSetup), 노멀맵은 위에서 비추는 형광등을 가정해 밝기만 살린다.
// 속성 이름은 Standard와 같아 재질 값을 그대로 쓴다.
Shader "ACO/Interior"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        [HDR] _EmissionColor ("Emission (albedo x this)", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _BakedLight ("Baked Light (Blender, UV2)", 2D) = "white" {}
        _BakedLightScale ("Baked Light Brightness", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma shader_feature_local _BAKEDLIGHT
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "UnityStandardUtils.cginc"

            sampler2D _MainTex, _BumpMap;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Metallic, _Glossiness;
            half4 _EmissionColor;
            sampler2D _BakedLight;
            half _BakedLightScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 lm : TEXCOORD1;
                float3 world : TEXCOORD2;
                float3 tx : TEXCOORD3;
                float3 ty : TEXCOORD4;
                float3 tz : TEXCOORD5;
                float2 uv1 : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv0, _MainTex);
                o.lm = v.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
                o.uv1 = v.uv1;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.tz = UnityObjectToWorldNormal(v.normal);
                o.tx = UnityObjectToWorldDir(v.tangent.xyz);
                o.ty = cross(o.tz, o.tx) * v.tangent.w * unity_WorldTransformParams.w;
                return o;
            }

            half3 Reflection(float3 world, half3 r, half roughness)
            {
                half3 dir = r;
                if (unity_SpecCube0_ProbePosition.w > 0)
                {
                    float3 toMax = (unity_SpecCube0_BoxMax.xyz - world) / r;
                    float3 toMin = (unity_SpecCube0_BoxMin.xyz - world) / r;
                    float3 box = (r > 0.0) ? toMax : toMin;
                    dir = world - unity_SpecCube0_ProbePosition.xyz + r * min(min(box.x, box.y), box.z);
                }
                half mip = roughness * (1.7 - 0.7 * roughness) * UNITY_SPECCUBE_LOD_STEPS;
                half4 c = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, dir, mip);
                return DecodeHDR(c, unity_SpecCube0_HDR);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;
                half3 tn = UnpackNormal(tex2D(_BumpMap, i.uv));
                half3 geo = normalize(i.tz);
                half3 n = normalize(i.tx * tn.x + i.ty * tn.y + i.tz * tn.z);
                half3 view = normalize(UnityWorldSpaceViewDir(i.world));

                #if defined(_BAKEDLIGHT)
                    half3 irradiance = tex2D(_BakedLight, i.uv1).rgb * _BakedLightScale;
                    irradiance *= (0.6 + 0.4 * n.y) / (0.6 + 0.4 * geo.y);
                #elif defined(LIGHTMAP_ON)
                    half3 irradiance = DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, i.lm));
                    irradiance *= (0.6 + 0.4 * n.y) / (0.6 + 0.4 * geo.y);
                #else
                    half3 irradiance = max(half3(0, 0, 0), ShadeSH9(half4(n, 1)));
                #endif

                half roughness = 1 - _Glossiness;
                half3 f0 = lerp(half3(0.04, 0.04, 0.04), albedo, _Metallic);
                half nv = saturate(dot(n, view));
                half edge = pow(1 - nv, 5);
                half3 fresnel = f0 + (max(half3(_Glossiness, _Glossiness, _Glossiness), f0) - f0) * edge;
                // 어두운 구석에서 반사가 떠 보이지 않게 밝기로 누른다
                half occlusion = saturate(dot(irradiance, half3(0.33, 0.33, 0.34)) * 2.5);
                half3 specular = Reflection(i.world, reflect(-view, n), roughness) * fresnel * occlusion;

                half3 color = albedo * (1 - _Metallic) * irradiance + specular + albedo * _EmissionColor.rgb;
                return fixed4(color, 1);
            }
            ENDCG
        }

        // 라이트 굽기가 표면 색과 발광을 읽는 패스
        Pass
        {
            Name "META"
            Tags { "LightMode" = "Meta" }
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityMetaPass.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Metallic;
            half4 _EmissionColor;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION, float2 uv0 : TEXCOORD0, float2 uv1 : TEXCOORD1, float2 uv2 : TEXCOORD2)
            {
                v2f o;
                o.pos = UnityMetaVertexPosition(vertex, uv1, uv2, unity_LightmapST, unity_DynamicLightmapST);
                o.uv = TRANSFORM_TEX(uv0, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;
                UnityMetaInput meta;
                UNITY_INITIALIZE_OUTPUT(UnityMetaInput, meta);
                meta.Albedo = albedo * (1 - _Metallic * 0.5);
                meta.Emission = albedo * _EmissionColor.rgb;
                return UnityMetaFragment(meta);
            }
            ENDCG
        }
    }
    // 그림자 패스를 빌린다
    FallBack "Legacy Shaders/VertexLit"
}
