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
        _ShadowFill ("Shadow fill (eclipse/shade min sun)", Range(0, 0.5)) = 0.15
        _FlashlightResponse ("Flashlight response on grass", Range(0, 1.5)) = 0.5
        _PointLightBoost ("Lantern/torch brightness on grass", Range(0, 4)) = 2.0
        _SpotGrassReach ("Concert light reach on grass (m)", Range(5, 250)) = 50
        _LanternGrassRadius ("Lantern grass radius (x range)", Range(0.1, 1.5)) = 0.5
        _LanternGrassTail ("Lantern grass far-reach tail", Range(0, 1)) = 0.35
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

        struct Input { float gradT; float3 worldPos; float3 worldNormal; float3 bladeUp; };

        fixed4 _TopColor;
        fixed4 _BottomColor;
        float _GradientPower;
        float _InvertGradient;
        float _AmbientBoost;
        float _ColorVarScale;
        float _ColorVarAmount;
        float _ShadowFill;           // min sun light kept in the directional sun's shadow (eclipse/shade)
        float _FlashlightResponse;   // scales the flashlight's effect on grass (1 = old look)
        float _PointLightBoost;      // scales lantern/torch brightness on grass (compensates the 0.5 grassStrength + blade angle)
        float _SpotGrassReach;       // distance (m) from the concert centre at which spot lights fade off the grass
        float3 _GrassSpotCenter;     // centroid of the injected concert SPOT lights, set by InstancedGrassRenderer
        float _LanternGrassRadius;   // shrinks the lantern/torch grass falloff distance (x the light's range; 0.5 = half)
        float _LanternGrassTail;     // brightness of the dim extended tail that carries lantern grass light out to ~full range (0 = old short cutoff)
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
        #define GRASS_MAX_POINT_LIGHTS 16
        float4 _GrassPointLightPos[GRASS_MAX_POINT_LIGHTS];    // xyz = world position
        float4 _GrassPointLightColor[GRASS_MAX_POINT_LIGHTS];  // rgb = colour * intensity * strength
        float4 _GrassPointLightParams[GRASS_MAX_POINT_LIGHTS]; // x = range, y = cosOuterHalfAngle, z = cosInnerHalfAngle
        float4 _GrassPointLightDir[GRASS_MAX_POINT_LIGHTS];    // xyz = spot forward (unit), w = 1 for SPOT, 0 for omni point
        float _GrassPointLightCount;

        // Half-Lambert wrap × shadow/attenuation. The wrap keeps the two-sided
        // (Cull Off) backfaces from going pure black at glancing sun angles
        // without re-introducing a constant glow. Scene ambient is added
        // automatically by the forward base pass, so grass inherits the same
        // ambient floor as everything else (dark at night, lit by day).
        half4 LightingGrassWrap(SurfaceOutput s, half3 lightDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);
            // Wrap floor scales with the DAY factor that surf stashed in s.Specular
            // (1 at local noon, 0 at the terminator/night side). In full day we keep
            // the soft half-Lambert wrap (floor 0.5) so the two-sided blades' shadow
            // sides don't go pure black. Toward the planet's sun-grazing edge the floor
            // falls to 0 = true Lambert, so the grass darkens exactly like the ground
            // instead of staying lit by the wrap and looking brighter than the terrain.
            half floorAmt = 0.5 * s.Specular;
            half wrapped = max(0, ndl * (1.0 - floorAmt) + floorAmt);
            // Floor the SHADOW attenuation so grass sitting in the directional sun's
            // shadow — the planet eclipse, or under a tree — keeps a little sun-coloured
            // light instead of dropping to black. The GROUND stays lit there via the
            // unshadowed point-sun, so this matches it. Only lifts shadowed pixels
            // (atten < _ShadowFill); fully-lit grass (atten = 1) is untouched, so the
            // sunny side is unchanged. The wrap term is ~0 facing away from the sun, so
            // the night side stays dark — no constant glow.
            half lit = max(atten, _ShadowFill);
            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * (wrapped * lit);
            c.a = s.Alpha;
            return c;
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float r = saturate(v.color.r);
            if (_InvertGradient > 0.5) r = 1.0 - r;
            o.gradT = saturate(pow(r, _GradientPower));
            // Blade's world up-axis = the instance's local +Y in world space. The
            // renderer tilts each blade to follow the terrain slope (slopeConform), so
            // this ~= the ground's surface normal. Used as the lighting normal for
            // overhead concert SPOTS so grass on a hill shades like the terrain (dark
            // on the back of a hill) instead of staying lit by a flat planet-radial.
            // Computed in the vertex stage where the per-instance matrix is valid.
            o.bladeUp = normalize(mul((float3x3)unity_ObjectToWorld, float3(0.0, 1.0, 0.0)));
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed3 c = lerp(_BottomColor.rgb, _TopColor.rgb, IN.gradT);
            // Per-patch brightness variation REMOVED (was: hashed ~_ColorVarScale-metre
            // patches to a lighter/darker green). All grass is now one uniform shade.
            o.Albedo = c;

            // Day factor for the lighting wrap (read in LightingGrassWrap via
            // s.Specular — unused by our custom lighting otherwise). 1 where the sun
            // is overhead, 0 at the terminator / night side. surf is the only place
            // with the world position, so we compute it here. _GrassPlanetCenter is
            // set globally by InstancedGrassRenderer; _WorldSpaceLightPos0.xyz is the
            // main directional sun's direction in the forward base pass.
            float3 radialUp = normalize(IN.worldPos - _GrassPlanetCenter);
            o.Specular = saturate(dot(radialUp, _WorldSpaceLightPos0.xyz));

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
            o.Emission   += c * _FlashlightColor.rgb * (spot * fatten * fwrap * _FlashlightResponse);

            // Lantern / world point lights. Omnidirectional version of the
            // flashlight block above — no cone, just distance falloff + the same
            // half-Lambert wrap. Skipped when no lights are injected (count 0).
            // Concert-distance dimmer: spot lights (only) fade off the grass past
            // _SpotGrassReach metres from the concert centre, so grass on far hills
            // goes dark with the terrain. Full within 0.3x the reach, then a long,
            // gentle gradient out to the reach (subtle, not a hard edge). Lanterns
            // (omni) are unaffected.
            float spotDistFade = smoothstep(_SpotGrassReach, _SpotGrassReach * 0.3,
                                            distance(IN.worldPos, _GrassSpotCenter));
            int gplCount = (int)_GrassPointLightCount;
            for (int li = 0; li < gplCount; li++)
            {
                float3 toP   = IN.worldPos - _GrassPointLightPos[li].xyz;
                float pdist  = length(toP);
                float3 pl    = toP / max(pdist, 1e-4);
                float pdn    = pdist / max(_GrassPointLightParams[li].x, 0.001);
                // Distance falloff. Lanterns (omni point, w=0) keep the original harsh
                // curve they were tuned with. SPOTS (concert, w=1) use a gentler curve
                // matched to Unity's real point/spot attenuation (which lights the
                // GROUND) — the old harsh window crushed the mid-range ~4x, so an
                // intensity-382 cone blew the ground bright but left the grass dark.
                // Lanterns get a shrunk radius (_LanternGrassRadius x the light's range)
                // so their grass glow matches the smaller lit ground circle instead of
                // reaching the light's full range. Spots use their own reach control.
                // Tight bright CORE — the original tuned near-falloff, unchanged, so
                // grass right next to the lantern looks exactly as it did before.
                float pdnPt = pdn / max(_LanternGrassRadius, 0.05);
                float core  = saturate(1.0 - pdnPt * pdnPt) / (1.0 + 25.0 * pdnPt * pdnPt);
                // Dim extended TAIL — a gentle, low-amplitude glow reaching ~the light's
                // full range (like the lit ground), filling the mid/far region where the
                // steep core has dropped to black. max() lets the bright core win up close
                // (near brightness preserved) while the tail only shows further out. The
                // old behaviour is _LanternGrassTail = 0.
                float tail  = saturate(1.0 - pdn * pdn) / (1.0 + 8.0 * pdn * pdn);
                float pattenPoint = max(core, tail * _LanternGrassTail);
                float pattenSpot  = (1.0 / (1.0 + 15.0 * pdn * pdn)) * smoothstep(1.0, 0.85, pdn);
                float patten = lerp(pattenPoint, pattenSpot, _GrassPointLightDir[li].w);
                // Light response normal: the terrain-aligned blade up-axis for ALL
                // injected lights (lanterns AND concert spots). Vertical blades barely
                // face an overhead light by their own face-normal, and a fill floor on
                // that normal lit them in a wide halo where the GROUND (real normal, at
                // a grazing angle) had already gone dark. Using bladeUp + a low floor
                // makes the grass shade like the ground — bright when the light is
                // overhead, dark at grazing — so a lantern's grass halo matches its
                // ground circle instead of glowing twice as far.
                half pndl    = dot(IN.bladeUp, -pl);
                half pfloor  = 0.15;
                half pwrap   = max(0, pndl * (1.0 - pfloor) + pfloor);
                // Spot cone gate (concert spotlights). _GrassPointLightDir.w = 1 marks
                // a SPOT: pl is the light->fragment direction, so dot() with the spot's
                // forward places the fragment in the cone, and smoothstep from the outer
                // to inner half-angle cosines softens the edge. w = 0 (lanterns/torches)
                // → spotF = 1 (omni, unchanged).
                float cosA   = dot(pl, _GrassPointLightDir[li].xyz);
                float spotF  = lerp(1.0, smoothstep(_GrassPointLightParams[li].y, _GrassPointLightParams[li].z, cosA), _GrassPointLightDir[li].w);
                // Apply the concert-distance dimmer to spots only (w=1); lanterns (w=0) keep full reach.
                float distFade = lerp(1.0, spotDistFade, _GrassPointLightDir[li].w);
                o.Emission  += c * _GrassPointLightColor[li].rgb * (patten * pwrap * spotF * _PointLightBoost * distFade);
            }

            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
