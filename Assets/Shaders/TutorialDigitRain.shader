// Tutorial box walls: see-through panes with 0s and 1s raining down them.
// (docs/superpowers/specs/2026-09-14-tutorial-box-design.md)
//
// Each pane is a grid of cells (_Cells columns across the quad). Every column
// has its own speed and phase; one bright head falls down it with a fading
// trail behind, and each cell draws a procedural 0 or 1 that re-rolls now and
// then. Unlit, transparent, double-sided — the Milky Way skybox shows through.
// No atmosphere post in the tutorial scene, so queue rules don't matter here.
Shader "Custom/TutorialDigitRain"
{
    Properties
    {
        _Color   ("Digit colour", Color) = (0.25, 1.0, 0.4, 1)
        _HeadColor ("Head colour", Color) = (0.85, 1.0, 0.9, 1)
        _Cells   ("Columns across the pane", Float) = 640
        _Aspect  ("Cell height / width", Float) = 1.6
        _Speed   ("Fall speed (cells per second)", Float) = 20
        _Trail   ("Trail length (cells)", Float) = 70
        _Flicker ("Digit re-roll rate (Hz)", Float) = 3
        _Alpha   ("Digit alpha", Range(0, 1)) = 0.9
        _Base    ("Pane tint alpha", Range(0, 0.3)) = 0.03
        [Toggle] _Radial ("Radial (ceiling: out from the centre)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color, _HeadColor;
            float _Cells, _Aspect, _Speed, _Trail, _Flicker, _Alpha, _Base, _Radial;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Procedural glyph in cell space (0..1)². 1 = bar with a flag, 0 = ring.
            float glyph(float2 c, float isOne)
            {
                float2 q = c - 0.5;
                if (isOne > 0.5)
                {
                    float bar  = step(abs(q.x - 0.04), 0.075) * step(abs(q.y), 0.34);
                    float flag = step(abs(q.y - 0.27), 0.07) * step(-0.2, q.x) * step(q.x, -0.04);
                    float foot = step(abs(q.y + 0.30), 0.045) * step(abs(q.x - 0.04), 0.2);
                    return max(bar, max(flag, foot));
                }
                float2 e = q / float2(0.22, 0.34);
                float d = length(e);
                return step(0.62, d) * step(d, 1.0);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float rows = _Cells / max(0.01, _Aspect);
                float2 uv = i.uv;
                if (_Radial > 0.5)
                {
                    // Ceiling: polar map. Columns run around the centre, the
                    // "fall" runs outward - every stream starts at the middle
                    // and races to the walls.
                    float2 d = i.uv - 0.5;
                    float ang = atan2(d.y, d.x) / 6.2831853 + 0.5;   // 0..1 around
                    float rad = length(d) * 1.4142136;              // 0 centre .. 1 corner
                    uv = float2(ang, 1.0 - rad);                     // uv.y decreasing = outward
                }
                float2 g = uv * float2(_Cells, rows);
                float col = floor(g.x);
                float row = floor(g.y);
                float2 cell = frac(g);

                // One head per column, falling (uv.y decreasing). Period covers
                // the pane plus the trail so the trail fully clears the bottom.
                float speed  = _Speed * (0.55 + 0.9 * hash11(col * 7.13 + 1.7));
                float period = rows + _Trail;
                float phase  = hash11(col * 3.71 + 0.3) * period;
                float head   = rows - fmod(_Time.y * speed + phase, period);
                float dist   = row - head;                  // cells above the head
                float inTrail = step(0.0, dist) * step(dist, _Trail);
                float fade   = saturate(1.0 - dist / max(1.0, _Trail));
                fade *= fade;
                float isHead = step(dist, 1.0) * inTrail;

                // Digit choice: per cell, re-rolled at _Flicker Hz with per-cell jitter.
                float jitter = hash21(float2(col, row)) * 10.0;
                float roll   = floor(_Time.y * _Flicker + jitter);
                float isOne  = step(0.5, hash21(float2(col * 1.37 + roll * 0.61, row * 0.91 - roll * 0.23)));
                float shape  = glyph(cell, isOne);

                float bright = inTrail * fade;
                fixed3 rgb = lerp(_Color.rgb, _HeadColor.rgb, isHead);
                float a = shape * bright * _Alpha + _Base;
                return fixed4(rgb * max(bright, _Base * 4.0), saturate(a));
            }
            ENDCG
        }
    }
    Fallback Off
}
