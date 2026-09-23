// Cave rock = MOON ROCK. This reproduces the moon terrain's own surface rule
// (Celestial/Shaders/Surface/MoonA.shader, read-only) so a cave looks like it
// was dug out of the moon and not dressed in a different stone:
//   • colour blends between the moon's two flat colours by height-noise,
//   • steep faces take the moon's steep colour (remapped over 0..0.3 of
//     1 - n·up, exactly as MoonA does),
//   • the same two normal maps (flat = craters, steep = rock), triplanar,
//     blended by steepness, at the moon's strength.
// The generator stores per vertex: R = noise, G = steepness, B = 1,
// A = SKY EXPOSURE. Exposure scales the directional light (forward base pass)
// and ambient, so the interior is dark; point/spot lights (the flashlight)
// are not scaled. Inside, the steep colour is lifted a little so walls read
// as dark rock under a torch rather than as nothing.
//
// Built-in RP, forward only. Referenced by the Cave_Rock_* material assets so
// it is included in builds (never rely on Shader.Find for this).
Shader "Custom/CaveRock"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Noise (triplanar)", 2D) = "gray" {}
        _NormalFlat ("Moon normal map: flat", 2D) = "bump" {}
        _NormalSteep ("Moon normal map: steep", 2D) = "bump" {}
        _Tiling ("Metres per tile", Float) = 2.5
        _NormalStrength ("Normal strength (moon uses 0.589)", Range(0, 1)) = 0.589
        _FlatColA ("Moon flat colour A", Color) = (1, 1, 1, 1)
        _FlatColB ("Moon flat colour B", Color) = (0.736, 0.736, 0.736, 1)
        _SteepCol ("Moon steep colour", Color) = (0.0577, 0.0469, 0.0849, 1)
        _SteepColInside ("Steep colour inside the cave", Color) = (0.24, 0.22, 0.27, 1)
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
        sampler2D _NormalFlat;
        sampler2D _NormalSteep;
        fixed4 _Color;
        float _Tiling;
        float _NormalStrength;
        fixed4 _FlatColA, _FlatColB, _SteepCol, _SteepColInside;
        float _ExposureFloor;
        float _ExposurePower;

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

        // Triplanar tangent-space normal from a normal map, whiteout blend,
        // returned in object space.
        float3 TriplanarNormal (sampler2D tex, float3 p, float3 n, float3 bw, float3 axisSign)
        {
            float3 tx = UnpackNormal(tex2D(tex, p.zy));
            float3 ty = UnpackNormal(tex2D(tex, p.xz));
            float3 tz = UnpackNormal(tex2D(tex, p.xy));
            tx.xy *= axisSign.x; ty.xy *= axisSign.y; tz.xy *= axisSign.z;
            tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
            ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
            tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
            return normalize(tx.zyx * bw.x + ty.xzy * bw.y + tz.xyz * bw.z);
        }

        void surf (Input IN, inout SurfaceOutputCave o)
        {
            float3 n = normalize(IN.objNormal);
            float3 bw = pow(abs(n), 4.0);
            bw /= max(1e-4, bw.x + bw.y + bw.z);
            float3 axisSign = sign(n);
            float3 p = IN.objPos / _Tiling;

            float noiseTex = dot(tex2D(_MainTex, p.xz * 0.37).rgb, float3(0.33, 0.34, 0.33)) * bw.y
                           + dot(tex2D(_MainTex, p.zy * 0.37).rgb, float3(0.33, 0.34, 0.33)) * bw.x
                           + dot(tex2D(_MainTex, p.xy * 0.37).rgb, float3(0.33, 0.34, 0.33)) * bw.z;
            float steep = saturate(IN.color.g);
            float exposure = saturate(IN.color.a);

            // The moon's colour rule: flat A → flat B by height-noise, then the
            // steep colour on slopes.
            float blend = smoothstep(0.35, 0.65, IN.color.r * 0.6 + noiseTex * 0.6 + steep * 0.25);
            fixed3 flat = lerp(_FlatColA.rgb, _FlatColB.rgb, blend);
            float inside = 1.0 - smoothstep(0.2, 0.6, exposure);
            fixed3 steepCol = lerp(_SteepCol.rgb, _SteepColInside.rgb, inside);
            fixed3 alb = lerp(flat, steepCol, steep);
            o.Albedo = alb * _Color.rgb;

            // The moon's normal rule: flat map on the flat, steep map on slopes.
            float3 nFlat = TriplanarNormal(_NormalFlat, p, n, bw, axisSign);
            float3 nSteep = TriplanarNormal(_NormalSteep, p, n, bw, axisSign);
            float3 objN = normalize(lerp(nFlat, nSteep, steep));
            objN = normalize(lerp(n, objN, _NormalStrength));

            // Object space → tangent space, which is what the surface shader wants.
            float3 T = IN.objTangent.xyz;
            if (length(T) < 1e-4) T = abs(n.y) < 0.9 ? cross(n, float3(0, 1, 0)) : cross(n, float3(1, 0, 0));
            T = normalize(T - n * dot(n, T));
            float3 B = cross(n, T) * (IN.objTangent.w < 0 ? -1.0 : 1.0);
            o.Normal = normalize(float3(dot(objN, T), dot(objN, B), dot(objN, n)));

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
