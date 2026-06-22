// Toon / cel shader for the "toon" render-style study condition.
// Built to be a drop-in stylised replacement for the realistic CC surfaces
// (skin, body, teeth, tongue, clothing) while preserving the SAME two
// Quest-specific fixes the realistic shaders use, so the only difference
// between the two study conditions is the SHADING, not occlusion or passthrough:
//
//   1. Meta Depth API occlusion  (see RL_SkinShader_Occlusion.shader) — real
//      geometry occludes the avatar. Same include / keyword / clip.
//   2. Passthrough opacity        (see QuestHairOpaque.shader) — output alpha is
//      forced to 1 so passthrough does not bleed through the avatar.
//
// Property names (_DiffuseMap, _DiffuseColor) deliberately match the CC skin
// shader so a copied material keeps its albedo texture binding.
Shader "QuestToon/Lit"
{
    Properties
    {
        _DiffuseMap("Diffuse Map", 2D) = "white" {}
        _DiffuseColor("Tint", Color) = (1,1,1,1)

        [Header(Toon Shading)]
        _RampThreshold("Shadow Threshold", Range(0, 1)) = 0.5
        _RampSmoothing("Shadow Softness", Range(0.001, 0.5)) = 0.15
        _ShadowTint("Shadow Tint", Color) = (0.82, 0.74, 0.7, 1)
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 1

        [Header(Outline)]
        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        // World metres at avatarScale 1.0. Multiplied at runtime by _OutlineScale
        // (= avatarScale, set by AvatarDeskPlacer) so it stays proportional in the
        // miniature condition. 0.005 => 5 mm human-sized, ~1 mm at 0.2 miniature.
        _OutlineWidth("Outline Width (m @ scale 1)", Range(0, 0.03)) = 0.005
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        // ---------------------------------------------------------------
        // PASS 1 — inverted-hull outline. Extrude backfaces along the normal in
        // WORLD space by _OutlineWidth * _OutlineScale, render solid colour.
        // _OutlineScale is fed by AvatarDeskPlacer = avatarScale, so the world
        // thickness shrinks with the miniature condition. (Object-space extrude
        // can't do this on a skinned mesh scaled at runtime: the scale is baked
        // into the skinned verts, not unity_ObjectToWorld, so both spaces give the
        // same fixed world thickness — which flooded the small facial concavities.)
        // Depth-occluded like the main pass so it never draws over real objects.
        // ---------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "UnityCG.cginc"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc"

            float  _OutlineWidth;
            float4 _OutlineColor;
            // Global, set by AvatarDeskPlacer = avatarScale.x. NOT a Property, so a
            // per-material serialized value never shadows the global. Defaults to 0
            // when unset (editor preview / realistic run) -> treated as 1 below.
            float  _OutlineScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos    = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Scale the hull by the avatar's transform scale, fed in from script.
                // (Editor preview / realistic run leaves it 0 -> fall back to 1.)
                float scale = (_OutlineScale > 0.0001) ? _OutlineScale : 1.0;
                worldPos += normalize(worldNormal) * _OutlineWidth * scale;

                o.worldPos = worldPos;
                o.pos      = UnityWorldToClipPos(worldPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                #if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
                    clip(CalculateEnvironmentDepthOcclusion(i.worldPos, 0.0) - 0.5);
                #endif
                return fixed4(_OutlineColor.rgb, 1.0); // alpha = 1 for passthrough
            }
            ENDCG
        }

        // ---------------------------------------------------------------
        // PASS 2 — toon-lit surface (custom cel lighting model).
        // ---------------------------------------------------------------
        Cull Back

        CGPROGRAM
        #pragma surface surf ToonRamp fullforwardshadows addshadow exclude_path:deferred
        #pragma target 3.0
        #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
        #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc"

        sampler2D _DiffuseMap;   // _DiffuseMap_ST is auto-declared by the surface compiler (uv_DiffuseMap in Input)
        half4     _DiffuseColor;
        half4     _ShadowTint;
        half      _RampThreshold;
        half      _RampSmoothing;
        half      _AmbientStrength;

        struct Input
        {
            float2 uv_DiffuseMap;
            float3 worldPos;
            float3 worldNormal;   // geometric (we never overwrite o.Normal) -> usable for SH ambient
        };

        // Quantise N.L (incl. shadow attenuation) into hard cel bands, then tint
        // the darkest band toward _ShadowTint instead of pure black.
        half4 LightingToonRamp(SurfaceOutput s, half3 lightDir, half atten)
        {
            // Half-Lambert keeps the shaded side bright -> flat, high-key illustration look.
            half ndl = dot(s.Normal, lightDir) * 0.5 + 0.5;
            ndl *= atten;
            // One soft shadow step. With a warm, light _ShadowTint this reads as a subtle,
            // flat shade rather than a hard two-tone cel.
            half lit = smoothstep(_RampThreshold - _RampSmoothing, _RampThreshold + _RampSmoothing, ndl);
            half3 ramp = lerp(_ShadowTint.rgb, half3(1, 1, 1), lit);

            half4 c;
            c.rgb = s.Albedo * _LightColor0.rgb * ramp;
            c.a   = 1.0;                                    // force opaque for passthrough
            return c;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // Meta Depth API: discard fragments behind real-world geometry.
            #if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
                clip(CalculateEnvironmentDepthOcclusion(IN.worldPos, 0.0) - 0.5);
            #endif

            fixed4 c  = tex2D(_DiffuseMap, IN.uv_DiffuseMap) * _DiffuseColor;
            o.Albedo  = c.rgb;
            // Flat SH ambient fill so the avatar isn't crushed to the shadow tint
            // in dim passthrough lighting (matches the realistic shader's ambient).
            o.Emission = c.rgb * ShadeSH9(half4(normalize(IN.worldNormal), 1.0)) * _AmbientStrength;
            o.Alpha   = 1.0;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
