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
///   * ElevenLabsConnection  <- task (taskKey), selected here as an A..H dropdown
///   * ConversationLogger    -> READS participantId / conditionLabel from here (it pulls, so no
///                              ordering race — see ConversationLogger.Start)
///
/// WHY PUSH IN Awake: AvatarPlacer reads its fields in the MRUK OnSceneLoaded callback and
/// ElevenLabsConnection reads taskKey inside Connect() (fired from AvatarPlacer.SetupVoice) —
/// both happen well AFTER every Awake/Start, so applying once in Awake (with a very early
/// execution order) guarantees the values are in place before anything reads them. The editor
/// also exposes an "Apply To Scene Now" button for edit-time preview.
///
/// IN A REAL SESSION YOU DO NOT EDIT THIS OBJECT. Everything arrives via StudySession:
///   * participant id + visual conditions <- STUDY SETUP in Launch_Scene (the one object the
///     researcher edits, once per participant). It lives there because the launch menu displays
///     the id, and while the menu runs MR_Scene is not loaded — so the config has to originate
///     in the scene that boots first.
///   * task <- the launch menu, chosen by the participant. Risk Level is a WITHIN-subjects
///     factor, so each participant runs several tasks; choosing it at runtime is what removes
///     the mid-session Android rebuild a per-task inspector change would need.
/// Both are pulled in Awake, BEFORE Apply(), so the rest of this class is unchanged and still
/// the only thing that talks to AvatarPlacer / ElevenLabsConnection.
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
    /// <summary>The eight study tasks, in fixed A..H order. Maps to the taskKey strings used by
    /// ElevenLabsConnection.TASK_BLOCKS and SwotPanel (low_A..low_D, high_E..high_H).</summary>
    public enum StudyTask
    {
        A_OfficeChairs,      // low_A
        B_PrinterToner,      // low_B
        C_CoffeeMachine,     // low_C
        D_Whiteboards,       // low_D
        E_ClientOnboarding,  // high_E
        F_ExpansionPilot,    // high_F
        G_SoftwareUpgrade,   // high_G
        H_Partnership,       // high_H
    }

    [Header("Session Identity (written into every log)")]
    [Tooltip("DEV FALLBACK. In a real session this is OVERWRITTEN at Awake by what the researcher " +
             "set on STUDY SETUP in Launch_Scene. It is used only when MR_Scene is opened " +
             "directly, so the scene stays runnable on its own.")]
    public string participantId = "P00";

    [Tooltip("Label for the experimental cell, e.g. 'human_female_real'. Leave EMPTY to " +
             "auto-derive it from the Scale + Avatar conditions below (e.g. 'mini_femaletoon').")]
    public string conditionLabel = "";

    [Header("Task")]
    [Tooltip("DEV FALLBACK — do NOT set this per trial. In a real session the participant picks " +
             "the task on the launch menu and it is written into this field at Awake. This value " +
             "is used only when MR_Scene is opened directly, so a dev run has a task to play. " +
             "Pushed to ElevenLabsConnection.taskKey, which feeds both the agent " +
             "({{task_context}}) and the participant's SWOT panel.")]
    public StudyTask task = StudyTask.A_OfficeChairs;

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

    /// <summary>The taskKey string (low_A..high_H) for the selected task.</summary>
    public string TaskKey => ToTaskKey(task);

    /// <summary>Condition label to log: the explicit one if set, else auto-derived from the conditions.</summary>
    public string ResolvedConditionLabel =>
        string.IsNullOrEmpty(conditionLabel) ? AutoLabel() : conditionLabel;

    public static string ToTaskKey(StudyTask t)
    {
        switch (t)
        {
            case StudyTask.A_OfficeChairs:     return "low_A";
            case StudyTask.B_PrinterToner:     return "low_B";
            case StudyTask.C_CoffeeMachine:    return "low_C";
            case StudyTask.D_Whiteboards:      return "low_D";
            case StudyTask.E_ClientOnboarding: return "high_E";
            case StudyTask.F_ExpansionPilot:   return "high_F";
            case StudyTask.G_SoftwareUpgrade:  return "high_G";
            case StudyTask.H_Partnership:      return "high_H";
            default:                           return "";
        }
    }

    private string AutoLabel()
    {
        string scale = scaleCondition == AvatarPlacer.ScaleCondition.Miniature ? "mini" : "human";
        return $"{scale}_{avatarVariant.ToString().ToLowerInvariant()}";
    }

    /// <summary>True when this run came through Launch_Scene rather than opening MR_Scene directly.</summary>
    public bool DrivenBySession => StudySession.HasConfig || StudySession.HasTask;

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

        if (StudySession.HasTask)
        {
            task = StudySession.Task;
            Debug.Log($"StudyControlPanel: task {task} came from the launch menu.");
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
            connection.taskKey = TaskKey;
        }
        else
        {
            Debug.LogWarning("StudyControlPanel: no ElevenLabsConnection found in the scene — the task " +
                             "was NOT applied.");
        }

        Debug.Log($"StudyControlPanel: applied — participant='{participantId}', condition='{ResolvedConditionLabel}', " +
                  $"task={task} ('{TaskKey}'), avatar={avatarVariant}, scale={scaleCondition} ({avatarScale}).");
    }

    private void ResolveRefs()
    {
        if (placer == null)     placer     = FindObjectOfType<AvatarPlacer>();
        if (connection == null) connection = FindObjectOfType<ElevenLabsConnection>();
    }
}
