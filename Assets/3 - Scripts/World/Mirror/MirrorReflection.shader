Shader "FX/MirrorReflection"
{
    Properties
    {
        _ReflectionTex ("Reflection", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        // Transparent: the reflection camera clears to alpha 0 and renders only
        // the astronaut, so the glass is invisible everywhere except where the
        // reflection of the player is (alpha 1). No more "tinted pane" look.
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float4 screen : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            sampler2D _ReflectionTex;
            fixed4 _Tint;

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2Dproj(_ReflectionTex, UNITY_PROJ_COORD(i.screen));
                return fixed4(c.rgb * _Tint.rgb, c.a);
            }
            ENDCG
        }
    }
}
