// Full-screen "you are underwater" wash for the flooding Poolrooms. Murky colour
// absorption toward a tint, edge vignette (claustrophobic), a gentle refraction
// wobble and faint drifting caustics. Driven by PoolFlood via UnderwaterImageEffect
// (_Intensity 0 above the surface -> 1 when the camera is submerged).
//
// Standalone image effect, appended after the post-process stack by
// UnderwaterImageEffect; pass-through when _Intensity is ~0.
Shader "Hidden/PoolroomsUnderwater"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0,1)) = 0
        _TintColor ("Tint", Color) = (0.10, 0.34, 0.32, 1)
        _WarpStrength ("Warp", Range(0,0.05)) = 0.010
        _Vignette ("Vignette", Range(0,2)) = 0.9
        _Caustic ("Caustic", Range(0,1)) = 0.35
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity;
            float4 _TintColor;
            float _WarpStrength;
            float _Vignette;
            float _Caustic;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;

                // Gentle underwater refraction wobble.
                float w = _WarpStrength * _Intensity;
                uv.x += sin(uv.y * 22.0 + t * 1.7) * w;
                uv.y += cos(uv.x * 18.0 + t * 1.3) * w;

                fixed4 col = tex2D(_MainTex, uv);

                // Murky colour: multiplicative absorption toward the tint, plus a touch
                // of desaturation so the whole view sinks toward the water's hue.
                float3 murk = col.rgb * _TintColor.rgb;
                float g = dot(col.rgb, float3(0.299, 0.587, 0.114));
                murk = lerp(murk, g.xxx * _TintColor.rgb, 0.25);
                col.rgb = lerp(col.rgb, murk, _Intensity);

                // Faint drifting caustics.
                float2 cuv = i.uv * 6.0;
                float caust = sin(cuv.x + t * 1.1) * sin(cuv.y + t * 0.9)
                            + sin((cuv.x + cuv.y) * 0.7 - t * 0.6);
                caust = saturate(caust * 0.25 + 0.3);
                col.rgb += _TintColor.rgb * caust * _Caustic * _Intensity * 0.15;

                // Vignette — darken the edges, claustrophobic.
                float2 d = i.uv - 0.5;
                float vig = saturate(1.0 - dot(d, d) * (_Vignette * 3.0));
                col.rgb *= lerp(1.0, vig, _Intensity);

                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
