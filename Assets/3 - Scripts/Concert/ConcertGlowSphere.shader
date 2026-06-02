// Additive glow with no depth test — used for the laser muzzle sphere so it
// draws through the laser housing instead of getting depth-occluded by it.
// Uses the surface normal facing the camera as a soft alpha falloff so the
// sphere looks like a soft round glow rather than a hard ball.
Shader "Concert/GlowSphere"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+200" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Blend One One
            Cull Off
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float facing = saturate(dot(normalize(i.worldNormal), viewDir));
                // Squared so center glows bright, silhouette fades smoothly.
                facing = facing * facing;
                return _Color * facing;
            }
            ENDCG
        }
    }
}
