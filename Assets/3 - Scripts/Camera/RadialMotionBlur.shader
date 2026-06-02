Shader "Hidden/RadialMotionBlur"
{
    // Hybrid motion blur: per-pixel smear driven by the SUM of
    //   1) Unity's _CameraMotionVectorsTexture (real, accurate motion blur
    //      on geometry — close objects smear correctly with parallax)
    //   2) A synthetic radial component centered on the player's
    //      perspective-projected velocity direction (gives the "warp"
    //      feel on sky / atmosphere pixels which have zero real motion).
    //
    // Realistic geometry blur + dramatic warp-speed feel in one pass. Tune
    // the two contributions independently via _Strength and _SyntheticStrength.
    Properties
    {
        _MainTex            ("Source", 2D) = "white" {}
        _Center             ("Center (UV)", Vector) = (0.5, 0.5, 0, 0)
        _Strength           ("Real motion vector multiplier", Float) = 0
        _SyntheticStrength  ("Synthetic radial strength", Float) = 0
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraMotionVectorsTexture;
            float2    _Center;
            float     _Strength;
            float     _SyntheticStrength;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };
            struct v2f
            {
                float2 uv     : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                return o;
            }

            #define SAMPLES 12

            fixed4 frag(v2f i) : SV_Target
            {
                // 1. Real per-pixel motion vector from Unity.
                float2 realMotion = tex2D(_CameraMotionVectorsTexture, i.uv).xy * _Strength;

                // 2. Synthetic radial component — outward from the
                //    perspective-projected velocity direction. Distance from
                //    center scales magnitude so center pixels stay sharp
                //    and edge pixels smear hard (correct perspective parallax).
                float2 dir = i.uv - _Center;
                float dist = length(dir);
                float2 syntheticMotion = float2(0, 0);
                if (dist > 0.0001)
                {
                    syntheticMotion = (dir / dist) * dist * _SyntheticStrength;
                }

                float2 totalMotion = realMotion + syntheticMotion;
                float magSqr = dot(totalMotion, totalMotion);
                if (magSqr < 1e-8) return tex2D(_MainTex, i.uv);

                float4 sum = 0;
                [unroll]
                for (int s = 0; s < SAMPLES; s++)
                {
                    float t = (float)s / (SAMPLES - 1) - 0.5; // -0.5 .. +0.5
                    float2 sampleUV = i.uv + totalMotion * t;
                    sum += tex2D(_MainTex, sampleUV);
                }
                return sum / SAMPLES;
            }
            ENDCG
        }
    }
    Fallback Off
}
