// One-material car shader: every part's colour, smoothness, metalness and glow are baked into
// the mesh (vertex colour + UV1), so a whole car body is a single draw call. Vertex alpha marks
// paintable panels, which take _PaintColor (one material per paint job). GPU instancing lets the
// four wheels share one draw call.
Shader "AR Coin Drive/Car"
{
    Properties
    {
        _PaintColor ("Paint", Color) = (0.8, 0.05, 0.05, 1)
        _PaintGloss ("Paint Smoothness", Range(0, 1)) = 0.86
        _PaintMetal ("Paint Metallic", Range(0, 1)) = 0.35
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            fixed4 _PaintColor;
            half _PaintGloss;
            half _PaintMetal;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float4 surface : TEXCOORD1; // x smoothness, y metallic, z emission
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 normal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                half3 ambient : TEXCOORD2;
                fixed4 color : TEXCOORD3;
                half3 surface : TEXCOORD4;
                SHADOW_COORDS(5)
            };

            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.ambient = max(half3(0, 0, 0), ShadeSH9(half4(o.normal, 1)));
                // Paint mask in alpha: painted panels take the material colour and finish.
                half paint = v.color.a;
                o.color = fixed4(lerp(v.color.rgb, _PaintColor.rgb, paint), 1);
                o.surface = half3(lerp(v.surface.x, _PaintGloss, paint), lerp(v.surface.y, _PaintMetal, paint), v.surface.z);
                TRANSFER_SHADOW(o);
                return o;
            }

            half3 Environment(half3 direction, half ambientLevel)
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

                half gloss = i.surface.x;
                half metal = i.surface.y;
                half3 albedo = i.color.rgb;
                half3 diffuseColor = albedo * (1.0 - metal);
                half3 f0 = lerp(half3(0.04, 0.04, 0.04), albedo, metal);
                half3 lightColor = _LightColor0.rgb * shadow;
                half3 color = diffuseColor * (lightColor * ndl + i.ambient);

                half specPower = exp2(10.0 * gloss + 1.0);
                half normalization = (specPower + 8.0) * 0.0397887;
                half fresnel = pow(1.0 - ndv, 5.0);
                half3 reflectance = f0 + (max(half3(gloss, gloss, gloss), f0) - f0) * fresnel;
                color += lightColor * ndl * pow(saturate(dot(n, h)), specPower) * normalization * reflectance;

                half ambientLevel = dot(i.ambient, half3(0.3, 0.59, 0.11)) * 1.6 + 0.05;
                color += Environment(reflect(-v, n), ambientLevel) * reflectance * lerp(0.25, 1.0, gloss * gloss);

                // Lamps glow regardless of lighting.
                color = lerp(color, albedo * 1.6, saturate(i.surface.z));
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
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

    Fallback Off
}
