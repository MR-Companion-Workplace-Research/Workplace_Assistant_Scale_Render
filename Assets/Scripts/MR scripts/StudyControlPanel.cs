using UnityEngine;
using Meta.XR.MRUtilityKit;

/// <summary>
/// SINGLE per-trial control surface for the Scale x Render Style study. Put this on ONE
/// dedicated scene object (e.g. "STUDY CONTROL"); it is the only thing a researcher edits
/// between participants. It gathers every per-trial knob in one place and pushes them into
/// the components that actually consume them, so nothing has to be set on the avatar or the
/// AgentVoice object anymore:
///
///   * AvatarPlacer          <- avatar variant, scale condition/value, placement, surface contact
///   * ElevenLabsConnection  <- the session's TWO tasks (firstTaskKey / secondTaskKey), each
///                              selected here as a P1..P4 / I1..I4 dropdown
///   * ConversationLogger    -> READS participantId / conditionLabel from here (it pulls, so no
///                              ordering race — see ConversationLogger.Start)
///
/// WHY PUSH IN Awake: AvatarPlacer reads its fields in the MRUK OnSceneLoaded callback and
/// ElevenLabsConnection reads its task keys inside Connect() (fired from AvatarPlacer.SetupVoice) —
/// both happen well AFTER every Awake/Start, so applying once in Awake (with a very early
/// execution order) guarantees the values are in place before anything reads them. The editor
/// also exposes an "Apply To Scene Now" button for edit-time preview.
///
/// IN A REAL SESSION YOU DO NOT EDIT THIS OBJECT. Everything arrives via StudySession:
///   * participant id + visual conditions <- STUDY SETUP in Launch_Scene (the one object the
///     researcher edits, once per participant). It lives there because the launch menu displays
///     the id, and while the menu runs MR_Scene is not loaded — so the config has to originate
///     in the scene that boots first.
///
/// THE TASKS ARE NOT PART OF THAT. A session runs a FIXED PAIR of tasks (First then Second)
/// chosen here in the inspector, so changing the pair means an Android rebuild — the launch
/// menu used to choose the task at runtime precisely to avoid that, and no longer does. If
/// mid-session task changes are ever wanted back, the pair has to travel via StudySession and
/// be pickable on the menu again.
///
/// The config is pulled in Awake, BEFORE Apply(), so the rest of this class is unchanged and
/// still the only thing that talks to AvatarPlacer / ElevenLabsConnection.
///
/// The inspector fields below are the DEV FALLBACK: with no session (i.e. MR_Scene opened
/// directly in the editor) the scene runs entirely off them, so ordinary dev testing never has
/// to go through the launch menu.
///
/// SETUP (one time):
/// 1. Create an empty GameObject, name it "STUDY CONTROL", and add this component.
/// 2. Leave the Wiring refs empty — they auto-find the AvatarPlacer / ElevenLabsConnection in
///    the scene. Assign them explicitly only if you have more than one of either.
/// That's it. Per participant, edit STUDY SETUP in Launch_Scene instead.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)] // apply before AvatarPlacer / ConversationLogger read anything
public class StudyControlPanel : MonoBehaviour
{
    /// <summary>
    /// The eight study tasks, in fixed order: four SWOT proposals (P1..P4, swot_chinese.md) then
    /// four incident reports (I1..I4, incident_chinese.md). The enum name's prefix IS the taskKey
    /// string used by ElevenLabsConnection.TASK_BLOCKS and TaskPanel.
    ///
    /// The earlier low_A..low_D small-money tasks were dropped when the task set was revised, so
    /// every task here is one of the two current report types. Anything that hard-codes the old
    /// keys (a saved override, an analysis script) will no longer resolve.
    /// </summary>
    public enum StudyTask
    {
        P1_ClientOnboarding, // P1 — SWOT proposal
        P2_ExpansionPilot,   // P2 — SWOT proposal
        P3_SoftwareUpgrade,  // P3 — SWOT proposal
        P4_Partnership,      // P4 — SWOT proposal
        I1_PackagingInjury,  // I1 — incident report
        I2_Ransomware,       // I2 — incident report
        I3_ChemicalSpill,    // I3 — incident report
        I4_DataBreach,       // I4 — incident report
    }

    [Header("Session Identity (written into every log)")]
    [Tooltip("DEV FALLBACK. In a real session this is OVERWRITTEN at Awake by what the researcher " +
             "set on STUDY SETUP in Launch_Scene. It is used only when MR_Scene is opened " +
             "directly, so the scene stays runnable on its own.")]
    public string participantId = "P00";

