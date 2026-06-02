Shader "Hidden/RawFishTrip"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Color Intensity", Range(0,1)) = 0
        _KaleidoStrength ("Kaleidoscope Strength", Range(0,1)) = 0
        _WaveStrength ("Wave Strength", Range(0,1)) = 0
        _TripTime ("Trip Time", Float) = 0
        _Aspect ("Aspect", Float) = 1.7777
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity;
            float _KaleidoStrength;
            float _WaveStrength;
            float _TripTime;
            float _Aspect;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // RGB <-> HSV (standard formulas, branchless)
            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            // Two-octave breathing wave — non-harmonic frequencies prevent obvious looping.
            float breathe(float t)
            {
                return 0.5 + 0.5 * (0.66 * sin(t * 0.5) + 0.34 * sin(t * 0.21 + 1.7));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _TripTime;
                float kc = _Intensity;        // colour-shift gate (rarity-independent)
                float kk = _KaleidoStrength;  // kaleidoscope/geometry gate (rarity-scaled)

                // ── Spatial folding (gated by _KaleidoStrength) ────────────
                float2 c = i.uv - 0.5;
                c.x *= _Aspect;

                float r = length(c);
                float a = atan2(c.y, c.x);

                // Slow rotation of the kaleidoscope's "axis"
                a += t * 0.12;

                // Kaleidoscope mirror: 6-segment wedge, fold via abs.
                float segments = 6.0;
                float wedge = 6.2831853 / segments;
                float folded = a - wedge * floor(a / wedge);
                folded = abs(folded - wedge * 0.5);

                // Subtle radial breathing wobble — only when kaleido is active.
                float wobble = sin(folded * 4.0 + t * 1.3) * 0.012 * kk;
                r += wobble;

                float mirrorWeight = (0.55 + 0.45 * sin(t * 0.4)) * kk;
                float2 mirrored;
                mirrored.x = cos(folded) * r;
                mirrored.y = sin(folded) * r;
                mirrored.x /= _Aspect;
                mirrored += 0.5;

                float2 sampleUV = lerp(i.uv, mirrored, mirrorWeight);

                // ── Chill wavy shimmer (gated by _WaveStrength) ────────────
                // Each axis is displaced by a sine of the OTHER axis — gives
                // the underwater / heat-haze feel. Two non-harmonic frequencies
                // and a slow breathing amplitude keep it from looking looped.
                float waveBreathe = 0.55 + 0.45 * sin(t * 0.32);
                float waveAmp = 0.018 * _WaveStrength * waveBreathe;
                sampleUV.x += sin(sampleUV.y * 4.5 + t * 0.5) * waveAmp;
                sampleUV.y += sin(sampleUV.x * 3.8 + t * 0.41 + 1.7) * waveAmp;

                // Per-channel chromatic separation — geometry-flavoured, gated by kk.
                float ca = 0.004 * kk * (0.5 + 0.5 * sin(t * 0.9));
                float2 dirCA = float2(cos(t * 0.6), sin(t * 0.6)) * ca;

                float4 sR = tex2D(_MainTex, sampleUV + dirCA);
                float4 sG = tex2D(_MainTex, sampleUV);
                float4 sB = tex2D(_MainTex, sampleUV - dirCA);
                float3 col = float3(sR.r, sG.g, sB.b);

                // ── Colour filter cycling (gated by _Intensity) ────────────
                float3 hsv = rgb2hsv(col);
                hsv.x = frac(hsv.x + t * 0.07 * kc);                          // hue drift only while colour-active
                hsv.y = saturate(hsv.y * (1.0 + 0.6 * kc * sin(t * 0.7 + 1.3)));
                hsv.z = saturate(hsv.z * (1.0 + 0.15 * kc * cos(t * 1.1)));
                float3 tripped = hsv2rgb(hsv);

                // Slow drifting colour wash on top — like coloured gels.
                float3 wash = 0.5 + 0.5 * float3(
                    sin(t * 0.31 + 0.0),
                    sin(t * 0.27 + 2.1),
                    sin(t * 0.23 + 4.2));
                tripped = lerp(tripped, tripped * (0.6 + 0.4 * wash) + 0.15 * wash, 0.55 * kc);

                // Vignette pulse — pulls focus inward when colour peaks.
                float vig = smoothstep(0.95, 0.35, length(i.uv - 0.5) * 1.4);
                tripped *= lerp(1.0, vig, 0.3 * kc);

                // Final blend uses a breathing envelope so the whole effect
                // pulses smoothly rather than holding flat.
                float env = lerp(0.55, 1.0, breathe(t));
                float blend = saturate(kc * env);

                float4 outCol;
                outCol.rgb = lerp(sG.rgb, tripped, blend);
                outCol.a = sG.a;
                return outCol;
            }
            ENDCG
        }
    }
}
