using UnityEditor;
using UnityEngine;

/// <summary>
/// Declutters the ElevenLabsConnection (AgentVoice) inspector. The task is now chosen on the
/// StudyControlPanel and pushed into taskKey at runtime, so it is shown here read-only for
/// confirmation only. The one-time agent config (agent id, api key, sample rates, interruption)
/// and the auto-filled taskContext sit under a collapsed "Advanced setup" foldout.
/// </summary>
[CustomEditor(typeof(ElevenLabsConnection))]
public class ElevenLabsConnectionEditor : Editor
{
    private static bool showAdvanced;

    private static readonly string[] AgentConfig =
    {
        "agentId", "apiKey", "inputSampleRate", "outputSampleRate", "allowInterruption", "taskContext",
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Task is selected on the STUDY CONTROL panel (A..H) and pushed here at runtime. " +
            "The fields below are one-time agent config — set them once.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(true))
        {
            var taskKey = serializedObject.FindProperty("taskKey");
            if (taskKey != null)
                EditorGUILayout.PropertyField(taskKey, new GUIContent("Task Key (driven)"));
        }

        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced setup (agent config)", true);
        if (showAdvanced)
        {
            EditorGUI.indentLevel++;
            foreach (var name in AgentConfig)
            {
                var prop = serializedObject.FindProperty(name);
                if (prop != null) EditorGUILayout.PropertyField(prop, true);
            }
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
