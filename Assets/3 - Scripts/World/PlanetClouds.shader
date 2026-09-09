// Puffy clouds: real 3D lumps of mesh, rendered SOFT and SEE-THROUGH.
//
// Sam's notes across three passes:
//   v1 (texture on a sphere shell): "the clouds are flat ... i would not use
//       them in my game."  A picture on a sphere has no parallax and no volume.
//   v2 (solid low-poly lumps): "those are better, but they look too fake, they
//       are completely solid looking and should be ... see through and fluffy."
//
// So the geometry stays — it is what gives real parallax and a real silhouette,
// and Sam confirmed that part was an improvement — and the SHADING is what
// changes. A cloud is not a surface, it is a volume you can partly see into,
// and three things here do that work:
//
//   • EDGE TRANSPARENCY. Alpha falls off where the surface turns away from you.
//     On a round puff that means the rim goes sheer while the middle stays
//     dense — which is exactly how a real cloud edge behaves, because you are
//     looking through less vapour there. This one term is most of the fix for
//     "completely solid looking".
//
//   • ACCUMULATION. Each cloud is 9-14 overlapping puffs drawn without depth
//     writing, so their alpha stacks: thin and wispy at the fringes where one
//     puff is between you and the sky, dense in the middle where five are. That
//     is a volume being integrated, cheaply, and it comes free from the shapes
//     already being there.
//
//   • BREAK-UP. A little positional noise on the alpha stops each puff reading
//     as a smooth ball.
//
// Cost stays low because clouds barely overlap each OTHER: this is one or two
// layers of transparency over part of the sky, not the dozens that make
// billboard cloud systems expensive.
//
// Lighting is custom because Standard makes a cloud look like a grey plastic
// boulder: WRAP (light carries far around a translucent body), a SILVER LINING
// (forward scattering through thin edges, brightest looking toward the sun) and
// a darker, cooler UNDERSIDE.
Shader "Custom/PlanetClouds"
{
    Properties
    {
        _Color        ("Cloud colour", Color) = (1,1,1,1)
        _ShadowColor  ("Shaded colour", Color) = (0.62,0.67,0.78,1)
        _UnderColor   ("Underside colour", Color) = (0.48,0.53,0.66,1)
        _UnderStrength("Underside strength", Range(0,1)) = 0.7
        _Wrap         ("Light wrap", Range(0,1)) = 0.6
        _RimStrength  ("Silver lining", Range(0,3)) = 1.1
        _RimPower     ("Silver lining tightness", Range(1,16)) = 5
        _Opacity      ("Density", Range(0,1)) = 0.72
        _EdgeSoftness ("Edge softness", Range(0.2,6)) = 2.2
        _Break        ("Surface break-up", Range(0,1)) = 0.35
        _BreakScale   ("Break-up scale", Float) = 0.9
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 200

        // Cull Off so the far side of a puff shows through the near side — that
        // internal layering is a lot of what makes it read as depth rather than
        // as a shell.
        Cull Off
        ZWrite Off

        CGPROGRAM
        // alpha:fade  — see-through, which is the whole point of this pass.
        // noforwardadd — the sun only, never a second pass for a lamp or torch.
        // addshadow    — clouds still cast a shadow on the ground below.
        #pragma surface surf Cloud alpha:fade noforwardadd addshadow
        #pragma multi_compile_instancing
        #pragma target 3.0

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
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
        half _Opacity, _EdgeSoftness, _Break;
        float _BreakScale;
        // Planet centre, set by PlanetClouds.cs — on a globe, "down" is a
        // different direction for every cloud in the sky.
        float3 _CloudPlanetCentre;

        half4 LightingCloud(SurfaceOutputCloud s, half3 lightDir, half3 viewDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);

            // WRAP. A cloud is translucent, so light carries a long way around
            // it; without this the dark side falls off like a billiard ball.
            half wrapped = saturate((ndl + _Wrap) / (1.0 + _Wrap));

            // SILVER LINING. Light scattered forward through a thin edge:
            // brightest looking toward the sun, only where the surface is turned
            // away from it.
            half rim = pow(saturate(dot(viewDir, -lightDir)), _RimPower)
                     * saturate(0.5 - ndl * 0.5) * _RimStrength;

            half3 lit = lerp(_ShadowColor.rgb, _Color.rgb, wrapped);
            lit = lerp(lit, _UnderColor.rgb, s.Under * _UnderStrength);

            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (lit * atten + rim);
            // Ambient, so a cloud on the night side is a silhouette rather than
            // a hole punched in the sky.
            c.rgb += s.Albedo * unity_AmbientSky.rgb * 0.35;
            c.a = s.Alpha;
            return c;
        }

        void surf (Input IN, inout SurfaceOutputCloud o)
        {
            o.Albedo = 1;

            float3 n = normalize(IN.worldNormal);
            float3 v = normalize(IN.viewDir);

            float3 up = normalize(IN.worldPos - _CloudPlanetCentre);
            o.Under = saturate(-dot(n, up));

            // EDGE TRANSPARENCY. Face-on you are looking through the thick of
            // the puff, so it is dense; at the rim you are looking through a
            // sliver, so it goes sheer. This is the term that stops it reading
            // as a solid object.
            half facing = saturate(dot(n, v));
            half a = pow(facing, _EdgeSoftness);

            // BREAK-UP so each puff is not a perfectly smooth ball. Three cheap
            // waves, no texture fetch.
            float3 p = IN.worldPos * _BreakScale;
            half noise = sin(p.x) * sin(p.y * 1.3 + 1.7) * sin(p.z * 0.8 + 3.1);
            a *= 1.0 - _Break * (0.5 - 0.5 * noise);

            o.Alpha = saturate(a * _Opacity);
        }
        ENDCG
    }

    FallBack "Diffuse"
}
