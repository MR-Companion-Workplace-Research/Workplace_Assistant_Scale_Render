// "V2" toon shader for the render-style study — POSTERIZED variant of QuestToon/Lit.
//
// Difference from QuestToon/Lit: that shader quantises the lighting into only 3
// tones and leaves the albedo fully realistic, so the face never breaks into flat
// colour areas. This variant bands on two axes:
//
//   PRIMARY — lighting: the light falloff is quantised into _LightBands flat bands
//   (shadow tint -> white). Because it's driven by N.L against the scene's
//   directional light, the bands sweep in a CONSTANT direction (lit side -> shaded
//   side) and look consistent across avatars, independent of their textures.
//
//   SECONDARY — albedo: the CC skin texture bakes shading INTO the colour map (AO in
//   the knuckle/finger-joint creases, eye sockets). So we FIRST flatten that out
//   (_AlbedoFlatten replaces each texel's brightness with the flat _FlatValue skin tone,
//   hue kept) — otherwise the banding locks onto those painted shadows and turns them
//   into hard dark blotches (worst on darker male skin). THEN the flattened brightness
//   is optionally quantised into _ColorSteps flat levels (hue+saturation preserved
//   exactly; value only, in sqrt space, so a band can never brighten a colour past the
//   gamut and clip it into a different hue), blended in by _PosterizeStrength. The colour
//   sample is mip-biased (_PosterizeBlur) so pore/noise detail doesn't shatter the regions.
//
// Everything else is IDENTICAL to QuestToon/Lit (same property names, so a material
// can be flipped between the two shaders losslessly for A/B comparison):
//   1. Meta Depth API occlusion (real geometry occludes the avatar).
//   2. Output alpha forced to 1 (passthrough opacity).
//   3. Inverted-hull outline pass scaled by the _OutlineScale global (AvatarPlacer).
//   4. Two-step cel lighting ramp on top of the posterised albedo.
Shader "QuestToonPosterized/Lit"
{
    Properties
    {
        _DiffuseMap("Diffuse Map", 2D) = "white" {}
        _DiffuseColor("Tint", Color) = (1,1,1,1)

        [Header(Posterize Lighting)]
        // PRIMARY banding: the light falloff is quantised into this many flat bands,
        // sweeping lit side -> shaded side in a constant direction set by the scene's
        // directional light. This is what makes the "~5 colour areas" consistent
        // across avatars — it does not depend on their textures.
        _LightBands("Light Bands", Range(2, 8)) = 5
        _RampSmoothing("Band Edge Softness", Range(0.001, 0.5)) = 0.03
        // Deeper than V1's (0.5, 0.4, 0.38): the darkest band's colour is the contrast
        // floor the _LightBands spread down to — darker floor = more obvious bands.
        _ShadowTint("Shadow Tint", Color) = (0.4, 0.3, 0.28, 1)
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 1

        [Header(Posterize Albedo)]
        // Removes the baked shading painted into the CC skin texture (ambient occlusion in
        // the knuckle/finger-joint creases, eye sockets) BEFORE banding, by replacing each
        // texel's brightness with the flat _FlatValue skin tone. Without this the value
        // banding locks onto those fake painted shadows and turns them into hard dark
        // blotches — worst on the darker male skin. 0 = keep the texture as painted;
        // 1 = fully flat skin, shaped only by the light bands (the toon look).
        _AlbedoFlatten("Skin Flatten (remove baked AO)", Range(0, 1)) = 0.6

        // The flat skin brightness _AlbedoFlatten pulls toward. AUTO-STAMPED per avatar by
        // Tools > Study > Toonify (median brightness of the material's own albedo; SHARED
        // across the four skin materials so the neck/wrist seams can't mismatch).
        // 0 = never stamped -> flattening is skipped, so an unstamped material renders
        // un-flattened rather than crushing to black.
        _FlatValue("Flat Skin Value (auto-stamped)", Range(0, 1)) = 0

        // How much of a dark PAINTED feature (lash line, nostril crease, lip line, jaw
        // crease) survives flattening. The albedo is split into chroma = colour / value;
        // on near-black texels an unclamped divide amplified their JPEG/ASTC block noise
        // ~10x, and the flatten then re-lit them to full skin brightness — that was the
        // speckled grid around the eyes / nose bridge / jaw. Clamping the divisor keeps
        // those texels proportionally dark instead. Fades in with _AlbedoFlatten, so at
        // flatten 0 the texture still passes through untouched.
        _FeatureFloor("Feature Darkness Floor", Range(0.02, 1)) = 0.25

        // SECONDARY banding from the (flattened) texture. 0 = off (flat skin only); 1 =
        // fully quantised into _ColorSteps levels. Because it now runs on the FLATTENED
        // albedo, the steps form over flat skin instead of the baked joint shadows.
        _PosterizeStrength("Texture Banding Strength", Range(0, 1)) = 0.4
        _ColorSteps("Color Steps", Range(2, 10)) = 5
        // Mip bias for the colour sample. Higher = smoother, larger colour regions
        // (fine skin detail is deliberately lost — that's toon). At _AlbedoFlatten 1 this
        // only affects HUE (brightness comes from _FlatValue), so it can be lowered for
        // crisper lips/brow colour without bringing the baked AO back.
        _PosterizeBlur("Region Smoothing (mip bias)", Range(0, 4)) = 2
        // 1 = neutral (keep the texture's own colours); >1 = more illustrated pop.
        _SaturationBoost("Saturation", Range(0, 2)) = 1

        [Header(Outline)]
        _OutlineColor("Outline Color", Color) = (0,0,0,1)
        // Same semantics as QuestToon/Lit: world metres at avatarScale 1, multiplied
        // at runtime by the _OutlineScale global (= avatarScale from AvatarPlacer).
        _OutlineWidth("Outline Width (m @ scale 1)", Range(0, 0.03)) = 0.0025
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        // ---------------------------------------------------------------
        // PASS 1 — inverted-hull outline. Byte-for-byte the same as QuestToon/Lit
        // (see that file for the world-space-extrude rationale on skinned meshes).
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
            // Global, set by AvatarPlacer = avatarScale.x. NOT a Property, so a
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
        // PASS 2 — toon-lit surface: posterised albedo x cel lighting ramp.
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
        half      _LightBands;
        half      _RampSmoothing;
        half      _AmbientStrength;
        half      _AlbedoFlatten;
        half      _FlatValue;
        half      _FeatureFloor;
        half      _PosterizeStrength;
        // Avatar-anchored virtual key light for the band ramp (world-space direction,
        // set by AvatarPlacer from the spawned avatar's final rotation). Bare uniform,
        // NOT a Property — same pattern as _OutlineScale: a global that a per-material
        // serialized value can never shadow. Zero when unset (editor preview /
        // realistic run) -> the ramp falls back to the real scene light.
        float3    _ToonKeyDir;
        half      _ColorSteps;
        half      _PosterizeBlur;
        half      _SaturationBoost;

        struct Input
        {
            float2 uv_DiffuseMap;
            float3 worldPos;
            float3 worldNormal;   // geometric (we never overwrite o.Normal) -> usable for SH ambient
        };

        // Flatten the baked shading in the albedo: replace each texel's BRIGHTNESS with the
        // flat _FlatValue skin tone (its own hue + saturation kept), so the painted ambient
        // occlusion — knuckle creases, finger-joint darkening, eye sockets — is removed
        // instead of being quantised into hard toon blocks. amount 0 = texture as painted;
        // 1 = fully flat skin tone. Runs BEFORE PosterizeValue so bands form over flat skin.
        //
        // 2026-07-20: the target used to be a SECOND, very-blurred tex2Dbias sample of the
        // same map (bias _PosterizeBlur + 3). On the 2048 CC head map that is a ~45x
        // reduction — roughly 15 texels across the entire face — and magnifying it
        // bilinearly painted a visible quad GRID over the skin, worst where one low-mip
        // texel straddled UV islands and dark features (inner eye corners, nose bridge,
        // jawline). A per-material constant has no spatial frequency at all, so the grid
        // cannot exist, and it costs one FEWER texture fetch. See Assets/Toon Shaders/README.md.
        inline half3 FlattenValue(half3 col, half amount)
        {
            half a = saturate(amount);
            half v = max(col.r, max(col.g, col.b));

            // Clamped divisor: an unclamped divide on near-black texels amplifies their
            // compression noise and lets the flatten re-light them to full skin brightness.
            // The floor fades in with `a` so flatten 0 still passes the texture through exactly.
            half3 chroma = col / max(v, max(_FeatureFloor * a, 1e-4));

            // _FlatValue 0 == never stamped by the Toonify tool: skip flattening rather
            // than crushing the material to black.
            half target = (_FlatValue > 0.001) ? _FlatValue : v;
            return chroma * lerp(v, target, a);
        }

        // Quantise BRIGHTNESS into _ColorSteps flat levels while preserving hue AND
        // saturation EXACTLY. We split the colour into its VALUE (the max channel) and a
        // chroma direction normalised so max channel == 1, quantise only the value in sqrt
        // (perceptual) space, then rebuild chroma * value.
        //
        // Why not the old luma-rescale (col * quantisedLuma / luma): rescaling a saturated
        // skin tone up to a brighter luma band pushed its strongest channel past 1.0, which
        // saturate() then clipped UNEVENLY across channels — so each luma band clipped to a
        // different hue and the face/hands broke into random differently-COLOURED blocks.
        // Here the result is chroma (max == 1) * value (<= 1), so it can never exceed the
        // gamut, never clips, and never shifts hue: skin bands stay the same colour at
        // different brightness (true cel shading). Dividing by the max channel (not a tiny
        // luminance) also stops dark texels blowing up into bright specks.
        inline half3 PosterizeValue(half3 col)
        {
            half v      = max(col.r, max(col.g, col.b));   // HSV value (brightness)
            half3 chroma = col / max(v, 1e-4);             // hue + saturation, max channel == 1
            half vp     = min(sqrt(saturate(v)), 0.9999);  // perceptual, kept off the top edge
            half vq     = (floor(vp * _ColorSteps) + 0.5) / _ColorSteps;
            vq *= vq;                                       // back to linear (0..1, never clips)
            return chroma * vq;
        }

        // Cel lighting quantised into _LightBands flat bands between _ShadowTint and
        // white. Half-Lambert N.L sweeps 0..1 from the shaded side to the lit side.
        //
        // Band DIRECTION comes from the avatar-anchored virtual key light (_ToonKeyDir)
        // when set: MRUK spawns the avatar at an arbitrary orientation relative to the
        // scene's fixed directional light, so real-light N.L leaves the face one flat
        // bright band in one room and uniformly shaded in another. The virtual key is
        // defined RELATIVE TO THE AVATAR and transformed to world by AvatarPlacer, so
        // the band layout on the face is identical in every room. It deliberately
        // ignores shadow attenuation — the bands are stylisation, not physical shading;
        // overall brightness still follows the real light via illum below.
        half4 LightingToonRamp(SurfaceOutput s, half3 lightDir, half atten)
        {
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
            // Anti-alias only the top sliver of each cell so edges stay crisp (inked).
            half e      = saturate(_RampSmoothing * bands);
            half band   = saturate((cell + smoothstep(1.0 - e, 1.0, f)) / (bands - 1.0));
            half3 ramp  = lerp(_ShadowTint.rgb, half3(1, 1, 1), band);

            // Illumination = direct light + SH ambient, SATURATED to 1 and MULTIPLIED
            // by the banded ramp — not added on top of it (no separate Emission term).
            // Additive ambient made bright albedos (light skin) clip at white, merging
            // the top bands into one blown-out region, while dark skin kept all bands.
            // Multiplying keeps every band visible regardless of albedo brightness,
            // and SH ambient still keeps the avatar lit in dim passthrough rooms.
            half3 illum = saturate(_LightColor0.rgb +
                                   ShadeSH9(half4(s.Normal, 1.0)) * _AmbientStrength);

            half4 c;
            c.rgb = s.Albedo * ramp * illum;
            c.a   = 1.0;                                    // force opaque for passthrough
            return c;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // Meta Depth API: discard fragments behind real-world geometry.
            #if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
                clip(CalculateEnvironmentDepthOcclusion(IN.worldPos, 0.0) - 0.5);
            #endif

            // Broad colour sample (mip-biased): the stylisation forms over the large tonal
            // shapes of the texture, not per-pixel pore/noise detail. This is the ONLY
            // albedo fetch — the flatten target is a constant, not a second blurred sample.
            half3 base = tex2Dbias(_DiffuseMap, float4(IN.uv_DiffuseMap, 0, _PosterizeBlur)).rgb * _DiffuseColor.rgb;

            half3 flatCol = FlattenValue(base, _AlbedoFlatten);                   // AO-free flat skin
            half3 col     = lerp(flatCol, PosterizeValue(flatCol), _PosterizeStrength); // optional banding on top

            half  l = dot(col, half3(0.299, 0.587, 0.114));
            col = saturate(lerp(half3(l, l, l), col, _SaturationBoost));

            o.Albedo  = col;
            // No Emission ambient here: SH ambient is folded into the lighting ramp
            // (see LightingToonRamp) so it can't wash out or clip the bands.
            o.Alpha   = 1.0;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
