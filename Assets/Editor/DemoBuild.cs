using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the PARTICIPANT PRACTICE app: a second, standalone APK that installs alongside the
/// study app on the same headset and rehearses the WHOLE session flow — launch the app, pick a
/// task on the menu, press START, then summon the SWOT sheet in MR — without exposing any task
/// content.
///
/// It ships TWO generated scenes, mirroring the study app's two:
///   DemoLaunch_Scene (from Launch_Scene) -> DemoScene (from MR_Scene)
///
/// WHY SEPARATE SCENES AND NOT A FLAG IN THE STUDY SCENES: the generated scenes contain no
/// avatar prefab references, no ElevenLabsConnection and no STUDY SETUP, so none of that is
/// compiled into the practice APK. A curious participant who sideloaded it off the headset
/// would find no agent id, no API key and no participant identities — which is not true of a
/// build that merely disables them at runtime. It also builds much smaller and faster without
/// the avatar assets.
///
/// WHY THEY ARE GENERATED RATHER THAN HAND-BUILT: the practice app is only useful if the menu,
/// the button and the panel behave EXACTLY as they do in the study, so a hand-maintained copy
/// that drifts defeats its purpose. Refreshing is one menu click, so after any change to
/// MR_Scene or Launch_Scene (panel offset, width, font, display seconds, the button mapping)
/// just re-run Create or Refresh Demo Scene.
///
/// KNOWN LIMIT: stripping is at the SCENE level. SwotPanel's script still holds the SWOT text as
/// string literals and that script ships in this APK, so "no task text in the practice build" is
/// true of the scenes, not of the compiled assembly. What the participant can SEE is blank
/// (demoBlankContent) and bare task letters — but do not treat the APK itself as scrubbed.
///
/// USAGE
///   1. Tools > Study > Create or Refresh Launch Scene  (once, if Launch_Scene does not exist)
///   2. Tools > Study > Create or Refresh Demo Scene    (re-run after editing either study scene)
///   3. Tools > Study > Build and Run Demo on Quest
/// </summary>
public static class DemoBuild
{
    // NOTE: the study scene was renamed SampleScene -> MR_Scene; this constant still pointed at
    // the old path, so Create or Refresh Demo Scene failed outright until it was corrected.
    private const string StudyScenePath = "Assets/Scenes/MR_Scene.unity";
    private const string DemoScenePath  = "Assets/Scenes/DemoScene.unity";

    // The practice app mirrors the real flow, so it gets its own copy of the launch menu that
    // loads DemoScene instead of MR_Scene. Generated from Launch_Scene for the same reason
    // DemoScene is generated from the study scene: a hand-maintained copy would drift, and a
    // practice run that behaves differently from the study is worse than no practice at all.
    private const string LaunchScenePath     = "Assets/Scenes/Launch_Scene.unity";
    private const string DemoLaunchScenePath = "Assets/Scenes/DemoLaunch_Scene.unity";

    // Distinct package name is what lets the practice app sit next to the study app on the
    // headset rather than overwriting it; the product name is the launcher label.
    private const string DemoProductName = "MR Demo";
    private const string DemoPackageName = "com.DefaultCompany.MRWorkplaceAssistant.Demo";

    private const string DemoBuildFolder = "Builds";

    /// <summary>
    /// Component types whose GameObject is stripped from the practice scene. These are exactly
    /// the objects that make up the study manipulation (avatar, agent voice) or that record
    /// data — a practice run must not spawn an avatar, must not reach ElevenLabs, and must not
    /// write a log file that could later be mistaken for a real session.
    /// </summary>
    private static readonly string[] StripComponentTypes =
    {
        "AvatarPlacer",            // spawns the avatar; also the ONLY caller of Connect()
        "StudyControlPanel",       // per-trial conditions; nothing left for it to drive
        "AgentVoiceController",    // mic capture + playback
        "ElevenLabsConnection",    // agent id / API key
        "RealtimeAPIConnection",   // OpenAI backend
        "ConversationLogger",      // keeps practice runs out of the data
        "ExperimenterRemoteTrigger",
    };

