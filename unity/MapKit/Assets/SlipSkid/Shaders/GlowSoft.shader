// Soft light for the 1.3.0 maps: lighthouse and searchlight beams, steam, spray, haze, force fields – what the opaque
// Glow shader turns into solid cones and baguettes. Additive (it only brightens what is behind it), no depth write,
// both faces, in the transparent queue (out of the game's depth priming and ink outline, like Glow). It fades:
//   along v (_FadeAlong 1: full at v = 0, nothing at v = 1 – a beam from its lamp, a plume from its vent);
//   towards the silhouette (_EdgeSoft > 0: the faces seen edge-on vanish, so a cone reads as a soft shaft);
//   into the level's linear fog (unity_FogParams, as Glow).
// _Bands and _Pulse as in Glow; _Strength scales it all (1 = the colour as given at its brightest).
Shader "SlipSkid/GlowSoft"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.9, 0.6, 1)
        _Color2 ("Second Color", Color) = (1, 1, 0.9, 1)
        _Bands ("Bands Along V", Float) = 0
        _Speed ("Scroll Speed", Float) = 0.5
        _Pulse ("Pulse Speed", Float) = 0
        _FadeAlong ("Fade Along V", Range(0, 1)) = 1
        _EdgeSoft ("Edge Softness", Float) = 1.5
        _Strength ("Strength", Float) = 0.8
        _FogAmount ("Fog Amount", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color, _Color2;
            float _Bands, _Speed, _Pulse, _FadeAlong, _EdgeSoft, _Strength, _FogAmount;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float depth : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float3 view : TEXCOORD3;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.depth = -UnityObjectToViewPos(v.vertex).z;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = 0;
                if (_Bands > 0)
                {
                    float wobble = sin(i.uv.x * 12.566 + _Time.y * 0.7) * 0.6;
                    t = 0.5 + 0.5 * sin((i.uv.y * _Bands - _Time.y * _Speed) * 6.2832 + wobble);
                }
                float3 c = lerp(_Color.rgb, _Color2.rgb, t);
                if (_Pulse > 0) c *= 0.75 + 0.25 * sin(_Time.y * _Pulse * 6.2832);
                float along = 1 - saturate(i.uv.y) * _FadeAlong;
                float facing = abs(dot(normalize(i.normal), normalize(i.view)));
                float edge = _EdgeSoft > 0 ? pow(facing, _EdgeSoft) : 1;
                float clear = unity_FogParams.z < 0 ? saturate(i.depth * unity_FogParams.z + unity_FogParams.w) : 1;
                float k = along * along * edge * _Strength * lerp(1, clear, _FogAmount);
                return fixed4(c * k, 1);
            }
            ENDCG
        }
    }
}
