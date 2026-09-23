// THE CAVE IS THE MOON. This is Celestial/MoonA.shader (the moon's own terrain
// shader, read-only in its zone) copied line for line, with three additions:
//   1. DARKNESS — the vertex alpha carries sky exposure (written by CaveSolid);
//      in the forward base pass the sun and the ambient/reflection terms are
//      multiplied by it, so the inside of a cave is dark while point lights
//      (flashlight, crystal glow) light it at full strength.
//   2. FEWER CRATERS DEEPER — the vertex blue channel is path distance from
//      the mouth; the crater normal map and the ejecta rays fade out along it.
//      A cave is sheltered; nothing hits it.
//   3. It compiles from Assets/Shaders (absolute include paths) and never
//      goes through the deferred path.
// Every material property has MoonA's name, so MoonSkinMaterialSync can copy
// the moon's live terrain material into ours with CopyPropertiesFromMaterial:
// same colours, same normal maps, same heightMinMax and bodyScale, same
// biome noise. The cave mesh is authored in the generator's unit-sphere
// space under a transform that reproduces the generator's, so vertPos and
// terrainData (UV0, copied from the nearest terrain vertex) mean the same
// thing here as on the ground beside it.
Shader "Custom/CaveMoonRock"
{
	Properties
	{
		[Header(Colours)]
		_PrimaryColA ("Primary A", Color) = (1,1,1,1)
		_SecondaryColA ("Secondary A", Color) = (1,1,1,1)
		_PrimaryColB ("Primary B", Color) = (1,1,1,1)
		_SecondaryColB ("Secondary B", Color) = (1,1,1,1)
		_EjectaCol ("Ejecta Col", Color) = (1,1,1,1)

		_BiomeBlendStrength ("Biome Blend", Float) = 2
		_BiomeWarpStrength("Biome Warp", Float) = 0
		[Toggle()]
      _UseEjecta("Use Ejecta", float) = 1
		_EjectaBrightness("Ejecta Brightness", Float) = 0.1

		[Header(Normals)]
		[NoScaleOffset] _CraterRay ("Crater Ray", 2D) = "white" {}
		[NoScaleOffset] _MainTex ("Albedo (RGB)", 2D) = "white" {}
		[NoScaleOffset] _NormalMapFlat ("Normal Map Flat", 2D) = "bump" {}
		[NoScaleOffset] _NormalMapSteep ("Normal Map Steep", 2D) = "bump" {}
		_NormalMapFlatScale ("Normal Map Flat Scale", Float) = 10
		_NormalMapSteepScale ("Normal Map Steep Scale", Float) = 10
		_MainTexScale ("Main Tex Scale", Float) = 10

		_RandomBiomeValues ("Random Biome Values", Vector) = (0,0,0,0)
		_NormalMapStrength ("Normal Map Strength", Range(0,1)) = 0.3

		[Header(Other)]
		_FresnelCol("Fresnel Colour", Color) = (1,1,1,1)
		_FresnelStrengthNear("Fresnel Strength Min", float) = 2
		_FresnelStrengthFar("Fresnel Strength Max", float) = 5
		_FresnelPow("Fresnel Power", float) = 12

		_SmoothnessA ("Smoothness A", Range(0,1)) = 0.5
		_SmoothnessB ("Smoothness B", Range(0,1)) = 0.5
		_SmoothnessEjecta ("Smoothness Ejecta", Range(0,1)) = 0.5
		_MetallicA ("Metallic A", Range(0,1)) = 0.0
		_MetallicB ("Metallic B", Range(0,1)) = 0.0

		_TestParams ("Test Params", Vector) = (0,0,0,0)

		[Header(Cave)]
		_ExposureFloor ("Minimum daylight inside", Range(0, 0.3)) = 0.05
		_ExposurePower ("Daylight falloff", Range(0.5, 3)) = 1.6
		_CraterFadeStart ("Craters fade out: start (path 0..1)", Range(0, 1)) = 0.25
		_CraterFadeEnd ("Craters fade out: end (path 0..1)", Range(0, 1)) = 0.9
	 }
	 SubShader
	 {
		Tags { "RenderType"="Opaque" }
		LOD 200

		CGPROGRAM
		#pragma surface surf CaveStd fullforwardshadows vertex:vert addshadow exclude_path:deferred exclude_path:prepass
		#pragma target 3.5

		#include "UnityPBSLighting.cginc"
		#include "Assets/3 - Scripts/Scripts/Celestial/Shaders/Includes/Triplanar.cginc"
		#include "Assets/3 - Scripts/Scripts/Celestial/Shaders/Includes/Math.cginc"

		sampler2D _MainTex;
		sampler2D _CraterRay;
		sampler2D _NormalMapFlat;
		sampler2D _NormalMapSteep;

		float _NormalMapStrength;
		float _NormalMapFlatScale;
		float _NormalMapSteepScale;
		float _MainTexScale;

		float _SmoothnessA;
		float _SmoothnessB;
		float _SmoothnessEjecta;
		float _MetallicA;
		float _MetallicB;

		float2 heightMinMax;

		float _EjectaBrightness;
		float _UseEjecta;

		float _BiomeBlendStrength;
		float _BiomeWarpStrength;
		float4 _RandomBiomeValues;
		float4 _TestParams;
		float _AvgBiomeNoiseDst;

		// Colors:
		float4 _PrimaryColA;
		float4 _SecondaryColA;
		float4 _PrimaryColB;
		float4 _SecondaryColB;
		float4 _EjectaCol;

		float4 _FresnelCol;
		float _FresnelStrengthNear;
		float _FresnelStrengthFar;
		float _FresnelPow;
		float bodyScale;

		float _ExposureFloor;
		float _ExposurePower;
		float _CraterFadeStart;
		float _CraterFadeEnd;

		// Per-fragment darkness, set in surf and read by the lighting function.
		static float g_exposure = 1.0;

		struct Input
		{
			float3 vertPos;
			float3 normal;
			float4 terrainData;
			float3 worldNormal;
			float4 tangent;
			float4 craterUV;
			float fresnel;
			float4 color : COLOR;
			INTERNAL_DATA
		};

		void vert (inout appdata_full v, out Input o) {
			UNITY_INITIALIZE_OUTPUT (Input,o);
			o.vertPos = v.vertex;
			o.terrainData = v.texcoord;
			o.normal = v.normal;
			o.tangent = v.tangent;
			//
			float craterDst = v.texcoord.y;
			float2 craterUV = 0.5 + float2(cos(v.texcoord.x), sin(v.texcoord.x)) * craterDst;
			o.craterUV = float4(craterUV.xy, craterDst, 0);

			// Fresnel (fade out when close to body)
			float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
			float3 bodyWorldCentre = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
			float camRadiiFromSurface = (length(bodyWorldCentre - _WorldSpaceCameraPos.xyz) - bodyScale) / bodyScale;
			float fresnelT = smoothstep(0,1,camRadiiFromSurface);
			float3 viewDir = normalize(worldPos - _WorldSpaceCameraPos.xyz);
			float3 normWorld = normalize(mul(unity_ObjectToWorld, float4(v.normal,0)));
			float fresStrength = lerp(_FresnelStrengthNear, _FresnelStrengthFar, fresnelT);
			o.fresnel = saturate(fresStrength * pow(1 + dot(viewDir, normWorld), _FresnelPow));
		}

		// Standard lighting with the cave's darkness applied: the sun (forward
		// base pass) and the ambient/reflection terms scale with exposure;
		// additional lights (forward add) do not.
		inline half4 LightingCaveStd (SurfaceOutputStandard s, float3 viewDir, UnityGI gi)
		{
			#ifdef UNITY_PASS_FORWARDBASE
				gi.light.color *= g_exposure;
			#endif
			gi.indirect.diffuse *= g_exposure;
			gi.indirect.specular *= g_exposure;
			return LightingStandard (s, viewDir, gi);
		}

		inline void LightingCaveStd_GI (SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
		{
			LightingStandard_GI (s, data, gi);
		}

		void surf (Input IN, inout SurfaceOutputStandard o)
		{
			g_exposure = _ExposureFloor + (1.0 - _ExposureFloor) * pow(saturate(IN.color.a), _ExposurePower);
			float craterFade = 1.0 - smoothstep(_CraterFadeStart, _CraterFadeEnd, IN.color.b);   // 1 at the mouth, 0 deep in

			float4 terrainData = IN.terrainData;
			float height01 = remap01(length(IN.vertPos), heightMinMax.x, heightMinMax.y);

			// Calculate steepness: 0 where totally flat, 1 at max steepness
			const float maxSteepnessApprox = 0.3;
			float steepness = 1 - dot(IN.normal, normalize(IN.vertPos));
			steepness = remap01(steepness, 0, maxSteepnessApprox);

			float warpNoise = Blend(0, 3, IN.terrainData.z);
				// Sample noise texture
			float4 texNoise = triplanar(IN.vertPos, IN.normal, _MainTexScale, _MainTex);

			// Sample normal maps:
			// There are two maps, one for steep slopes like mountains and crater walls, and one for flat regions
			// Slopes always use the steep map, but flat regions blend between the flat and steep maps to add variety
			float3 normalMapFlat = triplanarNormalTangentSpace(IN.vertPos, IN.normal, _NormalMapFlatScale, IN.tangent, _NormalMapFlat);
			float3 normalMapSteep = triplanarNormalTangentSpace(IN.vertPos, IN.normal, _NormalMapSteepScale, IN.tangent, _NormalMapSteep);
			float normalBlend = lerp(texNoise.r, texNoise.g, Blend(0, 2, terrainData.z));
			// Deeper in, the little craters go: the steep (rock) map takes over.
			normalBlend = max(normalBlend, 1.0 - craterFade);
			float3 flatAndSteepNormal = lerp(normalMapFlat, normalMapSteep, normalBlend);
			float3 normal = lerp(flatAndSteepNormal, normalMapSteep, steepness);
			normal = lerp(float3(0,0,1), normal, _NormalMapStrength);
			o.Normal = normal;

			// Blend between primary and secondary colours by height (plus some noise)
			float heightNoiseA = -texNoise.g * steepness - (texNoise.b - 0.5) * 0.7 + (texNoise.a - 0.5) * _RandomBiomeValues.x;
			float heightNoiseB = (texNoise.g-0.5) * _RandomBiomeValues.y + (texNoise.r-0.5) * _RandomBiomeValues.z;
			float heightBlendWeightA = Blend(0.5, 0.6, height01 + heightNoiseA) * warpNoise;
			float heightBlendWeightB = Blend(0.5, 0.6, height01 + heightNoiseB) * warpNoise;
			float3 colBiomeA = lerp(_PrimaryColA, _SecondaryColA, heightBlendWeightA);
			float3 colBiomeB = lerp(_PrimaryColB, _SecondaryColB, heightBlendWeightB);

			// Blend between colour A and B based on terrain data noise
			float biomeNoise = dot(texNoise.ga - 0.5, _RandomBiomeValues.zw) * 4;
			float biomeWeight = Blend(_AvgBiomeNoiseDst * 0.8 + terrainData.z * _BiomeWarpStrength, _BiomeBlendStrength + warpNoise * 15, terrainData.w + biomeNoise);
			float3 biomeCol = lerp(colBiomeA, colBiomeB, biomeWeight);

			// Crater ejecta (fades out with depth into the cave)
			float ejecta = tex2D(_CraterRay, IN.craterUV.xy).r;
			float craterAlpha = IN.craterUV.z < 1;
			craterAlpha = abs(IN.terrainData.y) < 0.5;
			float3 compositeCol = lerp(biomeCol, _EjectaCol, saturate(ejecta * 1.5) * craterAlpha * _UseEjecta * craterFade);
			compositeCol = lerp(compositeCol, _FresnelCol, IN.fresnel);
			o.Albedo = compositeCol;

			// Set metallic and smoothness
			o.Metallic = lerp(_MetallicA, _MetallicB, biomeWeight);
			float smoothness = lerp(_SmoothnessA, _SmoothnessB, biomeWeight);
			smoothness = lerp(smoothness, _SmoothnessEjecta, saturate(ejecta * 1.5) * craterAlpha * craterFade);
			o.Smoothness = smoothness;
		}
		ENDCG
	}
	FallBack "Diffuse"
}
