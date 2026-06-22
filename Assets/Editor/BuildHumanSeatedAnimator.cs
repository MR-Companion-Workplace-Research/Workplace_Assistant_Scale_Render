#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Rebuilds the seated (human-sized) animator controller via the AnimatorController API,
/// so it serialises correctly (hand-authored .controller YAML kept importing with empty
/// state machines). Operates on the EXISTING asset in place, so its GUID — and the
/// prefab reference to it — is preserved.
///
/// Result (matches the mini_animator gesture set, but seated):
///   Layer 0 "Base Layer"  (no mask)        -> Sitting Idle only (holds the seated legs).
///   Layer 1 "Upper Body"  (UpperBodyMask)  -> Sitting Idle + Waving / Gesturing /
///                                             Head Nod / Transition, same triggers.
/// Only the upper body is overridden, so gestures play while she stays seated.
///
/// Run: Tools > Study > Rebuild Human Seated Animator.
/// </summary>
public static class BuildHumanSeatedAnimator
{
    private const string ControllerPath = "Assets/Animations/Animators/human_animator 1.controller";

    // Resolved by GUID so renames/moves don't break this.
    private const string SittingGuid    = "46c161ae2e2a1f54c820ec25309fcad3"; // my_sitting.anim
    private const string WaveGuid       = "7865ac161c575455dbed28dad1961e06"; // wave.fbx
    private const string GestureGuid    = "19328664f435f4fdc9392a025581ae34"; // gesture clip
    private const string HeadNodGuid    = "5cb5fe1116dd047e0a87e98824f0140f"; // head_nod.fbx
    private const string TransitionGuid = "6a52c5a3a4dc24ede989d724d751917e"; // transition_idle.fbx
    private const string MaskGuid       = "5ac48ee6773193346af94035c53841db"; // UpperBodyMask.mask

    [MenuItem("Tools/Study/Rebuild Human Seated Animator")]
    private static void Build()
    {
        var sitting    = ClipByGuid(SittingGuid);
        var wave       = ClipByGuid(WaveGuid);
        var gesture    = ClipByGuid(GestureGuid);
        var headnod    = ClipByGuid(HeadNodGuid);
        var transition = ClipByGuid(TransitionGuid);
        var mask       = AssetDatabase.LoadAssetAtPath<AvatarMask>(AssetDatabase.GUIDToAssetPath(MaskGuid));

        if (sitting == null || wave == null || gesture == null || headnod == null || transition == null)
        { Fail("One or more animation clips could not be loaded (check the GUIDs / that the FBX imported)."); return; }
        if (mask == null) { Fail("UpperBodyMask.mask not found."); return; }

        // Delete any existing controller (the hand-authored one imported corrupt: orphaned
        // sub-assets + broken state refs). Recreate fresh so the asset is 100% API-authored.
        // NOTE: this assigns a NEW GUID — reassign the controller on the human prefab's Animator.
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        // Parameters.
        controller.AddParameter("WaveTrigger",    AnimatorControllerParameterType.Trigger);
        controller.AddParameter("GestureTrigger", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("HeadNodTrigger", AnimatorControllerParameterType.Trigger);

        // ---- Layer 0: Base (no mask) — seated idle only ----
        // CreateAnimatorControllerAtPath already gives us a "Base Layer" at index 0.
        var baseSM  = controller.layers[0].stateMachine;
        var baseSit = baseSM.AddState("Sitting Idle");
        baseSit.motion = sitting; baseSit.writeDefaultValues = true;
        baseSM.defaultState = baseSit;

        // ---- Layer 1: Upper Body (masked, override) — gestures ----
        controller.AddLayer("Upper Body");
        var layers = controller.layers;                 // getter returns a copy: edit then reassign
        layers[1].avatarMask   = mask;
        layers[1].defaultWeight = 1f;
        layers[1].blendingMode  = AnimatorLayerBlendingMode.Override;
        controller.layers = layers;

        var upSM   = controller.layers[1].stateMachine;
        var upSit  = upSM.AddState("Sitting Idle");         upSit.motion  = sitting;    upSit.writeDefaultValues  = true;
        var upWave = upSM.AddState("Waving");               upWave.motion = wave;       upWave.writeDefaultValues = true;
        var upGest = upSM.AddState("Gesturing");            upGest.motion = gesture;    upGest.writeDefaultValues = true; upGest.speed = 1.1f;
        var upNod  = upSM.AddState("Head Nod");             upNod.motion  = headnod;    upNod.writeDefaultValues  = true;
        var upTran = upSM.AddState("Transition Animation"); upTran.motion = transition; upTran.writeDefaultValues = true; upTran.speed = 1.1f;
        upSM.defaultState = upSit;

        // Trigger transitions out of the seated idle (interrupt current, no exit-time gate).
        Trig(upSit.AddTransition(upWave), "WaveTrigger",    0.5f, 0.97f);
        Trig(upSit.AddTransition(upGest), "GestureTrigger", 0.5f, 0.9027388f);
        Trig(upSit.AddTransition(upNod),  "HeadNodTrigger", 1.0f, 0.9f);

        // Return-to-idle on exit time.
        Exit(upWave.AddTransition(upSit),  0.25f, 0.925f);
        Exit(upGest.AddTransition(upTran), 1.5f,  0.5f);
        Exit(upNod.AddTransition(upSit),   0.25f, 0.75f);
        Exit(upTran.AddTransition(upSit),  1.0f,  1.0f);
        Trig(upTran.AddTransition(upGest), "GestureTrigger", 0.5f, 0.8484849f);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = controller;
        EditorGUIUtility.PingObject(controller);

        Debug.Log("Rebuilt 'human_animator 1' from scratch: Base Layer (Sitting Idle) + masked Upper Body " +
                  "(Waving / Gesturing / Head Nod / Transition). NEW GUID — reassign it on the human prefab's Animator.");
        EditorUtility.DisplayDialog("Seated Animator",
            "Rebuilt human_animator 1 from scratch (clean).\n\n" +
            "Base Layer = Sitting Idle.\n" +
            "Upper Body (masked, override) = Waving / Gesturing / Head Nod / Transition.\n\n" +
            "IMPORTANT: the controller has a NEW GUID, so assign it to the human-sized " +
            "prefab's Animator > Controller field again.", "OK");
    }

    private static void Trig(AnimatorStateTransition t, string trigger, float duration, float exitTime)
    {
        t.hasExitTime = false;
        t.exitTime = exitTime;
        t.hasFixedDuration = true;
        t.duration = duration;
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    private static void Exit(AnimatorStateTransition t, float duration, float exitTime)
    {
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.hasFixedDuration = true;
        t.duration = duration;
    }

    /// <summary>Loads the AnimationClip for a GUID, whether it's a standalone .anim
    /// (main asset) or a clip embedded in an FBX (asset representation).</summary>
    private static AnimationClip ClipByGuid(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return null;

        var main = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (main != null) return main;

        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        return null;
    }

    private static void Fail(string msg)
    {
        Debug.LogError("Rebuild Human Seated Animator: " + msg);
        EditorUtility.DisplayDialog("Seated Animator", msg, "OK");
    }
}
#endif
