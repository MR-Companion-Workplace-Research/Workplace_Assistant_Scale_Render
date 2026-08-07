using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Generates Launch_Scene: the passthrough-MR task-picker the participant sees before the
/// study scene. See <see cref="LaunchMenu"/> for what it shows and deliberately hides.
///
/// WHY GENERATED RATHER THAN HAND-BUILT: the scene is almost entirely a stock OVRCameraRig
/// plus one script — the interesting part (the panel) is built at runtime by LaunchMenu, the
/// same way TaskPanel builds itself. Generating it means the passthrough setup (underlay layer,
/// transparent camera clear, floor-level tracking) is applied identically every time instead of
/// being a checklist someone has to remember, and re-running fixes a scene that got broken.
///
/// It also deliberately does NOT copy the old Start_Scene: that scene belongs to the inherited
/// project, carries third-party branding this study has no affiliation with, and is a flat
/// screen-space canvas that would not render on the headset at all.
///
/// Regenerating PRESERVES the STUDY SETUP config, so a routine refresh (font, wording) never
/// silently resets the current participant's id and conditions to defaults.
///
/// USAGE
///   1. Tools > Study > Create or Refresh Launch Scene   (once, or after changing the menu)
///   2. Per participant: open Launch_Scene, select STUDY SETUP, set the id and conditions.
///   3. Build. The participant picks their task in-headset; no rebuild between tasks.
/// </summary>
public static class LaunchSceneBuild
{
    private const string LaunchScenePath = "Assets/Scenes/Launch_Scene.unity";
    private const string StudyScenePath  = "Assets/Scenes/MR_Scene.unity";
    private const string RigPrefabPath   = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

    // The menu is in Traditional Chinese, and the default TMP font (LiberationSans) has no CJK
    // glyphs — every character would render as a blank box. This is the same asset TaskPanel
    // uses, so the two panels look like one system. Wired automatically because forgetting it
    // produces a menu that is visibly broken only once it is on the headset.
    private const string CjkFontPath = "Assets/Fonts/msjh SDF.asset";

