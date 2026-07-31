#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click conversion of an avatar's materials to the toon render-style set, for
/// the Scale x Render Style study. Keeps the realistic prefab untouched: it only
/// rewrites the material slots on the avatar you run it on (intended: the
/// female_toon Prefab Variant, edited in Prefab Mode).
///
/// IMPORTANT: it derives the toon set from the BASE (realistic) materials via the
/// prefab-variant correspondence, NOT from whatever is currently assigned. So it is
/// idempotent AND self-healing — re-running fixes earlier mistakes instead of
/// cloning already-toonified materials.
///
/// For each renderer material it creates (once) a "<name>_Toon" clone in a Toon/
/// subfolder, repoints it to QuestToon/Lit or QuestToon/Hair, and copies the albedo
/// texture / tint / cutoff across BY NAME (source albedo may live under _DiffuseMap,
/// _MainTex, _BaseMap, ... depending on the original shader — otherwise it renders
/// white). Eyes/cornea/tearline are left realistic on purpose.
///
/// Usage:
///   1. Open female_toon (the Variant) in Prefab Mode (double-click it).
///   2. Select the root GameObject.
///   3. Tools > Study > Toonify Selected Avatar.
///   4. Save the prefab (Ctrl+S).
/// </summary>
public static class ToonifyAvatar
{
    private const string ToonSubfolder = "Toon";

    // Material/shader name fragments routed to the alpha-tested hair toon shader.
    private static readonly string[] HairLike = { "Hair", "Brow", "Scalp", "Eyelash" };
    // Fragments left on their realistic shaders (eyes read better un-stylised).
    private static readonly string[] KeepRealistic = { "Eye", "Cornea", "Tearline" };
    // Fragments treated as SKIN, which must all share one flat value — see StampFlatValues.
    private static readonly string[] SkinLike = { "Skin" };

    // Candidate property names for the same concept across the different source shaders.
    private static readonly string[] AlbedoNames = { "_DiffuseMap", "_MainTex", "_BaseMap", "_BaseColorMap" };
    private static readonly string[] TintNames = { "_DiffuseColor", "_BaseColor", "_Color" };
    private static readonly string[] CutoffNames = { "_AlphaClip", "_Cutoff" };

    // Outline width (world metres @ scale 1) written onto QuestToon/Lit materials.
    // The shader's own default (0.005) is visibly too heavy on a head-sized mesh — it
    // floods the small facial concavities (notably around the mouth) and reads ~2x too
    // thick. 0.0025 matches the tuned look of the original study avatar. Set here (not
    // left to the shader default) so every toonified avatar is consistent; re-running
    // Toonify pushes it onto already-created toon materials too.
    private const float LitOutlineWidth = 0.0025f;

    // Cel-shading recipe stamped onto every toon material so re-running Toonify gives a
    // consistent, deliberately non-realistic look — and overrides the soft / high-key
    // values serialized when a material was first created (those don't pick up new shader
    // defaults on their own). Both QuestToon/Lit and /Hair share these properties.
    // Tune the toon look HERE, then re-run Toonify.
    private const float ToonRampThreshold = 0.5f;   // shadow / mid band edge (half-Lambert)
    private const float ToonRampHighlight = 0.78f;  // mid / lit band edge
    private const float ToonRampSmoothing = 0.03f;  // small -> crisp inked cel edges
    private static readonly Color ToonShadowTint = new Color(0.5f, 0.4f, 0.38f, 1f); // deeper warm shade

