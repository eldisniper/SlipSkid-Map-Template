// Unlit glowing decoration for the 1.3.0 maps: neon tubes, lava, portals, lasers, geyser plumes, lighthouse beams.
// Like BoostGlow: plain CG with no LightMode tag, so URP draws it through its default unlit pass whatever the game's
// URP settings are; it ships inside the map bundle under a unique name and the loader leaves it alone (objects that
// use it are not KitRoads). Effects, all optional and combinable:
//   _Bands > 0  waves of _Color2 scrolling along v (lava flows, laser scan lines, waterfalls);
//   _Swirl != 0 spiral arms turning around the uv centre (portal discs; negative curls them the other way);
//   _Pulse > 0  the brightness breathes (beacons, warning lights).
// It fades part way into the level's linear fog (_FogAmount 0.6 by default: lights carry further than the toon scenery, and
// the biome's fog colour is often far from its sky – a dark green on the island biome; 0 for sky objects, 1 for full fog), computed here from unity_FogParams (start/end, which MapKitLoader stretches to the map) rather than
// with the fog keywords, whose variants the bundle build strips (the map scenes have fog off). No linear fog
// parameters (z = 0): no fog. It draws in the transparent queue (see the SubShader); the depth passes at the end are
// there for a material moved back to an opaque queue, which the game would otherwise never colour.
Shader "SlipSkid/Glow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.45, 0.1, 1)
        _Color2 ("Second Color", Color) = (1, 0.9, 0.35, 1)
        _Bands ("Bands Along V", Float) = 0
        _Speed ("Scroll Speed", Float) = 0.5
        _Swirl ("Swirl Arms", Float) = 0
        _Pulse ("Pulse Speed", Float) = 0
        _FogAmount ("Fog Amount", Range(0, 1)) = 0.6
    }
    SubShader
    {
        // Drawn after the opaque scenery (still solid: no blending, depth written and tested), so the game's ink
        // outline – drawn round what its depth prepass sees – leaves it alone: light has no black border, and thin
        // beams (lasers 10 cm wide) were all outline, black lines.
        Tags { "RenderType" = "Opaque" "Queue" = "Transparent" }
        Pass
        {
            ZWrite On
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color, _Color2;
            float _Bands, _Speed, _Swirl, _Pulse, _FogAmount;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float depth : TEXCOORD1; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.depth = -UnityObjectToViewPos(v.vertex).z;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = 0;
                if (_Bands > 0)
                {
                    float wobble = sin(i.uv.x * 12.566 + _Time.y * 0.7) * 0.6;   // whole waves round a ring: no notch at its seam
                    t = 0.5 + 0.5 * sin((i.uv.y * _Bands - _Time.y * _Speed) * 6.2832 + wobble);
                }
                if (_Swirl != 0)   // negative: the arms curl the other way
                {
                    float2 d = i.uv - 0.5;
                    float a = atan2(d.y, d.x);
                    float r = length(d);
                    t = 0.5 + 0.5 * sin(a * _Swirl + r * 22.0 - _Time.y * _Speed * 6.0);
                    t = lerp(t, 1.0, saturate(1.0 - r * 6.0));   // a bright eye in the middle
                }
                fixed4 c = lerp(_Color, _Color2, t);
                if (_Pulse > 0) c.rgb *= 0.85 + 0.15 * sin(_Time.y * _Pulse * 6.2832);   // 70-100 %: at 40 % pale lights looked grey and unlit
                // Linear fog: 1 = clear at fog start, 0 = fog colour at fog end.
                float clear = unity_FogParams.z < 0 ? saturate(i.depth * unity_FogParams.z + unity_FogParams.w) : 1;
                c.rgb = lerp(c.rgb, unity_FogColor.rgb, (1 - clear) * _FogAmount);
                return c;
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
