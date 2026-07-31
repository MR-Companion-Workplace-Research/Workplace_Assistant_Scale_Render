// Faithful re-implementation of Unity's built-in Standard shader (Opaque / Cutout)
// + Meta Quest Depth-API hard/soft occlusion.
//
// WHY THIS EXISTS: Unity's Standard shader is a built-in binary and cannot be
// copied byte-for-byte like the Reallusion CC surface shaders. This surface
// shader invokes the SAME Unity Standard PBR lighting model (#pragma surface
// surf Standard) with the SAME maps and keyword handling, so the lit colour
// matches Standard in practice. The ONLY added behaviour is the env-depth clip.
//
// Serves the 4 opaque/cutout materials: Rolled_sleeves_shirt, Knee_length_skirt,
// High_Heels, Female_Brow_Base_Transparency.
Shader "QuestOcclusion/StandardOccluded"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}

        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5

        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _GlossMapScale ("Smoothness Scale", Range(0,1)) = 1.0
        [Enum(Metallic Alpha,0,Albedo Alpha,1)] _SmoothnessTextureChannel ("Smoothness texture channel", Float) = 0

        [Gamma] _Metallic ("Metallic", Range(0,1)) = 0.0
        _MetallicGlossMap ("Metallic", 2D) = "white" {}

        [ToggleOff] _SpecularHighlights ("Specular Highlights", Float) = 1.0
        [ToggleOff] _GlossyReflections ("Glossy Reflections", Float) = 1.0

        _BumpScale ("Scale", Float) = 1.0
        _BumpMap ("Normal Map", 2D) = "bump" {}

        _Parallax ("Height Scale", Range(0.005, 0.08)) = 0.02
        _ParallaxMap ("Height Map", 2D) = "black" {}

        _OcclusionStrength ("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap ("Occlusion", 2D) = "white" {}

        // NOTE: default is BLACK so an unassigned emission map never glows.
        _EmissionColor ("Color", Color) = (0,0,0)
        _EmissionMap ("Emission", 2D) = "black" {}

        _DetailMask ("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMap ("Detail Albedo x2", 2D) = "grey" {}
        _DetailNormalMapScale ("Scale", Float) = 1.0
        _DetailNormalMap ("Normal Map", 2D) = "bump" {}
        [Enum(UV0,0,UV1,1)] _UVSec ("UV Set for secondary textures", Float) = 0

        // Kept so existing material's serialized blend props bind without warnings.
        [HideInInspector] _Mode ("__mode", Float) = 0.0
        [HideInInspector] _SrcBlend ("__src", Float) = 1.0
        [HideInInspector] _DstBlend ("__dst", Float) = 0.0
        [HideInInspector] _ZWrite ("__zw", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        // Same Standard PBR lighting model the built-in shader uses.
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        // Material-driven feature keywords (match Unity Standard). The metallic/gloss
        // map is keyword-gated (see surf) so materials WITHOUT one fall back to the
        // scalar _Metallic/_Glossiness instead of sampling the "white" default.
        #pragma shader_feature_local _ALPHATEST_ON
        #pragma shader_feature_local _EMISSION
        #pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
        #pragma shader_feature_local _GLOSSYREFLECTIONS_OFF
        #pragma shader_feature_local _METALLICGLOSSMAP

        // Meta Quest environment-depth occlusion.
        #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
        #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc"

        sampler2D _MainTex;
        sampler2D _MetallicGlossMap;
        sampler2D _BumpMap;
        sampler2D _OcclusionMap;
        sampler2D _EmissionMap;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };

        fixed4 _Color;
        half _Metallic;
        half _Glossiness;
        half _GlossMapScale;
        half _BumpScale;
        half _OcclusionStrength;
        half _Cutoff;
        fixed4 _EmissionColor;

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // --- Quest Depth-API occlusion: discard fragments behind real geometry ---
            #if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
                clip( CalculateEnvironmentDepthOcclusion( IN.worldPos, 0.0 ) - 0.5 );
            #endif

            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;

            // Cutout: same clip Standard performs in alphatest mode.
            #if defined(_ALPHATEST_ON)
                clip(c.a - _Cutoff);
            #endif

            o.Albedo = c.rgb;

            // Metallic + smoothness: use the gloss map ONLY when the material actually
            // assigns one (keyword set), exactly like Unity Standard. Plain-export CC
            // materials (skin/body/teeth/etc.) have NO _MetallicGlossMap -> an
            // unconditional sample reads the "white" default (metallic=1, smoothness=1)
            // and the surface renders as chrome/mirror (looks metallic + see-through over
            // passthrough). Fall back to the scalar _Metallic / _Glossiness in that case.
            #if defined(_METALLICGLOSSMAP)
                fixed4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
                o.Metallic = mg.r;
                o.Smoothness = mg.a * _GlossMapScale;
            #else
                o.Metallic = _Metallic;
                o.Smoothness = _Glossiness;
            #endif

            // Normal map (unassigned _BumpMap defaults to flat "bump" -> no-op).
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);

            // Unassigned occlusion map binds to white (1.0) -> no-op, exactly like Standard.
            half occ = tex2D(_OcclusionMap, IN.uv_MainTex).g;
            o.Occlusion = lerp(1.0h, occ, _OcclusionStrength);

            // Emission only when the material actually enables it (matches Standard).
            #if defined(_EMISSION)
                o.Emission = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor.rgb;
            #endif

            // Opaque/cutout surviving fragments must be fully opaque so Quest passthrough
            // does not bleed through (same reasoning as the QuestHairOpaque fix).
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Standard"
}
