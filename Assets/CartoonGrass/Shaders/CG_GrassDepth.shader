// Depth-only pass for the instanced grass. Graphics.DrawMeshInstanced grass is
// NOT included in Unity's camera depth-texture prepass, so the screen-space
// atmosphere post-process reads the BACKGROUND depth where grass is and washes
// out any blade silhouetted against the sky. InstancedGrassRenderer renders the
// same grass batches with this shader via a CommandBuffer at
// CameraEvent.AfterDepthTexture, so the grass appears in _CameraDepthTexture at
// its true depth and the atmosphere tints it correctly. ColorMask 0 = depth
// only (no colour written).
Shader "CartoonGrass/GrassDepth"
{
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
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

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
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return 0; }
            ENDCG
        }
    }
}
