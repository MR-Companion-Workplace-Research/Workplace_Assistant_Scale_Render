using UnityEditor;
using UnityEngine;

/// <summary>
/// Declutters the AgentVoiceController inspector. Every field here is one-time voice I/O tuning
/// (backend, connection refs, mic settings, audio playback, lip sync) that is auto-configured at
/// runtime by AvatarPlacer and never changed per trial, so it all sits under a collapsed
/// "Advanced setup" foldout.
/// </summary>
[CustomEditor(typeof(AgentVoiceController))]
public class AgentVoiceControllerEditor : Editor
{
    private static bool showAdvanced;

    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "Voice input/output is auto-configured at runtime (AvatarPlacer wires the audio source, " +
            "face mesh and connection). Nothing here changes per trial.",
            MessageType.Info);

        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced setup (voice tuning)", true);
        if (showAdvanced)
            DrawDefaultInspector();
    }
}
