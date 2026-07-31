using UnityEditor;
using UnityEngine;

/// <summary>
/// Declutters the ConversationLogger inspector. Participant ID / Condition Label now live on the
/// StudyControlPanel (the logger reads them from there), so this inspector shows just the live log
/// status; the capture options sit under a collapsed "Advanced setup" foldout.
/// </summary>
[CustomEditor(typeof(ConversationLogger))]
public class ConversationLoggerEditor : Editor
{
    private static bool showAdvanced;

    public override void OnInspectorGUI()
    {
        var logger = (ConversationLogger)target;

        EditorGUILayout.HelpBox(
            "Participant ID / Condition Label are set on the STUDY CONTROL panel — this component " +
            "just records the session to a file on the headset.",
            MessageType.Info);

        if (Application.isPlaying)
        {
            string path = logger.CurrentLogPath;
            EditorGUILayout.LabelField("Log file", string.IsNullOrEmpty(path) ? "(not open yet)" : path);
        }

        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced setup (capture options)", true);
        if (showAdvanced)
            DrawDefaultInspector();
    }
}
