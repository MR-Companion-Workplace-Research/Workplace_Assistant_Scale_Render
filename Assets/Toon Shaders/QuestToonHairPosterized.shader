// "V2" toon hair — POSTERIZED variant of QuestToon/Hair. See
// QuestToonLitPosterized.shader for what "posterized" adds over the V1 shaders:
// the albedo's brightness (value) is quantised into ~_ColorSteps flat levels — hue
// and saturation preserved — so the hair strand gradients collapse into distinct
// bands, matching the posterized body/face shader.
//
// Same structure as QuestToon/Hair: alpha-TEST (not blend), double-sided cards,
// NO outline pass (inverted hulls look broken on thin hair cards), and the same
// two Quest fixes (Meta Depth API occlusion; output alpha forced to 1 for
// passthrough). Property names match, so a material can be flipped between the
// V1 and V2 hair shaders losslessly for A/B comparison.
Shader "QuestToonPosterized/Hair"
{
    Properties
    {
        _DiffuseMap("Diffuse Map", 2D) = "white" {}
        _DiffuseColor("Tint", Color) = (1,1,1,1)
        _AlphaClip("Alpha Clip", Range(0, 1)) = 0.5

        [Header(Posterize Lighting)]
        // Matches QuestToonPosterized/Lit: light falloff quantised into N flat bands.
        _LightBands("Light Bands", Range(2, 8)) = 5
        _RampSmoothing("Band Edge Softness", Range(0.001, 0.5)) = 0.03
        // Matches QuestToonPosterized/Lit's deeper shadow floor (V1 keeps 0.5, 0.4, 0.38).
        _ShadowTint("Shadow Tint", Color) = (0.4, 0.3, 0.28, 1)
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 1

        [Header(Posterize Albedo)]
        _PosterizeStrength("Texture Banding Strength", Range(0, 1)) = 0.5
        _ColorSteps("Color Steps", Range(2, 10)) = 5
        _PosterizeBlur("Region Smoothing (mip bias)", Range(0, 4)) = 2
        _SaturationBoost("Saturation", Range(0, 2)) = 1
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
        half      _LightBands;
        half      _RampSmoothing;
        half      _AmbientStrength;
        half      _AlphaClip;
        half      _PosterizeStrength;
        // Avatar-anchored virtual key light — same global as QuestToonPosterized/Lit
        // (set by AvatarPlacer; zero -> fall back to the scene light) so hair and face
        // band in the same direction.
        float3    _ToonKeyDir;
        half      _ColorSteps;
        half      _PosterizeBlur;
        half      _SaturationBoost;

        struct Input
        {
            float2 uv_DiffuseMap;
            float3 worldPos;
            float3 worldNormal;
        };

        // Quantise BRIGHTNESS (value) only, preserving hue AND saturation exactly, so a
        // band can never brighten a colour past the gamut and clip it to a different hue —
        // see QuestToonPosterized/Lit for the full rationale.
        inline half3 PosterizeValue(half3 col)
        {
            half v      = max(col.r, max(col.g, col.b));   // HSV value (brightness)
            half3 chroma = col / max(v, 1e-4);             // hue + saturation, max channel == 1
            half vp     = min(sqrt(saturate(v)), 0.9999);  // perceptual, kept off the top edge
            half vq     = (floor(vp * _ColorSteps) + 0.5) / _ColorSteps;
            vq *= vq;                                       // back to linear (0..1, never clips)
            return chroma * vq;
        }

        half4 LightingToonRamp(SurfaceOutput s, half3 lightDir, half atten)
        {
            // Light falloff quantised into _LightBands flat bands (matches the Lit shader,
            // including the avatar-anchored virtual key — see that file for the rationale).
            half ndl;
            if (dot(_ToonKeyDir, _ToonKeyDir) > 0.001)
            {
                ndl = dot(s.Normal, normalize(_ToonKeyDir)) * 0.5 + 0.5;
            }
            else
            {
                ndl = dot(s.Normal, lightDir) * 0.5 + 0.5;
                ndl *= atten;
            }

            half bands  = max(_LightBands, 2.0);
            half scaled = min(ndl, 0.9999) * bands;
            half cell   = floor(scaled);
            half f      = scaled - cell;
            half e      = saturate(_RampSmoothing * bands);
            half band   = saturate((cell + smoothstep(1.0 - e, 1.0, f)) / (bands - 1.0));
            half3 ramp  = lerp(_ShadowTint.rgb, half3(1, 1, 1), band);

            // Ambient folded in multiplicatively (see QuestToonPosterized/Lit): additive
            // ambient clipped bright albedos at white and merged the top bands.
            half3 illum = saturate(_LightColor0.rgb +
                                   ShadeSH9(half4(s.Normal, 1.0)) * _AmbientStrength);

            half4 c;
            c.rgb = s.Albedo * ramp * illum;
            c.a   = 1.0;
            return c;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // Alpha test on the CRISP (unbiased) sample so the card silhouettes stay
            // sharp; only the colour uses the blurred sample below.
            fixed4 crisp = tex2D(_DiffuseMap, IN.uv_DiffuseMap) * _DiffuseColor;
            clip(crisp.a - _AlphaClip);

            #if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
                clip(CalculateEnvironmentDepthOcclusion(IN.worldPos, 0.0) - 0.5);
            #endif

            fixed4 c = tex2Dbias(_DiffuseMap, float4(IN.uv_DiffuseMap, 0, _PosterizeBlur)) * _DiffuseColor;
            half3 col = lerp(c.rgb, PosterizeValue(c.rgb), _PosterizeStrength);
            half  l   = dot(col, half3(0.299, 0.587, 0.114));
            col = saturate(lerp(half3(l, l, l), col, _SaturationBoost));

            o.Albedo   = col;
            // No Emission ambient: SH ambient is folded into the lighting ramp
            // (see LightingToonRamp) so it can't wash out or clip the bands.
            o.Alpha    = 1.0;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
