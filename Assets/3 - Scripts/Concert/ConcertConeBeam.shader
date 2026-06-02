// Volumetric cone beam for concert lights. Variant of Concert/Additive with
// two extra behaviors that prevent the cone visual from overexposing the
// scene when the camera is roughly aligned with the cone's axis (looking
// "into" the light):
//
//   1. Cull Front — only the FAR cone wall renders. From outside the cone
//      this produces a clean silhouette; from inside (camera inside the
//      cone volume), every wall is back-facing and would otherwise blanket
//      the screen with additive blend.
//
//   2. View-axis fade — strength scales by (1 - |dot(view, beamAxis)|^N)
//      so the cone fades to zero brightness when the camera looks straight
//      down the axis (toward or away from the apex). This is the same fix
//      used in Custom/FlashlightBeamCone — without it, additive overdraw
//      on the cone walls from a head-on angle lights up everything behind
//      the cone by 2-4× compared to a side view.
//
//   3. Near-apex fade — kills brightness in the first slice of the cone so
//      the apex region doesn't blow out as the camera approaches it.
//
// Properties mirror Concert/Additive (same _TintColor, _MainTex, _InvFade)
// so the existing ConcertBeamShared.MakeBeamMaterial setup hits the same
// property names. Two new properties tune the fades:
//   _ViewAxisFadePower  — higher = sharper falloff as view aligns
//   _NearApexFade       — fraction of cone length at apex to fade in

Shader "Concert/ConeBeam"
{
    Properties
    {
        _MainTex            ("Particle Texture", 2D)              = "white" {}
        _TintColor          ("Tint Color",       Color)           = (0.5, 0.5, 0.5, 0.5)
        _InvFade            ("Soft Particles Factor", Range(0.01, 3.0)) = 1.0
        _ViewAxisFadePower  ("View-Axis Fade Power", Range(0, 8))  = 3.0
        _NearApexFade       ("Near-Apex Fade Length (0..1)", Range(0, 0.5)) = 0.06
    }

    Category
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Blend SrcAlpha One
        ColorMask RGB
        // Render only back-facing triangles. Mesh winding has outward
        // normals; from inside the cone (camera near apex) all walls are
        // back-facing and Cull Off would paint the entire view additive.
        Cull Front
        Lighting Off
        ZWrite Off

        SubShader
        {
            Pass
            {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma target 2.0
                #pragma multi_compile_particles
                #pragma multi_compile_fog

                #include "UnityCG.cginc"

                sampler2D _MainTex;
                fixed4 _TintColor;
                float _ViewAxisFadePower;
                float _NearApexFade;

                struct appdata_t
                {
                    float4 vertex   : POSITION;
                    fixed4 color    : COLOR;
                    float2 texcoord : TEXCOORD0;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct v2f
                {
                    float4 vertex   : SV_POSITION;
                    fixed4 color    : COLOR;
                    float2 texcoord : TEXCOORD0;
                    float3 worldPos : TEXCOORD3;
                    UNITY_FOG_COORDS(1)
                    #ifdef SOFTPARTICLES_ON
                    float4 projPos  : TEXCOORD2;
                    #endif
                    UNITY_VERTEX_OUTPUT_STEREO
                };

                float4 _MainTex_ST;

                v2f vert(appdata_t v)
                {
                    v2f o;
                    UNITY_SETUP_INSTANCE_ID(v);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                    #ifdef SOFTPARTICLES_ON
                    o.projPos = ComputeScreenPos(o.vertex);
                    COMPUTE_EYEDEPTH(o.projPos.z);
                    #endif
                    o.color    = v.color;
                    o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                    UNITY_TRANSFER_FOG(o, o.vertex);
                    return o;
                }

                UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
                float _InvFade;

                fixed4 frag(v2f i) : SV_Target
                {
                    #ifdef SOFTPARTICLES_ON
                    float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
                    float partZ = i.projPos.z;
                    float fade  = saturate(_InvFade * (sceneZ - partZ));
                    i.color.a *= fade;
                    #endif

                    fixed4 col = 2.0f * i.color * _TintColor * tex2D(_MainTex, i.texcoord);

                    // View-axis fade. ConcertBeamShared.BuildConeMesh places
                    // the apex at local origin with the cone opening along
                    // local +Z, so the beam axis in world space is the
                    // object's local +Z direction transformed by its rotation.
                    float3 beamAxisWS = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1)));
                    float3 viewDir    = normalize(_WorldSpaceCameraPos - i.worldPos);
                    float  align      = abs(dot(viewDir, beamAxisWS));
                    float  axisFade   = 1.0 - pow(align, _ViewAxisFadePower);

                    // Near-apex fade. UV.y = 0 at apex, 1 at base — see
                    // ConcertBeamShared.BuildConeMesh.
                    float nearMask = smoothstep(0.0, max(0.0001, _NearApexFade), i.texcoord.y);

                    col.rgb *= axisFade * nearMask;

                    UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0)); // fog → black for additive
                    return col;
                }
                ENDCG
            }
        }
    }
}
