// Faithful re-implementation of Unity's built-in Standard shader
// (Transparent / PREMULTIPLIED alpha, i.e. Standard's "_Mode: 3")
// + Meta Quest Depth-API hard/soft occlusion.  See StandardOccluded.shader header.
//
// WHY THIS EXISTS (2026-07-20): the six eye-layer materials on both avatars —
// Std_Cornea_L/R, Std_Eye_Occlusion_L/R, Std_Tearline_L/R — were still on Unity's
// BUILT-IN Standard shader (m_Shader guid 0000000000000000f000000000000000,
// fileID 46). Built-in Standard has no Depth-API patch, so those layers were never
// clipped against environment depth: with the rest of the avatar correctly hidden
// behind real furniture, the cornea domes + eye-occlusion shells + tearlines kept
// drawing, and the eyes appeared to float in mid-air. The eyeball itself
// (Std_Eye_L/R) was already fine — it is opaque and sits on QuestOcclusion/StandardOccluded.
//
// WHY A THIRD VARIANT rather than reusing StandardOccludedFade: those materials are
// Standard "Transparent" mode (_Mode 3 => Blend One OneMinusSrcAlpha, PREMULTIPLIED),
// not "Fade" (_Mode 2 => Blend SrcAlpha OneMinusSrcAlpha). The difference is visible
// on a glossy lens like the cornea: in premultiplied mode the specular highlight and
// reflections keep full strength while only the albedo fades, which is exactly what
// makes a CC cornea read as wet glass. Fade would dim the highlight along with the
// albedo and flatten the eye. Using this variant keeps the eyes looking identical to
// before — the ONLY behavioural change is the added environment-depth clip.
//
// PASSTHROUGH NOTE: premultiplied blending composites alpha as
//     A_out = A_src + A_dst * (1 - A_src)
// so over the opaque eyeball/face behind it (A_dst = 1) the result stays 1 and Quest
// passthrough cannot bleed through the eyes. (Fade would give A_src^2 + A_dst(1-A_src)
// < 1 and WOULD bleed — see the keepalpha gotcha that motivated Quest/HairOpaque.)
//
// Metallic/smoothness are keyword-gated exactly like StandardOccluded: none of these
// materials assigns a _MetallicGlossMap, so they must fall back to the scalar
// _Metallic / _Glossiness rather than sampling the "white" default (which would make
// the cornea a fully metallic mirror). NOTE: StandardOccludedFade does NOT do this
// and samples the map unconditionally.
Shader "QuestOcclusion/StandardOccludedTransparent"
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

        // Defaults match Standard's Transparent mode, so the materials' already-serialized
        // blend props bind without warnings and without changing their values.
        [HideInInspector] _Mode ("__mode", Float) = 3.0
        [HideInInspector] _SrcBlend ("__src", Float) = 1.0
        [HideInInspector] _DstBlend ("__dst", Float) = 10.0
        [HideInInspector] _ZWrite ("__zw", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 300

        CGPROGRAM
        // alpha:premul = premultiplied blending (Blend One OneMinusSrcAlpha), ZWrite off.
        // keepalpha preserves o.Alpha for the blend (without it the surface compiler forces
        // alpha to 1, which with One/OneMinusSrcAlpha would render these layers fully opaque).
        #pragma surface surf Standard fullforwardshadows alpha:premul keepalpha
        #pragma target 3.0

        // Material-driven feature keywords (match Unity Standard). The metallic/gloss map is
        // keyword-gated (see surf) so materials WITHOUT one fall back to the scalar
        // _Metallic/_Glossiness instead of sampling the "white" default.
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
        fixed4 _EmissionColor;

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // --- Quest Depth-API occlusion: discard fragments behind real geometry ---
            // This single clip is the whole point of this shader: it is what the built-in
            // Standard shader these materials used to sit on could not do.
            #if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
                clip( CalculateEnvironmentDepthOcclusion( IN.worldPos, 0.0 ) - 0.5 );
            #endif

            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;

            // Use the gloss map ONLY when the material actually assigns one — none of the
            // eye layers do, and sampling the "white" default would force metallic 1 /
            // smoothness 1 and turn the cornea into a mirror.
            #if defined(_METALLICGLOSSMAP)
                fixed4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
                o.Metallic = mg.r;
                o.Smoothness = mg.a * _GlossMapScale;
            #else
                o.Metallic = _Metallic;
                o.Smoothness = _Glossiness;
            #endif

            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);

            half occ = tex2D(_OcclusionMap, IN.uv_MainTex).g;
            o.Occlusion = lerp(1.0h, occ, _OcclusionStrength);

            #if defined(_EMISSION)
                o.Emission = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor.rgb;
            #endif

            // Genuine transparency preserved (cornea lens, eye-occlusion shell, tearline).
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Standard"
}