    /// <summary>
    /// Extra objects stripped by name: the avatar's gaze targets (meaningless with no avatar)
    /// and a leftover test cube.
    /// </summary>
    private static readonly string[] StripNames =
    {
        "female_real_EyeTarget", "female_real_HeadTarget",
        "male_real_EyeTarget",   "male_real_HeadTarget",
        "ExampleCubePrefab",
    };

    // =====================================================================
    //  1. Generate the practice scene from the study scene
    // =====================================================================

    [MenuItem("Tools/Study/Create or Refresh Demo Scene")]
    public static void CreateOrRefreshDemoScene()
    {
        if (!File.Exists(StudyScenePath))
        {
            EditorUtility.DisplayDialog("Demo scene",
                $"Could not find the study scene at:\n{StudyScenePath}", "OK");
            return;
        }

        bool exists = File.Exists(DemoScenePath);
        if (exists && !EditorUtility.DisplayDialog("Refresh demo scene?",
                $"{DemoScenePath} and {DemoLaunchScenePath} will be REGENERATED from " +
                $"{Path.GetFileName(StudyScenePath)} and {Path.GetFileName(LaunchScenePath)}.\n\n" +
                "Any edit made directly to those demo scenes will be lost.",
                "Regenerate", "Cancel"))
            return;

        // Save whatever the user is working on before we start opening scenes under them.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (exists) AssetDatabase.DeleteAsset(DemoScenePath);
        if (!AssetDatabase.CopyAsset(StudyScenePath, DemoScenePath))
        {
            EditorUtility.DisplayDialog("Demo scene",
                $"Copying {StudyScenePath} failed.", "OK");
            return;
        }
        AssetDatabase.Refresh();

        Scene scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);

        var removed = new List<string>();
        foreach (var typeName in StripComponentTypes) StripByComponent(scene, typeName, removed);
        foreach (var objName in StripNames)           StripByName(scene, objName, removed);

        // The one behavioural difference that remains: headers, no content.
        int panels = 0;
        foreach (var panel in Object.FindObjectsOfType<SwotPanel>())
        {
            panel.demoBlankContent = true;
            EditorUtility.SetDirty(panel);
            panels++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, DemoScenePath);

        string launchReport = CreateOrRefreshDemoLaunchScene();

        string report = $"Demo scene written to {DemoScenePath}\n\n" +
                        $"Stripped {removed.Count} object(s):\n  " + string.Join("\n  ", removed) +
                        $"\n\nSWOT panels set to blank content: {panels}" +
                        $"\n\n{launchReport}" +
                        $"\n\n{VerifyNoAvatarContent()}";
        Debug.Log("DemoBuild: " + report);

        if (panels == 0)
            Debug.LogWarning("DemoBuild: no SwotPanel found in the demo scene — the practice app " +
                             "would have nothing to show. Check that MR_Scene still has one.");

        // Leave the researcher back in the study scene: DemoScene is a generated artifact, and
        // editing it by mistake (thinking it is the study scene) would be silently thrown away
        // on the next refresh.
        EditorSceneManager.OpenScene(StudyScenePath, OpenSceneMode.Single);

