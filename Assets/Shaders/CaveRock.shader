// Cave rock. Near the mouth it IS moon rock — the same colours, the same two
// normal maps and the same steepness rule as the moon's own terrain shader
// (Celestial/Shaders/Surface/MoonA.shader, read-only), so the sinkhole is made
// of the crater it sits in. Going in, it fades to the cave's own stone: the
// procedural cave albedo + normal (cracks, strata, dripstone) — a cave is
// sheltered, nothing hits it, so it has no little craters.
//
// Per vertex (written by CaveSolid): R = noise, G = steepness (0..1 over
// 0..0.3 of 1 - n·up, as MoonA remaps it), B = path distance from the mouth
// (0 at the mouth → 1 at ~22 m in), A = sky exposure.
// Exposure scales the directional light (forward base pass) and ambient so the
// interior is dark; point/spot lights (flashlight, crystal glow) are not scaled.
//
// Built-in RP, forward only. Referenced by the Cave_Rock_* material assets so
// it is included in builds (never rely on Shader.Find for this).
Shader "Custom/CaveRock"
{
    Properties
    {
        _Color ("Tint (cave stone)", Color) = (1,1,1,1)
        _MainTex ("Cave albedo (triplanar)", 2D) = "gray" {}
        _BumpMap ("Cave normal RG=xy (triplanar)", 2D) = "bump" {}
        _Tiling ("Cave: metres per tile", Float) = 3.5
        _BumpScale ("Cave normal strength", Range(0, 3)) = 1.2
        _NormalFlat ("Moon normal map: flat", 2D) = "bump" {}
        _NormalSteep ("Moon normal map: steep", 2D) = "bump" {}
        _MoonTiling ("Moon: metres per tile", Float) = 2.5
        _NormalStrength ("Moon normal strength (moon uses 0.589)", Range(0, 1)) = 0.589
        _FlatColA ("Moon flat colour A", Color) = (1, 1, 1, 1)
        _FlatColB ("Moon flat colour B", Color) = (0.736, 0.736, 0.736, 1)
        _SteepCol ("Moon steep colour", Color) = (0.0577, 0.0469, 0.0849, 1)
        _MoonBrightness ("Moon brightness match", Range(0.5, 1.2)) = 0.86
        _FadeStart ("Craters fade out: start (path 0..1)", Range(0, 1)) = 0.3
        _FadeEnd ("Craters fade out: end (path 0..1)", Range(0, 1)) = 1.0
        _ExposureFloor ("Minimum daylight inside", Range(0, 0.3)) = 0.03
        _ExposurePower ("Daylight falloff", Range(0.5, 3)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf CaveLambert fullforwardshadows addshadow vertex:vert exclude_path:deferred exclude_path:prepass
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _NormalFlat;
        sampler2D _NormalSteep;
        fixed4 _Color;
        float _Tiling, _BumpScale, _MoonTiling, _NormalStrength, _MoonBrightness, _FadeStart, _FadeEnd;
        fixed4 _FlatColA, _FlatColB, _SteepCol;
        float _ExposureFloor, _ExposurePower;

        struct Input
        {
            float3 objPos;
            float3 objNormal;
            float4 objTangent;
            float4 color : COLOR;
        };

        struct SurfaceOutputCave
        {
            fixed3 Albedo;
            fixed3 Normal;
            fixed3 Emission;
            half Specular;
            fixed Gloss;
            fixed Alpha;
            half Exposure;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.objPos = v.vertex.xyz;
            o.objNormal = v.normal.xyz;
            o.objTangent = v.tangent;
        }

        half4 LightingCaveLambert (SurfaceOutputCave s, UnityGI gi)
        {
            half ndl = max(0, dot(s.Normal, gi.light.dir));
            half k = 1;
            #ifdef UNITY_PASS_FORWARDBASE
                k = s.Exposure;     // the sun
            #endif
            half4 c;
            c.rgb = s.Albedo * gi.light.color * ndl * k;
            #ifdef UNITY_LIGHT_FUNCTION_APPLY_INDIRECT
                c.rgb += s.Albedo * gi.indirect.diffuse * s.Exposure;   // ambient / SH
            #endif
            c.a = s.Alpha;
            return c;
        }

        void LightingCaveLambert_GI (SurfaceOutputCave s, UnityGIInput data, inout UnityGI gi)
        {
            gi = UnityGI_Base(data, 1.0, s.Normal);
        }

        // Whiteout-blended triplanar normal, returned in object space.
        float3 TriplanarNormal (float3 tx, float3 ty, float3 tz, float3 n, float3 bw, float3 axisSign)
        {
            tx.xy *= axisSign.x; ty.xy *= axisSign.y; tz.xy *= axisSign.z;
            tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
            ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
            tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
            return normalize(tx.zyx * bw.x + ty.xzy * bw.y + tz.xyz * bw.z);
        }

        float3 RawNormal (float2 rg, float scale)
        {
            float2 xy = (rg * 2.0 - 1.0) * scale;
            return float3(xy, sqrt(saturate(1.0 - dot(xy, xy))));
        }

        void surf (Input IN, inout SurfaceOutputCave o)
        {
            float3 n = normalize(IN.objNormal);
            float3 bw = pow(abs(n), 4.0);
            bw /= max(1e-4, bw.x + bw.y + bw.z);
            float3 axisSign = sign(n);

            float steep = saturate(IN.color.g);
            float exposure = saturate(IN.color.a);
            // 1 = moon rock (at the mouth), 0 = cave stone (deep inside).
            float moon = 1.0 - smoothstep(_FadeStart, _FadeEnd, IN.color.b);

            // ── moon rock ──
            float3 pm = IN.objPos / _MoonTiling;
            float noiseTex = dot(tex2D(_MainTex, pm.xz * 0.37).rgb, float3(0.33, 0.34, 0.33)) * bw.y
                           + dot(tex2D(_MainTex, pm.zy * 0.37).rgb, float3(0.33, 0.34, 0.33)) * bw.x
                           + dot(tex2D(_MainTex, pm.xy * 0.37).rgb, float3(0.33, 0.34, 0.33)) * bw.z;
            float blend = smoothstep(0.25, 0.6, IN.color.r * 0.6 + noiseTex * 0.6 + steep * 0.25);
            fixed3 flat = lerp(_FlatColA.rgb, _FlatColB.rgb, blend);
            fixed3 moonAlb = lerp(flat, _SteepCol.rgb, steep) * _MoonBrightness;
            float3 nFlat = TriplanarNormal(UnpackNormal(tex2D(_NormalFlat, pm.zy)), UnpackNormal(tex2D(_NormalFlat, pm.xz)), UnpackNormal(tex2D(_NormalFlat, pm.xy)), n, bw, axisSign);
            float3 nSteep = TriplanarNormal(UnpackNormal(tex2D(_NormalSteep, pm.zy)), UnpackNormal(tex2D(_NormalSteep, pm.xz)), UnpackNormal(tex2D(_NormalSteep, pm.xy)), n, bw, axisSign);
            float3 moonN = normalize(lerp(n, normalize(lerp(nFlat, nSteep, steep)), _NormalStrength));

            // ── cave stone ──
            float3 pc = IN.objPos / _Tiling;
            fixed3 caveAlb = (tex2D(_MainTex, pc.zy).rgb * bw.x + tex2D(_MainTex, pc.xz).rgb * bw.y + tex2D(_MainTex, pc.xy).rgb * bw.z) * _Color.rgb;
            float3 caveN = TriplanarNormal(RawNormal(tex2D(_BumpMap, pc.zy).rg, _BumpScale), RawNormal(tex2D(_BumpMap, pc.xz).rg, _BumpScale), RawNormal(tex2D(_BumpMap, pc.xy).rg, _BumpScale), n, bw, axisSign);

            // Moon rock everywhere (Sam): the moon's colours throughout, the
            // moon's steep colour lifted a little inside so torch-lit walls read
            // as dark rock; the CRATERS fade out with depth (a cave is sheltered)
            // and the rock normal map takes over, with a touch of the cave's
            // own cracks deep down.
            fixed3 steepInside = lerp(_SteepCol.rgb, fixed3(0.24, 0.22, 0.27), 1.0 - moon);
            fixed3 moonAlbInside = lerp(flat, steepInside, steep) * _MoonBrightness;
            fixed3 alb = lerp(moonAlbInside, moonAlb, moon);
            float3 deepN = normalize(lerp(nSteep, caveN, 0.35));
            float3 objN = normalize(lerp(normalize(lerp(n, deepN, _NormalStrength)), moonN, moon));

            // Object space → tangent space, which is what the surface shader wants.
            float3 T = IN.objTangent.xyz;
            if (length(T) < 1e-4) T = abs(n.y) < 0.9 ? cross(n, float3(0, 1, 0)) : cross(n, float3(1, 0, 0));
            T = normalize(T - n * dot(n, T));
            float3 B = cross(n, T) * (IN.objTangent.w < 0 ? -1.0 : 1.0);
            o.Normal = normalize(float3(dot(objN, T), dot(objN, B), dot(objN, n)));

            o.Albedo = alb;
            o.Exposure = _ExposureFloor + (1.0 - _ExposureFloor) * pow(exposure, _ExposurePower);
            o.Emission = 0;
            o.Specular = 0;
            o.Gloss = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
