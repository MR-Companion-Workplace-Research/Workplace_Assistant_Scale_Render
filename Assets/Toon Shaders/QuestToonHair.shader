// Toon hair for the "toon" render-style condition. Same cel ramp as
// QuestToon/Lit, but with alpha-TEST (not blend) and double-sided cards, and
// NO outline (inverted-hull outlines on thin hair cards look broken).
//
// Carries the same two Quest fixes as the rest of the avatar:
//   1. Meta Depth API occlusion.
//   2. Output alpha forced to 1 (passthrough opacity).
//
// Property names match the CC hair shader (_DiffuseMap, _AlphaClip) so a copied
// hair material keeps its texture binding and cutoff.
Shader "QuestToon/Hair"
{
    Properties
    {
        _DiffuseMap("Diffuse Map", 2D) = "white" {}
        _DiffuseColor("Tint", Color) = (1,1,1,1)
        _AlphaClip("Alpha Clip", Range(0, 1)) = 0.5

        [Header(Toon Shading)]
        // Matches QuestToon/Lit: two hard cel steps -> three flat tones, crisp edges.
        _RampThreshold("Shadow Threshold", Range(0, 1)) = 0.5
        _RampHighlight("Highlight Threshold", Range(0, 1)) = 0.78
        _RampSmoothing("Edge Softness", Range(0.001, 0.5)) = 0.03
        _ShadowTint("Shadow Tint", Color) = (0.5, 0.4, 0.38, 1)
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On

        CGPROGRAM
        #pragma surface surf ToonRamp fullforwardshadows addshadow exclude_path:deferred
        #pragma target 3.0
        #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
        #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc"

        sampler2D _DiffuseMap;   // _DiffuseMap_ST is auto-declared by the surface compiler (uv_DiffuseMap in Input)
        half4     _DiffuseColor;
        half4     _ShadowTint;
        half      _RampThreshold;
        half      _RampHighlight;
        half      _RampSmoothing;
        half      _AmbientStrength;
        half      _AlphaClip;

        struct Input
        {
            float2 uv_DiffuseMap;
            float3 worldPos;
            float3 worldNormal;
        };

        half4 LightingToonRamp(SurfaceOutput s, half3 lightDir, half atten)
        {
            // Two crisp cel steps -> three flat tones (matches QuestToon/Lit).
            half ndl = dot(s.Normal, lightDir) * 0.5 + 0.5;
            ndl *= atten;
            half e    = _RampSmoothing;
            half lo   = smoothstep(_RampThreshold - e, _RampThreshold + e, ndl);
            half hi   = smoothstep(_RampHighlight - e, _RampHighlight + e, ndl);
            half band = (lo + hi) * 0.5;                    // 0 = shadow, 0.5 = mid, 1 = lit
            half3 ramp = lerp(_ShadowTint.rgb, half3(1, 1, 1), band);

            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * ramp;
            c.a   = 1.0;
            return c;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_DiffuseMap, IN.uv_DiffuseMap) * _DiffuseColor;
            clip(c.a - _AlphaClip);                        // alpha test -> crisp, opaque hair

            #if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
                clip(CalculateEnvironmentDepthOcclusion(IN.worldPos, 0.0) - 0.5);
            #endif

            o.Albedo   = c.rgb;
            o.Emission = c.rgb * ShadeSH9(half4(normalize(IN.worldNormal), 1.0)) * _AmbientStrength;
            o.Alpha    = 1.0;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
