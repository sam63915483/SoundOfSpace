// The astronaut's firefly glow (Sam, 2026-09-11: "make it so the actual
// astronaut body glows, not just have a light inside them").
//
// WHY A SHELL AND NOT EMISSION. The astronaut's materials are EMBEDDED in
// Astronaut.fbx (ModelImporter materialLocation = InPrefab) — the Suit.mat on
// disk is not what the model uses, and an embedded material cannot have the
// Standard shader's _EMISSION keyword switched on, so writing _EmissionColor
// into its property block draws nothing. This is the same trick
// PlayerShadowProxy uses for the shadow: a clone of each body renderer (same
// mesh, same bones) drawn with THIS shader on top of the suit.
//
// Additive, unlit, no depth write, nudged a hair toward the camera (Offset) so
// it always wins the depth test against the surface it is copying while
// anything genuinely in front (your own held item) still hides it. A fresnel
// rim makes it read as a body RADIATING light rather than a flat orange
// paint job, and the HDR intensity lets the Bloom pass catch the rim.
//
//   _Level     0..1  the glow's envelope × breathing, set per frame by FireflyGlow
//   _Strength  0..1  per material slot (the visor stays dark)
Shader "SoundOfSpace/FireflyGlowShell"
{
    Properties
    {
        _GlowColor ("Glow colour", Color) = (1.0, 0.68, 0.22, 1)
        _Intensity ("Intensity (HDR)", Float) = 1.6
        _Base      ("Surface glow (0 = rim only)", Range(0, 1)) = 0.35
        _RimPower  ("Rim tightness", Float) = 2.2
        _Level     ("Level (runtime)", Range(0, 1)) = 0
        _Strength  ("Strength (per slot, runtime)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            half4 _GlowColor;
            float _Intensity;
            float _Base;
            float _RimPower;
            float _Level;
            float _Strength;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float3 wNormal : TEXCOORD0;
                float3 wView   : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wNormal = UnityObjectToWorldNormal(v.normal);
                float3 wPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wView = _WorldSpaceCameraPos - wPos;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float k = _Level * _Strength;
                if (k <= 0.0005) discard;
                float3 n = normalize(i.wNormal);
                float3 v = normalize(i.wView);
                float fres = pow(1.0 - saturate(dot(n, v)), _RimPower);
                float shape = _Base + (1.0 - _Base) * fres;
                return float4(_GlowColor.rgb * (_Intensity * k * shape), 1.0);
            }
            ENDCG
        }
    }
}
