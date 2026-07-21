// Animated force-field shield for bubble domes. Built-in RP, transparent,
// TWO-SIDED (visible from inside and outside), with a fresnel rim, a scrolling
// energy texture (procedural fallback when none is set), environment reflection,
// and a slow pulse.
Shader "Custom/ForceField"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.8, 1.0, 1.0)
        _MainTex ("Pattern (scrolls)", 2D) = "white" {}
        _RimPower ("Rim Power", Range(0.5, 8)) = 3.0
        _RimStrength ("Rim Strength", Range(0, 4)) = 1.8
        _Scroll ("Scroll Speed", Float) = 0.08
        _PatternScale ("Pattern Scale", Float) = 4.0
        _PatternStrength ("Pattern Strength", Range(0, 2)) = 0.8
        _Pulse ("Pulse Speed", Float) = 1.8
        _BaseAlpha ("Base Alpha (inside visibility)", Range(0, 1)) = 0.12
        _Reflect ("Reflection Strength", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off                    // render both faces → visible from inside the bubble

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldViewDir : TEXCOORD2;
                float3 objPos : TEXCOORD3;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _RimPower, _RimStrength, _Scroll, _PatternScale, _PatternStrength, _Pulse, _BaseAlpha, _Reflect;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldViewDir = normalize(WorldSpaceViewDir(v.vertex));
                o.objPos = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n  = normalize(i.worldNormal);
                float3 vd = normalize(i.worldViewDir);
                float rim = pow(saturate(1.0 - abs(dot(n, vd))), _RimPower) * _RimStrength;

                float t = _Time.y;
                // Two texture layers scrolling opposite ways → shimmering cells.
                float2 uv1 = i.uv * _PatternScale + float2(0.0, t * _Scroll);
                float2 uv2 = i.uv * _PatternScale * 1.3 - float2(t * _Scroll * 0.7, 0.0);
                float tex = (tex2D(_MainTex, uv1).r + tex2D(_MainTex, uv2).r) * 0.5;
                // Procedural bands so it still moves with a flat/white texture.
                float bands = sin(i.objPos.y * _PatternScale + t * _Scroll * 6.2831) * 0.5 + 0.5;
                float pattern = tex * (0.5 + 0.5 * bands);

                float pulse = 0.8 + 0.2 * sin(t * _Pulse);

                // Environment reflection (probe / skybox) for a glassy sheen.
                float3 refl = reflect(-vd, n);
                half4 env = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, refl);
                float3 reflCol = DecodeHDR(env, unity_SpecCube0_HDR) * _Reflect * (rim * 0.5 + 0.3);

                float energy = rim + pattern * _PatternStrength;
                float3 rgb = _Color.rgb * (energy + 0.25) + reflCol;
                float alpha = saturate(energy + _BaseAlpha) * _Color.a * pulse;
                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
