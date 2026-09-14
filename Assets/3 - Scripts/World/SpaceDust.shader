Shader "Custom/SpaceDust"
{
    Properties
    {
        _MainTex ("Glow", 2D) = "white" {}
    }
    SubShader
    {
        // Queue <= 2500 so the [ImageEffectOpaque] atmosphere/ocean post-process
        // processes (washes/dims) the dust like other opaque-bucket geometry,
        // instead of drawing on top of it. (CLAUDE.md transparent-queue gotcha.)
        // NOTE: Resources/SpaceDust.mat overrides this with a custom queue of 3000
        // — that is what actually runs. Left as-is on purpose (2026-09-14): the
        // GPU port must not change the look; revisit separately.
        Tags { "Queue"="Transparent-550" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One        // additive
        ZWrite Off           // soft glow, no hard depth footprint
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:setupDust
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            // ── GPU-resident path (SpaceDustField.GpuPath): the compute in
            // Resources/SpaceDustStep.compute integrated the speck and wrote its
            // colour; this just reads them. Legacy DrawMeshInstanced path untouched.
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
            struct DustData { float3 local; float threshold; float sizeRand; float phase; float2 pad; };
            StructuredBuffer<DustData> _Dust;
            StructuredBuffer<float4> _DustColor;
            StructuredBuffer<uint> _DustVisIdx;
            float4 _DustCamPos;
            float _DustGlowSize;
            static float3 _dustCenter;
            static float _dustSize;
            static float4 _dustCol;
#endif
            void setupDust()
            {
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                uint i = _DustVisIdx[unity_InstanceID];
                DustData d = _Dust[i];
                _dustCenter = _DustCamPos.xyz + d.local;
                _dustSize = _DustGlowSize * d.sizeRand;
                _dustCol = _DustColor[i];
                unity_ObjectToWorld = float4x4(1, 0, 0, _dustCenter.x, 0, 1, 0, _dustCenter.y, 0, 0, 1, _dustCenter.z, 0, 0, 0, 1);
                unity_WorldToObject = float4x4(1, 0, 0, -_dustCenter.x, 0, 1, 0, -_dustCenter.y, 0, 0, 1, -_dustCenter.z, 0, 0, 0, 1);
#endif
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 col : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                float3 center;
                float size;
                float4 col;
#ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                center = _dustCenter;
                size = _dustSize;
                col = _dustCol;
#else
                // Instance center + uniform scale from the per-instance matrix
                center = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
                size = length(float3(unity_ObjectToWorld[0][0],
                                     unity_ObjectToWorld[1][0],
                                     unity_ObjectToWorld[2][0]));
                col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
#endif
                // Camera-facing billboard: V rows are the camera basis in world space
                float3 camR = UNITY_MATRIX_V[0].xyz;
                float3 camU = UNITY_MATRIX_V[1].xyz;
                float3 wpos = center + (camR * v.vertex.x + camU * v.vertex.y) * size;

                o.pos = mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.col = col;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                fixed g = tex2D(_MainTex, i.uv).a;      // radial glow mask
                fixed3 c = i.col.rgb * g * i.col.a;     // rgb = amber tint, a = brightness
                return fixed4(c, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
