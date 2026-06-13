// Lit, semi-transparent glass that the atmosphere/ocean post-process actually
// composites correctly.
//
// WHY THIS EXISTS: the village windows and the ship's space-net used the
// built-in Standard shader in Transparent mode (render queue 3000). The
// atmosphere is an [ImageEffectOpaque] post effect — it only touches geometry
// at queue <= 2500. So those transparents drew AFTER it and never received the
// day/night scattering + aerial perspective the walls get: on the dark side
// they kept their raw colour (glowing at night), and at a distance they read
// darker than the lit-by-atmosphere wall around them.
//
// Setting the queue to <= 2500 on the *material* doesn't stick — Unity's
// Standard-material logic resets a material queue override back to Transparent
// (3000) on the next reimport. Baking the queue into the SHADER (here) is the
// only durable fix, which is why the project already has a "Glass Early Queue"
// shader; this one adds a main-texture slot + plain Lambert lighting so it can
// host the existing window/net materials unchanged.
Shader "Custom/AtmosphereGlass"
{
    Properties
    {
        _Color ("Color (tint, alpha = opacity)", Color) = (1,1,1,0.5)
        _MainTex ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
    }
    SubShader
    {
        // Queue AlphaTest (2450) <= 2500 so the atmosphere composites it with the
        // opaque scene. Baked here so it can't be reset like a material override.
        Tags { "RenderType"="Transparent" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        LOD 200

        // Standard alpha blend, two-sided (windows/net read from both faces),
        // depth-test on but no depth write (normal transparent behaviour).
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        CGPROGRAM
        // Lambert so the glass darkens on the planet's night side exactly like
        // the walls (it's the day/night match that was missing). fullforwardshadows
        // lets cast shadows fall on it so it doesn't glow inside a shadow.
        #pragma surface surf Lambert fullforwardshadows alpha:fade
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;

        struct Input { float2 uv_MainTex; };

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
