using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for the StudyControlPanel. Draws the default grouped fields, shows the resolved
/// task key + condition label so the researcher can confirm what will be logged, and adds an
/// "Apply To Scene Now" button that pushes the values into the other components in edit mode
/// (handy for previewing; at runtime the panel applies itself in Awake anyway).
/// </summary>
[CustomEditor(typeof(StudyControlPanel))]
public class StudyControlPanelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var panel = (StudyControlPanel)target;

        DrawDefaultInspector();

        EditorGUILayout.Space();

        // Every field above is a DEV FALLBACK in a real session — at Awake they are overwritten
        // by STUDY SETUP and the launch menu. Saying so here stops anyone editing this object
        // per participant and wondering why it had no effect.
        if (Application.isPlaying && panel.DrivenBySession)
        {
            EditorGUILayout.HelpBox(
                "Driven by the session — these values came from STUDY SETUP in Launch_Scene and " +
                "the participant's menu choice.",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "DEV FALLBACK ONLY for the identity and condition fields — in a real session " +
                "those are overwritten at Awake by STUDY SETUP in Launch_Scene, and are used " +
                "here only when MR_Scene is opened directly.\n\n" +
                "THE TWO TASK FIELDS ARE NOT A FALLBACK: they are the real setting, and they are " +
                "baked in at build time. To configure a participant's identity or conditions, " +
                "edit STUDY SETUP instead.",
                MessageType.Warning);
        }

        EditorGUILayout.HelpBox(
            $"Currently:  participant='{panel.participantId}'   condition='{panel.ResolvedConditionLabel}'\n" +
            $"Tasks:  1st '{panel.FirstTaskKey}'  →  2nd '{panel.SecondTaskKey}'",
            MessageType.None);

        if (GUILayout.Button("Apply To Scene Now"))
        {
            panel.Apply();
            if (panel.placer != null) EditorUtility.SetDirty(panel.placer);
            if (panel.connection != null) EditorUtility.SetDirty(panel.connection);
            if (!Application.isPlaying)
            {
                var scene = panel.gameObject.scene;
                if (scene.IsValid())
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }
}
