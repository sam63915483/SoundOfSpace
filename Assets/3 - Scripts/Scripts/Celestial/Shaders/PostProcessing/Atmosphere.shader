Shader "Hidden/Atmosphere"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#include "UnityCG.cginc"
			#include "../Includes/Math.cginc"
			//

			struct appdata {
					float4 vertex : POSITION;
					float4 uv : TEXCOORD0;
			};

			struct v2f {
					float4 pos : SV_POSITION;
					float2 uv : TEXCOORD0;
					float3 viewVector : TEXCOORD1;
			};

			v2f vert (appdata v) {
					v2f output;
					output.pos = UnityObjectToClipPos(v.vertex);
					output.uv = v.uv;
					// Camera space matches OpenGL convention where cam forward is -z. In unity forward is positive z.
					// (https://docs.unity3d.com/ScriptReference/Camera-cameraToWorldMatrix.html)
					float3 viewVector = mul(unity_CameraInvProjection, float4(v.uv.xy * 2 - 1, 0, -1));
					output.viewVector = mul(unity_CameraToWorld, float4(viewVector,0));
					return output;
			}

			float2 squareUV(float2 uv) {
				float width = _ScreenParams.x;
				float height =_ScreenParams.y;
				//float minDim = min(width, height);
				float scale = 1000;
				float x = uv.x * width;
				float y = uv.y * height;
				return float2 (x/scale, y/scale);
			}



			sampler2D _BlueNoise;
			sampler2D _MainTex;
			sampler2D _BakedOpticalDepth;
			sampler2D _CameraDepthTexture;
			float4 params;

			float3 dirToSun;

			float3 planetCentre;
			float atmosphereRadius;
			float oceanRadius;
			float planetRadius;

			// ── Cave cutouts ────────────────────────────────────────────────
			// Same globals OceanEffect.shader uses, set by CaveOceanCutout.
			//
			// This shader clips the atmosphere at the ocean surface:
			//     dstToSurface = min(sceneDepth, dstToOcean)
			// Stand inside a cave below sea level and dstToOcean is 0, so
			// dstThroughAtmosphere collapses to 0, the atmosphere is skipped
			// entirely and you see the raw space skybox — the sky goes black the
			// moment you drop into the hole. Nothing to do with the ocean being
			// drawn; the atmosphere removes ITSELF.
			//
			// Guarded on _NumCaveCapsules, so with no caves in the scene this is
			// bit-for-bit the original behaviour.
			#define MAX_CAVE_CAPSULES 32
			int _NumCaveCapsules;
			float4 _CaveCapsuleA[MAX_CAVE_CAPSULES];   // xyz = endpoint A, w = radius
			float4 _CaveCapsuleB[MAX_CAVE_CAPSULES];   // xyz = endpoint B

			bool InsideCaveCapsule(float3 p) {
				for (int c = 0; c < _NumCaveCapsules; c++) {
					float3 a = _CaveCapsuleA[c].xyz;
					float3 b = _CaveCapsuleB[c].xyz;
					float r = _CaveCapsuleA[c].w;
					float3 ab = b - a;
					float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
					if (distance(p, a + ab * t) < r) return true;
				}
				return false;
			}

			// Paramaters
			int numInScatteringPoints;
			int numOpticalDepthPoints;
			float intensity;
			float4 scatteringCoefficients;
			float ditherStrength;
			float ditherScale;
			float densityFalloff;

			
			float densityAtPoint(float3 densitySamplePoint) {
				// CLAMPED AT THE SURFACE. Below it this height goes negative, and
				// then exp(-height01 * densityFalloff) is exp(+something) — the
				// air density grows exponentially the deeper you are. Nothing
				// ever sampled below the surface before, because the atmosphere
				// was clipped at the ocean; now that caves let it run underground
				// that blow-up shows up as a bright fog filling the cave as soon
				// as the camera goes in.
				//
				// There is no air below the ground, so the honest value is
				// "no denser than at sea level".
				float heightAboveSurface = max(0, length(densitySamplePoint - planetCentre) - planetRadius);
				float height01 = heightAboveSurface / (atmosphereRadius - planetRadius);
				float localDensity = exp(-height01 * densityFalloff) * (1 - height01);
				return localDensity;
			}
			
			float opticalDepth(float3 rayOrigin, float3 rayDir, float rayLength) {
				float3 densitySamplePoint = rayOrigin;
				float stepSize = rayLength / (numOpticalDepthPoints - 1);
				float opticalDepth = 0;

				for (int i = 0; i < numOpticalDepthPoints; i ++) {
					float localDensity = densityAtPoint(densitySamplePoint);
					opticalDepth += localDensity * stepSize;
					densitySamplePoint += rayDir * stepSize;
				}
				return opticalDepth;
			}

			float opticalDepthBaked(float3 rayOrigin, float3 rayDir) {
				float height = length(rayOrigin - planetCentre) - planetRadius;
				float height01 = saturate(height / (atmosphereRadius - planetRadius));

				float uvX = 1 - (dot(normalize(rayOrigin - planetCentre), rayDir) * .5 + .5);
				return tex2Dlod(_BakedOpticalDepth, float4(uvX, height01,0,0));
			}

			float opticalDepthBaked2(float3 rayOrigin, float3 rayDir, float rayLength) {
				float3 endPoint = rayOrigin + rayDir * rayLength;
				float d = dot(rayDir, normalize(rayOrigin-planetCentre));
				float opticalDepth = 0;

				const float blendStrength = 1.5;
				float w = saturate(d * blendStrength + .5);
				
				float d1 = opticalDepthBaked(rayOrigin, rayDir) - opticalDepthBaked(endPoint, rayDir);
				float d2 = opticalDepthBaked(endPoint, -rayDir) - opticalDepthBaked(rayOrigin, -rayDir);

				opticalDepth = lerp(d2, d1, w);
				return opticalDepth;
			}
			
			float3 calculateLight(float3 rayOrigin, float3 rayDir, float rayLength, float3 originalCol, float2 uv) {
				float blueNoise = tex2Dlod(_BlueNoise, float4(squareUV(uv) * ditherScale,0,0));
				blueNoise = (blueNoise - 0.5) * ditherStrength;
				
				float3 inScatterPoint = rayOrigin;
				float stepSize = rayLength / (numInScatteringPoints - 1);
				float3 inScatteredLight = 0;
				float viewRayOpticalDepth = 0;

				for (int i = 0; i < numInScatteringPoints; i ++) {
					float sunRayLength = raySphere(planetCentre, atmosphereRadius, inScatterPoint, dirToSun).y;
					float sunRayOpticalDepth = opticalDepthBaked(inScatterPoint + dirToSun * ditherStrength, dirToSun);
					float localDensity = densityAtPoint(inScatterPoint);
					viewRayOpticalDepth = opticalDepthBaked2(rayOrigin, rayDir, stepSize * i);
					float3 transmittance = exp(-(sunRayOpticalDepth + viewRayOpticalDepth) * scatteringCoefficients);
					
					inScatteredLight += localDensity * transmittance;
					inScatterPoint += rayDir * stepSize;
				}
				inScatteredLight *= scatteringCoefficients * intensity * stepSize / planetRadius;
				inScatteredLight += blueNoise * 0.01;

				// Attenuate brightness of original col (i.e light reflected from planet surfaces)
				// brightnessAdaptionStrength was 0.15 (Sebastian Lague's original "hacky" auto-eye-adaption term).
				// It dims the ground proportionally to camera-ray-direction in-scattered light, which made
				// the ground brightness visibly "breathe" as the player turned the camera in third-person.
				// Zeroed out 2026 to kill that artifact. The physically-correct atmosphere thickness dimming
				// (reflectedLightOutScatterStrength * viewRayOpticalDepth) is preserved.
				const float brightnessAdaptionStrength = 0.0;
				const float reflectedLightOutScatterStrength = 3;
				float brightnessAdaption = dot (inScatteredLight,1) * brightnessAdaptionStrength;
				float brightnessSum = viewRayOpticalDepth * intensity * reflectedLightOutScatterStrength + brightnessAdaption;
				float reflectedLightStrength = exp(-brightnessSum);
				float hdrStrength = saturate(dot(originalCol,1)/3-1);
				reflectedLightStrength = lerp(reflectedLightStrength, 1, hdrStrength);
				float3 reflectedLight = originalCol * reflectedLightStrength;

				float3 finalCol = reflectedLight + inScatteredLight;

				
				return finalCol;
			}


			float4 frag (v2f i) : SV_Target
			{
				float4 originalCol = tex2D(_MainTex, i.uv);
				float sceneDepthNonLinear = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
				float sceneDepth = LinearEyeDepth(sceneDepthNonLinear) * length(i.viewVector);
											
				float3 rayOrigin = _WorldSpaceCameraPos;
				float3 rayDir = normalize(i.viewVector);
				
				float2 oceanHit = raySphere(planetCentre, oceanRadius, rayOrigin, rayDir);
				float dstToOcean = oceanHit.x;
				// Where along the ray the AIR begins. 0 = the camera is already in
				// air. Set by the under-water rule below to the point where the
				// ray leaves the sea (second sanctioned exception, 2026-09-08).
				float airStart = 0;

				// ── Caves ───────────────────────────────────────────────────
				// NOTHING HERE MAY DEPEND ON WHERE THE CAMERA IS. Testing the
				// camera position is what made the cave visibly change the
				// instant you stepped through the mouth: a binary switch on
				// rayOrigin means every pixel is treated one way outside and
				// another way inside, so it pops. Both tests below are about
				// what THIS RAY does, so a given surface looks identical from
				// five metres outside and one metre inside.
				if (_NumCaveCapsules > 0) {

					// 1. Where this ray would meet "water" — is that point
					//    actually inside a cave? Then it isn't water, and it must
					//    not clip the atmosphere away. (Standing in a cave below
					//    sea level, this point is the camera itself, which is
					//    what stopped the sky going black.)
					float3 oceanPoint = rayOrigin + rayDir * max(dstToOcean, 0);
					if (InsideCaveCapsule(oceanPoint)) dstToOcean = 1e20;

					// 2. If the ray ENDS inside the cave it never reached open
					//    air, so there is no sky along it and no atmosphere to
					//    add — that's a rock wall, and it should look like one
					//    from either side of the mouth.
					float3 rayEnd = rayOrigin + rayDir * min(sceneDepth, 500);
					if (InsideCaveCapsule(rayEnd)) return originalCol;
				}

				// ── Under water (Sam-approved edit, 2026-09-08) ─────────────
				// With the camera inside the ocean sphere, dstToOcean is 0, so
				// the clip below collapsed dstThroughAtmosphere to 0 and the sky
				// went BLACK the instant the head dipped under (the space skybox,
				// tinted by the water pass). The sea surface is not where the
				// sky ends — it is where the AIR begins: start the scattering at
				// the point the ray LEAVES the water (oceanHit.y, the exit
				// distance when the origin is inside) and let the ocean pass,
				// which composites after this one, tint that sky by water depth.
				// Looking DOWN at the seabed the exit is beyond the terrain, the
				// segment goes negative and nothing is added — as before.
				// Strictly conditional: above water this branch never runs, so
				// every existing scene is bit-identical. Caves keep priority —
				// their rule above already set dstToOcean to 1e20, so the ray is
				// treated as air from the camera exactly as it was.
				if (dstToOcean < 1e19 && oceanHit.y > 0 &&
				    dot(rayOrigin - planetCentre, rayOrigin - planetCentre) < oceanRadius * oceanRadius) {
					airStart = oceanHit.y;
					dstToOcean = 1e20;
				}

				float dstToSurface = min(sceneDepth, dstToOcean);

				float2 hitInfo = raySphere(planetCentre, atmosphereRadius, rayOrigin, rayDir);
				float dstToAtmosphere = max(hitInfo.x, airStart);
				float dstThroughAtmosphere = min(hitInfo.x + hitInfo.y, dstToSurface) - dstToAtmosphere;
				
				if (dstThroughAtmosphere > 0) {
					const float epsilon = 0.0001;
					float3 pointInAtmosphere = rayOrigin + rayDir * (dstToAtmosphere + epsilon);
					float3 light = calculateLight(pointInAtmosphere, rayDir, dstThroughAtmosphere - epsilon * 2, originalCol, i.uv);
					return float4(light, 1);
				}
				return originalCol;
			}


			ENDCG
		}
	}
}
