// Bench/GlassPanel — liquid-glass card for world-space UGUI.
// Rounded-box SDF mask + bright rim edge + slow diagonal specular
// sweep + soft top glow. Single pass, no grab pass (no real
// backdrop blur — translucency reads as glass over passthrough).
// UI-compatible: stencil masking, clip rect, vertex-colour tint
// (so CanvasGroup fades and Image.color both work).
Shader "Bench/GlassPanel"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _RectSize ("Rect Size (px)", Vector) = (900, 560, 0, 0)
        _Radius ("Corner Radius (px)", Float) = 28
        _EdgeWidth ("Edge Width (px)", Float) = 2
        _FillColor ("Fill", Color) = (0.059, 0.071, 0.094, 0.75)
        _RimColor ("Rim", Color) = (1, 1, 1, 0.4)
        _RimBoost ("Rim Boost (selected)", Float) = 0
        _SweepColor ("Sweep Tint", Color) = (1, 1, 1, 0.10)
        _SweepPeriod ("Sweep Period (s)", Float) = 8
        _SweepEnabled ("Sweep Enabled", Float) = 1
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Glass"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float2 _RectSize;
            float _Radius;
            float _EdgeWidth;
            fixed4 _FillColor;
            fixed4 _RimColor;
            float _RimBoost;
            fixed4 _SweepColor;
            float _SweepPeriod;
            float _SweepEnabled;
            float4 _ClipRect;

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Rounded-box SDF in pixels (negative inside).
                float2 p = i.uv * _RectSize - 0.5 * _RectSize;
                float2 b = 0.5 * _RectSize - _Radius;
                float2 q = abs(p) - b;
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - _Radius;

                float inside = 1.0 - smoothstep(-1.5, 1.0, d);
                float outside = smoothstep(0.0, 1.5, d);
                float edge = (1.0 - smoothstep(0.0, _EdgeWidth, -d)) * (1.0 - outside);

                // Diagonal specular sweep, one pass per period.
                float sweep = 0.0;
                if (_SweepEnabled > 0.5 && _SweepPeriod > 0.0)
                {
                    float span = _RectSize.x + _RectSize.y * 0.35;
                    float s = (i.uv.x * _RectSize.x + i.uv.y * _RectSize.y * 0.35) / span;
                    float ph = frac(_Time.y / _SweepPeriod) * 1.3 - 0.15;
                    sweep = smoothstep(0.10, 0.0, abs(s - ph));
                }
                float topGlow = pow(1.0 - i.uv.y, 3.0) * 0.08;

                float rimA = saturate(_RimColor.a + _RimBoost);
                fixed3 rgb = _FillColor.rgb
                    + _RimColor.rgb * (edge * rimA)
                    + _SweepColor.rgb * (sweep * _SweepColor.a * inside)
                    + topGlow * inside;
                float a = max(inside * _FillColor.a, edge * rimA);

                fixed4 color = fixed4(rgb, a) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
