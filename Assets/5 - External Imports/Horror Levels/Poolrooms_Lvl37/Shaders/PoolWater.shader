Shader "Custom/PoolWater"
{
    // Built-in RP refractive water. Forward-lit vertex/fragment (NOT a surface shader, so it can
    // GrabPass the framebuffer for refraction). Procedural ripple normals = 5 directional sine
    // octaves + 2 scrolling value-noise layers, all sampled in OBJECT-LOCAL space normalized by the
    // object's world scale -> every water object shows the same lively wave-count at any size, with
    // no textures. The surface composites: refracted background (distorted by the ripple normal) +
    // reflection-probe reflection (fresnel-weighted) + a sharp specular sun glint. One shared
    // material drives all instances automatically.
    Properties
    {
        _Color         ("Water Tint (RGB) + Body Opacity (A)", Color) = (0.10, 0.34, 0.42, 0.55)
        _Smoothness    ("Glint Sharpness", Range(0,1)) = 0.93
        _WaveScale     ("Ripple Density", Float) = 14.0
        _WaveSpeed     ("Ripple Speed", Float) = 0.4
        _WaveStrength  ("Ripple Strength", Range(0,1)) = 0.33
        _NoiseScale    ("Detail Noise Density", Float) = 22.0
        _NoiseStrength ("Detail Noise Strength", Range(0,2)) = 0.6
        _RefractStrength ("Refraction Strength", Range(0,0.2)) = 0.045
        _ReflStrength  ("Reflection Strength", Range(0,1)) = 0.5
        _FresnelColor  ("Edge Sheen Color", Color) = (0.55, 0.80, 0.90, 1)
        _FresnelPower  ("Edge Sheen Power", Range(0.5,8)) = 4.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 200

        // Grab the scene behind the water once per frame (shared by all water objects).
        GrabPass { "_WaterGrab" }

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            ZWrite On
            Cull Off   // double-sided: also render the underside so a submerged player sees the surface / waterline from below

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _Color;
            half   _Smoothness;
            float  _WaveScale;
            float  _WaveSpeed;
            half   _WaveStrength;
            float  _NoiseScale;
            half   _NoiseStrength;
            half   _RefractStrength;
            half   _ReflStrength;
            fixed4 _FresnelColor;
            half   _FresnelPower;

            sampler2D _WaterGrab;

            // ---- Directional ripple octaves (unit dirs, rising freq, falling amp, desynced speed) ----
            #define WAVE_COUNT 5
            static const float2 WAVE_DIR[WAVE_COUNT] = {
                float2( 0.981,  0.196),
                float2(-0.600,  0.800),
                float2( 0.316, -0.949),
                float2( 0.840,  0.543),
                float2(-0.250, -0.968)
            };
            static const float WAVE_FREQ[WAVE_COUNT]  = { 1.0, 1.9, 2.7, 4.1, 5.9 };
            static const float WAVE_AMP[WAVE_COUNT]   = { 1.0, 0.52, 0.34, 0.19, 0.11 };
            static const float WAVE_SPEED[WAVE_COUNT] = { 1.0, 1.27, 0.83, 1.61, 0.62 };
            #define WAVE_AMP_SUM 2.16

            // ---- Cheap value noise (hash-lattice, smooth-interp) for irregular micro-detail ----
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float4 grabPos  : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // World-XZ gradient of the combined sine+noise height field -> perturbed world normal.
            float3 rippleNormal(float2 worldXZ)
            {
                // Object-local plane coords normalized by world scale -> identical look at any size.
                float2 objScale = float2(
                    length(unity_ObjectToWorld._m00_m10_m20),
                    length(unity_ObjectToWorld._m02_m12_m22));
                float2 objOriginXZ = float2(unity_ObjectToWorld._m03, unity_ObjectToWorld._m23);
                float2 local = (worldXZ - objOriginXZ) / max(objScale, 1e-3);

                float t = _Time.y * _WaveSpeed;

                // Summed directional sines.
                float2 p = local * (_WaveScale * 0.1);
                float2 grad = 0;
                [unroll]
                for (int i = 0; i < WAVE_COUNT; i++)
                {
                    float ph = dot(p, WAVE_DIR[i]) * WAVE_FREQ[i] + t * WAVE_SPEED[i];
                    grad += (WAVE_AMP[i] * WAVE_FREQ[i] * cos(ph)) * WAVE_DIR[i];
                }
                grad *= (1.0 / WAVE_AMP_SUM);

                // Two scrolling noise layers (finite-difference gradients) break the sine regularity.
                const float2x2 ROT = float2x2(0.80, -0.60, 0.60, 0.80);
                float e = 0.75;

                float2 n1 = local * (_NoiseScale * 0.1) + t * float2(0.13, 0.21);
                float c1 = vnoise(n1);
                float2 g1 = float2(vnoise(n1 + float2(e, 0)) - c1, vnoise(n1 + float2(0, e)) - c1) / e;

                float2 n2 = mul(ROT, local) * (_NoiseScale * 0.21) - t * float2(0.17, 0.09);
                float c2 = vnoise(n2);
                float2 g2 = float2(vnoise(n2 + float2(e, 0)) - c2, vnoise(n2 + float2(0, e)) - c2) / e;

                grad += (g1 + 0.6 * g2) * _NoiseStrength;

                return normalize(float3(-grad.x * _WaveStrength, 1.0, -grad.y * _WaveStrength));
            }

            fixed4 frag (v2f IN, fixed facing : VFACE) : SV_Target
            {
                float3 N = rippleNormal(IN.worldPos.xz);
                if (facing < 0) N = -N;   // backface (seen from underwater) → point the normal down at the camera so it lights/refracts correctly
                float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos);

                // Fresnel: more reflective / sheened at grazing angles.
                half fres = pow(1.0 - saturate(dot(V, N)), _FresnelPower);

                // Refraction: offset the grabbed background by the ripple slope.
                float2 guv = IN.grabPos.xy / IN.grabPos.w;
                guv += N.xz * _RefractStrength;
                fixed3 refr = tex2D(_WaterGrab, guv).rgb;

                // Tinted water body over the refracted floor (deeper tint, less see-through).
                fixed3 body = lerp(refr, _Color.rgb, _Color.a);

                // Reflection probe reflection, fresnel-weighted.
                half3 R = reflect(-V, N);
                half4 rgbm = UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, R);
                half3 refl = DecodeHDR(rgbm, unity_SpecCube0_HDR);
                fixed3 col = lerp(body, refl, saturate(fres * _ReflStrength + _ReflStrength * 0.15));

                // Sharp specular sun glint off the ripples.
                half3 L = normalize(_WorldSpaceLightPos0.xyz);
                half3 H = normalize(L + V);
                half spec = pow(saturate(dot(N, H)), lerp(16.0, 400.0, _Smoothness));
                col += _LightColor0.rgb * spec;

                // Edge sheen.
                col += _FresnelColor.rgb * fres * 0.25;

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Transparent/VertexLit"
}
