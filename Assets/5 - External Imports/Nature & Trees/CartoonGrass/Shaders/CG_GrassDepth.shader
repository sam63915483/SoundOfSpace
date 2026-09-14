// Depth-only pass for the instanced grass. Graphics.DrawMeshInstanced grass is
// NOT included in Unity's camera depth-texture prepass, so the screen-space
// atmosphere post-process reads the BACKGROUND depth where grass is and washes
// out any blade silhouetted against the sky. InstancedGrassRenderer renders the
// same grass batches with this shader via a CommandBuffer at
// CameraEvent.AfterDepthTexture, so the grass appears in _CameraDepthTexture at
// its true depth and the atmosphere tints it correctly. ColorMask 0 = depth
// only (no colour written).
//
// Silhouette dilation (_DepthDilatePixels): the depth texture is single-sampled
// even when colour rendering is MSAA. A thin, far blade anti-aliases into a
// partly-green pixel in colour, but its sliver misses that pixel's single depth
// sample — so the atmosphere reads "sky" there and washes the blade to sky
// colour (the see-through / "glass" blades on the horizon). To stop it we fatten
// each blade's DEPTH silhouette by a couple of screen pixels along its width,
// so the depth sample lands inside the blade and the atmosphere reads "near".
// Width direction is derived from the blade's local up axis (these meshes grow
// along +Y), so it works regardless of the mesh's normals. Colour is never
// dilated — this is depth-only, so the visible blade is unchanged.
Shader "CartoonGrass/GrassDepth"
{
    Properties
    {
        _DepthDilatePixels ("Depth silhouette dilation (px)", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setupGrassDepth
            #include "UnityCG.cginc"

            // ── GPU-resident grass (InstancedGrassRenderer.gpuResident) ─────────
            // Graphics.DrawMeshInstancedIndirect path: the instance matrix is not in
            // Unity's instance array — it is rebuilt here from the planet's frame and
            // a planet-local blade matrix, both handed over in buffers. The compute
            // cull (GrassCull.compute) wrote which slots are visible this frame.
            // Guarded so the legacy DrawMeshInstanced path compiles exactly as before.
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            struct GrassBladeData { float4x4 m; float4 meta; };
            StructuredBuffer<GrassBladeData> _GrassBlades;
            StructuredBuffer<uint> _GrassVisIdx;
            float4x4 _GrassL2W;
            float4 _GrassViewerLocal;   // viewer in the planet's local frame
            float4 _GrassGrow;          // near radius, far radius, falloff exponent, grow band
#endif
            void setupGrassDepth()
            {
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                GrassBladeData bd = _GrassBlades[_GrassVisIdx[unity_InstanceID]];
                // Grow-in: the same density curve GrassCull.compute culled with. A blade
                // scales from nothing over the last _GrassGrow.w of density as you approach,
                // on its own random (meta.z), so grass never pops into existence.
                float3 pl = float3(bd.m._m03, bd.m._m13, bd.m._m23);
                float d = distance(pl, _GrassViewerLocal.xyz);
                float t = saturate((d - _GrassGrow.x) / max(0.01, _GrassGrow.y - _GrassGrow.x));
                float density = pow(1.0 - t, _GrassGrow.z);
                float g = max(0.001, saturate((density - bd.meta.z) / max(0.001, _GrassGrow.w)));
                float4x4 m = mul(_GrassL2W, bd.m);
                m._m00_m01_m02 *= g; m._m10_m11_m12 *= g; m._m20_m21_m22 *= g;
                unity_ObjectToWorld = m;
                // Affine inverse for rotation × UNIFORM scale + translation: (R S)^-1 = Rᵀ / s².
                float3 c0 = float3(m._m00, m._m10, m._m20);
                float invS2 = 1.0 / max(1e-8, dot(c0, c0));
                float3x3 ri = transpose((float3x3)m) * invS2;
                float3 tr = float3(m._m03, m._m13, m._m23);
                float3 it = -mul(ri, tr);
                unity_WorldToObject = float4x4(
                    ri._m00, ri._m01, ri._m02, it.x,
                    ri._m10, ri._m11, ri._m12, it.y,
                    ri._m20, ri._m21, ri._m22, it.z,
                    0.0, 0.0, 0.0, 1.0);
#endif
            }

            float _DepthDilatePixels;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                // Fatten the silhouette along the blade's screen-space WIDTH by a
                // fixed pixel amount (constant on screen regardless of distance,
                // via the * o.pos.w that cancels the later perspective divide).
                if (_DepthDilatePixels > 0.0)
                {
                    // Screen-space direction of the blade's local width axis (+X).
                    // We push +X vertices that way and -X vertices the other way,
                    // keyed off sign(v.vertex.x). That key is STABLE even when the
                    // blade is edge-on/sub-pixel — the exact case that washes out —
                    // unlike a screen-space side test, which collapses to ~0 there
                    // (the bug in the previous version: thin blades got no widening).
                    float4 baseClip  = UnityObjectToClipPos(float4(0.0, 0.0, 0.0, 1.0));
                    float4 widthClip = UnityObjectToClipPos(float4(1.0, 0.0, 0.0, 1.0));
                    float2 wdir = widthClip.xy / max(1e-5, widthClip.w)
                                - baseClip.xy  / max(1e-5, baseClip.w);
                    float wlen = length(wdir);
                    if (wlen > 1e-6)
                    {
                        wdir /= wlen;
                        // 2/_ScreenParams = NDC per pixel; * o.pos.w keeps it a
                        // constant pixel amount after the perspective divide.
                        float2 ndcPerPix = 2.0 / _ScreenParams.xy;
                        o.pos.xy += wdir * sign(v.vertex.x)
                                  * (_DepthDilatePixels * ndcPerPix) * o.pos.w;
                    }
                }
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return 0; }
            ENDCG
        }
    }
}
