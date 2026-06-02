// Transparent blood-decal projector for the Built-in Render Pipeline. Projects
// a blood splat texture's alpha onto whatever geometry is inside the projector
// frustum, so a blood pool conforms perfectly to curved planet terrain (unlike
// a flat billboard splat). Used by BloodFX.SpawnPool via BloodDecalFader.
Shader "Custom/BloodDecalProjector"
{
    Properties
    {
        _Color ("Blood Color", Color) = (0.45, 0.02, 0.02, 1.0)
        _ShadowTex ("Blood Decal (alpha = shape)", 2D) = "black" {}
    }
    Subshader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            ColorMask RGB
            Blend SrcAlpha OneMinusSrcAlpha
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 uvShadow : TEXCOORD0;
                float4 pos : SV_POSITION;
            };

            float4x4 unity_Projector;
            sampler2D _ShadowTex;
            fixed4 _Color;

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.uvShadow = mul(unity_Projector, vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Clip anything behind the projector or outside its [0,1] box so
                // the decal only lands on the patch of ground beneath it.
                clip(i.uvShadow.w);
                float2 uv = i.uvShadow.xy / i.uvShadow.w;
                clip(uv);
                clip(1.0 - uv);

                fixed texAlpha = tex2D(_ShadowTex, uv).a;
                fixed4 res;
                res.rgb = _Color.rgb;
                res.a = texAlpha * _Color.a;
                return res;
            }
            ENDCG
        }
    }
    Fallback Off
}
