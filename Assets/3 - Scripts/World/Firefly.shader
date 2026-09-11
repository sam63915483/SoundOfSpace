// Fireflies (Sam, 2026-09-11): a bobber-sized bug with a dark head and a
// yellow-orange glowing tail, plus a soft additive halo so it reads as a
// twinkle from a hundred metres. See docs/superpowers/specs/2026-09-11-fireflies-design.md.
//
// ONE shader, TWO materials (Resources/FireflyBody.mat, Resources/FireflyHalo.mat):
//   • body  — opaque, ZWrite on, queue 2000, _Halo 0. The mesh's uv.x runs
//             0 (head) .. 1 (tail); the tail end glows, the head stays dark.
//             Wing verts are flagged uv.y >= 1.5 and draw flat dark.
//   • halo  — additive (One One), ZWrite off, queue 3000, _Halo 1. A unit quad
//             billboarded in the vertex shader and pushed a little toward the
//             camera so the bug's own body never punches a hole in its glow.
//             Queue 3000 is deliberate (CLAUDE.md corollary): it draws AFTER the
//             atmosphere post, so the glow is never fogged or dimmed by it.
//
// The glow colour is HDR on purpose (about 4x over white). The atmosphere post
// exempts pixels whose channels average >= 2 from its distance dimming
// (`hdrStrength` in Atmosphere.shader's calculateLight), so the tail stays
// bright on the night side, and the Bloom effect picks it up.
//
// GPU-instanced with PER-INSTANCE blink data, so a hundred bugs sharing a mesh
// draw in a couple of calls. The blink is computed HERE from _Time so nothing
// has to touch a MaterialPropertyBlock per frame; FireflyVisual.Blink() in C#
// mirrors the exact same curve for the real point light on the nearest bugs.
//
//   _Blink.x  phase   (0..1, per bug)
//   _Blink.y  period  (seconds)
//   _Blink.z  floor   (0..1 — how dim the "off" half is; held bug uses ~0.8)
//   _Blink.w  gaze    (0/1 — the focused bug is held at full glow)
//   _Fade     0..1    swarm fade in/out + catch shrink
//
// WINGS FLAP in the vertex shader (Sam, 2026-09-11: "real fireflies flap their
// wings very fast"). Wing verts (uv.y >= 1.5) rotate about a hinge along the
// body at the wing root, _FlapHz times a second, offset per bug by the blink
// phase so a swarm never beats in unison. Pure vertex maths — no animator, no
// per-frame CPU, and it batches like everything else.
Shader "SoundOfSpace/Firefly"
{
    Properties
    {
        _HeadColor ("Head colour", Color) = (0.10, 0.075, 0.05, 1)
        _WingColor ("Wing colour", Color) = (0.16, 0.13, 0.09, 1)
        _GlowColor ("Glow colour", Color) = (1.0, 0.68, 0.22, 1)
        _Intensity ("Glow intensity (HDR)", Float) = 4.0
        _TailStart ("Tail start along the body (0 head .. 1 tail)", Range(0, 1)) = 0.42
        _Halo      ("Halo mode", Float) = 0
        _HaloPower ("Halo falloff power", Float) = 2.6
        _HaloAlpha ("Halo strength", Float) = 0.55
        _HaloLift  ("Halo push toward the camera (m)", Float) = 0.12
        _FlapHz    ("Wing beats per second", Float) = 18
        _FlapAngle ("Wing flap half-angle (degrees)", Float) = 38
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        // Per-instance (set through a MaterialPropertyBlock; see above).
        _Blink ("Blink (phase, period, floor, gaze)", Vector) = (0, 3, 0.25, 0)
        _Fade  ("Fade", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0
            #include "UnityCG.cginc"

            half4 _HeadColor;
            half4 _WingColor;
            half4 _GlowColor;
            float  _Intensity;
            float  _TailStart;
            float  _Halo;
            float  _HaloPower;
            float  _HaloAlpha;
            float  _HaloLift;
            float  _FlapHz;
            float  _FlapAngle;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Blink)
                UNITY_DEFINE_INSTANCED_PROP(float,  _Fade)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // The same curve FireflyVisual.Blink() computes on the CPU. A plain
            // sine looks like a metronome; squaring the positive half gives a
            // firefly's short bright flash and long dim tail-off.
            float BlinkLevel(float4 b)
            {
                float t = _Time.y / max(0.2, b.y) + b.x;
                float s = 0.5 + 0.5 * sin(t * 6.2831853);
                float pulse = s * s;
                float level = lerp(b.z, 1.0, pulse);
                return max(level, b.w);   // gazed: pinned at full
            }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.uv = v.uv;

                if (_Halo > 0.5)
                {
                    // Billboard: the quad's own XY, laid along the camera's
                    // right/up, at the object's world position. The object's X
                    // axis length carries the halo size (the builder scales the
                    // halo object uniformly).
                    float3 centre = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                    float  size   = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                    float3 camR   = float3(UNITY_MATRIX_V[0].x, UNITY_MATRIX_V[0].y, UNITY_MATRIX_V[0].z);
                    float3 camU   = float3(UNITY_MATRIX_V[1].x, UNITY_MATRIX_V[1].y, UNITY_MATRIX_V[1].z);
                    float3 toCam  = _WorldSpaceCameraPos - centre;
                    float  d      = length(toCam);
                    float3 lift   = d > 0.001 ? toCam / d * min(_HaloLift, d * 0.5) : float3(0, 0, 0);
                    float3 world  = centre + camR * v.vertex.x * size + camU * v.vertex.y * size + lift;
                    o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                }
                else
                {
                    float4 pos = v.vertex;
                    if (v.uv.y >= 1.5)
                    {
                        // Hinge = the wing root line (x = ±0.010, y = 0.045 in
                        // FireflyVisual.BodyMesh), parallel to the body axis.
                        // Rotate (x, y) about it; the sign makes the two wings
                        // mirror each other so both tips rise together.
                        float4 blink = UNITY_ACCESS_INSTANCED_PROP(Props, _Blink);
                        float side  = v.vertex.x < 0 ? -1.0 : 1.0;
                        float rootX = side * 0.010;
                        float rootY = 0.045;
                        float a  = sin(_Time.y * _FlapHz * 6.2831853 + blink.x * 6.2831853)
                                 * radians(_FlapAngle) * side;
                        float ca = cos(a), sa = sin(a);
                        float dx = v.vertex.x - rootX;
                        float dy = v.vertex.y - rootY;
                        pos.x = rootX + dx * ca - dy * sa;
                        pos.y = rootY + dx * sa + dy * ca;
                    }
                    o.pos = UnityObjectToClipPos(pos);
                }
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 blink = UNITY_ACCESS_INSTANCED_PROP(Props, _Blink);
                float  fade  = UNITY_ACCESS_INSTANCED_PROP(Props, _Fade);
                float  level = BlinkLevel(blink) * fade;

                if (_Halo > 0.5)
                {
                    float2 p = (i.uv - 0.5) * 2.0;
                    float  r = saturate(1.0 - length(p));
                    float  a = pow(r, _HaloPower) * _HaloAlpha * level;
                    // Additive: colour IS the contribution; alpha unused.
                    return float4(_GlowColor.rgb * _Intensity * 0.5 * a, a);
                }

                // Wings: flat dark, a little of the tail's light spilling on.
                if (i.uv.y >= 1.5)
                    return float4(_WingColor.rgb + _GlowColor.rgb * 0.08 * level, 1);

                // Body: head dark, tail glowing. uv.x 0 = head, 1 = tail.
                float k = smoothstep(_TailStart, _TailStart + 0.28, i.uv.x);
                float3 glow = _GlowColor.rgb * _Intensity * level;
                // A touch of the glow bleeds onto the head so the whole bug is
                // findable at night, not just a floating spark.
                float3 head = _HeadColor.rgb + _GlowColor.rgb * 0.10 * level;
                return float4(lerp(head, glow, k), 1);
            }
            ENDCG
        }
    }
    // No Fallback on purpose: no ShadowCaster pass, so a bug neither casts a
    // shadow nor lands in the depth texture — a glowing thing throwing a
    // shadow reads wrong, and the HDR tail is exempt from the atmosphere's
    // depth-based dimming anyway.
}