    [Tooltip("Label for the experimental cell, e.g. 'human_female_real'. Leave EMPTY to " +
             "auto-derive it from the Scale + Avatar conditions below (e.g. 'mini_femaletoon').")]
    public string conditionLabel = "";

    [Header("Tasks — TWO per session, in this order")]
    [Tooltip("The task the participant does FIRST. Pushed to ElevenLabsConnection.firstTaskKey, " +
             "which feeds the agent's {{first_task_context}} and is the sheet TaskPanel shows " +
             "until the agent hands over.")]
    public StudyTask firstTask = StudyTask.P1_ClientOnboarding;

    [Tooltip("The task the participant does SECOND. Pushed to ElevenLabsConnection.secondTaskKey " +
             "-> {{second_task_context}}. Both tasks are sent to the agent at the START of the " +
             "one conversation; the agent moves the participant from one to the other, and the " +
             "sheet follows when it says its scripted hand-over line.\n\n" +
             "Set this to a DIFFERENT task from the first — the same task twice is warned about " +
             "but not prevented.")]
    public StudyTask secondTask = StudyTask.I1_PackagingInjury;

    [Header("Study Condition — Avatar")]
    [Tooltip("Which avatar variant this session uses (gender x render style). The matching prefab " +
             "slot on AvatarPlacer is spawned.")]
    public AvatarPlacer.AvatarVariant avatarVariant = AvatarPlacer.AvatarVariant.FemaleReal;

    [Header("Study Condition — Scale")]
    [Tooltip("Which Scale condition this session runs. Human Sized attaches the human-sized animator; " +
             "Miniature attaches the miniature animator. Keep this consistent with Avatar Scale below " +
             "(a mismatch is warned about at spawn).")]
    public AvatarPlacer.ScaleCondition scaleCondition = AvatarPlacer.ScaleCondition.HumanSized;

    [Tooltip("Uniform scale of the spawned avatar. 1 = human-sized, ~0.2 = miniature.")]
    public float avatarScale = 1f;

    [Header("Placement Target")]
    [Tooltip("Which MR scene anchor to spawn the avatar on (e.g. TABLE for a desk, COUCH for a sofa). " +
             "The first room anchor matching this label is used.")]
    public MRUKAnchor.SceneLabels spawnAnchorLabel = MRUKAnchor.SceneLabels.TABLE;

    [Header("Placement Settings")]
    [Tooltip("Offset from the anchor center (world space). X = left/right, Z = forward/back. " +
             "Y is ignored when Snap To Surface is on — use Contact Height Adjust instead.")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("If true, the avatar rotates to face the user's head on spawn.")]
    public bool faceUser = true;

    [Tooltip("If true, facing only rotates on the Y axis (keeps the avatar upright).")]
    public bool constrainToYAxis = true;

    [Header("Surface Contact")]
    [Tooltip("If true, the avatar is vertically snapped so its contact point rests ON the anchor " +
             "surface after scaling (scale-independent placement).")]
    public bool snapToSurface = true;

    [Tooltip("Which point rests on the surface. FeetOnSurface = standing on a TABLE (miniature); " +
             "SeatedOnSurface = hips on the cushion of a COUCH (human-sized).")]
    public AvatarPlacer.SurfaceContact surfaceContact = AvatarPlacer.SurfaceContact.FeetOnSurface;

    [Tooltip("Vertical fine-tune of the contact point, in meters (after scaling). A small NEGATIVE " +
             "value sinks a seated avatar into the cushion so it doesn't hover.")]
    public float contactHeightAdjust = 0f;

    [Header("Wiring (auto-found in the scene if left empty)")]
    [Tooltip("The avatar placer this panel drives. Auto-found if empty.")]
    public AvatarPlacer placer;

    [Tooltip("The ElevenLabs connection this panel pushes the task to. Auto-found if empty.")]
    public ElevenLabsConnection connection;

    // ---- Derived values other components read directly ----

    /// <summary>The taskKey string (P1..P4, I1..I4) for the first task.</summary>
    public string FirstTaskKey => ToTaskKey(firstTask);

    /// <summary>The taskKey string for the second task.</summary>
    public string SecondTaskKey => ToTaskKey(secondTask);

    /// <summary>Condition label to log: the explicit one if set, else auto-derived from the conditions.</summary>
    public string ResolvedConditionLabel =>
        string.IsNullOrEmpty(conditionLabel) ? AutoLabel() : conditionLabel;

