// Unlit glowing chevrons for boost pads. Plain CG with no LightMode tag, so URP draws it through
// its default unlit pass, in the transparent queue (see the SubShader); the depth passes at the end are there for a
// material moved back to an opaque queue, which the game would otherwise never colour.
// It ships inside the map bundle and has a unique name, so the map loader leaves it alone.
Shader "SlipSkid/BoostGlow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.8, 0.05, 1)
        _StripeColor ("Stripe Color", Color) = (1, 0.35, 0, 1)
        _Speed ("Scroll Speed", Float) = 2
        _Count ("Chevron Count", Float) = 3
    }
    SubShader
    {
        // Drawn after the opaque road (still solid: no blending, depth written and tested), like Glow: out of the game's
        // depth priming and ink outline – from ~80 m a pad was all outline and read as a black hole in the road.
        Tags { "RenderType" = "Opaque" "Queue" = "Transparent" }
        Pass
        {
            ZWrite On
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color, _StripeColor;
            float _Speed, _Count;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float chevron = frac(i.uv.y * _Count - abs(i.uv.x - 0.5) * 1.5 - _Time.y * _Speed);
                return chevron < 0.5 ? _Color : _StripeColor;
            }
            ENDCG
        }
        // The game's URP renderer (URP-HighFidelity) primes depth: it draws every opaque object's DepthOnly /
        // DepthNormals pass first, then colours only the pixels whose depth matches exactly. Without these two
        // passes the colour pass above never shows – only the outline the game draws round its depth (2026-10-27).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert (float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            half4 frag () : SV_Target { return 0; }
            ENDCG
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; };
            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            half4 frag (v2f i) : SV_Target { return half4(normalize(i.normal), 0); }
            ENDCG
        }
    }
}