    // Posterized (V2) recipe, stamped whenever a material carries the V2 properties —
    // both here and when switching styles below — so materials serialized with older
    // V2 defaults (e.g. saturation 1.15) get refreshed. Tune the V2 look HERE.
    // 2026-07-15: flatten=1 + posterize OFF fixed the dark-block problem (multi-level value
    // banding locked onto the baked AO in knuckle creases / eye sockets), but overshot into
    // a totally flat pastel face — with soft 3-band lighting there were no visible cel bands
    // left at all. 2026-07-18: keep the flat skin, but crisp the light-band edges back up
    // and add a 4th band. (A texture-anchored "feature shadow" band re-inking the baked AO
    // was tried the same day and REMOVED — it darkened creases in a way that hurt the look.)
    private const float PosterLightBands = 4.5f;       // flat lighting bands, lit -> shaded
    private const float PosterRampSmoothing = 0.06f; // crisp inked band edges (shader scales by band count)
    private const float PosterFlatten = 1f;          // fully flatten baked AO out of the albedo
    private const float PosterStrength = 0f;         // texture posterize OFF -> flat skin + light bands only
    private const float PosterColorSteps = 5f;       // unused at strength 0; kept for manual per-material tuning
    private const float PosterBlur = 2.5f;           // mip bias for the colour sample
    private const float PosterSaturation = 1f;       // neutral — keep the texture's colours
    // 2026-07-20: how dark a painted feature (lash line, nostril crease, lip line) stays after
    // flattening. The shader splits albedo into chroma = colour / value; dividing by a near-zero
    // value on those texels amplified their compression noise ~10x and the flatten then re-lit
    // them to full skin brightness — the speckled blocks around the eyes / nose bridge / jaw.
    private const float PosterFeatureFloor = 0.25f;
    // V2-only shadow tint, deeper than V1's (0.5, 0.4, 0.38): with 4 evenly spread bands the
    // per-band step was only ~17% brightness and the bands read as subtle. Darker floor =
    // bigger step per band = more obvious banding, still warm (R>G>B) so shadows don't go grey.
    private static readonly Color PosterShadowTint = new Color(0.4f, 0.3f, 0.28f, 1f);

    [MenuItem("Tools/Study/Toonify Selected Avatar")]
    private static void Toonify()
    {
        var root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("Toonify", "Select the avatar root GameObject first " +
                "(open female_toon in Prefab Mode, select its root).", "OK");
            return;
        }

        var litShader = Shader.Find("QuestToon/Lit");
        var hairShader = Shader.Find("QuestToon/Hair");
        if (litShader == null || hairShader == null)
        {
            EditorUtility.DisplayDialog("Toonify",
                "Could not find QuestToon/Lit or QuestToon/Hair. Make sure the toon shaders " +
                "compiled (check the Console).", "OK");
            return;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            EditorUtility.DisplayDialog("Toonify", "No renderers found under " + root.name + ".", "OK");
            return;
        }

        string toonFolder = ResolveToonFolder(renderers);
        if (toonFolder == null)
        {
            EditorUtility.DisplayDialog("Toonify",
                "Could not locate a source material folder to place the Toon set next to. " +
                "Are the avatar's materials saved as assets?", "OK");
            return;
        }

        var cache = new Dictionary<Material, Material>();
        int converted = 0, kept = 0;

        foreach (var r in renderers)
        {
            Material[] srcMats = SourceMaterials(r);   // realistic originals from the base prefab
            var dst = new Material[srcMats.Length];

            for (int i = 0; i < srcMats.Length; i++)
            {
                var m = srcMats[i];
                if (m == null) { dst[i] = null; continue; }

                if (NameMatches(m, KeepRealistic)) { dst[i] = m; kept++; continue; }

                if (!cache.TryGetValue(m, out var toon))
                {
                    var shader = NameMatches(m, HairLike) ? hairShader : litShader;
                    toon = GetOrCreateToonMaterial(m, shader, toonFolder);
                    cache[m] = toon;
                }
                dst[i] = toon;
                converted++;
            }

            Undo.RecordObject(r, "Toonify materials");
            r.sharedMaterials = dst;
            EditorUtility.SetDirty(r);
        }

