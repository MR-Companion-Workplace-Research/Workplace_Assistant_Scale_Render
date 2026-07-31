using UnityEngine;

/// <summary>
/// THE ONE OBJECT THE RESEARCHER EDITS. Put this on an object named "STUDY SETUP" in
/// Launch_Scene; set the participant id and the visual conditions here before each participant,
/// and nothing else in the project needs touching.
///
/// WHY IT LIVES IN Launch_Scene AND NOT MR_Scene: the launch menu shows the participant id so
/// the researcher can confirm the right build is on the headset without taking it off. Launch
/// and MR are separate scenes, so while the menu is running MR_Scene is not loaded and its
/// STUDY CONTROL object does not exist yet — the menu physically cannot read an id off it.
/// Putting the config in the scene that boots first means one place to edit AND a menu that can
/// display it, with no second copy to drift out of sync.
///
/// HOW IT REACHES THE AVATAR: this component copies its config into <see cref="StudySession"/>
/// in Awake. <see cref="StudyControlPanel"/> in MR_Scene reads it back after the scene load and
/// applies it to AvatarPlacer / ElevenLabsConnection exactly as it always did. The task travels
/// the same way, set by <see cref="LaunchMenu"/> when the participant presses START.
///
/// OPENING MR_Scene DIRECTLY still works: with no session there is nothing to read, so
/// StudyControlPanel falls back to its own inspector values. Ordinary dev testing is unaffected.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-600)] // publish before LaunchMenu (and anything else) reads the session
public class StudySetup : MonoBehaviour
{
    [Tooltip("Set these once per participant, before building. The task is NOT here — the " +
             "participant picks it on the menu each run, which is what removes the rebuild " +
             "between trials.")]
    public StudyConfig config = new StudyConfig();

    private void Awake()
    {
        Publish();
    }

    /// <summary>
    /// Hand the config to the session so it survives the load into MR_Scene. A clone is stored
    /// so nothing can mutate it after the fact — this object is destroyed with Launch_Scene.
    /// </summary>
    public void Publish()
    {
        StudySession.SetConfig(config.Clone());
        Debug.Log($"StudySetup: published — participant='{config.participantId}', " +
                  $"condition='{config.ResolvedConditionLabel}', avatar={config.avatarVariant}, " +
                  $"scale={config.scaleCondition} ({config.avatarScale}).");
    }
}
