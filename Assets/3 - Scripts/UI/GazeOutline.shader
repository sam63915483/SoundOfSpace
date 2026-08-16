// Inverted-hull rim for the gaze-highlight system (GazeHighlight.cs): the
// looked-at interactable's meshes are re-drawn front-culled and inflated
// along their normals, leaving only a thin silhouette visible around the
// original. ZWrite/ZTest normal, so occluded parts of the rim stay hidden —
// the gaze needs line of sight anyway, and see-through glow would read as an
// x-ray. Works on SkinnedMeshRenderers for free (skinning runs before the
// vertex shader). Built-in RP.
Shader "SoundOfSpace/GazeOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.77, 0.42, 1)
        _OutlineWidth ("Outline Width (m)", Float) = 0.02
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+20" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _OutlineColor;
            float _OutlineWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 wnorm = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.pos = UnityWorldToClipPos(wpos + wnorm * _OutlineWidth);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }
}
