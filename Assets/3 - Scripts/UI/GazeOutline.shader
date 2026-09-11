// Inverted-hull rim for the gaze-highlight system (GazeHighlight.cs): the
// looked-at interactable's meshes are re-drawn front-culled and inflated
// along their normals, leaving only a thin silhouette visible around the
// original. ZWrite/ZTest normal, so occluded parts of the rim stay hidden —
// the gaze needs line of sight anyway, and see-through glow would read as an
// x-ray. Works on SkinnedMeshRenderers for free (skinning runs before the
// vertex shader). Built-in RP.
//
// ── The stencil mask (2026-09-11) ────────────────────────────────────────
// A plain inflated hull draws EVERY inflated face that survives the depth
// test — including the ones inside the object's own outline. On a mesh with
// concavities that is a disaster: an eye socket's normals point OUT of the
// socket, i.e. toward the viewer, so inflating pushes those faces in front of
// the socket rim and you get a green ring inside the eye. Same for an open
// mouth. Sam's cats: "it outlines their eye sockets and mouth as well as
// their body".
//
// So the MASK pass first draws the object's own, un-inflated surface into the
// stencil (colour and depth untouched), and the OUTLINE pass then draws only
// where that mask is NOT set — outside the object's visible silhouette. What
// is left is the outer edge and nothing else, whatever the mesh's topology.
Shader "SoundOfSpace/GazeOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.77, 0.42, 1)
        _OutlineWidth ("Outline Width (m)", Float) = 0.02
        // The wipe (see GazeHighlight): 0 = no rim, 1 = full rim. Pixels whose
        // height along _RevealUp, normalised between _RevealMin and _RevealMax,
        // is above _Reveal are clipped. So it fills bottom-up as this rises and
        // empties top-down as it falls.
        _Reveal ("Reveal", Range(0, 1)) = 1
        _RevealUp ("Reveal Up (world)", Vector) = (0, 1, 0, 0)
        _RevealMin ("Reveal Min (along up)", Float) = 0
        _RevealMax ("Reveal Max (along up)", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+20" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        // Pass 1 — MASK. The object's real, un-inflated surface, marking the
        // stencil across its whole projected silhouette. No colour, no depth.
        //
        // ZTest ALWAYS, deliberately. The first version used LEqual so the mask
        // would "land exactly on the surface the object already drew" — and it
        // did not, because the clone is a separate skinned mesh and its depth
        // comes out a rounding error off the original's. Whenever it landed a
        // hair FARTHER the test failed, the stencil was never written, and the
        // outline drew everywhere as if there were no mask at all. Worst at
        // the eyes, where the separate eyeball renderers sit in FRONT of the
        // socket surface, so the socket's mask failed every time.
        //
        // With Always, the mask is simply "everywhere this object's front faces
        // project" — the true silhouette, regardless of depth precision, and
        // regardless of small child renderers (eyes) that are excluded from
        // outlining but still sit inside the body's projected area. Occlusion
        // is still correct: the OUTLINE pass keeps its own normal depth test, so
        // a rim behind a wall stays hidden. The mask covering the object even
        // where it is occluded only ever REMOVES rim, never adds it.
        Pass
        {
            Name "MASK"
            Cull Back
            ZWrite Off
            ZTest Always
            ColorMask 0
            Stencil
            {
                Ref 5
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return 0; }
            ENDCG
        }

        // Pass 2 — OUTLINE. The inflated hull, front-culled, drawn only where
        // the mask above did NOT land: outside the visible silhouette.
        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On
            Stencil
            {
                Ref 5
                Comp NotEqual
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _Reveal;
            float4 _RevealUp;
            float _RevealMin;
            float _RevealMax;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float  h   : TEXCOORD0;   // 0 at the object's bottom, 1 at its top
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 wnorm = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(wpos + wnorm * _OutlineWidth);
                // Height is measured on the UN-inflated position so the wipe
                // line sits on the object, not on the puffed-out hull.
                float span = max(_RevealMax - _RevealMin, 0.001);
                o.h = (dot(wpos, _RevealUp.xyz) - _RevealMin) / span;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // The wipe. clip() discards where the pixel is above the
                // reveal line; a hair of slack so a fully-revealed outline
                // never loses its topmost pixels to float error.
                clip(_Reveal + 0.01 - i.h);
                return _OutlineColor;
            }
            ENDCG
        }
    }
}
