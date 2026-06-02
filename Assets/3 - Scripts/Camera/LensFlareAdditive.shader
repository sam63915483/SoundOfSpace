// Additive UI shader used by LensFlareRegistry for the sun halo + ghost
// chain. Standard UI shaders use alpha blending which makes lens-flare
// textures look like translucent stickers rather than glow.
//
// Blend One One: pure additive. The fragment multiplies tex.rgb by its own
// luminance (max channel) plus the vertex color, so dark pixels contribute
// nothing without relying on a reliable source alpha channel — important
// because Unity sometimes flattens PSD alpha during import.
Shader "UI/LensFlareAdditive"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Pass
        {
            Cull Off
            Lighting Off
            ZWrite Off
            ZTest Always
            Blend One One
            ColorMask RGB

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, IN.texcoord);
                // Use luminance as the implicit "intensity" so black texels
                // contribute zero under additive blending even if the source
                // alpha was flattened to 1 during import.
                float lum = max(t.r, max(t.g, t.b));
                fixed3 rgb = t.rgb * lum * IN.color.rgb * IN.color.a;
                return fixed4(rgb, 1);
            }
            ENDCG
        }
    }
}
