// "Entering a black hole" vignette → tesseract. A chromatic swirl vignette grows in
// from the screen edges as _Intensity rises (screen-centred). Over the LAST 40%
// (_Intensity > 0.6) a crazily-tumbling nested 4D hypercube (tesseract) folds in
// AROUND THE BLACK HOLE'S CORE — it is anchored to the core's on-screen position
// (_CoreUV), NOT the screen centre, so it sits on the actual hole and tracks it as
// you move/look. Its lines take the screen's own colours.
//
// Standalone full-screen image effect, appended after the post-process stack by
// BlackHoleVignetteEffect; pass-through when _Intensity is ~0.
Shader "Hidden/BlackHoleVignette"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0,1)) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity;
            float4 _CoreUV;      // black-hole core position in screen UV (.xy); the tesseract centres here
            float _AnimTime;     // animation clock that speeds up the closer you get (set by BlackHoleCapture)
            float _KaleidoStrength;   // kaleidoscope mirror amount — warps the WHOLE effect (scene + swirl + tesseract)
            float _KaleidoWave;       // wavy heat-haze shimmer amount
            float _Sway;              // woozy whole-frame camera-sway amount
            float _Collapse;          // 0..1 rush-to-white singularity flash at the moment of crossing into the Backrooms
            float _CockpitMask;       // 1 while piloting, 0 on foot — edge-fades the kaleidoscope off the hull, occludes the tesseract behind the hull, +50% tesseract opacity
            uniform sampler2D_float _CameraDepthTexture;

            float2 rot2(float2 p, float a) { float s = sin(a), c = cos(a); return float2(p.x * c - p.y * s, p.x * s + p.y * c); }

            float segDist(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-6));
                return length(pa - ba * h);
            }

            // Woozy "camera sway" — a slow whole-frame roll + positional drift about the
            // hole, like a drunken/tripping camera. Screen-space so it works while piloting
            // (where the on-foot camera systems don't run). Identity at 0.
            float2 SwayWarp(float2 uv, float2 ctr, float sway, float tt, float aspect)
            {
                if (sway <= 0.0001) return uv;
                float ang = (sin(tt * 0.7) * 0.6 + sin(tt * 0.27 + 1.3) * 0.4) * 0.08 * sway;
                float2 c = uv - ctr; c.x *= aspect;
                float s = sin(ang), co = cos(ang);
                c = float2(c.x * co - c.y * s, c.x * s + c.y * co);
                c.x /= aspect;
                float2 drift = float2(sin(tt * 0.5), cos(tt * 0.43 + 2.1)) * 0.03 * sway;
                return ctr + c + drift;
            }

            // Kaleidoscope UV warp (the mushroom-trip 6-way fold), centred on the hole.
            // Applied to the whole frame BEFORE the swirl/tesseract are computed, so the
            // scene, swirl AND tesseract all mirror + shimmer together. Returns uv unchanged
            // when both gates are ~0. (Same fold math as Hidden/RawFishTrip.)
            float2 KaleidoWarp(float2 uv, float2 ctr, float strength, float wave, float tt, float aspect)
            {
                if (strength <= 0.0001 && wave <= 0.0001) return uv;
                float2 c = uv - ctr; c.x *= aspect;
                float r = length(c);
                float ang = atan2(c.y, c.x) + tt * 0.06;
                float wedge = 6.2831853 / 6.0;
                float folded = abs((ang - wedge * floor(ang / wedge)) - wedge * 0.5);
                r += sin(folded * 4.0 + tt * 1.3) * 0.012 * strength;
                float mw = (0.55 + 0.45 * sin(tt * 0.4)) * strength;
                // Fade the fold out toward the screen edges (measured from the hole) so it only
                // kaleidoscopes the area right AROUND the hole — never the bright sun/horizon at
                // the periphery, which when mirrored 6-fold turned into a messy scramble on foot.
                // This edge-fade is exactly what makes the COCKPIT version look good; it used to
                // be gated to the cockpit only (via _CockpitMask), leaving the on-foot fold
                // full-screen and ugly. Applying the SAME fade in both modes gives the on-foot
                // dive the cockpit's clean, tight fold in every look direction. (In the cockpit
                // the hull also occludes the bright exterior, which is why it was always clean.)
                mw *= 1.0 - smoothstep(0.45, 0.85, r);
                float2 mir = float2(cos(folded) * r / aspect, sin(folded) * r) + ctr;
                float2 su = lerp(uv, mir, mw);
                float waveAmp = 0.018 * wave * (0.55 + 0.45 * sin(tt * 0.32));
                su.x += sin(su.y * 4.5 + tt * 0.5) * waveAmp;
                su.y += sin(su.x * 3.8 + tt * 0.41 + 1.7) * waveAmp;
                return su;
            }

            // 16 hypercube vertices (±1 in 4D) and its 32 edges (vertex pairs differing
            // in exactly one coordinate). Hardcoded to avoid bitwise int ops.
            static const float4 TVERT[16] = {
                float4(-1,-1,-1,-1), float4( 1,-1,-1,-1), float4(-1, 1,-1,-1), float4( 1, 1,-1,-1),
                float4(-1,-1, 1,-1), float4( 1,-1, 1,-1), float4(-1, 1, 1,-1), float4( 1, 1, 1,-1),
                float4(-1,-1,-1, 1), float4( 1,-1,-1, 1), float4(-1, 1,-1, 1), float4( 1, 1,-1, 1),
                float4(-1,-1, 1, 1), float4( 1,-1, 1, 1), float4(-1, 1, 1, 1), float4( 1, 1, 1, 1)
            };
            static const int2 TEDGE[32] = {
                int2(0,1),  int2(2,3),  int2(4,5),  int2(6,7),  int2(8,9),  int2(10,11), int2(12,13), int2(14,15),
                int2(0,2),  int2(1,3),  int2(4,6),  int2(5,7),  int2(8,10), int2(9,11),  int2(12,14), int2(13,15),
                int2(0,4),  int2(1,5),  int2(2,6),  int2(3,7),  int2(8,12), int2(9,13),  int2(10,14), int2(11,15),
                int2(0,8),  int2(1,9),  int2(2,10), int2(3,11), int2(4,12), int2(5,13),  int2(6,14),  int2(7,15)
            };

            // One tesseract, tumbling in 4D over time (multiple rotation planes = the
            // "crazy moving" look) plus a depth fold that deepens as you near the core.
            float TessWire(float2 p, float scale, float tt, float fold)
            {
                float2 proj[16];
                [unroll]
                for (int vi = 0; vi < 16; vi++)
                {
                    float4 v = TVERT[vi];
                    v.xw = rot2(v.xw, tt * 0.60 + fold);   // 4D planes, animated
                    v.yz = rot2(v.yz, tt * 0.45);
                    v.zw = rot2(v.zw, tt * 0.33);
                    v.xy = rot2(v.xy, tt * 0.22);
                    float3 q = v.xyz / (3.0 - v.w);        // 4D -> 3D perspective
                    float2 p2 = q.xy / (3.4 - q.z);        // 3D -> 2D perspective
                    proj[vi] = p2 * scale;
                }
                float minD = 1e9;
                [unroll]
                for (int e = 0; e < 32; e++)
                    minD = min(minD, segDist(p, proj[TEDGE[e].x], proj[TEDGE[e].y]));
                return smoothstep(0.0010, 0.0, minD);   // very thin crisp wireframe
            }

            // Two nested tesseracts (cube-in-a-cube), centred on the BLACK-HOLE CORE,
            // folding/zooming deeper as you reach the centre.
            fixed4 Tesseract(float2 uv, float t4)   // rgb = amber line colour, a = wire coverage
            {
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float2 b = (uv - _CoreUV.xy); b.x *= aspect;   // anchored to the core
                float lum = dot(tex2D(_MainTex, uv).rgb, float3(0.299, 0.587, 0.114));

                float tt = _AnimTime;   // accelerates near the core, so the whole stream speeds up

                // Endless stream: each layer is born tiny at the core, grows exponentially
                // outward, and fades as it leaves the screen — then its cycle wraps and a
                // fresh small one spawns. Staggered phases make a continuous flow. Faded
                // (off-screen) layers are skipped so we never render tesseracts nobody sees.
                const int   N    = 6;
                const float sMin = 0.18;   // spawn size at the core
                const float sMax = 7.0;    // size at which it's well off-screen
                const float spd  = 0.16;   // grow / cycle speed

                float glow = 0.0;
                [unroll]
                for (int k = 0; k < N; k++)
                {
                    float g = frac(tt * spd + (float)k / (float)N);
                    float a = smoothstep(0.0, 0.06, g) * (1.0 - smoothstep(0.80, 1.0, g));
                    if (a >= 0.01)   // skip faded / off-screen layers (no over-render)
                    {
                        float s  = sMin * pow(sMax / sMin, g) * (1.0 + t4 * 0.4);
                        float ph = tt * 0.5 + (float)k * 1.1;   // tumble
                        glow += TessWire(b, s, ph, 0.5 + t4 * 0.5) * a;
                    }
                }

                // Amber — the colour of the glowing dust swirling into the core: warmer in
                // shadow, brighter where the scene behind glows. Small floor keeps the amber
                // visible even over the black event horizon.
                // Solid amber (the dust colour), only gently brighter where the scene glows
                // so it stays a real colour instead of blowing out to white. Coverage = glow.
                fixed3 amber = lerp(fixed3(1.0, 0.55, 0.18), fixed3(1.0, 0.82, 0.45), saturate(lum * 2.0));
                amber *= (0.9 + 0.3 * lum);
                return fixed4(amber, saturate(glow));
            }

            fixed4 frag (v2f_img i) : SV_Target
            {
                float aspectK = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                // Woozy camera-sway first, then the kaleidoscope fold — so the scene, swirl
                // AND tesseract all sway, fold and shimmer together. Everything below uses wuv.
                float2 suv0 = SwayWarp(i.uv, _CoreUV.xy, _Sway, _Time.y, aspectK);
                float2 wuv = KaleidoWarp(suv0, _CoreUV.xy, _KaleidoStrength, _KaleidoWave, _Time.y, aspectK);

                fixed4 original = tex2D(_MainTex, wuv);
                if (_Intensity <= 0.0001 && _Collapse <= 0.0001) return original;

                // Swirl AND tesseract anchor to the black-hole core on screen, so the
                // whole effect radiates from the hole rather than the screen centre.
                float2 center = _CoreUV.xy;
                float2 d = wuv - center;
                float r = length(d) / 0.70710678;            // 0 at the core .. ~1 toward the corners

                float innerEdge = 1.0 - _Intensity;
                float vign = smoothstep(innerEdge, 1.0, r) * _Intensity;
                float t4 = smoothstep(0.60, 1.0, _Intensity); // LAST 40% -> tesseract folds in

                if (vign <= 0.0001 && t4 <= 0.0001 && _Collapse <= 0.0001) return original;

                fixed3 finalCol = original.rgb;

                if (vign > 0.0001)
                {
                    // Swirl + slight inward pull + radial chromatic aberration from the real
                    // screen colours, then darken the rim toward black.
                    float swirl = vign * (2.0 + 5.0 * _Intensity);
                    float2 sd = rot2(d, swirl) * (1.0 - vign * 0.18);
                    float2 suv = center + sd;
                    float2 dir = d / max(length(d), 1e-5);
                    float ca = vign * 0.05;
                    fixed3 col;
                    col.r = tex2D(_MainTex, suv + dir * ca).r;
                    col.g = tex2D(_MainTex, suv).g;
                    col.b = tex2D(_MainTex, suv - dir * ca).b;
                    col *= 1.0 - smoothstep(0.8, 1.25, r) * _Intensity;
                    finalCol = lerp(original.rgb, col, vign);
                }

                // Smooth hand-off: as the tesseract folds in (t4: 0→1) the swirl calms and
                // darkens into the void behind it, then the core-anchored tesseract adds in.
                if (t4 > 0.001)
                {
                    // Gentle fade-in, then OPAQUE amber lines: alpha-blend toward the line
                    // colour (not additive), so they read as a solid wireframe instead of a
                    // blown-out glow. More opaque the closer you get to the core.
                    float appear  = smoothstep(0.0, 0.25, t4);
                    float opacity = appear * lerp(0.18, 0.5, t4) * (1.0 + 0.5 * _CockpitMask);   // translucent; +50% while piloting
                    finalCol *= (1.0 - 0.6 * t4);
                    fixed4 tess = Tesseract(wuv, t4);
                    // In the ship, hide the tesseract behind the hull (near geometry) so it
                    // only shows through the window / against space. No effect on foot.
                    float occ = 1.0;
                    if (_CockpitMask > 0.5)
                        occ = smoothstep(3.0, 8.0, LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv)));
                    finalCol = lerp(finalCol, tess.rgb, saturate(tess.a) * opacity * occ);
                }

                // Singularity flash: rush the whole frame to white at the moment of crossing.
                finalCol = lerp(finalCol, fixed3(1.0, 1.0, 1.0), saturate(_Collapse));
                return fixed4(finalCol, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
