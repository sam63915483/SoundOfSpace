// Puffy 3D clouds — REAL GEOMETRY, not a texture painted on a sphere.
//
// Sam, 2026-09-09, on the first attempt: "basically made a sphere around the
// planets and then the clouds are painted flat onto that, but literally, the
// clouds are flat. i was hoping for actual 3d puffy looking white clouds that
// are overhead ... i would not use them in my game."
//
// He was right, and a shell can never fix that: a texture on a sphere has no
// parallax, no silhouette against itself, and no volume. So the clouds are now
// clusters of actual lumps of mesh. They are genuinely three-dimensional
// because they genuinely ARE three-dimensional — you can fly around one, and
// its far side goes past its near side as you move.
//
// It is also the CHEAPER option, which is the part that is easy to miss.
// Volumetric raymarching would cost a fortune, and stacks of see-through
// billboards drown in overdraw. Low-poly opaque lumps are just... meshes: a few
// hundred triangles each, instanced, one draw call per shape, and they cast
// ordinary shadows through the ordinary shadow map.
//
// ── Why a custom lighting model ───────────────────────────────────────────
// Standard shading makes a cloud look like a grey plastic boulder. Three
// things make a lump of mesh read as cloud instead:
//   • WRAP — light bleeds a long way around a cloud, so it stays bright well
//     past the terminator instead of falling to black at 90 degrees.
//   • SILVER LINING — sunlight scattering through the thin edges toward the
//     viewer, brightest when you look toward the sun.
//   • A DARKER, COOLER UNDERSIDE — the single strongest cue that this is a
//     body of water vapour with weight, lit from above.
Shader "Custom/PlanetClouds"
{
    Properties
    {
        _Color        ("Cloud colour", Color) = (1,1,1,1)
        _ShadowColor  ("Shaded colour", Color) = (0.58,0.63,0.74,1)
        _UnderColor   ("Underside colour", Color) = (0.42,0.47,0.60,1)
        _UnderStrength("Underside strength", Range(0,1)) = 0.8
        _Wrap         ("Light wrap", Range(0,1)) = 0.6
        _RimStrength  ("Silver lining", Range(0,3)) = 1.0
        _RimPower     ("Silver lining tightness", Range(1,16)) = 5
    }

    SubShader
    {
        // Opaque. Clouds are solid lumps here, which is what lets them cast a
        // normal shadow and sort correctly against each other with no blending,
        // no sorting order and no overdraw.
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGPROGRAM
        // noforwardadd: the sun only. A cloud must never pick up a second
        // lighting pass from a lamp or a torch — same reasoning as the grass.
        #pragma surface surf Cloud fullforwardshadows noforwardadd
        #pragma multi_compile_instancing
        #pragma target 3.0

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        struct SurfaceOutputCloud
        {
            fixed3 Albedo;
            fixed3 Normal;
            fixed3 Emission;
            half Under;      // 0 = facing the sky, 1 = facing the ground
            fixed Alpha;
        };

        fixed4 _Color, _ShadowColor, _UnderColor;
        half _UnderStrength, _Wrap, _RimStrength, _RimPower;
        // Planet centre, set by PlanetClouds.cs — "down" is toward it, and on a
        // sphere that is different for every cloud.
        float3 _CloudPlanetCentre;

        half4 LightingCloud(SurfaceOutputCloud s, half3 lightDir, half3 viewDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);

            // WRAP. A cloud is translucent, so light carries a long way around
            // it; without this the dark side falls off like a billiard ball.
            half wrapped = saturate((ndl + _Wrap) / (1.0 + _Wrap));

            // SILVER LINING. Light scattered forward through a thin edge:
            // brightest looking toward the sun, and only where the surface is
            // turned away from it.
            half rim = pow(saturate(dot(viewDir, -lightDir)), _RimPower)
                     * saturate(0.5 - ndl * 0.5) * _RimStrength;

            half3 lit = lerp(_ShadowColor.rgb, _Color.rgb, wrapped);
            // The underside is darker and cooler no matter where the sun is —
            // this is the cue that sells "solid body lit from above".
            lit = lerp(lit, _UnderColor.rgb, s.Under * _UnderStrength);

            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (lit * atten + rim);
            // Ambient, so a cloud on the night side is a silhouette rather than
            // a hole in the sky.
            c.rgb += s.Albedo * unity_AmbientSky.rgb * 0.35;
            c.a = 1;
            return c;
        }

        void surf (Input IN, inout SurfaceOutputCloud o)
        {
            o.Albedo = 1;
            // "Down" is toward the planet, which on a globe is a different
            // direction for every cloud in the sky.
            float3 up = normalize(IN.worldPos - _CloudPlanetCentre);
            o.Under = saturate(-dot(normalize(IN.worldNormal), up));
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
