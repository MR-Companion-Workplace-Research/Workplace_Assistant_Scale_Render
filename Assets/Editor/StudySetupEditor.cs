using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for STUDY SETUP — the one object the researcher edits per participant.
///
/// WHY A CUSTOM EDITOR FOR SOMETHING THIS SMALL: the fields live inside a nested StudyConfig
/// object (declared once, so StudySetup / StudySession / StudyControlPanel cannot drift apart),
/// and Unity draws a nested [Serializable] class as a collapsed foldout. That would put an extra
/// click and a layer of indirection in front of the fields this whole design exists to make
/// obvious. Drawing the children inline gives a flat inspector — the nesting stays an
/// implementation detail rather than something the researcher has to navigate.
/// </summary>
[CustomEditor(typeof(StudySetup))]
public class StudySetupEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var setup = (StudySetup)target;

        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Set these once per participant, before building.\n\n" +
            "The TASK is not here — the participant picks it on the menu each run, which is what " +
            "removes the rebuild between trials.",
            MessageType.Info);

        // Draw the nested config's children inline, so the inspector reads as a flat list.
        var config = serializedObject.FindProperty("config");
        if (config != null)
        {
            var end = config.GetEndProperty();
            var it = config.Copy();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren) && !SerializedProperty.EqualContents(it, end))
            {
                EditorGUILayout.PropertyField(it, true);
                enterChildren = false;
            }
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            $"Will log as:  participant='{setup.config.participantId}'   " +
            $"condition='{setup.config.ResolvedConditionLabel}'",
            MessageType.None);
    }
}