        // Second pass, once every toon material for this avatar exists: the flat skin value
        // has to be shared across the four skin materials, so it can't be done per-material.
        StampFlatValues(cache.Values);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Toonify: {converted} material slot(s) -> toon, {kept} kept realistic (eyes). " +
                  $"Toon materials in: {toonFolder}. Now save the prefab (Ctrl+S).");
        EditorUtility.DisplayDialog("Toonify",
            $"Done.\n\n{converted} slot(s) converted, {kept} kept realistic (eyes).\n\n" +
            "Now SAVE the prefab (Ctrl+S) to persist the overrides.", "OK");
    }

    // -----------------------------------------------------------------------
    // A/B comparison between the two toon styles. V1 = QuestToon/* (cel-lit only,
    // albedo stays realistic). V2 = QuestToonPosterized/* (albedo luminance also
    // quantised into flat colour regions). The two shader families share property
    // names, so flipping a material's shader is lossless — flip back any time.
    // Runs on the toon materials CURRENTLY assigned under the selected avatar
    // (select the female_toon root, in Prefab Mode or the scene).
    // -----------------------------------------------------------------------
    private static readonly (string v1, string v2)[] StylePairs =
    {
        ("QuestToon/Lit",  "QuestToonPosterized/Lit"),
        ("QuestToon/Hair", "QuestToonPosterized/Hair"),
    };

    [MenuItem("Tools/Study/Toon Style/Use Posterized (V2)")]
    private static void UsePosterizedStyle() => SwapToonStyle(true);

    [MenuItem("Tools/Study/Toon Style/Use Original Cel (V1)")]
    private static void UseOriginalStyle() => SwapToonStyle(false);

    private static void SwapToonStyle(bool toPosterized)
    {
        var root = Selection.activeGameObject;
        if (root == null)
        {
            EditorUtility.DisplayDialog("Toon Style", "Select the toon avatar root GameObject first.", "OK");
            return;
        }

        var seen = new HashSet<Material>();
        int swapped = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null || !seen.Add(m)) continue;

                // Already on the requested V2 family: just refresh the recipe, so this
                // menu item doubles as "restamp the tuned V2 values" after a recipe change.
                if (toPosterized && m.shader.name.StartsWith("QuestToonPosterized/"))
                {
                    Undo.RecordObject(m, "Swap toon style");
                    StampPosterizeRecipe(m);
                    EditorUtility.SetDirty(m);
                    swapped++;
                    continue;
                }

                foreach (var (v1, v2) in StylePairs)
                {
                    string from = toPosterized ? v1 : v2;
                    if (m.shader.name != from) continue;

                    string to = toPosterized ? v2 : v1;
                    var shader = Shader.Find(to);
                    if (shader == null)
                    {
                        EditorUtility.DisplayDialog("Toon Style",
                            $"Shader '{to}' not found — check it compiled (Console).", "OK");
                        return;
                    }
                    Undo.RecordObject(m, "Swap toon style");
                    m.shader = shader;
                    // Refresh the V2 recipe so values serialized under an older shader
                    // revision (e.g. saturation 1.15) don't stick around.
                    if (toPosterized) StampPosterizeRecipe(m);
                    EditorUtility.SetDirty(m);
                    swapped++;
                    break;
                }
            }

        // Same second pass as Toonify: recompute the shared flat skin value, so this menu
        // item stays a complete "restamp the tuned V2 recipe" for an already-toonified avatar.
        if (toPosterized) StampFlatValues(seen);

        AssetDatabase.SaveAssets();
        string style = toPosterized ? "Posterized (V2)" : "Original Cel (V1)";
        Debug.Log($"Toon Style: {swapped} material(s) -> {style}.");
        EditorUtility.DisplayDialog("Toon Style",
            swapped == 0
                ? "No toon materials found under the selection. Run Toonify first, and make " +
                  "sure you selected the toon avatar's root."
                : $"{swapped} material(s) switched to {style}.\n\nMaterial assets were changed " +
                  "directly, so this applies everywhere they are used — no prefab save needed.",
            "OK");
    }

    // -----------------------------------------------------------------------
    // Editor preview of the avatar-anchored toon key light. At runtime AvatarPlacer
    // sets the _ToonKeyDir shader global from the spawned avatar's rotation; in the
    // editor (including Prefab Mode) nothing sets it, so the posterized shaders fall
    // back to the scene/preview light and the tuned band layout is NOT visible.
    // "Preview In Editor" stamps the global for this editor session so the bands can
    // be judged in the Prefab Mode viewport. Prefab Mode poses the avatar at the
    // origin facing +Z (avatar-local == world), so the same direction AvatarPlacer
    // would apply reads correctly — lit from the viewer's top-right when looking at
    // the face. Uses the scene AvatarPlacer's tuned toonKeyLocalDirection when one
    // exists, so preview and runtime stay in sync. Play mode overwrites the global
    // at spawn, so a leftover preview can't leak into a trial.
    // -----------------------------------------------------------------------
    private static readonly int ToonKeyDirId = Shader.PropertyToID("_ToonKeyDir");
    private static readonly Vector3 DefaultToonKeyDir = new Vector3(-0.5f, 0.7f, 0.9f);

    [MenuItem("Tools/Study/Toon Key Light/Preview In Editor")]
    private static void PreviewToonKeyLight()
    {
        var placer = Object.FindObjectOfType<AvatarPlacer>();
        Vector3 dir = placer != null && placer.toonKeyLocalDirection.sqrMagnitude > 0.0001f
            ? placer.toonKeyLocalDirection
            : DefaultToonKeyDir;
        Shader.SetGlobalVector(ToonKeyDirId, dir.normalized);
        SceneView.RepaintAll();
        Debug.Log($"Toon Key Light preview ON (dir {dir.normalized}, " +
                  (placer != null ? "from AvatarPlacer" : "built-in default") +
                  "). Bands in Prefab Mode / Scene view now match the runtime layout. " +
                  "Clear via Tools > Study > Toon Key Light > Clear Preview.");
    }

    [MenuItem("Tools/Study/Toon Key Light/Clear Preview")]
    private static void ClearToonKeyLightPreview()
    {
        Shader.SetGlobalVector(ToonKeyDirId, Vector4.zero);
        SceneView.RepaintAll();
        Debug.Log("Toon Key Light preview OFF — editor bands follow the scene light again.");
    }

    /// <summary>The realistic source materials for a renderer: the base prefab's materials
    /// (via variant correspondence) so we never derive a toon material from a toon material.</summary>
    private static Material[] SourceMaterials(Renderer r)
    {
        var sourceRenderer = PrefabUtility.GetCorrespondingObjectFromSource(r) as Renderer;
        if (sourceRenderer != null &&
            sourceRenderer.sharedMaterials.Length == r.sharedMaterials.Length)
            return sourceRenderer.sharedMaterials;
        return r.sharedMaterials; // running on the base itself, or no correspondence
    }

    private static Material GetOrCreateToonMaterial(Material src, Shader shader, string toonFolder)
    {
        string path = $"{toonFolder}/{src.name}_Toon.mat";
        var toon = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (toon == null)
        {
            toon = new Material(src);
            AssetDatabase.CreateAsset(toon, path);
        }

        // Respect a style choice made via Tools > Study > Toon Style: if this toon
        // material was switched to the Posterized (V2) family, keep it there on
        // re-runs instead of silently reverting it to V1.
        if (toon.shader != null && toon.shader.name.StartsWith("QuestToonPosterized/"))
        {
            var v2 = Shader.Find(shader.name.EndsWith("/Hair")
                ? "QuestToonPosterized/Hair" : "QuestToonPosterized/Lit");
            if (v2 != null) shader = v2;
        }

        toon.shader = shader;
        TransferAppearance(src, toon, shader);
        EditorUtility.SetDirty(toon);
        return toon;
    }

    /// <summary>Copy albedo texture / tint / cutoff from the source material into the toon
    /// shader's properties, regardless of which property names the source shader used.</summary>
    private static void TransferAppearance(Material src, Material toon, Shader shader)
    {
        if (TryGetTexture(src, AlbedoNames, out var tex, out var texProp))
        {
            toon.SetTexture("_DiffuseMap", tex);
            toon.SetTextureScale("_DiffuseMap", src.GetTextureScale(texProp));
            toon.SetTextureOffset("_DiffuseMap", src.GetTextureOffset(texProp));
        }
        else
        {
            Debug.LogWarning($"Toonify: no albedo texture found on '{src.name}' " +
                             $"(tried {string.Join(", ", AlbedoNames)}); it may render flat.");
        }

        foreach (var c in TintNames)
            if (src.HasProperty(c))
            {
                Color col = src.GetColor(c);
                col.a = 1f;
                toon.SetColor("_DiffuseColor", col);
                break;
            }

        if (shader.name.EndsWith("/Hair"))
            foreach (var k in CutoffNames)
                if (src.HasProperty(k))
                {
                    toon.SetFloat("_AlphaClip", RawAlphaCutoff(src, src.GetFloat(k)));
                    break;
                }

        // Outline is on the Lit shaders only (Hair has none). Force the tuned width so the
        // toon set doesn't inherit the shader's too-thick 0.005 default.
        if (shader.name.EndsWith("/Lit") && toon.HasProperty("_OutlineWidth"))
            toon.SetFloat("_OutlineWidth", LitOutlineWidth);

        // Cel-shading recipe (all toon shaders share these props; the V1-only /
        // V2-only ones are guarded by HasProperty). Stamped so re-running Toonify
        // overrides stale values serialized on older materials.
        if (toon.HasProperty("_RampThreshold")) toon.SetFloat("_RampThreshold", ToonRampThreshold);
        if (toon.HasProperty("_RampHighlight")) toon.SetFloat("_RampHighlight", ToonRampHighlight);
        if (toon.HasProperty("_RampSmoothing")) toon.SetFloat("_RampSmoothing", ToonRampSmoothing);
        if (toon.HasProperty("_ShadowTint"))    toon.SetColor("_ShadowTint", ToonShadowTint);
        StampPosterizeRecipe(toon);
    }

    /// <summary>Write the tuned V2 (posterized) recipe onto a material if it carries those
    /// properties. Also called when switching styles, so materials serialized with older
    /// V2 defaults pick up the current recipe.</summary>
    private static void StampPosterizeRecipe(Material toon)
    {
        if (toon.HasProperty("_LightBands"))        toon.SetFloat("_LightBands", PosterLightBands);
        // Soft band edges are a V2 (posterized) choice — gate on the V2-only _LightBands so V1
        // keeps its crisp inked 0.03 edges (which TransferAppearance stamps from ToonRampSmoothing).
        if (toon.HasProperty("_LightBands"))        toon.SetFloat("_RampSmoothing", PosterRampSmoothing);
        // Deeper V2 shadow floor (gated on the V2-only _LightBands so V1 keeps its tint).
        // Runs AFTER TransferAppearance's shared ToonShadowTint stamp, so V2 wins.
        if (toon.HasProperty("_LightBands"))        toon.SetColor("_ShadowTint", PosterShadowTint);
        if (toon.HasProperty("_AlbedoFlatten"))     toon.SetFloat("_AlbedoFlatten", PosterFlatten);
        if (toon.HasProperty("_PosterizeStrength")) toon.SetFloat("_PosterizeStrength", PosterStrength);
        if (toon.HasProperty("_ColorSteps"))        toon.SetFloat("_ColorSteps", PosterColorSteps);
        if (toon.HasProperty("_PosterizeBlur"))     toon.SetFloat("_PosterizeBlur", PosterBlur);
        if (toon.HasProperty("_SaturationBoost"))   toon.SetFloat("_SaturationBoost", PosterSaturation);
        if (toon.HasProperty("_FeatureFloor"))      toon.SetFloat("_FeatureFloor", PosterFeatureFloor);
        // NOTE: _FlatValue is NOT stamped here — it is per-texture AND shared across the skin
        // materials, so it needs the whole avatar in view. StampFlatValues does it in a second pass.
    }

    /// <summary>Second pass: compute and stamp _FlatValue, the flat skin brightness that the
    /// V2 shader's Skin Flatten pulls each texel toward.
    ///
    /// The skin is split across four materials (head / arm-hands / body / leg) whose atlases
    /// hold very different content — the head map is full of dark lashes, lips and brows, the
    /// body map is nearly uniform skin — so their medians differ. Giving each its own value
    /// would put a visible brightness STEP at the neck and wrist seams, so all skin materials
    /// get ONE shared value. Non-skin materials (clothes, shoes) each keep their own.</summary>
    private static void StampFlatValues(IEnumerable<Material> mats)
    {
        var skin = new List<Material>();
        float skinTotal = 0f;

        foreach (var m in mats)
        {
            if (m == null || !m.HasProperty("_FlatValue")) continue;   // V1 / hair: no flatten path

            if (!TryComputeFlatValue(m, out float v))
            {
                Debug.LogWarning($"Toonify: could not read the albedo of '{m.name}' to compute its " +
                                 "flat skin value. _FlatValue left as-is — if it was never stamped, " +
                                 "Skin Flatten is skipped on this material and it renders un-flattened.");
                continue;
            }

            if (NameMatches(m, SkinLike)) { skin.Add(m); skinTotal += v; }
            else { m.SetFloat("_FlatValue", v); EditorUtility.SetDirty(m); }
        }

        if (skin.Count == 0) return;

        float shared = skinTotal / skin.Count;
        foreach (var m in skin) { m.SetFloat("_FlatValue", shared); EditorUtility.SetDirty(m); }
        Debug.Log($"Toonify: flat skin value {shared:0.000} stamped on {skin.Count} skin material(s) " +
                  "(shared, so the neck and wrist seams match).");
    }

    /// <summary>Median brightness (HSV value = max channel) of a material's albedo, expressed in
    /// the same space the shader works in: converted to linear if the project is Linear, and with
    /// the material tint applied — i.e. exactly the "v" the shader computes per texel.
    /// MEDIAN, not mean, so dark lash/lip texels and the unused atlas gutter can't drag it off
    /// the true skin tone.</summary>
    private static bool TryComputeFlatValue(Material m, out float value)
    {
        value = 0f;

        var tex = m.HasProperty("_DiffuseMap") ? m.GetTexture("_DiffuseMap") : null;
        if (tex == null) return false;

        string path = AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

        // Decode the source file directly rather than toggling the importer's Read/Write flag:
        // no reimport, no .meta churn, and it works regardless of the compressed GPU format.
        var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!tmp.LoadImage(File.ReadAllBytes(path), false)) return false;

            var px = tmp.GetPixels32();
            if (px.Length == 0) return false;

            var hist = new int[256];
            int n = 0;
            int stride = Mathf.Max(1, px.Length / 200000);   // ~200k samples is ample for a median
            for (int i = 0; i < px.Length; i += stride)
            {
                int v = Mathf.Max(px[i].r, Mathf.Max(px[i].g, px[i].b));
                if (v < 16) continue;                        // unused atlas gutter (black padding)
                hist[v]++;
                n++;
            }
            if (n == 0) return false;

            int halfway = n / 2, acc = 0, median = 128;
            for (int b = 0; b < 256; b++)
            {
                acc += hist[b];
                if (acc >= halfway) { median = b; break; }
            }
            value = median / 255f;
        }
        finally
        {
            Object.DestroyImmediate(tmp);
        }

        // LoadImage hands back sRGB bytes. In a Linear project the shader samples that same texel
        // already converted to linear, so match it — otherwise the flattened skin lands too bright.
        if (PlayerSettings.colorSpace == ColorSpace.Linear)
            value = Mathf.GammaToLinearSpace(value);

        // The shader flattens the TINTED colour (base = tex * _DiffuseColor).
        if (m.HasProperty("_DiffuseColor"))
        {
            Color t = m.GetColor("_DiffuseColor");
            value *= Mathf.Max(t.r, Mathf.Max(t.g, t.b));
        }

        value = Mathf.Clamp01(value);
        return value > 0.001f;
    }

    /// <summary>Translate a CC hair shader's alpha cutoff into the RAW-TEXTURE cutoff that the
    /// toon hair shaders actually clip against.
    ///
    /// 2026-07-20 — this fixes the "male eyebrows are transparent on the headset" bug.
    /// Quest/HairOpaque (and the Reallusion shaders it was copied from) do NOT clip the
    /// texture's alpha directly — they remap it first:
    ///     a' = pow(saturate(a / _AlphaRemap), _AlphaPower);   clip(a' - _AlphaClip);
    /// The toon hair shaders clip raw alpha (`clip(crisp.a - _AlphaClip)`), so copying
    /// _AlphaClip across verbatim silently RAISES the real threshold. With the values on this
    /// project's brows/lashes (_AlphaRemap 0.75, _AlphaPower 1, _AlphaClip 0.33) the realistic
    /// effective cutoff is 0.75 x 0.33 = 0.2475, but the toon material was clipping at 0.33 —
    /// 33% higher.
    ///
    /// That gap matters because brow cards are mostly transparent (only ~6% of texels clear the
    /// cutoff), so alpha coverage is fragile as the mip chain averages alpha down. Measured on
    /// Female_Angled_Transparency_Diffuse: at 0.2475 coverage is still 124% of mip0 at mip 6,
    /// but at 0.33 it falls to 44% and reaches ZERO by mip 7. So the toon brows vanish roughly
    /// a mip level earlier than the realistic ones — invisible in the editor (where you view the
    /// face close up at mip 0) but gone at headset viewing distance, and much sooner in the
    /// miniature scale condition. Inverting the remap keeps the two conditions matched.</summary>
    private static float RawAlphaCutoff(Material src, float cutoff)
    {
        float remap = src.HasProperty("_AlphaRemap") ? src.GetFloat("_AlphaRemap") : 1f;
        float power = src.HasProperty("_AlphaPower") ? src.GetFloat("_AlphaPower") : 1f;
        if (remap <= 0.0001f) remap = 1f;   // guard: a 0 remap would divide by zero in the source shader
        if (power <= 0.0001f) power = 1f;

        // a' = (a / remap)^power  =>  a = remap * cutoff^(1/power)
        return Mathf.Clamp01(remap * Mathf.Pow(Mathf.Clamp01(cutoff), 1f / power));
    }

    private static bool TryGetTexture(Material m, string[] names, out Texture tex, out string usedName)
    {
        foreach (var n in names)
            if (m.HasProperty(n))
            {
                var t = m.GetTexture(n);
                if (t != null) { tex = t; usedName = n; return true; }
            }
        tex = null; usedName = null;
        return false;
    }

    private static string ResolveToonFolder(Renderer[] renderers)
    {
        foreach (var r in renderers)
        {
            foreach (var m in SourceMaterials(r))
            {
                if (m == null) continue;
                string mp = AssetDatabase.GetAssetPath(m);
                if (string.IsNullOrEmpty(mp)) continue;
                string dir = Path.GetDirectoryName(mp).Replace('\\', '/');
                string toon = $"{dir}/{ToonSubfolder}";
                if (!AssetDatabase.IsValidFolder(toon))
                    AssetDatabase.CreateFolder(dir, ToonSubfolder);
                return toon;
            }
        }
        return null;
    }

    private static bool NameMatches(Material m, string[] fragments)
    {
        string n = m.name ?? "";
        string s = m.shader != null ? m.shader.name : "";
        foreach (var f in fragments)
            if (n.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
}
#endif