        EditorUtility.DisplayDialog("Demo scene ready", report, "OK");
    }

    /// <summary>
    /// Generates the practice launch menu from Launch_Scene, so the participant rehearses the
    /// WHOLE process — pick a task, press START, land in MR, summon the sheet — rather than only
    /// the button press. The flow is the thing being practised, so it must match exactly.
    ///
    /// THE THREE DIFFERENCES FROM THE REAL MENU, and why each is required:
    ///  - loads DemoScene instead of MR_Scene (there is no study scene in this APK);
    ///  - shows 練習模式 instead of a participant id (this APK is built once and sideloaded to
    ///    every headset, so a baked-in id would be somebody else's);
    ///  - STUDY SETUP is deleted, so no participant's identity or condition is compiled in.
    ///
    /// The A..H buttons are kept, and stay bare letters exactly as in the study. They carry no
    /// task content — the labels are derived from the enum letter, never the scenario name — so
    /// practising the choice reveals nothing about the tasks themselves.
    /// </summary>
    private static string CreateOrRefreshDemoLaunchScene()
    {
        if (!File.Exists(LaunchScenePath))
        {
            string msg = $"NO PRACTICE MENU: {LaunchScenePath} does not exist. Run " +
                         "Tools > Study > Create or Refresh Launch Scene first, then regenerate " +
                         "the demo. The practice app will boot straight into DemoScene.";
            Debug.LogWarning("DemoBuild: " + msg);
            return msg;
        }

        if (File.Exists(DemoLaunchScenePath)) AssetDatabase.DeleteAsset(DemoLaunchScenePath);
        if (!AssetDatabase.CopyAsset(LaunchScenePath, DemoLaunchScenePath))
        {
            string msg = $"NO PRACTICE MENU: copying {LaunchScenePath} failed.";
            Debug.LogError("DemoBuild: " + msg);
            return msg;
        }
        AssetDatabase.Refresh();

        Scene scene = EditorSceneManager.OpenScene(DemoLaunchScenePath, OpenSceneMode.Single);

        int menus = 0;
        foreach (var menu in Object.FindObjectsOfType<LaunchMenu>())
        {
            menu.demoMode        = true;
            menu.setup           = null;
            menu.studySceneName  = Path.GetFileNameWithoutExtension(DemoScenePath);
            menu.instruction     = "練習模式。請任選一個任務，然後按下「開始」。";
            EditorUtility.SetDirty(menu);
            menus++;
        }

        // Delete STUDY SETUP outright rather than just unhooking it: it carries the current
        // participant's id and experimental condition, and this APK is sideloaded to every
        // headset and kept between sessions. Nulling the reference would still leave those
        // values sitting in the shipped scene file.
        foreach (var setup in Object.FindObjectsOfType<StudySetup>())
            Object.DestroyImmediate(setup.gameObject);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, DemoLaunchScenePath);

        if (menus == 0)
        {
            string msg = $"NO PRACTICE MENU: {LaunchScenePath} contains no LaunchMenu component.";
            Debug.LogWarning("DemoBuild: " + msg);
            return msg;
        }

        return $"Practice menu written to {DemoLaunchScenePath} " +
               $"(loads {Path.GetFileNameWithoutExtension(DemoScenePath)}, header reads PRACTICE, " +
               "no settings asset referenced).";
    }

    private static void StripByComponent(Scene scene, string typeName, List<string> removed)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;                                  // missing script
                if (mb.GetType().Name != typeName) continue;

                GameObject victim = mb.gameObject;
                if (IsEssential(victim))
                {
                    // Never delete the rig or the panel itself just because a study script was
                    // parked on it — drop the component alone and say so.
                    Debug.LogWarning($"DemoBuild: '{typeName}' sits on essential object " +
                                     $"'{victim.name}'. Removing the component only.");
                    Object.DestroyImmediate(mb);
                    removed.Add($"{victim.name} ({typeName} component only)");
                    continue;
                }

                removed.Add($"{victim.name} ({typeName})");
                Object.DestroyImmediate(victim);
                break; // the object is gone; re-scan this root from scratch on the next type
            }
        }
    }

    private static void StripByName(Scene scene, string objName, List<string> removed)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.name != objName) continue;
                if (IsEssential(t.gameObject)) continue;
                removed.Add($"{t.name} (by name)");
                Object.DestroyImmediate(t.gameObject);
                break;
            }
        }
    }

    /// <summary>
    /// Asset folders that must NOT end up in the practice APK. A dependency check is the honest
    /// test here: Unity bundles a scene's dependency closure, so "no avatar in the build" is
    /// exactly "no avatar asset in GetDependencies". Checking the object list instead would only
    /// prove the strip list ran — not that nothing else still references an avatar.
    /// </summary>
    private static readonly string[] ForbiddenDependencyFolders =
    {
        "Assets/Virtual Agents",   // the Reallusion avatars (female_real/toon, male_real/toon)
    };

    /// <summary>
    /// Confirms the generated scene pulls in no avatar assets, and reports its dependency
    /// footprint. Runs after the scene is saved, so it inspects what would actually be built.
    /// </summary>
    private static string VerifyNoAvatarContent() => VerifyNoAvatarContent(out _);

    private static string VerifyNoAvatarContent(out bool clean)
    {
        // Check EVERY scene that ships in the practice APK, not just DemoScene — otherwise the
        // guarantee only covers half the build.
        string[] shipped = File.Exists(DemoLaunchScenePath)
            ? new[] { DemoScenePath, DemoLaunchScenePath }
            : new[] { DemoScenePath };

        string[] deps = AssetDatabase.GetDependencies(shipped, true);

        var offenders = deps
            .Where(d => ForbiddenDependencyFolders.Any(
                       f => d.StartsWith(f, System.StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        clean = offenders.Length == 0;

        if (!clean)
        {
            string detail = "AVATAR ASSETS STILL REFERENCED — the demo would ship them:\n  " +
                            string.Join("\n  ", offenders.Take(10)) +
                            (offenders.Length > 10 ? $"\n  ...and {offenders.Length - 10} more" : "");
            Debug.LogError("DemoBuild: " + detail);
            return detail;
        }

        return $"Verified: no avatar assets referenced ({deps.Length} dependencies total).";
    }

    // =====================================================================
    //  Device preflight
    // =====================================================================

    /// <summary>
    /// Verifies a Quest is attached and authorized before a multi-minute build starts.
    /// Returns true to proceed. On anything unexpected it ASKS rather than blocks, so a quirk
    /// in adb discovery can never stop you from building.
    /// </summary>
    private static bool CheckDeviceConnected()
    {
        string adb = FindAdb();
        if (adb == null)
        {
            // Not fatal: Unity's own Build And Run finds its tooling independently of this check.
            return EditorUtility.DisplayDialog("Device check skipped",
                "Could not locate adb, so the headset could not be verified.\n\n" +
                "Build anyway? It will still try to install on a connected Quest.",
                "Build anyway", "Cancel");
        }

        string output;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(adb, "devices")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"DemoBuild: could not run adb ({e.Message}); skipping device check.");
            return true;
        }

        int ready = 0, notReady = 0;
        foreach (var rawLine in output.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices")) continue;
            string[] parts = line.Split('\t');
            if (parts.Length < 2) continue;
            if (parts[1].Trim() == "device") ready++;
            else notReady++;   // unauthorized / offline / no permissions
        }

        if (ready == 0)
        {
            EditorUtility.DisplayDialog("No Quest detected",
                (notReady > 0
                    ? "A device is attached but is not ready (unauthorized or offline).\n\n" +
                      "Put the headset on and accept the 'Allow USB debugging' prompt, then try again."
                    : "No headset found over adb.\n\n" +
                      "Check: USB cable connected, headset awake and worn, Developer Mode on, " +
                      "and 'Allow USB debugging' accepted.") +
                "\n\nadb output:\n" + output.Trim(),
                "OK");
            return false;
        }

        if (ready > 1)
        {
            // Unity installs to its selected Run Device; with several attached that may not be
            // the one you mean.
            return EditorUtility.DisplayDialog("Multiple devices",
                $"{ready} devices are connected. Unity will install to the one selected as " +
                "Run Device in Build Settings, which may not be the headset you intend.\n\nContinue?",
                "Continue", "Cancel");
        }

        Debug.Log("DemoBuild: one Quest connected and authorized — proceeding.");
        return true;
    }

    /// <summary>
    /// Locates adb. Prefers the SDK Unity is configured to use, then the SDK bundled with the
    /// editor's Android module (the usual case on this machine — there is no standalone SDK),
    /// then whatever is on PATH.
    /// </summary>
    private static string FindAdb()
    {
        var roots = new List<string>
        {
            EditorPrefs.GetString("AndroidSdkRoot"),
            Path.Combine(EditorApplication.applicationContentsPath,
                         "PlaybackEngines/AndroidPlayer/SDK"),
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            foreach (var exe in new[] { "adb.exe", "adb" })
            {
                string candidate = Path.Combine(root, Path.Combine("platform-tools", exe));
                if (File.Exists(candidate)) return candidate;
            }
        }

        string pathEnv = System.Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            foreach (var exe in new[] { "adb.exe", "adb" })
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* malformed PATH entry */ }
            }
        }

        return null;
    }

    /// <summary>The practice app is useless without the camera rig or the panel it summons.</summary>
    private static bool IsEssential(GameObject go) =>
        go.GetComponentInChildren<OVRCameraRig>(true) != null ||
        go.GetComponentInChildren<SwotPanel>(true) != null;

    // =====================================================================
    //  2. Build the practice APK under its own name / package
    // =====================================================================

    [MenuItem("Tools/Study/Build and Run Demo on Quest")]
    public static void BuildDemoApk()
    {
        if (!File.Exists(DemoScenePath))
        {
            EditorUtility.DisplayDialog("Build demo APK",
                "DemoScene does not exist yet.\n\nRun Tools > Study > Create or Refresh Demo Scene first.",
                "OK");
            return;
        }

        // Re-verify at BUILD time, not just at generation time: DemoScene is a file on disk and
        // may have been hand-edited since it was generated. This is the last point at which an
        // avatar could be caught before it reaches a participant's headset.
        string avatarCheck = VerifyNoAvatarContent(out bool clean);
        if (!clean && !EditorUtility.DisplayDialog("Avatar assets in the demo scene!",
                avatarCheck + "\n\nThe practice app is supposed to contain no avatar. " +
                "Re-run Create or Refresh Demo Scene.\n\nBuild anyway?",
                "Build anyway", "Cancel"))
            return;

        // Check the headset BEFORE building: a build takes minutes, and finding out at the
        // install step that the Quest was asleep, unplugged or unauthorized wastes all of it.
        if (!CheckDeviceConnected()) return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Directory.CreateDirectory(DemoBuildFolder);
        string apkPath = Path.Combine(DemoBuildFolder, "MRWorkplaceAssistant_Demo.apk");

        // Remember the study identity so a demo build can never leave the project configured
        // to overwrite the study app on the next normal build.
        string prevProduct = PlayerSettings.productName;
        string prevPackage = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);

        try
        {
            PlayerSettings.productName = DemoProductName;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, DemoPackageName);

            var options = new BuildPlayerOptions
            {
                // The practice menu FIRST — index 0 is what the app boots into — then the
                // practice scene it loads. Still ONLY practice scenes: no study scene, no
                // avatar, no agent. If the menu was never generated, boot straight into
                // DemoScene rather than shipping a build that boots to a missing scene.
                scenes           = File.Exists(DemoLaunchScenePath)
                                       ? new[] { DemoLaunchScenePath, DemoScenePath }
                                       : new[] { DemoScenePath },
                locationPathName = apkPath,
                target           = BuildTarget.Android,
                targetGroup      = BuildTargetGroup.Android,
                // AutoRunPlayer = Unity's "Build And Run": installs the APK on the connected
                // Quest and launches it. The APK is still written to apkPath, so you keep a
                // copy to sideload onto a second headset later.
                options          = BuildOptions.AutoRunPlayer,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                string msg = $"Installed and launched on the headset.\n\n" +
                             $"App name: {DemoProductName}\nPackage:  {DemoPackageName}\n" +
                             $"Size: {summary.totalSize / (1024 * 1024)} MB\n" +
                             $"APK kept at:\n{Path.GetFullPath(apkPath)}\n\n" +
                             $"Put the headset on — it should already be running. Find it later " +
                             $"under Library > Unknown Sources > {DemoProductName}.";
                Debug.Log("DemoBuild: " + msg);
                EditorUtility.DisplayDialog("Demo running on Quest", msg, "OK");
            }
            else
            {
                Debug.LogError($"DemoBuild: build {summary.result} with {summary.totalErrors} error(s).");
                EditorUtility.DisplayDialog("Demo APK failed",
                    $"Build {summary.result}. See the Console for details.", "OK");
            }
        }
        finally
        {
            // Restore in a finally so a failed or cancelled build still puts the study identity
            // back — otherwise the next study build would ship under the demo package name.
            PlayerSettings.productName = prevProduct;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, prevPackage);
            AssetDatabase.SaveAssets();
            Debug.Log($"DemoBuild: restored product name '{prevProduct}' / package '{prevPackage}'.");
        }
    }
}
