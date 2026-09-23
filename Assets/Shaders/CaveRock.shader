// Cave rock: object-space triplanar albedo + normal, faceted vertex normals,
// vertex-colour tint, and SKY EXPOSURE in the vertex alpha.
//
// The exposure is what makes the inside of a cave dark. Unity's ambient light
// and the sun reach everywhere — there is no occlusion for ambient, and sun
// shadows from a planet's terrain onto geometry 30 m underground are not
// something to rely on. So the generator measures, per vertex, how much sky
// each point can see and stores it in COLOR.a; here the directional light
// (forward base pass) and the ambient/SH term are multiplied by it. Point and
// spot lights — the flashlight, the placed cave lamps — are NOT, so they light
// the rock at full strength.
//
// Built-in RP, forward only. Referenced by the Cave_Rock_* material assets, so
// it is included in builds (never rely on Shader.Find for this).
Shader "Custom/CaveRock"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Albedo (triplanar)", 2D) = "white" {}
        _BumpMap ("Normal RG=xy (triplanar)", 2D) = "bump" {}
        _Tiling ("Metres per tile", Float) = 3.5
        _BumpScale ("Normal strength", Range(0, 3)) = 1.2
        _ExposureFloor ("Minimum daylight inside", Range(0, 0.3)) = 0.03
        _ExposurePower ("Daylight falloff", Range(0.5, 3)) = 1.6
        _MoonColor ("Surface rock colour (matches the moon)", Color) = (0.78, 0.78, 0.78, 1)
        _MoonBlend ("How fully sky-lit rock takes that colour", Range(0, 1)) = 0.9
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
        fixed4 _Color;
        float _Tiling;
        float _BumpScale;
        float _ExposureFloor;
        float _ExposurePower;
        fixed4 _MoonColor;
        float _MoonBlend;

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

        void surf (Input IN, inout SurfaceOutputCave o)
        {
            float3 n = normalize(IN.objNormal);
            float3 bw = pow(abs(n), 4.0);
            bw /= max(1e-4, bw.x + bw.y + bw.z);
            float3 axisSign = sign(n);
            float3 p = IN.objPos / _Tiling;

            // Albedo from three projections.
            fixed3 cx = tex2D(_MainTex, p.zy).rgb;
            fixed3 cy = tex2D(_MainTex, p.xz).rgb;
            fixed3 cz = tex2D(_MainTex, p.xy).rgb;
            fixed3 alb = cx * bw.x + cy * bw.y + cz * bw.z;

            // Normals: whiteout blend into object space.
            float2 tx = (tex2D(_BumpMap, p.zy).rg * 2.0 - 1.0) * _BumpScale;
            float2 ty = (tex2D(_BumpMap, p.xz).rg * 2.0 - 1.0) * _BumpScale;
            float2 tz = (tex2D(_BumpMap, p.xy).rg * 2.0 - 1.0) * _BumpScale;
            float3 nX = float3(tx, sqrt(saturate(1.0 - dot(tx, tx))));
            float3 nY = float3(ty, sqrt(saturate(1.0 - dot(ty, ty))));
            float3 nZ = float3(tz, sqrt(saturate(1.0 - dot(tz, tz))));
            nX.xy *= axisSign.x; nY.xy *= axisSign.y; nZ.xy *= axisSign.z;
            nX = float3(nX.xy + n.zy, abs(nX.z) * n.x);
            nY = float3(nY.xy + n.xz, abs(nY.z) * n.y);
            nZ = float3(nZ.xy + n.xy, abs(nZ.z) * n.z);
            float3 objN = normalize(nX.zyx * bw.x + nY.xzy * bw.y + nZ.xyz * bw.z);

            // Object space → tangent space, which is what the surface shader wants.
            float3 T = IN.objTangent.xyz;
            float tl = length(T);
            if (tl < 1e-4) T = abs(n.y) < 0.9 ? cross(n, float3(0, 1, 0)) : cross(n, float3(1, 0, 0));
            T = normalize(T - n * dot(n, T));
            float3 B = cross(n, T) * (IN.objTangent.w < 0 ? -1.0 : 1.0);
            float3 tsN = float3(dot(objN, T), dot(objN, B), dot(objN, n));
            o.Normal = normalize(tsN);

            // Rock the sky can see is the moon's surface: flat moon grey, no
            // cracks — so a mouth reads as part of the moon, not a lump on it.
            float surface = smoothstep(0.25, 0.7, IN.color.a) * _MoonBlend;
            fixed3 inside = alb * IN.color.rgb * _Color.rgb;
            o.Albedo = lerp(inside, _MoonColor.rgb, surface);
            o.Normal = normalize(lerp(o.Normal, float3(0, 0, 1), surface * 0.8));
            o.Exposure = _ExposureFloor + (1.0 - _ExposureFloor) * pow(saturate(IN.color.a), _ExposurePower);
            o.Emission = 0;
            o.Specular = 0;
            o.Gloss = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