    [MenuItem("Tools/Study/Create or Refresh Launch Scene")]
    public static void CreateOrRefreshLaunchScene()
    {
        // Never silently discard whatever the researcher had open.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
        if (rigPrefab == null)
        {
            EditorUtility.DisplayDialog("Launch scene",
                $"Could not find the OVRCameraRig prefab at:\n{RigPrefabPath}\n\n" +
                "Is the Meta XR Core SDK still installed?", "OK");
            return;
        }

        // Rescue the current participant's config BEFORE the scene is replaced. Regenerating is
        // routine (font changes, text changes), and silently resetting the participant id and
        // conditions to defaults each time would quietly corrupt a session's data.
        StudyConfig preserved = ReadExistingConfig();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- Camera rig, configured for passthrough MR ----
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
        rig.name = "OVRCameraRig";
        ConfigurePassthrough(rig);

        // ---- The one object the researcher edits ----
        // Preserved across a refresh: this holds the current participant's id and conditions, and
        // silently resetting them to defaults on every regenerate would be a data-integrity bug.
        var setupGO = new GameObject("STUDY SETUP");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(setupGO, scene);
        var setup = setupGO.AddComponent<StudySetup>();
        if (preserved != null)
        {
            setup.config = preserved;
            Debug.Log($"LaunchSceneBuild: carried over STUDY SETUP config " +
                      $"(participant='{preserved.participantId}').");
        }

        // ---- The menu ----
        var menuGO = new GameObject("LAUNCH MENU");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(menuGO, scene);
        var menu = menuGO.AddComponent<LaunchMenu>();
        menu.setup = setup;
        menu.studySceneName = Path.GetFileNameWithoutExtension(StudyScenePath);

        var cjkFont = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(CjkFontPath);
        if (cjkFont != null)
        {
            menu.fontOverride = cjkFont;
        }
        else
        {
            Debug.LogError($"LaunchSceneBuild: CJK font not found at {CjkFontPath}. The menu text " +
                           "is Traditional Chinese, so without it every character on the launch " +
                           "menu renders as a blank box. Assign Font Override manually.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(LaunchScenePath));
        EditorSceneManager.SaveScene(scene, LaunchScenePath);

        UpdateBuildSettings();

        Selection.activeGameObject = setupGO;

        EditorUtility.DisplayDialog("Launch scene",
            $"Created {LaunchScenePath}.\n\n" +
            $"Build Settings now boot from Launch_Scene, then load " +
            $"{Path.GetFileNameWithoutExtension(StudyScenePath)}.\n\n" +
            (preserved != null
                ? $"Carried over the existing STUDY SETUP config (participant " +
                  $"'{preserved.participantId}').\n\n"
                : "") +
            "PER PARTICIPANT: select STUDY SETUP (now selected in the hierarchy) and set the " +
            "participant id and conditions there. Nothing else needs editing — MR_Scene picks " +
            "them up automatically, and the participant picks the task in-headset.",
            "OK");
    }

    /// <summary>
    /// Reads the STUDY SETUP config out of the existing Launch_Scene, if there is one, so a
    /// refresh does not throw away the participant id and conditions currently set up. Opens the
    /// scene additively and closes it again, leaving whatever the researcher had open untouched.
    /// </summary>
    private static StudyConfig ReadExistingConfig()
    {
        if (!File.Exists(LaunchScenePath)) return null;

        var scene = EditorSceneManager.OpenScene(LaunchScenePath, OpenSceneMode.Additive);
        StudyConfig found = null;
        try
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var setup = root.GetComponentInChildren<StudySetup>(true);
                if (setup != null && setup.config != null) { found = setup.config.Clone(); break; }
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
        return found;
    }


    /// <summary>
    /// Passthrough setup, mirroring what MR_Scene already does so the participant does not see
    /// the room change appearance across the scene load: passthrough enabled on OVRManager, an
    /// Underlay passthrough layer, floor-level tracking, and cameras cleared to TRANSPARENT
    /// black — an opaque clear colour would paint over the passthrough underlay and leave the
    /// participant staring at a black void with a menu in it.
    /// </summary>
    private static void ConfigurePassthrough(GameObject rig)
    {
        var manager = rig.GetComponentInChildren<OVRManager>(true);
        if (manager != null)
        {
            manager.isInsightPassthroughEnabled = true;
            // Tracking origin is deliberately NOT set here: trackingOriginType is a runtime
            // property whose setter talks to OVRPlugin, so assigning it in edit mode would not
            // reliably persist to the serialized field. The prefab already defaults to
            // FloorLevel, which is what MR_Scene uses (_trackingOriginType: 1).
        }
        else
        {
            Debug.LogWarning("LaunchSceneBuild: no OVRManager on the rig — passthrough not enabled.");
        }

        var layer = rig.GetComponent<OVRPassthroughLayer>() ?? rig.AddComponent<OVRPassthroughLayer>();
        layer.projectionSurfaceType = OVRPassthroughLayer.ProjectionSurfaceType.Reconstructed;
        layer.overlayType = OVROverlay.OverlayType.Underlay;

        foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }
    }


    /// <summary>
    /// Put Launch_Scene first (index 0 is what the app boots into) and the study scene second,
    /// both enabled. Everything else already listed is preserved but left disabled — an enabled
    /// stray scene would bloat the APK with assets from the inherited project.
    /// </summary>
    private static void UpdateBuildSettings()
    {
        var studyGuid = AssetDatabase.AssetPathToGUID(StudyScenePath);

        var ordered = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(LaunchScenePath, true),
        };

        if (string.IsNullOrEmpty(studyGuid))
            Debug.LogWarning($"LaunchSceneBuild: {StudyScenePath} not found — START will fail to load it.");
        else
            ordered.Add(new EditorBuildSettingsScene(StudyScenePath, true));

        foreach (var s in EditorBuildSettings.scenes)
        {
            if (s.path == LaunchScenePath || s.path == StudyScenePath) continue;
            ordered.Add(new EditorBuildSettingsScene(s.path, false));
        }

        EditorBuildSettings.scenes = ordered.ToArray();
        Debug.Log("LaunchSceneBuild: build settings updated — boots into Launch_Scene, then " +
                  Path.GetFileNameWithoutExtension(StudyScenePath) + ".");
    }
}
