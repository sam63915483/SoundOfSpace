// Double-sided variant of "Custom/sFuture Glass Early Queue"
// (Assets/sFuture Modules Pro/Materials/Glass_EarlyQueue.shader). Identical except
// `Cull Off`, so a flat window pane renders from BOTH sides (the player is inside
// the pod looking out). Queue "AlphaTest" (2450, <= 2500) keeps it BEHIND the
// planet's [ImageEffectOpaque] atmosphere/ocean post so the sky renders through
// the glass (the transparent-queue gotcha in CLAUDE.md).
Shader "Custom/StasisPodGlassDoubleSided" {
    Properties {
        _Color ("Color", Color) = (0.10, 0.12, 0.15, 0.35)
        _SpecColor ("Specular", Color) = (0.2, 0.2, 0.2, 1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.6
    }

    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        LOD 200
        Cull Off

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
