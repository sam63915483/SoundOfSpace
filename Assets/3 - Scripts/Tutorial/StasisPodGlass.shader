// Smoky double-sided window glass for the stasis pod (player is inside looking out).
//   • `Cull Off`  — a flat pane renders from BOTH sides.
//   • `ZWrite Off` — the pane never occludes depth.
//   • Queue "Transparent" (3000) — the pane renders AFTER the planet's atmosphere
//     and ocean, which are an [ImageEffectOpaque] post-process (CustomPostProcessing)
//     that composites between the opaque queue (<= 2500) and transparent geometry.
//     At an EARLY queue (<= 2500) the pane drew BEFORE that composite, so you saw
//     bare terrain through the glass but no atmosphere/ocean. Drawing it in the
//     Transparent queue tints the already-composited planet+atmosphere+ocean image,
//     so all three show through the glass.
Shader "Custom/StasisPodGlassDoubleSided" {
    Properties {
        _Color ("Color", Color) = (0.10, 0.12, 0.15, 0.35)
        _SpecColor ("Specular", Color) = (0.2, 0.2, 0.2, 1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.6
    }

    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 200
        Cull Off
        ZWrite Off

        CGPROGRAM
        #pragma surface surf StandardSpecular alpha:premul
        #pragma target 3.0

        struct Input {
            float2 uv_MainTex;
        };

        half _Glossiness;
        fixed4 _Color;

        void surf (Input IN, inout SurfaceOutputStandardSpecular o) {
            o.Albedo = _Color.rgb;
            o.Specular = _SpecColor.rgb;
            o.Smoothness = _Glossiness;
            o.Alpha = _Color.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
