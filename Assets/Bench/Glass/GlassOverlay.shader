// Bench/GlassOverlay — Tier 1 full-FOV frost shell (inside-out
// sphere around the head). Frosted vertical gradient + faint
// animated caustics + very slow drift. _Dissolve animates 0 -> 1
// to melt the layer away. Drawn early in Transparent so panels
// composite over it; depth-tested so opaque virtual objects win.
Shader "Bench/GlassOverlay"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.59, 0.71, 0.82, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.55
        _CausticIntensity ("Caustic Intensity", Range(0, 0.1)) = 0.025
        _CausticSpeed ("Caustic Speed", Float) = 0.12
        _DriftSpeed ("Drift Speed", Float) = 0.02
        _DriftEnabled ("Drift Enabled", Float) = 1
        _Dissolve ("Dissolve (0 solid, 1 gone)", Range(0, 1)) = 0
    }
    SubShader
    {
        // Opaque bucket (always before transparent UI), blended
        // with depth test + no depth write so the room shows through.
        Tags { "Queue" = "Geometry" "RenderType" = "Transparent" }
        Cull Off // single shell: render both faces, avoids winding doubt
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float _Opacity;
            float _CausticIntensity;
            float _CausticSpeed;
            float _DriftSpeed;
            float _DriftEnabled;
            float _Dissolve;

            struct appdata_t
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewDir = normalize(o.worldPos - _WorldSpaceCameraPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                float drift = _DriftEnabled > 0.5 ? t * _DriftSpeed : 0.0;
                // Frosted gradient: a touch denser toward top and bottom.
                float band = 1.0 - abs(i.viewDir.y);
                float base = _Opacity * (0.72 + 0.28 * band);
                // Faint animated caustics.
                float sp = _DriftEnabled > 0.5 ? _CausticSpeed : 0.0;
                float3 q = i.worldPos * 0.16 + drift;
                float s = sin(q.x + t * sp) * sin(q.y * 1.13 - t * sp * 0.8)
                        * sin(q.z * 0.91 + t * sp * 0.6);
                float caustic = pow(max(s, 0.0), 8.0) * _CausticIntensity * 40.0;
                float a = saturate(base + caustic * base) * (1.0 - _Dissolve);
                fixed3 rgb = _Tint.rgb + caustic * 0.35;
                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
}
