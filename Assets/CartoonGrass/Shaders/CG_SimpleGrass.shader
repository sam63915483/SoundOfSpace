// Simple, reliable Built-in render pipeline grass shader for the CartoonGrass
// meshes. The pack's own grass Shader Graphs render BLACK in this Built-in
// project (their procedural colour path doesn't translate) and generate a
// huge number of shader variants (~7 min compile). This reproduces the
// intended look cheaply and reliably:
//   • colour = vertical gradient between _BottomColor (blade base) and
//     _TopColor (blade tip), driven by the mesh's vertex-colour RED channel,
//     which the pack bakes as a 0..1 height-along-blade value.
//   • two-sided (Cull Off) so flat grass "cards" show from both sides.
//   • lit (Lambert) so it matches the world's day/night, PLUS a small
//     emission floor (_AmbientBoost) so grass is never pure black in shadow
//     or on a backface.
//   • GPU instancing enabled — needed because the planet is carpeted with
//     thousands of blades.
//   • Opaque / Geometry queue (<=2500) so it renders behind atmosphere/ocean.
Shader "CartoonGrass/SimpleGrass"
{
    Properties
    {
        _TopColor ("Top Color (tip)", Color) = (0.64, 0.81, 0.17, 1)
        _BottomColor ("Bottom Color (base)", Color) = (0.32, 0.45, 0.17, 1)
        _GradientPower ("Gradient Power", Range(0.25, 4)) = 1.0
        [Toggle] _InvertGradient ("Invert Gradient", Float) = 0
        _AmbientBoost ("Ambient Boost (avoids black)", Range(0, 1)) = 0.35
        _ColorVarScale ("Colour Variation Scale (m)", Float) = 6
        _ColorVarAmount ("Colour Variation Amount", Range(0, 0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off

        CGPROGRAM
        // Custom wrapped-Lambert lighting (replaces built-in Lambert) so grass:
        //   • darkens on the planet's NIGHT side (sun N·L falls to ~0), matching
        //     the rest of the world instead of staying flat green, and
        //   • receives the sun's shadow map (atten) → grass under a tree or in
        //     any cast shadow goes dark instead of looking like it glows.
        // fullforwardshadows pulls in the shadow-sampling path; the renderer
        // must also have receiveShadows = true (InstancedGrassRenderer sets it).
        #pragma surface surf GrassWrap vertex:vert fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing

        struct Input { float gradT; float3 worldPos; float3 worldNormal; };

        fixed4 _TopColor;
        fixed4 _BottomColor;
        float _GradientPower;
        float _InvertGradient;
        float _AmbientBoost;
        float _ColorVarScale;
        float _ColorVarAmount;
        float3 _GrassPlanetCenter;   // set globally by InstancedGrassRenderer (per-patch colour hash)

        // Player flashlight, injected globally by PlayerFlashlight. The grass is
        // drawn with Graphics.DrawMeshInstanced, which does NOT receive Unity's
        // additive per-pixel forward lights — so a spot light like the torch
        // never reaches it the normal way (the planet ground, a real renderer,
        // does light up, leaving grass the only thing black at night). We add
        // the spot contribution by hand below. _FlashlightColor is black when
        // the torch is off → exactly zero effect, so day/sun behaviour is
        // unchanged.
        float3 _FlashlightPos;
        float3 _FlashlightDir;       // light forward (world, unit)
        fixed4 _FlashlightColor;     // colour * intensity (black = off)
        float4 _FlashlightParams;    // x=range, y=cosOuterHalfAngle, z=cosInnerHalfAngle

        // World POINT lights (lanterns, etc.) that should reach the instanced
        // grass — same reason as the flashlight: DrawMeshInstanced grass never
        // receives Unity's additive forward lights, so a lantern over the grass
        // leaves it dark. InstancedGrassRenderer injects the nearby ones (from
        // GrassPointLight markers) into these globals each frame; _GrassPointLightCount
        // is 0 when none are near, so the loop below is skipped entirely.
        #define GRASS_MAX_POINT_LIGHTS 8
        float4 _GrassPointLightPos[GRASS_MAX_POINT_LIGHTS];    // xyz = world position
        float4 _GrassPointLightColor[GRASS_MAX_POINT_LIGHTS];  // rgb = colour * intensity * strength
        float4 _GrassPointLightParams[GRASS_MAX_POINT_LIGHTS]; // x = range
        float _GrassPointLightCount;

        // Half-Lambert wrap × shadow/attenuation. The wrap keeps the two-sided
        // (Cull Off) backfaces from going pure black at glancing sun angles
        // without re-introducing a constant glow. Scene ambient is added
        // automatically by the forward base pass, so grass inherits the same
        // ambient floor as everything else (dark at night, lit by day).
        half4 LightingGrassWrap(SurfaceOutput s, half3 lightDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);
            half wrapped = max(0, ndl * 0.5 + 0.5);
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (wrapped * atten);
            c.a = s.Alpha;
            return c;
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float r = saturate(v.color.r);
            if (_InvertGradient > 0.5) r = 1.0 - r;
            o.gradT = saturate(pow(r, _GradientPower));
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed3 c = lerp(_BottomColor.rgb, _TopColor.rgb, IN.gradT);
            // Per-patch brightness variation so the field isn't one flat green.
            // Hash a planet-relative cell index (relative keeps the hash input
            // small at the planet's large world coords) → stable per-patch shade.
            float3 rel = IN.worldPos - _GrassPlanetCenter;
            float3 pcell = floor(rel / max(0.001, _ColorVarScale));
            float vh = frac(sin(dot(pcell, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            c *= lerp(1.0 - _ColorVarAmount, 1.0 + _ColorVarAmount, vh);
            o.Albedo = c;
            // Only a whisper of self-emission (scaled WAY down from the old
            // constant floor) so deep shadow isn't pure black — far too small
            // to read as a night-time glow. Set _AmbientBoost to 0 on the
            // material to kill it entirely.
            o.Emission = c * _AmbientBoost * 0.06;

            // Flashlight (added as emission because the instanced grass can't
            // receive the real additive spot light — see the uniform block
            // above). Cheap cone test + soft distance falloff, with the same
            // half-Lambert wrap the sun uses so two-sided blades don't snap to
            // black. Zero work-effect when the torch is off (_FlashlightColor
            // is black).
            float3 toFrag = IN.worldPos - _FlashlightPos;
            float fdist   = length(toFrag);
            float3 fl     = toFrag / max(fdist, 1e-4);
            float cosA    = dot(fl, _FlashlightDir);
            float spot    = smoothstep(_FlashlightParams.y, _FlashlightParams.z, cosA);
            // Distance falloff matched to Unity's built-in spot attenuation
            // (~1/(1+25(d/range)^2)) so grass dims with distance exactly like the
            // lit ground — flying up now dims the grass instead of lighting it
            // the same at every height. The (1 - dn^2) window takes it cleanly to
            // 0 at the light's range.
            float dn      = fdist / max(_FlashlightParams.x, 0.001);
            float fatten  = saturate(1.0 - dn * dn) / (1.0 + 25.0 * dn * dn);
            half fndl     = dot(normalize(IN.worldNormal), -fl);
            half fwrap    = max(0, fndl * 0.5 + 0.5);
            o.Emission   += c * _FlashlightColor.rgb * (spot * fatten * fwrap);

            // Lantern / world point lights. Omnidirectional version of the
            // flashlight block above — no cone, just distance falloff + the same
            // half-Lambert wrap. Skipped when no lights are injected (count 0).
            int gplCount = (int)_GrassPointLightCount;
            for (int li = 0; li < gplCount; li++)
            {
                float3 toP   = IN.worldPos - _GrassPointLightPos[li].xyz;
                float pdist  = length(toP);
                float3 pl    = toP / max(pdist, 1e-4);
                float pdn    = pdist / max(_GrassPointLightParams[li].x, 0.001);
                float patten = saturate(1.0 - pdn * pdn) / (1.0 + 25.0 * pdn * pdn);
                half pndl    = dot(normalize(IN.worldNormal), -pl);
                half pwrap   = max(0, pndl * 0.5 + 0.5);
                o.Emission  += c * _GrassPointLightColor[li].rgb * (patten * pwrap);
            }

            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
