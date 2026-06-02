Shader "Custom/sFuture Glass Early Queue" {
    Properties {
        _Color ("Color", Color) = (1,1,1,0.5)
        _SpecColor ("Specular", Color) = (0.2,0.2,0.2,1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
    }

    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        LOD 200

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
