// Invisible floor that only shows the shadows virtual objects cast, so in AR the car's shadow
// lands on the real floor. Drawn right after opaque geometry (shadow receiving is not
// available in the transparent queue) and blended over the camera image.
Shader "AR Coin Drive/Shadow Catcher"
{
    Properties
    {
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.55
        _FadeRadius ("Fade Radius", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+20" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"

            half _ShadowStrength;
            float _FadeRadius;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 local : TEXCOORD0;
                SHADOW_COORDS(1)
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.local = v.vertex.xz;
                TRANSFER_SHADOW(o);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half shadow = SHADOW_ATTENUATION(i);
                half fade = 1.0 - smoothstep(_FadeRadius * 0.7, _FadeRadius, length(i.local));
                return fixed4(0, 0, 0, (1.0 - shadow) * _ShadowStrength * fade);
            }
            ENDCG
        }
    }

    Fallback Off
}
