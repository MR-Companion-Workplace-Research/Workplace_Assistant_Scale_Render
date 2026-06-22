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

    // Candidate property names for the same concept across the different source shaders.
    private static readonly string[] AlbedoNames = { "_DiffuseMap", "_MainTex", "_BaseMap", "_BaseColorMap" };
    private static readonly string[] TintNames = { "_DiffuseColor", "_BaseColor", "_Color" };
    private static readonly string[] CutoffNames = { "_AlphaClip", "_Cutoff" };

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

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Toonify: {converted} material slot(s) -> toon, {kept} kept realistic (eyes). " +
                  $"Toon materials in: {toonFolder}. Now save the prefab (Ctrl+S).");
        EditorUtility.DisplayDialog("Toonify",
            $"Done.\n\n{converted} slot(s) converted, {kept} kept realistic (eyes).\n\n" +
            "Now SAVE the prefab (Ctrl+S) to persist the overrides.", "OK");
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

        if (shader.name == "QuestToon/Hair")
            foreach (var k in CutoffNames)
                if (src.HasProperty(k))
                {
                    toon.SetFloat("_AlphaClip", src.GetFloat(k));
                    break;
                }
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