    /// <summary>
    /// The taskKey for a task, taken from the part of the enum name before the underscore
    /// (P3_SoftwareUpgrade -> "P3"). Derived rather than mapped in a switch so that adding or
    /// renaming a task is a single edit to the enum and the key can never fall out of step with
    /// it — the old hand-written switch was a second list to keep in sync.
    /// </summary>
    public static string ToTaskKey(StudyTask t)
    {
        string s = t.ToString();
        int underscore = s.IndexOf('_');
        return underscore > 0 ? s.Substring(0, underscore) : s;
    }

    private string AutoLabel()
    {
        string scale = scaleCondition == AvatarPlacer.ScaleCondition.Miniature ? "mini" : "human";
        return $"{scale}_{avatarVariant.ToString().ToLowerInvariant()}";
    }

    /// <summary>True when this run came through Launch_Scene rather than opening MR_Scene directly.</summary>
    public bool DrivenBySession => StudySession.HasConfig;

    private void Awake()
    {
        PullFromSession();
        Apply();
    }

    /// <summary>
    /// Take everything the session is carrying: the researcher's config (published by STUDY SETUP
    /// in Launch_Scene) and the task the participant picked on the menu.
    ///
    /// WHY COPY INTO OUR OWN FIELDS RATHER THAN READ THROUGH: ConversationLogger, the custom
    /// inspector and Apply() all already read these fields. Copying keeps this panel the single
    /// thing that talks to them, so nothing downstream had to change. It also means the inspector
    /// shows the values actually in force at runtime, not stale ones the session has overridden.
    ///
    /// Each half is independent: opening MR_Scene directly gives neither and the inspector values
    /// stand, which is what keeps the scene runnable on its own for dev testing.
    /// </summary>
    private void PullFromSession()
    {
        if (StudySession.HasConfig)
        {
            var c = StudySession.Config;
            participantId       = c.participantId;
            conditionLabel      = c.conditionLabel;
            avatarVariant       = c.avatarVariant;
            scaleCondition      = c.scaleCondition;
            avatarScale         = c.avatarScale;
            spawnAnchorLabel    = c.spawnAnchorLabel;
            positionOffset      = c.positionOffset;
            faceUser            = c.faceUser;
            constrainToYAxis    = c.constrainToYAxis;
            snapToSurface       = c.snapToSurface;
            surfaceContact      = c.surfaceContact;
            contactHeightAdjust = c.contactHeightAdjust;

            Debug.Log($"StudyControlPanel: config came from STUDY SETUP — participant=" +
                      $"'{participantId}', condition='{ResolvedConditionLabel}'.");
        }

    }

    /// <summary>
    /// Push every per-trial value into the components that consume it. Safe to call again at
    /// runtime; identity (participant / condition) is not pushed because ConversationLogger reads
    /// it straight off this panel.
    /// </summary>
    public void Apply()
    {
        ResolveRefs();

        if (placer != null)
        {
            placer.avatarVariant       = avatarVariant;
            placer.scaleCondition      = scaleCondition;
            placer.avatarScale         = Vector3.one * avatarScale;
            placer.spawnAnchorLabel    = spawnAnchorLabel;
            placer.positionOffset      = positionOffset;
            placer.faceUser            = faceUser;
            placer.constrainToYAxis    = constrainToYAxis;
            placer.snapToSurface       = snapToSurface;
            placer.surfaceContact      = surfaceContact;
            placer.contactHeightAdjust = contactHeightAdjust;
        }
        else
        {
            Debug.LogWarning("StudyControlPanel: no AvatarPlacer found in the scene — avatar/placement " +
                             "conditions were NOT applied.");
        }

        if (connection != null)
        {
            connection.firstTaskKey = FirstTaskKey;
            connection.secondTaskKey = SecondTaskKey;

            if (firstTask == secondTask)
            {
                Debug.LogWarning($"StudyControlPanel: First Task and Second Task are both " +
                    $"{firstTask}. The participant would be given the same scenario twice.");
            }
        }
        else
        {
            Debug.LogWarning("StudyControlPanel: no ElevenLabsConnection found in the scene — the task " +
                             "was NOT applied.");
        }

        Debug.Log($"StudyControlPanel: applied — participant='{participantId}', condition='{ResolvedConditionLabel}', " +
                  $"tasks={firstTask} ('{FirstTaskKey}') then {secondTask} ('{SecondTaskKey}'), " +
                  $"avatar={avatarVariant}, scale={scaleCondition} ({avatarScale}).");
    }

    private void ResolveRefs()
    {
        if (placer == null)     placer     = FindObjectOfType<AvatarPlacer>();
        if (connection == null) connection = FindObjectOfType<ElevenLabsConnection>();
    }
}
