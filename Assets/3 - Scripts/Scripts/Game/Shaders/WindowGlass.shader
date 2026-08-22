// Glass you LOOK THROUGH at the planet. Identical to AtmosphereGlass except for
// the render queue, which is the whole point.
//
// ── WHY THIS EXISTS: AtmosphereGlass's premise is wrong for windows ──────
// AtmosphereGlass puts glass at queue 2450 so the [ImageEffectOpaque]
// atmosphere/ocean composite paints the pane itself (it fixed windows "glowing"
// on the night side when viewed from OUTSIDE). That reasoning holds for a pane
// you look AT. It is actively wrong for a pane you look THROUGH.
//
// The atmosphere post reads _CameraDepthTexture and does, in effect:
//
//     dstToSurface        = min(sceneDepth, dstToOcean);
//     dstThroughAtmosphere = min(hitInfo.y, dstToSurface - dstToAtmosphere);
//     if (dstThroughAtmosphere > 0) { ...scatter... }   // else originalCol, untouched
//
// A pane at queue <= 2500 lands in that depth texture at arm's length, so
// sceneDepth is ~0 for every window pixel, the branch is skipped, and those
// pixels keep their RAW colour. Standing in a village house that read as:
//   • daytime  — sky dark through the window (the blue IS the post; there is no
//     blue skybox behind it), while outside was blue;
//   • nighttime — ground and trees grey and lit through the window, because the
//     post is also what multiplies the night side down (reflectedLight =
//     originalCol * reflectedLightStrength). Skip it and you keep raw ambient.
//
// ZWrite Off and Cast Shadows = Off are NOT enough to stay out of that texture —
// both were tried. Queue > 2500 is what works, and it is what the two panes in
// this project that behave correctly already do: Shuttle_PodGlass.mat
// (m_CustomRenderQueue 3000) and StasisPodGlass.shader, whose header records the
// same finding from the stasis pod.
//
// The queue is baked into the SHADER, not set on the material, because a
// material queue override does not survive reimport.
//
// TRADE-OFF, accepted deliberately: drawing after the composite means the pane
// is no longer tinted by the atmosphere, so at night it reads as a faint tint
// over a dark scene rather than going dark with the walls. That is exactly how
// the shuttle's windows already behave. Lower the material's _Color alpha if it
// ever reads too strongly.
//
// AtmosphereGlass is left alone — the space net still uses it, and the net was
// never the thing you look through at a planet.
Shader "Custom/WindowGlass"
{
    Properties
    {
        _Color ("Color (tint, alpha = opacity)", Color) = (1,1,1,0.5)
        _MainTex ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
    }
    SubShader
    {
        // Queue Transparent (3000) > 2500: draws AFTER the atmosphere/ocean
        // composite, so it tints the finished planet image instead of replacing
        // the depth the composite is computed from.
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        CGPROGRAM
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
