using UnityEditor;
using UnityEngine;

/// <summary>
/// Declutters the AvatarPlacer inspector for the study workflow.
///
/// The per-trial conditions (avatar variant, scale, placement, surface contact) are now driven
/// by the StudyControlPanel, so they are hidden here to avoid two-places-to-edit confusion.
/// Only the ONE-TIME setup (prefab slots, animators, camera rig, face mesh, lip sync) remains,
/// tucked under a collapsed "Advanced setup" foldout. No fields are removed — everything is
/// still serialized, just not shown by default.
/// </summary>
[CustomEditor(typeof(AvatarPlacer))]
public class AvatarPlacerEditor : Editor
{
    private static bool showAdvanced;

    // One-time setup fields, assigned once when the scene is built and never touched per trial.
    private static readonly string[] OneTimeSetup =
    {
        "femaleRealPrefab", "femaleToonPrefab", "maleRealPrefab", "maleToonPrefab",
        "avatarPrefab", "cameraRig",
        "humanSizedAnimator", "miniatureAnimator",
        "faceMeshName", "mouthBlendShapeName",
        "useSalsaLipSync",
        "toonKeyLocalDirection",   // toon band key light — tuned once for the look, not per trial
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Per-trial conditions (avatar variant, scale, placement, surface contact) are set on " +
            "the STUDY CONTROL panel and applied to this component at runtime.\n\n" +
            "Only the one-time setup below lives here.",
            MessageType.Info);

        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced setup (assigned once)", true);
        if (showAdvanced)
        {
            EditorGUI.indentLevel++;
            foreach (var name in OneTimeSetup)
            {
                var prop = serializedObject.FindProperty(name);
                if (prop != null) EditorGUILayout.PropertyField(prop, true);
            }
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
