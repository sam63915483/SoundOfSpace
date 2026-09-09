// A planet's cloud layer: one thin sphere shell above the surface (Sam,
// 2026-09-09 — "clouds on planets that have atmospheres would be very cool").
//
// ── Why a shell and not fluffy blobs ──────────────────────────────────────
// Individual cloud billboards are the classic framerate killer: dozens of
// overlapping see-through layers, each shading every pixel underneath it. A
// single shell is ONE layer of transparency over the sky part of the screen and
// one draw call. Volumetric raymarching is out of the question at this budget
// and would also fight the atmosphere post-effect, which is in the do-not-touch
// zone.
//
// ── The sky problem ───────────────────────────────────────────────────────
// This game has NO SKYBOX — the blue is a full-screen post-effect, and it is
// forbidden code. That effect runs on OPAQUE geometry only ([ImageEffectOpaque]),
// so a transparent shell drawn afterwards never receives the sky's colour. The
// cloud is therefore tinted toward the sky HERE (_SkyTint, strongest toward the
// horizon and folded into the day/night term), which keeps the whole thing
// outside the forbidden zone by construction.
//
// ── Shape ─────────────────────────────────────────────────────────────────
// Sampled TRIPLANAR from the object-space direction, so there are no UV seams
// and no pole pinching on a sphere, and it rotates with the planet for free.
// Two octaves at different speeds give the layer some internal movement rather
// than a texture sliding across the sky in one piece.
Shader "Custom/PlanetClouds"
{
    Properties
    {
        _NoiseTex   ("Cloud noise (tiling)", 2D) = "white" {}
        _SunColor   ("Sunlit colour", Color) = (1,0.98,0.95,1)
        _ShadowColor("Shaded colour", Color) = (0.38,0.42,0.52,1)
        _SkyTint    ("Sky tint", Color) = (0.45,0.62,0.85,1)
        _SkyTintAmount ("Sky tint amount", Range(0,1)) = 0.35
        _Scale      ("Noise scale", Float) = 2.5
        _DetailScale("Detail scale", Float) = 2.7
        _Coverage   ("Coverage", Range(0,1)) = 0.52
        _Softness   ("Edge softness", Range(0.01,0.6)) = 0.22
        _Opacity    ("Opacity", Range(0,1)) = 0.85
        _DriftSpeed ("Drift speed", Float) = 0.004
        _NearFade   ("Near fade distance", Float) = 18
        _UnderLit   ("Underside darkening", Range(0,1)) = 0.45
    }

    SubShader
    {
        // Transparent, and deliberately so. The corollary of CLAUDE.md's
        // queue rule: this is something you look THROUGH and past, it must not
        // land in _CameraDepthTexture, or it would clip the atmosphere and the
        // ocean at the cloud layer instead of at the ground.
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        // Double sided: you fly up through this layer, so its underside is as
        // visible as its top.
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _NoiseTex;
            float4 _SunColor, _ShadowColor, _SkyTint;
            float _SkyTintAmount, _Scale, _DetailScale, _Coverage, _Softness;
            float _Opacity, _DriftSpeed, _NearFade, _UnderLit;
            // Set from PlanetClouds.cs — the same sun the ocean effect uses.
            float3 _CloudSunDir;

            struct appdata { float4 vertex : POSITION; };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 objDir : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objDir = normalize(v.vertex.xyz);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = normalize(UnityObjectToWorldNormal(v.vertex.xyz));
                return o;
            }

            // Triplanar: three samples blended by the axis weights of the
            // direction. On a sphere this is what avoids both the seam and the
            // pinched poles a UV sphere would give.
            float Triplanar(float3 p, float3 blend)
            {
                float x = tex2D(_NoiseTex, p.yz).r;
                float y = tex2D(_NoiseTex, p.xz).r;
                float z = tex2D(_NoiseTex, p.xy).r;
                return x * blend.x + y * blend.y + z * blend.z;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 d = normalize(i.objDir);
                float3 blend = abs(d);
                blend /= max(blend.x + blend.y + blend.z, 1e-4);

                float t = _Time.y * _DriftSpeed;
                float n1 = Triplanar(d * _Scale + float3(t, t * 0.6, -t * 0.8), blend);
                float n2 = Triplanar(d * (_Scale * _DetailScale)
                                     + float3(-t * 2.3, t * 1.7, t * 2.9), blend);
                float n = n1 * 0.65 + n2 * 0.35;

                // Coverage carves the cloud out of the noise; softness is how
                // wispy the edges are.
                float a = smoothstep(_Coverage, _Coverage + _Softness, n) * _Opacity;
                if (a < 0.004) discard;

                // Lighting from the shell's own outward normal: the terminator
                // falls across the cloud layer exactly as it does on the planet,
                // so clouds go dark on the night side and catch the light at
                // dawn without any extra work.
                float ndl = dot(normalize(i.worldNormal), _CloudSunDir);
                float lit = saturate(ndl * 0.5 + 0.5);
                float day = saturate(ndl * 2.0 + 0.35);          // night side falls off fast
                float3 col = lerp(_ShadowColor.rgb, _SunColor.rgb, lit) * day;

                // Undersides are darker than tops — the cheapest cue that this
                // is a solid body of cloud rather than a painted sphere.
                if (facing < 0) col *= (1.0 - _UnderLit);

                // Stand in for the atmosphere, which cannot reach a transparent
                // surface. Strongest toward the horizon, and it dies with the
                // light so night clouds do not glow blue.
                float horizon = 1.0 - saturate(abs(dot(normalize(i.worldNormal),
                                    normalize(i.worldPos - _WorldSpaceCameraPos))));
                col = lerp(col, _SkyTint.rgb * day, _SkyTintAmount * horizon);

                // Fade out as you pass through the layer, so flying up through
                // it is a soft dissolve instead of the camera punching a hole in
                // a wall.
                float dist = distance(i.worldPos, _WorldSpaceCameraPos);
                a *= smoothstep(0.0, max(_NearFade, 0.01), dist);

                return fixed4(col, a);
            }
            ENDCG
        }
    }

    FallBack Off
}
