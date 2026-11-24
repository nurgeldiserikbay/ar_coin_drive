// Mobile-friendly "animated film" car paint: soft wrapped light, a glossy clear coat
// and a procedural sky reflection, so no reflection probe or texture is required.
Shader "AR Coin Drive/Toon"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.35
        _Metallic ("Metallic", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "Paint"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Color;
            half _Glossiness;
            half _Metallic;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half3 normal : TEXCOORD0;
                half3 view : TEXCOORD1;
                half3 ambient : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = WorldSpaceViewDir(v.vertex);
                o.ambient = ShadeSH9(half4(o.normal, 1));
                return o;
            }

            // A simple studio sky: bright horizon band, blue zenith, warm dark ground.
            half3 SkyColor(half3 direction)
            {
                half up = direction.y;
                half3 sky = lerp(half3(0.98, 0.95, 0.88), half3(0.36, 0.60, 0.92), saturate(up * 1.7));
                half3 ground = lerp(half3(0.42, 0.38, 0.34), half3(0.14, 0.13, 0.13), saturate(-up * 2.2));
                return lerp(ground, sky, smoothstep(-0.05, 0.03, up));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half3 n = normalize(i.normal);
                half3 v = normalize(i.view);
                half3 l = normalize(_WorldSpaceLightPos0.xyz);
                half3 h = normalize(l + v);
                half ndl = dot(n, l);
                half ndv = saturate(dot(n, v));

                half wrapped = saturate((ndl + 0.38) / 1.38);
                wrapped *= wrapped;
                half skyFill = 0.5 + 0.5 * n.y;
                half3 diffuseLight = _LightColor0.rgb * wrapped * 0.82
                    + i.ambient * 0.35
                    + lerp(half3(0.20, 0.17, 0.15), half3(0.34, 0.38, 0.44), skyFill);
                half3 albedo = _Color.rgb * (1.0 - _Metallic * 0.65);
                half3 color = albedo * diffuseLight;

                half3 f0 = lerp(half3(0.045, 0.045, 0.045), _Color.rgb, _Metallic);
                half fresnel = pow(1.0 - ndv, 5.0);
                half3 reflectance = f0 + (1.0 - f0) * fresnel;
                half3 reflection = SkyColor(reflect(-v, n));
                color += reflection * reflectance * _Glossiness * _Glossiness * 1.25;

                half specPower = lerp(12.0, 420.0, _Glossiness * _Glossiness);
                half spec = pow(saturate(dot(n, h)), specPower) * _Glossiness * 1.35;
                color += _LightColor0.rgb * lerp(half3(1, 1, 1), _Color.rgb, _Metallic * 0.6) * spec;

                return fixed4(color, 1);
            }
            ENDCG
        }
    }

    Fallback "Mobile/Diffuse"
}
