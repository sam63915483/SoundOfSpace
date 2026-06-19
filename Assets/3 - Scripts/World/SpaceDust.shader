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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

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

                // Instance center + uniform scale from the per-instance matrix
                float3 center = mul(unity_ObjectToWorld, float4(0,0,0,1)).xyz;
                float size = length(float3(unity_ObjectToWorld[0][0],
                                           unity_ObjectToWorld[1][0],
                                           unity_ObjectToWorld[2][0]));
                // Camera-facing billboard: V rows are the camera basis in world space
                float3 camR = UNITY_MATRIX_V[0].xyz;
                float3 camU = UNITY_MATRIX_V[1].xyz;
                float3 wpos = center + (camR * v.vertex.x + camU * v.vertex.y) * size;

                o.pos = mul(UNITY_MATRIX_VP, float4(wpos, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
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
