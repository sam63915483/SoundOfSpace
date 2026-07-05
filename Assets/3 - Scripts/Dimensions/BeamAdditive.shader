// Unlit additive glow for the D6 lighthouse beam cone (and other light shafts).
// Standard-Fade multiplied its emission by the low alpha and then fogged it toward
// the near-black night fog colour, so the cone always rendered dark. This shader is
// unlit, additive (can only brighten), double-sided, and ignores fog entirely.
// NOTE: found via Shader.Find at runtime — if a Windows build ever drops it, add it
// to Always Included Shaders (Project Settings > Graphics).
Shader "Dimensions/BeamAdditive"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.95, 0.75, 0.1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
