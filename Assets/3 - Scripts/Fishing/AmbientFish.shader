// Ambient fish, drawn instanced with PER-FISH colour and PER-FISH depth fade.
//
// Why this exists (Sam, 2026-09-09 playtest 2): "they don't seem to get less
// visible the deeper they are ... especially where some of them glow, the
// glowing ones can be at the bottom of the ocean and be the same visibility as
// if they were at the top."
//
// The first build leaned on the ocean post-effect to fade them, which was the
// wrong call. That effect does tint by water depth, but it tints TOWARD A
// BRIGHT LIT BLUE — a tinted fish is still a clearly readable shape — and an
// emissive fish is far brighter than the water, so it survives the tint almost
// untouched and then blooms on top of it. Fading has to be done on the fish.
//
// Doing it here also makes every fish cheaper rather than dearer: the stock
// Standard shader has no per-instance colour, so pass 1/2 needed a separate
// material and draw call per SPECIES. With the species tint and the fade moved
// into instance data, every fish sharing a tier's mesh draws in ONE call per
// submesh no matter how many species are on screen.
//
// The material carries the model part's own colour; the instance carries the
// species tint and the fade. The blend below is the same rule as
// FishSpeciesVisuals.BlendPartColour — take the tint's hue, keep the part's own
// brightness — so a fish in the water and a fish on the line agree.
Shader "Custom/AmbientFish"
{
    Properties
    {
        _PartColor  ("Model part colour", Color) = (1,1,1,1)
        _DeepColor  ("Deep water colour", Color) = (0.05,0.14,0.20,1)
        _TintBlend  ("Species tint blend", Range(0,1)) = 0.55
        _Glossiness ("Smoothness", Range(0,1)) = 0.55
        _Metallic   ("Metallic", Range(0,1)) = 0.05
    }

    SubShader
    {
        // Opaque, queue 2000. Deliberate: at 2500 or below the fish land in
        // _CameraDepthTexture, which is what lets the atmosphere and ocean
        // post-effects treat them as real scene geometry. Above 2500 they would
        // float visibly on top of the water. See CLAUDE.md's transparent-queue
        // rule and its corollary.
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGPROGRAM
        // noforwardadd: the sun only, never an extra pass per point light. Same
        // reasoning as the grass shader — a fish is small, underwater, and never
        // worth a second lighting pass.
        // noshadow / nolightmap: they neither cast nor receive, and the draw
        // call already passes ShadowCastingMode.Off, so these only strip
        // variants that would otherwise be compiled and shipped for nothing.
        #pragma surface surf Standard noforwardadd noshadow nolightmap
        #pragma multi_compile_instancing
        #pragma target 3.0

        struct Input { float3 worldPos; };

        fixed4 _PartColor;
        fixed4 _DeepColor;
        half _TintBlend;
        half _Glossiness;
        half _Metallic;

        UNITY_INSTANCING_BUFFER_START(Props)
            // rgb = this fish's species tint
            UNITY_DEFINE_INSTANCED_PROP(float4, _SpeciesTint)
            // x = fade (0 visible .. 1 completely lost in the water)
            // y = rarity glow strength
            UNITY_DEFINE_INSTANCED_PROP(float4, _FadeGlow)
        UNITY_INSTANCING_BUFFER_END(Props)

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _SpeciesTint);
            float4 fg   = UNITY_ACCESS_INSTANCED_PROP(Props, _FadeGlow);
            float fade  = saturate(fg.x);
            float glow  = fg.y;

            float3 mixed = lerp(_PartColor.rgb, tint.rgb, _TintBlend);
            float lumO = dot(_PartColor.rgb, float3(0.299, 0.587, 0.114));
            float lumM = dot(mixed,          float3(0.299, 0.587, 0.114));
            mixed *= clamp(lumO / max(lumM, 1e-4), 0.55, 1.8);

            // Fade toward the water itself rather than toward transparency: a
            // fish that has become the colour of the water around it has
            // vanished, and it stays fully opaque so it can still occlude
            // properly and stay out of the transparent queue.
            o.Albedo = lerp(mixed, _DeepColor.rgb, fade);

            // The glow fades with the SAME curve, which is the half Sam
            // actually noticed: a rare on the bottom must not shine like a rare
            // just under the surface.
            o.Emission = tint.rgb * glow * (1.0 - fade);

            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness * (1.0 - fade * 0.8);
            o.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
