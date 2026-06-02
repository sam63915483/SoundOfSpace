// Blit shader used by PlayerPhoneUI's CommandBuffer to copy the main
// camera's final color buffer into the phone RT while forcing alpha=1.
// Without this, the skybox + additive-blend effects (lasers, lights)
// write low alpha into the RT and the live-feed RawImage displays them
// dimmed against the black backdrop.
//
// Uses explicit vert/frag (not UnityCG's vert_img helper) because the
// helper can occasionally be stripped or misbehave in built players.
// Registered in Project Settings → Graphics → Always Included Shaders
// by Assets/3 - Scripts/Editor/PhoneShaderRegistration.cs so the build
// bundles it.
Shader "Hidden/PhoneOpaqueBlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ZTest Always
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex   : POSITION;
                float2 uv       : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
            };

            sampler2D _MainTex;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return half4(tex2D(_MainTex, i.uv).rgb, 1.0);
            }
            ENDCG
        }
    }
}
