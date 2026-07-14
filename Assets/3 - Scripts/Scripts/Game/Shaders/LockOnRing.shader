Shader "GameUI/LockOnRing"
{
	// Lock-on bracket material for LockOnUI's Graphics.DrawMesh rings.
	// Replaces the built-in UI/Default material: that one ignores scene depth
	// outside a canvas, so the brackets drew straight through the cockpit
	// hull. ZTest LEqual makes opaque geometry (the ship interior) occlude
	// them per-pixel; the transparent canopy glass writes no depth, so the
	// brackets stay visible through the windshield. Transparent+10 keeps them
	// rendering after the atmosphere image effect, like the old material.
	Properties
	{
		_Color ("Colour", Color) = (1,1,1,1)
	}
	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent+10" }
		ZTest LEqual
		ZWrite Off
		Cull Off
		Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			float4 _Color;

			struct appdata { float4 vertex : POSITION; };
			struct v2f { float4 vertex : SV_POSITION; };

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos (v.vertex);
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				return _Color;
			}
			ENDCG
		}
	}
}
