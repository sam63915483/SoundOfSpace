Shader "Hidden/ChromaticAberration"
{
    // Real per-pixel chromatic aberration. Samples R, G, B channels at
    // different radial offsets from the screen center — R outward, G at
    // center, B inward — producing the colored fringing you see at the
    // edges of cheap-camera footage. Falls off with dist² so the center
    // of the image stays sharp and only the corners fringe.
    Properties
    {
        _MainTex  ("Source", 2D) = "white" {}
        _Strength ("Strength", Float) = 0
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float     _Strength;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };
            struct v2f
            {
                float2 uv     : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Strength <= 0.0001)
                    return tex2D(_MainTex, i.uv);

                // Radial offset from screen center, scaled by dist² so the
                // center pixels barely shift and corner pixels shift max.
                float2 dir  = i.uv - float2(0.5, 0.5);
                float  dist = length(dir);
                float2 off  = (dist > 1e-5 ? dir / dist : float2(0,0)) * dist * dist * _Strength;

                // R sampled outward of center, B sampled inward, G at the
                // pixel itself. This is the textbook channel-split.
                float r = tex2D(_MainTex, i.uv + off).r;
                float g = tex2D(_MainTex, i.uv).g;
                float b = tex2D(_MainTex, i.uv - off).b;
                float a = tex2D(_MainTex, i.uv).a;
                return fixed4(r, g, b, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
