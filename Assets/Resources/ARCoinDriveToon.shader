// Mobile physically-inspired surface for every procedural model: Lambert diffuse with the
// scene's spherical-harmonics ambient (fed by AR light estimation on the phone), a normalized
// Blinn-Phong highlight, Fresnel reflection of a neutral indoor environment, real-time
// shadows from the main light, and an optional tiled texture (asphalt, wood, fabric).
// The file keeps its old name so the material asset that ships it stays linked.
Shader "AR Coin Drive/Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0, 1)) = 0.35
        _Metallic ("Metallic", Range(0, 1)) = 0
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
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            half _Glossiness;
            half _Metallic;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                half3 ambient : TEXCOORD3;
                SHADOW_COORDS(4)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.ambient = max(half3(0, 0, 0), ShadeSH9(half4(o.normal, 1)));
                TRANSFER_SHADOW(o);
                return o;
            }

            // Neutral room: bright ceiling light, soft walls, darker floor. Scaled by the ambient
            // level so reflections dim in a dark room instead of glowing.
            half3 Environment(half3 direction, half3 ambientLevel)
            {
                half up = direction.y;
                half3 ceiling = lerp(half3(0.80, 0.80, 0.78), half3(1.25, 1.22, 1.15), saturate(up * 2.0 - 0.8));
                half3 walls = half3(0.55, 0.54, 0.52);
                half3 floorColor = half3(0.22, 0.20, 0.19);
                half3 env = up > 0 ? lerp(walls, ceiling, saturate(up * 1.6)) : lerp(walls, floorColor, saturate(-up * 3.0));
                return env * ambientLevel;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half3 n = normalize(i.normal);
                half3 v = normalize(UnityWorldSpaceViewDir(i.worldPos));
                half3 l = normalize(_WorldSpaceLightPos0.xyz);
                half3 h = normalize(l + v);
                half ndl = saturate(dot(n, l));
                half ndv = saturate(dot(n, v));
                half shadow = SHADOW_ATTENUATION(i);

                half3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;
                half3 diffuseColor = albedo * (1.0 - _Metallic);
                half3 f0 = lerp(half3(0.04, 0.04, 0.04), albedo, _Metallic);

                half3 lightColor = _LightColor0.rgb * shadow;
                half3 color = diffuseColor * (lightColor * ndl + i.ambient);

                // Normalized Blinn-Phong: tight, bright highlights on gloss, broad dim ones on matte.
                half smooth2 = _Glossiness * _Glossiness;
                half specPower = exp2(10.0 * _Glossiness + 1.0);
                half normalization = (specPower + 8.0) * 0.0397887;
                half fresnel = pow(1.0 - ndv, 5.0);
                half3 reflectance = f0 + (max(half3(_Glossiness, _Glossiness, _Glossiness), f0) - f0) * fresnel;
                half spec = pow(saturate(dot(n, h)), specPower) * normalization;
                color += lightColor * ndl * spec * reflectance;

                half ambientLevel = dot(i.ambient, half3(0.3, 0.59, 0.11)) * 1.6 + 0.05;
                color += Environment(reflect(-v, n), ambientLevel) * reflectance * lerp(0.25, 1.0, smooth2);

                return fixed4(color, 1);
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    Fallback "Mobile/Diffuse"
}
