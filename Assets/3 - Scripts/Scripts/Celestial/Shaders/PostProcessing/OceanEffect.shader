Shader "Hidden/Ocean"
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
			#include "../Includes/Triplanar.cginc"

			struct appdata {
					float4 vertex : POSITION;
					float2 uv : TEXCOORD0;
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
					float3 viewVector = mul(unity_CameraInvProjection, float4(v.uv * 2 - 1, 0, -1));
					output.viewVector = mul(unity_CameraToWorld, float4(viewVector,0));
					return output;
			}

			float4 colA;
			float4 colB;
			float4 specularCol;
			float depthMultiplier;
			float alphaMultiplier;
			float smoothness;


			sampler2D waveNormalA;
			sampler2D waveNormalB;
			float waveStrength;
			float waveNormalScale;
			float waveSpeed;

			sampler2D _MainTex;
			sampler2D _CameraDepthTexture;
			float4 params;

			float planetScale;
			float3 oceanCentre;
			float oceanRadius;
			float3 dirToSun;

			// ── Cave cutouts ────────────────────────────────────────────────
			// Air pockets inside the ocean sphere. A cave dug below sea level is
			// still DRY, but this effect only knows about a sphere of water, so
			// it drew the sea across the cave mouth and flooded the interior —
			// and, because the underwater material is composited AFTER the
			// atmosphere, it painted the sky black from inside too.
			//
			// Set by CaveOceanCutout (a plain MonoBehaviour, outside this
			// folder). If _NumCaveCapsules is 0 — i.e. always, on any scene
			// without caves — every line below is skipped and this effect
			// behaves exactly as it did before.
			// Must match CaveOceanCutout.MaxCapsules. The current cave alone is
			// 21 capsules (tunnels + branches + rooms), so this has headroom.
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

			fixed4 frag (v2f i) : SV_Target
			{
	
				fixed4 originalCol = tex2D(_MainTex, i.uv);

				float3 rayPos = _WorldSpaceCameraPos;
				float viewLength = length(i.viewVector);
				float3 rayDir = i.viewVector / viewLength;

				float nonlin_depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
            float sceneDepth = LinearEyeDepth(nonlin_depth) * viewLength;

				float2 hitInfo = raySphere(oceanCentre, oceanRadius, rayPos, rayDir);
				float dstToOcean = hitInfo.x;
				float dstThroughOcean = hitInfo.y;
				float3 rayOceanIntersectPos = rayPos + rayDir * dstToOcean - oceanCentre;

				// dst that view ray travels through ocean (before hitting terrain / exiting ocean)
				float oceanViewDepth = min(dstThroughOcean, sceneDepth - dstToOcean);

				// Caves are air, not water. If the stretch of "ocean" this ray
				// crosses lies inside a cave, there is nothing to draw: looking
				// into a cave mouth from outside, and standing inside one below
				// sea level, are both this case.
				if (_NumCaveCapsules > 0 && oceanViewDepth > 0) {
					float3 spanStart = rayPos + rayDir * max(dstToOcean, 0);
					float3 spanEnd = rayPos + rayDir * (max(dstToOcean, 0) + oceanViewDepth);
					if (InsideCaveCapsule(spanStart) || InsideCaveCapsule((spanStart + spanEnd) * 0.5)) {
						oceanViewDepth = 0;
					}
				}

				if (oceanViewDepth > 0) {
					float3 clipPlanePos = rayPos + i.viewVector * _ProjectionParams.y;

					float dstAboveWater = length(clipPlanePos - oceanCentre) - oceanRadius;

					float t = 1 - exp(-oceanViewDepth / planetScale * depthMultiplier);
					float alpha =  1-exp(-oceanViewDepth / planetScale * alphaMultiplier);
					float4 oceanCol = lerp(colA, colB, t);

					float3 oceanSphereNormal = normalize(rayOceanIntersectPos);

					float2 waveOffsetA = float2(_Time.x * waveSpeed, _Time.x * waveSpeed * 0.8);
					float2 waveOffsetB = float2(_Time.x * waveSpeed * - 0.8, _Time.x * waveSpeed * -0.3);
					float3 waveNormal = triplanarNormal(rayOceanIntersectPos, oceanSphereNormal, waveNormalScale / planetScale, waveOffsetA, waveNormalA);
					waveNormal = triplanarNormal(rayOceanIntersectPos, waveNormal, waveNormalScale / planetScale, waveOffsetB, waveNormalB);
					waveNormal = normalize(lerp(oceanSphereNormal, waveNormal, waveStrength));
					//return float4(oceanNormal * .5 + .5,1);
					float diffuseLighting = saturate(dot(oceanSphereNormal, dirToSun));
					float specularAngle = acos(dot(normalize(dirToSun - rayDir), waveNormal));
					float specularExponent = specularAngle / (1 - smoothness);
					float specularHighlight = exp(-specularExponent * specularExponent);
				
					oceanCol *= diffuseLighting;
					oceanCol += specularHighlight * (dstAboveWater > 0) * specularCol;
					
					//return float4(oceanSphereNormal,1);
					float4 finalCol =  originalCol * (1-alpha) + oceanCol * alpha;
					return float4(finalCol.xyz, params.x);
				}

				
				return originalCol;
			}
			ENDCG
		}
	}
}
