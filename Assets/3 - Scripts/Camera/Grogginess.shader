// Grogginess full-screen image effect for the Mission 1 wake-up sequence.
//
// Standalone post effect: double-vision (horizontal twin offset) + a soft box
// blur, both scaled by _Intensity (1 = fully groggy, 0 = perfectly sharp).
// Driven by GrogginessImageEffect, which is attached to the camera ONLY during
// the cold open and removed at handoff. Touches none of the planet/atmosphere
// post-processing — it runs last, on the already-composited image.
Shader "Hidden/Grogginess"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0,1)) = 0
        _Offset ("Double-Vision Offset", Float) = 0.018
        _Blur ("Blur Radius", Float) = 0.0035
    }
    SubShader
    {
        // No culling or depth — standard full-screen blit.
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity;
            float _Offset;
            float _Blur;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float amt = saturate(_Intensity);
                fixed4 sharp = tex2D(_MainTex, i.uv);
                if (amt <= 0.0001) return sharp;

                // Double vision: average a left/right shifted pair.
                float2 off = float2(_Offset * amt, 0.0);
                fixed4 dbl = (tex2D(_MainTex, i.uv + off) + tex2D(_MainTex, i.uv - off)) * 0.5;

                // Cheap 4-tap box blur scaled by intensity.
                float b = _Blur * amt;
                fixed4 blur = (tex2D(_MainTex, i.uv + float2(b, 0.0))
                             + tex2D(_MainTex, i.uv - float2(b, 0.0))
                             + tex2D(_MainTex, i.uv + float2(0.0, b))
                             + tex2D(_MainTex, i.uv - float2(0.0, b))) * 0.25;

                fixed4 groggy = lerp(blur, dbl, 0.5);
                return lerp(sharp, groggy, amt);
            }
            ENDCG
        }
    }
    Fallback Off
}
