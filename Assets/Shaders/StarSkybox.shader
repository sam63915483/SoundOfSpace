// Custom/StarSkybox — Unity's Skybox/Panoramic (lat-long) sampling of the 8k
// star map PLUS a procedural layer of pixel-sharp stars on top (Sam,
// 2026-09-15: "sharper brighter stars on top to make it seem even higher
// quality", the Outer Wilds trick — including the 4-point spikes on the
// bright ones).
//
// Why it lives in the SKYBOX shader and nowhere else: everything that happens
// to the sky in this game — the ocean covering it below the horizon, the
// atmosphere post adding blue by day and multiplying the night side down, the
// cave / underwater rules — is done by the planet post-effect, which runs right
// after the skybox pass ([ImageEffectOpaque]) and before transparents. Stars
// drawn HERE get exactly the same treatment as the texture behind them, so they
// can never look different through water or air. (Space dust had to move to
// queue 3000 and renders after that post — the opposite trade.)
//
// The stars are analytic (no texture): one candidate star per cell of a cube-
// face grid over the sphere, two grids (sparse bright + dense dim), each star a
// tiny gaussian point sized in SCREEN PIXELS via derivatives, so it stays crisp
// at any resolution or zoom. The bright layer searches its 3x3 neighbourhood so
// spikes can cross cell borders; cube-face seam pixels (where derivatives are
// meaningless) are skipped.
//
// POLE FIX (2026-09-15, the "weird lines" straight up/down): lat-long sampling
// near a pole has a huge longitude derivative per pixel, so the hardware picks
// a very blurry mip and smears every star radially — a warp-speed burst at the
// zenith. Unity's own Skybox/Panoramic does the same; the old cubemap import had
// no pole. The mip choice is driven by the LATITUDE derivative only (the
// longitude one is capped at 2x it): the source is already smooth sideways at
// the poles, so under-sampling there loses nothing.
//
// Peak star brightness is capped (_StarCap, default 1 = the map's own maximum)
// so by day the sharp stars are exactly as visible as the map's brightest ones.
Shader "Custom/StarSkybox"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (.5, .5, .5, .5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _MainTex ("Spherical (HDR)", 2D) = "grey" {}

        [Header(Sharp stars)]
        [Toggle] _Stars ("Stars On", Float) = 1
        _StarDensity ("Density: bright layer (fraction of cells lit)", Range(0, 1)) = 0.06
        _StarDensityDim ("Density: dim layer (fraction of cells lit)", Range(0, 1)) = 0.05
        _StarBrightness ("Brightness", Range(0, 4)) = 2.0
        _StarCap ("Peak cap (1 = as bright as the map)", Range(0.1, 4)) = 1.0
        _StarSize ("Size (pixels)", Range(0.4, 4)) = 1.3
        _StarColorVariation ("Colour variation", Range(0, 1)) = 0.5
        _StarTwinkle ("Twinkle amount", Range(0, 1)) = 0.45
        _StarTwinkleSpeed ("Twinkle speed", Range(0, 8)) = 2.0
        _StarSeed ("Seed", Float) = 7
        _StarGridBright ("Grid: bright layer (cells per face edge)", Range(8, 128)) = 40
        _StarGridDim ("Grid: dim layer (cells per face edge)", Range(8, 256)) = 110

        [Header(Spikes on the bright stars)]
        _SpikeLength ("Spike length (pixels)", Range(0, 30)) = 9
        _SpikeStrength ("Spike strength", Range(0, 2)) = 1.0
        _SpikeThreshold ("Spikes on stars brighter than", Range(0, 1)) = 0.5
        _SpikeThreshold8 ("Big 8-spike stars brighter than", Range(0, 1)) = 0.82
        _SpikePulse ("Breathing (spikes + core follow the twinkle)", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _Tint;
            half _Exposure;
            float _Rotation;

            float _Stars;
            float _StarDensity, _StarDensityDim, _StarBrightness, _StarCap, _StarSize;
            float _StarColorVariation, _StarTwinkle, _StarTwinkleSpeed, _StarSeed;
            float _StarGridBright, _StarGridDim;
            float _SpikeLength, _SpikeStrength, _SpikeThreshold, _SpikeThreshold8, _SpikePulse;

            // ── Unity's panoramic mapping, verbatim, so the map's orientation is unchanged ──
            inline float2 ToRadialCoords(float3 coords)
            {
                float3 n = normalize(coords);
                float latitude = acos(n.y);
                float longitude = atan2(n.z, n.x);
                float2 sphereCoords = float2(longitude, latitude) * float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);
                return float2(0.5, 1.0) - sphereCoords;
            }

            float3 RotateAroundYInDegrees(float3 vertex, float degrees)
            {
                float alpha = degrees * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, vertex.xz), vertex.y).xzy;
            }

            struct appdata_t { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 rotated = RotateAroundYInDegrees(v.vertex.xyz, _Rotation);
                o.vertex = UnityObjectToClipPos(rotated);
                o.texcoord = v.vertex.xyz;
                return o;
            }

            // ── Integer hash (pcg3d) — stable, no sin() precision cliffs ──
            uint3 pcg3d(uint3 v)
            {
                v = v * 1664525u + 1013904223u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                v ^= v >> 16u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                return v;
            }
            float3 hash3(uint3 v) { return float3(pcg3d(v)) * (1.0 / 4294967296.0); }

            // Direction -> cube face index + face uv in [-1,1]² (a near-uniform grid on the sphere).
            void CubeFace(float3 d, out float face, out float2 uv)
            {
                float3 a = abs(d);
                if (a.x >= a.y && a.x >= a.z) { face = d.x > 0 ? 0 : 1; uv = float2(d.x > 0 ? -d.z : d.z, d.y) / a.x; }
                else if (a.y >= a.z)          { face = d.y > 0 ? 2 : 3; uv = float2(d.x, d.y > 0 ? -d.z : d.z) / a.y; }
                else                          { face = d.z > 0 ? 4 : 5; uv = float2(d.z > 0 ? d.x : -d.x, d.y) / a.z; }
            }

            // One star layer: additive RGB for this pixel. reach = 0 (own cell only) or
            // 1 (3x3 — needed when spikes can leave the cell). spikeLen 0 = no spikes.
            float3 StarLayer(float face, float2 uv, float gridN, float density, float layerSeed,
                             float brightScale, int reach, float spikeLen)
            {
                float2 p = (uv * 0.5 + 0.5) * gridN;
                // Cell units -> screen pixels (per axis), taken BEFORE any early-out
                // (derivatives must not sit past a divergent branch). A cube-face seam
                // pixel sees a jump of many cells: skip it, its derivatives are garbage.
                float2 fw = fwidth(p);
                if (fw.x > 0.5 || fw.y > 0.5) return 0;
                float2 pxPerCell = 1.0 / max(fw, 1e-5);
                float2 cell0 = floor(p);

                float3 acc = 0;
                [unroll] for (int oy = -1; oy <= 1; oy++)
                [unroll] for (int ox = -1; ox <= 1; ox++)
                {
                    if (abs(ox) > reach || abs(oy) > reach) continue;
                    float2 cell = cell0 + float2(ox, oy);
                    if (cell.x < 0 || cell.y < 0 || cell.x >= gridN || cell.y >= gridN) continue;   // other faces own those

                    uint3 key = uint3((uint)cell.x, (uint)cell.y, (uint)face) + uint3(0x9E37u, 0x79B9u, (uint)(_StarSeed * 131.0 + layerSeed));
                    float3 h1 = hash3(key);
                    float3 h2 = hash3(key + uint3(0x5bd1u, 0xe995u, 0x7f4au));
                    if (h1.x > density) continue;                     // no star in this cell

                    // Position inside its cell with a margin so the CORE never crosses a cell edge.
                    float2 sp = cell + 0.25 + 0.5 * h1.yz;
                    float2 dPx = (p - sp) * pxPerCell;

                    // Brightness: more dim than bright (quadratic); bigger cores for the bright ones.
                    float b = h2.x * h2.x;

                    // Twinkle: a slow per-star breath (random phase + rate) plus a faint,
                    // faster flicker. `wob` (-1..1) drives brightness AND, scaled by
                    // _SpikePulse, the core size and spike length, so the star visibly
                    // breathes. Free: the sky is re-evaluated per pixel every frame anyway.
                    float wob = sin(_Time.y * _StarTwinkleSpeed * (0.6 + 0.8 * h2.z) + h1.x * 40.0);
                    float flick = sin(_Time.y * _StarTwinkleSpeed * (4.5 + 3.0 * h1.y) + h2.y * 50.0);
                    float tw = saturate(1.0 - _StarTwinkle * (0.5 + 0.5 * wob) - 0.25 * _StarTwinkle * (0.5 + 0.5 * flick));   // saturate: at Twinkle 1 this could go negative
                    float pulse = 1.0 + _SpikePulse * 0.5 * wob;

                    // The brightest tier: bigger core, 8 arms (4 axis + 4 diagonal).
                    float big = (spikeLen > 0.0 && b > _SpikeThreshold8) ? 1.0 : 0.0;
                    float size = _StarSize * (0.6 + 0.8 * b) * (1.0 + 0.6 * big) * (1.0 + 0.5 * (pulse - 1.0));
                    float i = exp(-dot(dPx, dPx) / (size * size));

                    // Diffraction spikes on the brighter stars: thin arms, length grows
                    // with brightness above the threshold, soft tapered tips.
                    if (spikeLen > 0.0 && b > _SpikeThreshold)
                    {
                        float t = sqrt((b - _SpikeThreshold) / max(1e-3, 1.0 - _SpikeThreshold));   // ramps in fast, so spiked stars read as spiked
                        float L = spikeLen * (0.35 + 0.65 * t) * (1.0 + 0.5 * big) * pulse;
                        float w = 0.75 + 0.35 * big;
                        float ax = abs(dPx.x), ay = abs(dPx.y);
                        float armX = exp(-ay * ay / (w * w)) * pow(saturate(1.0 - ax / L), 2.0);
                        float armY = exp(-ax * ax / (w * w)) * pow(saturate(1.0 - ay / L), 2.0);
                        float arms = max(armX, armY);
                        if (big > 0.5)
                        {
                            // Diagonal arms: the same test in a 45°-rotated frame, a bit shorter.
                            float2 dq = float2(dPx.x + dPx.y, dPx.x - dPx.y) * 0.70710678;
                            float Ld = L * 0.7;
                            float qx = abs(dq.x), qy = abs(dq.y);
                            float armD1 = exp(-qy * qy / (w * w)) * pow(saturate(1.0 - qx / Ld), 2.0);
                            float armD2 = exp(-qx * qx / (w * w)) * pow(saturate(1.0 - qy / Ld), 2.0);
                            arms = max(arms, 0.85 * max(armD1, armD2));
                        }
                        i = max(i, _SpikeStrength * t * arms);
                    }

                    // Colour temperature: cool blue-white ... warm orange-white.
                    float3 cool = float3(0.72, 0.84, 1.0);
                    float3 warm = float3(1.0, 0.82, 0.62);
                    float3 tint = lerp(1.0, lerp(cool, warm, h2.y), _StarColorVariation);

                    // Cap THIS star first, then twinkle it. Twinkling before the cap was
                    // invisible on every bright star: they sat far above 1.0 and clamped
                    // flat, so the dimming never reached the screen (Sam, 2026-09-15:
                    // "didn't notice any stars twinkling").
                    acc += tint * (min(b * brightScale * _StarBrightness * i, _StarCap) * tw);
                }
                return acc;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.texcoord);

                // Base map — same mapping as Skybox/Panoramic. Derivatives fixed across the
                // atan2 wrap (the one-pixel mip seam line), and the longitude derivative
                // capped so the poles don't pick a smeared mip (see header).
                float2 tc = ToRadialCoords(dir);
                float2 dx = ddx(tc), dy = ddy(tc);
                if (abs(dx.x) > 0.5) dx.x -= sign(dx.x);
                if (abs(dy.x) > 0.5) dy.x -= sign(dy.x);
                float du = max(abs(dx.x), abs(dy.x));
                float dv = max(abs(dx.y), abs(dy.y));
                float capU = dv * 2.0 + 1e-6;
                if (du > capU) { float s = capU / du; dx.x *= s; dy.x *= s; }
                float3 c = tex2Dgrad(_MainTex, tc, dx, dy).rgb;

                if (_Stars > 0.5)
                {
                    // i.texcoord is the UNROTATED vertex (the mesh was rotated instead, as in
                    // Unity's shader), so this same dir is the map's sample direction: stars
                    // seeded from it turn with the map under _Rotation.
                    float face; float2 uv;
                    CubeFace(dir, face, uv);
                    float3 stars = StarLayer(face, uv, _StarGridBright, _StarDensity,    11.0, 1.0, 1, _SpikeLength)
                                 + StarLayer(face, uv, _StarGridDim,    _StarDensityDim, 23.0, 0.5, 0, 0.0);
                    stars = min(stars, _StarCap.xxx);   // each star is capped inside StarLayer; this only catches two overlapping
                    c += stars;
                }

                c = c * _Tint.rgb * unity_ColorSpaceDouble.rgb;
                c *= _Exposure;
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
