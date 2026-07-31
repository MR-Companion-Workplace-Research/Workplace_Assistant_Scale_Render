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
                "DEV FALLBACK ONLY. In a real session every field above is overwritten at Awake:\n" +
                "  • participant id + conditions  ←  STUDY SETUP in Launch_Scene\n" +
                "  • task  ←  the participant's choice on the launch menu\n\n" +
                "These values are used only when MR_Scene is opened directly, so it stays " +
                "runnable on its own. To configure a participant, edit STUDY SETUP instead.",
                MessageType.Warning);
        }

        EditorGUILayout.HelpBox(
            $"Currently:  participant='{panel.participantId}'   condition='{panel.ResolvedConditionLabel}'\n" +
            $"Task key:  '{panel.TaskKey}'",
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
