using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Floating task sheet summoned in front of the participant on demand. ONE component serves
/// BOTH task families the study runs:
///
///   * SWOT proposals  (P1..P4, source: swot_chinese.md)     -> 優勢 / 劣勢 / 機會 / 威脅
///   * Incident reports (I1..I4, source: incident_chinese.md) -> 事故細節 / 立即影響 / 造成原因 / 潛在公司損失
///
/// WHY ONE SCRIPT AND NOT TWO: the sheet is a measurement instrument. Two components would be
/// two copies of the placement, timing, logging and font handling, and the moment they drift the
/// two families are no longer comparable. Only the CONTENT differs between families — the four
/// cell headers and the text in them — so that is the only thing this class branches on
/// (<see cref="PanelKind"/>). Everything a participant experiences (size, position, fonts,
/// colours, display time, the button) is shared code and therefore identical by construction.
///
/// WHY THIS REPLACES THE PNG (HeadImageTag): a baked PNG is rasterized, so at a small world size
/// viewed through Quest passthrough the text is always soft. This panel renders REAL text with
/// TextMeshPro (SDF), which stays crisp at any scale or distance.
///
/// RESEARCH-INSTRUMENT NOTES (Scale x Render Style study):
///  - The panel must look IDENTICAL in every condition. The canvas is its OWN root object (never
///    parented to the avatar), sized in absolute meters, so it does NOT scale with the
///    miniature/human-sized manipulation, and its flat style does not change with realistic/toon.
///  - ONE SHEET SIZE FOR EVERY TASK. The reference height is sized for the LONGEST content in the
///    study (the incident-report cells, ~40% more text than a SWOT cell), so SWOT sheets carry
///    some blank space at the bottom of their cells. That is deliberate: a sheet that changed
///    shape between task families would make sheet geometry a second thing that varies with the
///    task, on top of the content. See RefHeight.
///  - AVATAR-ANCHORED (default, 2026-08-05): the sheet is world-fixed BESIDE the virtual human,
///    on the participant's right, and is visible for the whole trial rather than summoned.
///    READ THIS BEFORE CHANGING IT BACK OR FORTH — the project has now been round the loop twice:
///      * originally avatar-anchored; dropped 2026-06-26 because the avatar sits ~1.5 m away in
///        the human-sized "seated across the table" cell and well under a meter in the miniature
///        cell, so the sheet's APPARENT size (= legibility) varied with the Scale IV;
///      * then head-anchored at a fixed distance, then CONTROLLER-anchored ("held paper", which
///        hands apparent size to the participant via a natural gesture);
///      * back to avatar-anchored 2026-08-05 by request. The confound above is understood and
///        accepted, not overlooked. What is NOT conceded to it: the sheet keeps a constant
///        PHYSICAL size in both Scale cells (see PositionAtAvatar), so beside a 0.2-scale
///        miniature it is about as tall as the avatar. That is the deliberate trade.
///    Head and the two Controller modes are all still there, and Avatar mode degrades to Head
///    placement when there is no avatar (the practice build strips AvatarPlacer).
///  - The 高風險/高報酬 label carried by the source documents is deliberately NOT shown, and the
///    keys are neutral (P1..P4, I1..I4): showing either would cue the manipulation.
///  - NO PER-CELL COLOUR. All four cell headers are the same colour; hierarchy comes from size
///    and position alone. Colour-coding the four SWOT cells (green strengths / red weaknesses …)
///    carries a valence the incident cells have no equivalent for, so the two families would not
///    have looked like the same instrument.
///  - Content is keyed by the SAME task keys as ElevenLabsConnection.TASK_BLOCKS. The panel reads
///    ElevenLabsConnection.taskKey at show-time so the panel and the agent can never drift onto
///    different tasks.
///
/// SETUP (put this on a PERSISTENT scene object, NOT the avatar prefab):
/// 1. Create an empty GameObject in the scene (e.g. "TASK PANEL") and attach this. It must live
///    in the scene at edit time so the Controller Buttons Mapper can reference it — the avatar is
///    spawned at runtime, so a component on the avatar prefab could NOT be wired into an
///    inspector UnityEvent.
/// 2. Leave Avatar Placer empty — it is auto-found. Nothing else has to be wired for Avatar mode:
///    the panel waits for AvatarPlacer to spawn the avatar and then parks itself beside it.
/// 3. The "Controller Buttons Mapper" building block wired to ShowPanel() (Button = Button.Two,
///    B on the right controller / Y on the left, Mode = Button Down) is still there and still
///    works, but with Always Visible on it has nothing left to reveal. Keep it: turning Always
///    Visible off restores summon-on-demand with no rewiring.
/// 4. REQUIRED (Chinese build): assign Font Override a TMP font asset that has Traditional
///    Chinese glyphs. The content below is zh-TW and the project's default TMP font
///    (LiberationSans) is Latin-only, so without this every character renders as a blank box.
/// 5. (Optional) tune Avatar Side / Avatar Side Gap Fraction / Avatar Height Fraction to move the
///    sheet around the avatar, and Panel Width Meters for its physical size.
///
/// The task is chosen from ElevenLabsConnection.taskKey automatically. For solo testing without
/// that script, set Task Key Override (e.g. "P1" or "I3").
/// </summary>
[DisallowMultipleComponent]
public class TaskPanel : MonoBehaviour
{
    [Header("Task source")]
    [Tooltip("Reads its CurrentTaskKey to pick which task sheet to show, so the sheet and the " +
             "conversation can never be on different tasks. A session runs two tasks; this " +
             "follows the hand-over automatically. Auto-found in the scene if left empty.")]
    public ElevenLabsConnection connection;

    [Tooltip("Forces a specific task key (e.g. P1, I3) regardless of the connection. " +
             "Leave EMPTY in the real study so the panel always follows the agent's current task.")]
    public string taskKeyOverride = "";

    [Tooltip("PRACTICE BUILD ONLY — must stay OFF in MR_Scene. When ON, the panel shows its " +
             "four headers with EMPTY cells instead of any task content, so a participant can " +
             "rehearse the button and the held-paper gesture without previewing a real scenario. " +
             "Set automatically by Tools > Study > Create or Refresh Demo Scene.")]
    public bool demoBlankContent = false;

    [Tooltip("Optional: the conversation logger to notify when the panel is summoned (drops a " +
             "timestamped 'panel_shown' marker into the log, with the task key as detail). " +
             "Auto-found in the scene if left empty; leave empty for solo testing.")]
    public ConversationLogger conversationLogger;

    [Header("Timing")]
    [Tooltip("IGNORED when Always Visible is on. Otherwise: seconds the panel stays up before " +
             "auto-hiding, with each button press re-showing and refreshing this timer.")]
    public float displaySeconds = 10f;

    [Tooltip("Show the sheet for the whole trial instead of summoning it. In Avatar mode it " +
             "appears as soon as the avatar spawns and never auto-hides, so the participant " +
             "always has the task in view. The B/Y button still works (it re-reads the task " +
             "key and logs another 'panel_shown' marker), it just has nothing to reveal.")]
    public bool alwaysVisible = true;

    public enum AnchorMode
    {
        // NOTE: serialized as an int, so only ever APPEND to this list — inserting a value
        // silently reinterprets every scene that already stores one.
        RightController, // held in the right hand (B button), like a sheet of paper
        LeftController,  // held in the left hand (Y button)
        Head,            // fallback: world-locked in front of the participant
        Avatar,          // world-fixed beside the virtual human (the study default)
    }

    [Header("Placement")]
    [Tooltip("Where the panel appears. Avatar = world-fixed beside the virtual human (the study " +
             "default). RightController / LeftController = it rides that controller like a held " +
             "sheet of paper. Head = world-locked in front of the participant. Avatar mode falls " +
             "back to Head placement if there is no avatar to anchor to (e.g. the practice build, " +
             "which strips AvatarPlacer).")]
    public AnchorMode anchorMode = AnchorMode.Avatar;

    [Tooltip("In CONTROLLER and HEAD modes: keep turning to face the participant (billboard) so " +
             "they never see the panel's back; when off, a controller mode rigidly follows the " +
             "controller's orientation (a true clipboard) using Controller Tilt.\n\n" +
             "IN AVATAR MODE THIS DOES NOT BILLBOARD. The sheet is world-locked the moment it is " +
             "placed and nothing about it updates afterwards — this only picks WHICH fixed " +
             "orientation is taken: ON = square up with the participant as they are at placement " +
             "time; OFF = square up with the avatar (steadier if the participant moves a lot, " +
             "since it does not depend on where they were standing just then).")]
    public bool faceCamera = true;

    [Header("Placement — Avatar mode")]
    [Tooltip("The placer that spawns the avatar this panel sits beside. Auto-found in the scene " +
             "if left empty. The avatar itself does not exist until MRUK loads the room, so the " +
             "panel waits for it rather than anchoring to whatever is there at startup.")]
    public AvatarPlacer avatarPlacer;

    public enum AvatarSide
    {
        ParticipantRight, // to the RIGHT of the participant's view (= the avatar's own left)
        ParticipantLeft,
    }

    [Tooltip("Which side of the avatar the sheet sits on, FROM THE PARTICIPANT'S POINT OF VIEW. " +
             "The avatar faces the participant, so the participant's right is the avatar's own " +
             "left — this is stated from the participant's side because that is the one that " +
             "matters for where the sheet appears on screen.")]
    public AvatarSide avatarSide = AvatarSide.ParticipantRight;

    [Tooltip("HOW FAR OUT the sheet sits: clearance between the avatar's side and the near edge " +
             "of the sheet, as a FRACTION OF THE AVATAR'S HEIGHT rather than in meters. A fixed " +
             "gap in meters that looks right next to a seated human is wider than the whole " +
             "avatar in the miniature condition; a fraction keeps the composition reading the " +
             "same in both.\n\n" +
             "MAY BE NEGATIVE, and often should be: the measured half-width already includes a " +
             "silhouette allowance sized for the skull, which runs generous at the shoulders, so " +
             "negative values are how you tuck the sheet in against the body. Around -0.06 puts " +
             "its edge roughly on the avatar's outline.\n\n" +
             "NOTE this is only the GAP. The sheet's distance from the avatar's centre is " +
             "(measured half-width + this gap + half the sheet), so making the sheet WIDER pushes " +
             "its centre out without moving its near edge. Turn on Verbose Logging and read the " +
             "'placed beside avatar' line, which breaks the offset into those three parts.")]
    public float avatarSideGapFraction = 0.02f;

    [Tooltip("HOW HIGH the sheet sits: the height of its CENTER as a fraction of the avatar's " +
             "height measured up from its feet (0 = at the feet, 1 = the very top of the head). " +
             "A fraction, so it lands on the same part of the body at either Scale.\n\n" +
             "Rough guide: 0.5 waist, 0.72 shoulders, 0.90 beside the head (default — keeps the " +
             "sheet and the avatar's face in one glance), 1.0 level with the crown, above which " +
             "it floats over the head.")]
    public float avatarHeightFraction = 0.9f;

    [Header("Placement — Controller modes")]
    [Tooltip("Offset of the panel from the controller, in the controller's LOCAL space " +
             "(X = right, Y = up, Z = forward/where the controller points), in meters. " +
             "The panel sits here relative to the hand, like a clipboard just above it. " +
             "Keep Z >= ~0.18: the rendered Touch controller model + tracking ring reach " +
             "~0.1 m past the hand anchor and visibly block/pierce a closer panel.")]
    public Vector3 controllerLocalOffset = new Vector3(0f, 0.04f, 0.18f);

    [Tooltip("Extra tilt of the panel relative to the controller, in degrees. Only used when " +
             "Face Camera is OFF. e.g. X = 45 angles the paper up toward the reader's face.")]
    public Vector3 controllerTilt = new Vector3(40f, 0f, 0f);

    [Header("Placement — Head mode (fallback)")]
    [Tooltip("HEAD MODE ONLY. Distance straight in front of the participant's head, in meters. " +
             "Closer = larger apparent text. ~0.65 m reads comfortably through Quest passthrough.")]
    public float viewDistance = 0.65f;

    [Tooltip("HEAD MODE ONLY. Sideways shift from straight-ahead, in meters (positive = the " +
             "participant's right). Keeps the panel off to one side so it doesn't cover the agent.")]
    public float lateralOffset = 0.32f;

    [Tooltip("HEAD MODE ONLY. Vertical shift relative to eye height, in meters " +
             "(negative = below eye level, a natural slightly-downward reading angle).")]
    public float heightOffset = -0.08f;

    [Header("Panel size & style")]
    [Tooltip("Physical width of the panel in meters. Height follows the reference aspect " +
             "(RefWidth x RefHeight). CONSTANT across all conditions AND all tasks — do not " +
             "drive this from the avatar scale.\n\n" +
             "SIZE THIS FOR THE READING DISTANCE. Apparent body-text size in arcminutes is " +
             "roughly (26 / 1100) * panelWidthMeters / distance * 3438. Below ~27' the text was " +
             "already judged strained on Quest passthrough. In Avatar mode the sheet sits about " +
             "1.55 m away in the human-sized cell and 0.86 m in the miniature one, so 0.5 m gives " +
             "~26' and ~47' respectively; the old 0.3 m (set for the hand-held mode, where it was " +
             "only ~0.35 m from the eye) dropped to ~16' and was too small to read comfortably.\n\n" +
             "Height follows automatically, and the text scales with it — wrapping and layout are " +
             "in reference pixels, so nothing re-flows when you change this.")]
    public float panelWidthMeters = 0.5f;

    [Tooltip("REQUIRED for the Chinese build: a TMP font asset with Traditional Chinese (CJK) " +
             "coverage, e.g. Noto Sans TC / Source Han Sans. The project's default TMP font " +
             "(LiberationSans) has NO CJK glyphs, so leaving this empty renders every character " +
             "as a blank box. NOTE 'Assets/Fonts/msjh SDF' is a STATIC atlas: a character that " +
             "was not baked into it will not render and will not fall back, so if you reword any " +
             "text below, verify the new characters actually appear on the headset.")]
    public TMP_FontAsset fontOverride;

    [Header("Input (optional fallback)")]
    [Tooltip("Leave OFF when using the Controller Buttons Mapper building block. If ON, this " +
             "component polls OVRInput directly so it works without the building block (handy for testing).")]
    public bool useBuiltInInput = false;

    [Header("Debug")]
    [Tooltip("Logs each step of ShowPanel()/placement so you can see in the console (or adb logcat) " +
             "whether the button fired, the task key resolved, and where the panel was placed. " +
             "Turn OFF for the real study.")]
    public bool verboseLogging = true;

    [Header("References (optional)")]
    [Tooltip("The participant's head transform the panel anchors to / faces. " +
             "Falls back to Camera.main (the OVR center-eye).")]
    public Transform cameraTransform;

    // AVATAR AND HEAD MODES: the sheet's world pose, taken once and then simply re-applied. The
    // sheet is attached to NOTHING — not the head, not the avatar. It is a fixed point in the
    // room that happens to have been computed from where the avatar was standing.
    // Re-snapshotted on ShowPanel() and on an Inspector tuning change, and nothing else.
    // (Controller modes ignore this — they follow the controller live every frame, by design.)
    // Head mode uses the position only; Avatar mode locks the rotation too.
    private Vector3 panelWorldPos;
    private Quaternion panelWorldRot = Quaternion.identity;
    private bool placed;

    // False while the avatar measurement is provisional (a rig the Animator has not posed yet).
    // Such a placement is applied for the frame but never committed, so it re-derives.
    private bool measurementSettled;

    // Last values pushed through ApplyTuningChanges, so Inspector edits take effect live.
    private float appliedWidth, appliedGapFraction, appliedHeightFraction;
    private AvatarSide appliedSide;
    private bool appliedFaceCamera;

    // Cached OVR rig so we can read the controller (hand) anchors each frame.
    private OVRCameraRig cachedRig;

    // AVATAR MODE: the avatar's rendered bounds, measured ONCE (see MeasureAvatar). Re-measured
    // only if the avatar's transform changes, which is why the matrix is kept alongside them.
    private Transform measuredAvatar;
    private Matrix4x4 measuredMatrix;
    private Bounds measuredBounds;
    private bool hasMeasuredBounds;
    private bool loggedPlacement;
    private bool warnedUnposed;

    // FindObjectOfType is not free, and in Avatar mode the lookup would otherwise run every
    // frame in any scene that legitimately has no placer (the practice build strips it).
    private bool placerSearched;

    // ALWAYS-VISIBLE: retry cadence for the initial show, and the delay before the first try.
    // The delay is not cosmetic — head tracking is invalid for the first frames, so a panel
    // that falls back to Head placement on frame 1 world-locks itself at the origin. LaunchMenu
    // carries the same guard for the same reason.
    private const float AutoShowDelaySeconds = 0.5f;
    private const float AutoShowRetrySeconds = 0.5f;
    private float nextAutoShowTime;

    // Suppresses the ABORT warning from repeating every retry while a bad task key is in force;
    // reset on a successful show so a later bad key still reports.
    private string lastAbortedKey;

    // ---- Reference canvas resolution (pixels). Width maps to panelWidthMeters. ----
    //
    // HOW RefHeight WAS CHOSEN, so it can be re-derived if the content changes: physical text
    // size is fontSize * panelWidthMeters / RefWidth, so RefWidth and the font sizes are what fix
    // legibility and must not move. RefHeight is then just "how much paper is needed". A cell is
    // (RefWidth * 0.94 / 2) wide, i.e. ~18.5 full-width CJK characters per line at body size 26;
    // measured against the real msjh advances, the longest cell in either document
    // (I4 潛在公司損失) wraps to 4 lines, and the cells below hold 5. That spare line is not
    // padding for its own sake: TMP applies CJK line-break rules (it will not start a line with
    // 、 。 ）…), which can push one more break than a naive width calculation predicts.
    // Dropping the old 任務摘要 block is what paid for the taller cells: 750 -> 700 overall
    // despite every cell growing.
    private const float RefWidth = 1100f;
    private const float RefHeight = 700f;

    // ---- Layout (fractions of the canvas) ----
    private const float SideMargin = 0.03f;      // left/right margin shared by every row
    private const float TitleBottom = 0.875f;    // title occupies TitleBottom..1
    private const float MetaBottom = 0.775f;     // role/task row occupies MetaBottom..TitleBottom
    private const float GridTop = 0.755f;        // 2x2 cell grid occupies 0.02..GridTop
    private const float GridBottom = 0.02f;
    private const float CellHeaderTop = 0.74f;   // within a cell: header above, body below
    private const float TextPad = 8f;            // inset of every text block from its rect, in px

    // ---- Font sizes (reference px; physical size = size * panelWidthMeters / RefWidth) ----
    private const float TitleSize = 48f;
    private const float MetaSize = 28f;
    private const float CellHeaderSize = 32f;
    private const float CellBodySize = 26f;

    // ---- Colours. Deliberately uniform across the four cells (see class header). ----
    private static readonly Color BackColor = new Color(0.07f, 0.09f, 0.13f, 0.94f);
    private static readonly Color CellColor = new Color(1f, 1f, 1f, 0.05f);
    private static readonly Color TitleColor = Color.white;
    private static readonly Color MetaColor = new Color(0.72f, 0.78f, 0.88f);
    private static readonly Color CellHeaderColor = Color.white;
    private static readonly Color CellBodyColor = new Color(0.90f, 0.92f, 0.95f);

    // Built lazily on first show.
    private GameObject canvasGO;
    private RectTransform canvasRect;
    private TextMeshProUGUI titleText, roleText, taskTypeText;
    private readonly TextMeshProUGUI[] cellHeaders = new TextMeshProUGUI[CellCount];
    private readonly TextMeshProUGUI[] cellBodies = new TextMeshProUGUI[CellCount];

    private const int CellCount = 4;

    private float hideAtTime;
    private bool isShowing;

    private void Start()
    {
        if (connection == null) connection = FindObjectOfType<ElevenLabsConnection>();
        if (connection != null) connection.OnTaskAdvanced += HandleTaskAdvanced;
        ResolveCamera();
    }

    /// <summary>
    /// Resolve the head transform the panel anchors to. On Quest the OVR center-eye camera
    /// is often NOT tagged "MainCamera", so Camera.main can be null — fall back to the
    /// OVRCameraRig's centerEyeAnchor (then any camera) so placement never silently fails,
    /// which would otherwise strand the panel at world origin (out of view).
    /// </summary>
    private void ResolveCamera()
    {
        if (cameraTransform != null) return;
        if (Camera.main != null) { cameraTransform = Camera.main.transform; Log("camera = Camera.main."); return; }
        var rig = FindObjectOfType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null) { cameraTransform = rig.centerEyeAnchor; Log("camera = OVRCameraRig.centerEyeAnchor (Camera.main was null)."); return; }
        var anyCam = FindObjectOfType<Camera>();
        if (anyCam != null) { cameraTransform = anyCam.transform; Log("camera = first Camera found (no Camera.main, no OVR rig)."); return; }
        Debug.LogWarning("TaskPanel: could NOT resolve any camera — the panel cannot be placed and will stay at world origin. Assign Camera Transform in the Inspector.");
    }

    /// <summary>
    /// The controller (OVR hand anchor) the panel currently rides, for the chosen anchorMode.
    /// Returns null in Head and Avatar modes. Falls back to null (caller then uses head
    /// placement) if the OVR rig / hand anchor can't be found, so the panel never disappears.
    /// </summary>
    private Transform ResolveControllerTransform()
    {
        if (anchorMode != AnchorMode.RightController && anchorMode != AnchorMode.LeftController)
            return null;
        if (cachedRig == null) cachedRig = FindObjectOfType<OVRCameraRig>();
        if (cachedRig == null) return null;
        return anchorMode == AnchorMode.LeftController ? cachedRig.leftHandAnchor : cachedRig.rightHandAnchor;
    }

    /// <summary>The AvatarPlacer that spawns the avatar, or null if this scene has none.</summary>
    private AvatarPlacer ResolvePlacer()
    {
        if (avatarPlacer != null) return avatarPlacer;
        if (placerSearched) return null;         // searched once; a scene without one stays without one
        placerSearched = true;
        avatarPlacer = FindObjectOfType<AvatarPlacer>();
        return avatarPlacer;
    }

    /// <summary>
    /// The spawned avatar's transform, or null in any other anchor mode / before it spawns.
    /// </summary>
    private Transform ResolveAvatarTransform()
    {
        if (anchorMode != AnchorMode.Avatar) return null;
        var placer = ResolvePlacer();
        if (placer == null) return null;
        var avatar = placer.SpawnedAvatar;
        return avatar != null ? avatar.transform : null;
    }

    // =========================================================================
    //  PUBLIC API — wire ShowPanel() to the Controller Buttons Mapper callback.
    // =========================================================================

    /// <summary>Show (or refresh) the sheet for the current task and restart the auto-hide timer.</summary>
    public void ShowPanel()
    {
        // STEP 1 — proves the button actually reached this method (rules out wiring).
        Log("ShowPanel() CALLED — the button event reached the panel.");

        // PRACTICE SCENE: show the panel's STRUCTURE with no task content. The task lookup is
        // bypassed rather than fed a key, because DemoScene has no ElevenLabsConnection to set
        // one — and any real key would leak a task the participant must not see before the trial.
        if (demoBlankContent)
        {
            Show(DemoContent, "demo");
            return;
        }

        string key = ResolveTaskKey();
        Log($"resolved task key = '{key}'  (taskKeyOverride='{taskKeyOverride}', " +
            $"connection={(connection != null ? "set" : "NULL")}, " +
            $"connection.CurrentTaskKey='{(connection != null ? connection.CurrentTaskKey : "<none>")}').");

        if (string.IsNullOrEmpty(key) || !TASKS.TryGetValue(key, out var content))
        {
            // Reported once per distinct bad key: with Always Visible on, this path is retried
            // on a timer, and a warning every retry would bury the rest of the log.
            if (key != lastAbortedKey)
            {
                lastAbortedKey = key;
                Debug.LogWarning($"TaskPanel: ABORT — no content for task key '{key}'. " +
                    "Set Task Key Override (e.g. P1) for testing, or make sure the agent set " +
                    "ElevenLabsConnection's task keys. Valid keys: " + string.Join(", ", TASKS.Keys));
            }
            return;
        }

        lastAbortedKey = null;
        Show(content, key);
    }

    /// <summary>
    /// Build (once), fill, place and time out the panel. Shared by the study path and the
    /// practice path so the two can never diverge in HOW the panel behaves — the practice
    /// build differs only in WHAT it contains.
    /// </summary>
    private void Show(TaskContent content, string key)
    {
        EnsureBuilt();
        Populate(content);
        canvasGO.SetActive(true);
        isShowing = true;
        placed = false;  // Avatar/Head modes: re-snapshot the world pose each summon, so B/Y
                         // re-places the sheet for wherever the participant is now.
                         // (Controller modes ignore this — they track the hand every frame.)
        hideAtTime = Time.time + Mathf.Max(0.1f, displaySeconds);
        PositionPanel(); // place immediately so it doesn't pop in at a stale spot

        LogPanelShown(key); // drop a 'panel_shown' marker into the conversation log

        Log($"shown — canvas active={canvasGO.activeInHierarchy}, " +
            $"worldPos={canvasGO.transform.position}, camera={(cameraTransform != null ? cameraTransform.name : "NULL")}.");
    }

    /// <summary>
    /// Records a timestamped 'panel_shown' marker in the conversation log (with the task key as
    /// detail) each time the panel is successfully summoned, so the analysis can line up sheet
    /// views against what the agent was saying. Optional: no-op if no ConversationLogger exists.
    /// </summary>
    private void LogPanelShown(string key)
    {
        if (conversationLogger == null)
        {
            // The logger usually lives on the same object as the connection (AgentVoice).
            if (connection != null) conversationLogger = connection.GetComponent<ConversationLogger>();
            if (conversationLogger == null) conversationLogger = FindObjectOfType<ConversationLogger>();
        }

        if (conversationLogger != null) conversationLogger.LogMarker("panel_shown", key);
    }

    private void Log(string msg)
    {
        if (verboseLogging) Debug.Log("TaskPanel: " + msg);
    }

    /// <summary>Hide the panel immediately.</summary>
    public void HidePanel()
    {
        if (canvasGO != null) canvasGO.SetActive(false);
        isShowing = false;
    }

    /// <summary>Toggle the panel (show with a fresh timer, or hide if already up).</summary>
    public void TogglePanel()
    {
        if (isShowing) HidePanel();
        else ShowPanel();
    }

    private string ResolveTaskKey()
    {
        if (!string.IsNullOrEmpty(taskKeyOverride)) return taskKeyOverride;
        return connection != null ? connection.CurrentTaskKey : "";
    }

    /// <summary>
    /// The session has moved to the second task: swap the sheet's CONTENT without re-placing it.
    ///
    /// Deliberately not ShowPanel(): that clears the placement, and a sheet that jumps to a new
    /// spot at the same moment the agent hands over would read as a glitch. The participant
    /// should just look back at the same sheet and find the new task on it.
    /// </summary>
    private void HandleTaskAdvanced(string newKey)
    {
        Log($"agent handed over to task '{newKey}' — refreshing the sheet in place.");

        if (demoBlankContent) return;                    // practice build shows no task content
        if (!isShowing) return;                          // it will pick the new key up when shown
        if (string.IsNullOrEmpty(newKey) || !TASKS.TryGetValue(newKey, out var content)) return;

        Populate(content);
        LogPanelShown(newKey);   // marks WHEN the participant's reference switched
    }

    // =========================================================================
    //  Update / placement
    // =========================================================================

    private void Update()
    {
        if (useBuiltInInput && OVRInput.GetDown(OVRInput.Button.Two))
            ShowPanel();

        if (alwaysVisible && !isShowing) MaintainAlwaysVisible();
    }

    /// <summary>
    /// Bring the sheet up by itself and keep it up. Retried on a timer rather than attempted
    /// once, because everything it depends on arrives late: the task key is pushed in Awake, the
    /// avatar only exists after MRUK loads the room, and head tracking is not valid on the first
    /// frames.
    /// </summary>
    private void MaintainAlwaysVisible()
    {
        if (Time.timeSinceLevelLoad < AutoShowDelaySeconds) return;
        if (Time.time < nextAutoShowTime) return;
        nextAutoShowTime = Time.time + AutoShowRetrySeconds;

        // Avatar mode: WAIT for the avatar instead of showing the sheet at the Head-mode
        // fallback position and having it jump across the room when the avatar spawns. If the
        // scene has no AvatarPlacer at all (the practice build strips it) nothing is coming, so
        // show at the fallback position rather than never showing.
        if (anchorMode == AnchorMode.Avatar && ResolvePlacer() != null && ResolveAvatarTransform() == null)
            return;

        ShowPanel();
    }

    private void LateUpdate()
    {
        if (!isShowing) return;

        if (!alwaysVisible && Time.time >= hideAtTime)
        {
            HidePanel();
            return;
        }

        PositionPanel();
    }

    /// <summary>
    /// Pick up Inspector edits to the layout knobs while the app is running, so they can be
    /// tuned live on the headset instead of through a rebuild each time.
    ///
    /// WHY THIS IS NEEDED: none of these are read per frame. panelWidthMeters is applied once in
    /// EnsureBuilt (which returns early forever after), and the placement fractions are only read
    /// while the sheet is unplaced. Without this, editing any of them in Play mode appears to do
    /// nothing at all, which is a miserable way to try to find a good value.
    /// </summary>
    private void ApplyTuningChanges()
    {
        if (Mathf.Approximately(appliedWidth, panelWidthMeters)
            && Mathf.Approximately(appliedGapFraction, avatarSideGapFraction)
            && Mathf.Approximately(appliedHeightFraction, avatarHeightFraction)
            && appliedSide == avatarSide
            && appliedFaceCamera == faceCamera)
            return;

        appliedWidth = panelWidthMeters;
        appliedGapFraction = avatarSideGapFraction;
        appliedHeightFraction = avatarHeightFraction;
        appliedSide = avatarSide;
        appliedFaceCamera = faceCamera;

        // Physical size lives in the canvas scale: reference pixels -> meters.
        canvasRect.localScale = Vector3.one * (panelWidthMeters / RefWidth);

        // Force the placement to be derived again from the new numbers.
        placed = false;
        loggedPlacement = false;
    }

    private void PositionPanel()
    {
        if (canvasRect == null) return;

        ApplyTuningChanges();

        // AVATAR MODE (study default): world-fixed beside the virtual human. Returns false if
        // the avatar isn't there (or can't be measured), which drops through to Head placement
        // so the sheet is never simply missing.
        if (anchorMode == AnchorMode.Avatar && PositionAtAvatar()) return;

        // CONTROLLER MODE: the panel rides the controller every frame, like a sheet of
        // paper in the hand — the participant pulls it closer to read it more clearly.
        Transform controller = ResolveControllerTransform();
        if (controller != null)
        {
            PositionAtController(controller);
            return;
        }

        // HEAD MODE (or any resolve failure): world-lock in front of the head.
        PositionAtHead();
    }

    /// <summary>
    /// Park the sheet beside the virtual human, on the participant's chosen side, at a constant
    /// PHYSICAL size.
    ///
    /// WHY THE SHEET DOES NOT SCALE WITH THE AVATAR: it is the measurement instrument. Shrinking
    /// it by the Scale factor would put ~1 mm text on a 0.06 m sheet in the miniature condition,
    /// making legibility itself a function of the Scale IV. The consequence is accepted and
    /// deliberate — beside a 0.2-scale miniature the sheet is about as tall as the avatar.
    ///
    /// WHY THE OFFSETS ARE FRACTIONS OF AVATAR HEIGHT: only the sheet is held constant. Where it
    /// sits relative to the body (how far out to the side, how high up) is composition, and a
    /// gap in absolute meters that looks right beside a seated adult is wider than the entire
    /// miniature. See avatarSideGapFraction / avatarHeightFraction.
    ///
    /// KNOWN LIMITATION (the reason this mode was dropped in June and is back by request): the
    /// avatar sits at very different distances in the two Scale cells, so anchoring to it makes
    /// the sheet's APPARENT size vary with the Scale IV even though its physical size does not.
    /// </summary>
    private bool PositionAtAvatar()
    {
        Transform avatar = ResolveAvatarTransform();
        if (avatar == null) return false;

        // ALREADY PLACED: re-apply the stored WORLD pose and return. The sheet is bound to
        // nothing — this branch reads neither the participant's head nor the avatar's transform,
        // which is the whole mechanism by which it stays still.
        //
        // FOUR ITERATIONS LANDED HERE. Do not re-derive placement below this line, and do not
        // re-attach it to anything:
        //   1. recomputing the pose every frame made the sheet ORBIT the avatar (the
        //      participant's-right axis comes from the head-to-avatar vector) and wobble
        //      (billboarding re-aims at the centre-eye anchor, which swings on the neck);
        //   2. a WORLD snapshot fixed that but TELEPORTED on head turns — not because world
        //      space was wrong, but because MeasureAvatar used to clear `placed` whenever it
        //      re-measured, and the Animator (on the avatar's root, Apply Root Motion ON)
        //      rewrites that root every frame and so tripped the measurement cache constantly.
        //      Re-placing then re-read the camera. That bug is fixed at its source: re-measuring
        //      no longer disturbs an existing placement;
        //   3. binding the offset to the AVATAR fixed the teleport, but glued the sheet to a
        //      transform that root motion jiggles every frame, so it visibly bobbed along with
        //      the idle animation — most obvious in the miniature cell, where the avatar sits
        //      much closer to the eye and the same wobble covers more of the view;
        //   4. so: a plain world pose, attached to nothing. The avatar's position is an INPUT to
        //      computing it once, not a thing the sheet follows.
        // The sheet therefore does not track the avatar if the idle animation drifts it. That is
        // the point, and B/Y (ShowPanel) re-places if the two ever need re-aligning.
        if (placed)
        {
            canvasRect.SetPositionAndRotation(panelWorldPos, panelWorldRot);
            return true;
        }

        if (!MeasureAvatar(avatar, out Bounds bounds)) return false;

        ResolveCamera();
        if (cameraTransform == null) return false;

        // "The participant's right" is taken from where the PARTICIPANT stands, not from the
        // avatar's own facing — the avatar is turned to face them, so the two are opposite and
        // using the wrong one puts the sheet on the wrong side of the screen.
        Vector3 toAvatar = bounds.center - cameraTransform.position;
        toAvatar.y = 0f;
        if (toAvatar.sqrMagnitude < 1e-4f) toAvatar = Vector3.forward;
        toAvatar.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, toAvatar);   // participant's right
        if (avatarSide == AvatarSide.ParticipantLeft) side = -side;

        float avatarHeight = Mathf.Max(bounds.size.y, 0.01f);

        // NOT clamped to >= 0. The measured half-width already carries BoneSilhouettePad, which
        // is sized for the skull rather than the shoulders and so runs generous sideways; a
        // negative gap is the intended way to claw that back and tuck the sheet in against the
        // body. Only the sheet's own half-width stops it reaching the avatar's centre line.
        float gap = avatarHeight * avatarSideGapFraction;
        float outward = ExtentAlong(bounds, side)     // half the avatar's width on that axis
                      + gap
                      + panelWidthMeters * 0.5f;      // ...to the sheet's centre, not its edge

        panelWorldPos = bounds.center + side * outward;
        panelWorldPos.y = bounds.min.y + avatarHeight * avatarHeightFraction;

        // faceCamera does NOT mean "keep billboarding" here — nothing in this mode updates after
        // placement. It chooses WHICH fixed orientation gets taken.
        Vector3 facing;
        if (faceCamera)
        {
            // Square up with the participant as they are right now. The canvas shows its front
            // when its forward points AWAY from the reader, hence panel-minus-eye.
            facing = panelWorldPos - cameraTransform.position;
        }
        else
        {
            // Square up with the AVATAR: its forward points at the participant, so the sheet's
            // forward is the avatar's reversed. Steadier if the participant moves a lot, since
            // it does not depend on where they happened to be at placement time.
            facing = -avatar.forward;
        }
        facing.y = 0f;   // keep the sheet upright; a pitched sheet reads as falling over
        panelWorldRot = facing.sqrMagnitude > 1e-4f
            ? Quaternion.LookRotation(facing.normalized, Vector3.up)
            : canvasRect.rotation;

        // A provisional measurement (rig not posed yet) is shown for this frame but not
        // committed, so the next frame re-derives it once the pose lands.
        placed = measurementSettled;

        canvasRect.SetPositionAndRotation(panelWorldPos, panelWorldRot);

        // Reported once, because "the sheet is too far out" is not diagnosable from looking at
        // it — the offset is mostly the MEASURED half-width of the avatar, and a wrong
        // measurement looks exactly like a wrong Side Gap Fraction. These numbers say which.
        if (placed && !loggedPlacement)
        {
            loggedPlacement = true;
            Log($"placed beside avatar — avatar half-width {ExtentAlong(bounds, side):0.###} m " +
                $"+ gap {gap:0.###} m + half sheet {panelWidthMeters * 0.5f:0.###} m " +
                $"= {outward:0.###} m to the {avatarSide}; sheet {panelWidthMeters:0.##} m wide, " +
                $"{Vector3.Distance(panelWorldPos, cameraTransform.position):0.##} m from the eye. " +
                "World-locked and bound to nothing from here — it will not move again, and will " +
                "NOT follow the avatar, until ShowPanel() is called.");
        }

        return true;
    }

    /// <summary>
    /// World-space extent of the avatar's BODY, measured ONCE and re-used.
    ///
    /// DO NOT "SIMPLIFY" THIS BACK TO Renderer.bounds. That is what it did first, and it put the
    /// sheet over a metre out to the side, 41 degrees off-axis, visibly detached from the avatar.
    /// A SkinnedMeshRenderer reports the mesh's BIND-POSE AABB unless m_UpdateWhenOffscreen is on
    /// (it is off on these Character Creator meshes, and turning it on costs a per-frame
    /// re-skin). The bind pose is a T-pose, so the body mesh's serialized bounds are 1.72 m WIDE
    /// against a real seated width of about half a metre — the arms are measured stretched out
    /// sideways even though the avatar in the room has them at its sides.
    ///
    /// Bone positions are the LIVE pose, so they measure the avatar that is actually there.
    ///
    /// NOT measured every frame: the pose moves constantly, and a sheet positioned from live
    /// measurements would drift and jitter beside the avatar all trial. Re-measured only if the
    /// avatar's own transform changes, which covers the runtime SetScale / UpdateFacing paths.
    /// </summary>
    private bool MeasureAvatar(Transform avatar, out Bounds bounds)
    {
        Matrix4x4 m = avatar.localToWorldMatrix;
        if (hasMeasuredBounds && measuredAvatar == avatar && measuredMatrix == m)
        {
            bounds = measuredBounds;
            measurementSettled = true;
            return true;
        }

        // NOTE this cache misses constantly in practice: the Animator is on the avatar's ROOT
        // with Apply Root Motion on, so it rewrites that transform every frame. That is fine —
        // re-measuring is cheap and, crucially, it no longer disturbs an existing placement.
        // An earlier version cleared `placed` here, which handed the sheet's position back to
        // whatever the participant's head was doing and made it teleport on head turns.

        string how = "bones";
        if (!TryMeasureFromBones(avatar, out bounds))
        {
            how = "renderers (no humanoid rig — bind-pose bounds, expect it to sit too far out)";
            if (!TryMeasureFromRenderers(avatar, out bounds))
            {
                Log("avatar could not be measured at all — falling back to head placement.");
                return false;
            }
        }

        // A posed human is roughly a third as wide as it is tall; a T-pose is as wide as it is
        // tall. Measuring a rig the Animator has not posed yet would shove the sheet out to the
        // side exactly as the old Renderer.bounds version did — and that looks identical to a
        // mis-set Side Gap Fraction, which is what made it hard to spot the first time. So:
        // say so, and do NOT cache it, which re-measures next frame and self-corrects once the
        // pose lands.
        float widest = Mathf.Max(bounds.size.x, bounds.size.z);
        bool plausible = widest <= 0.75f * Mathf.Max(bounds.size.y, 0.01f);
        if (!plausible)
        {
            // Once only: this runs every frame until the pose settles, and if it never does
            // (a rig that is genuinely built T-posed) one warning is the whole message anyway.
            if (!warnedUnposed)
            {
                warnedUnposed = true;
                Debug.LogWarning($"TaskPanel: avatar measured {widest:0.##} m wide against " +
                    $"{bounds.size.y:0.##} m tall — those are T-pose proportions, so the rig was " +
                    "probably not posed when it was measured. The sheet will sit too far out to " +
                    "the side until this settles; it re-measures every frame until it does.");
            }
            measurementSettled = false;   // caller shows it this frame but will not commit it
            return true;
        }

        measuredAvatar = avatar;
        measuredMatrix = m;
        measuredBounds = bounds;
        hasMeasuredBounds = true;
        measurementSettled = true;
        Log($"measured avatar from {how}: center={bounds.center}, size={bounds.size}.");
        return true;
    }

    /// <summary>
    /// The humanoid skeleton, sampled to a box. Optional bones (UpperChest, Neck) are absent on
    /// plenty of rigs, so every lookup is allowed to fail — what matters is that the shoulders,
    /// hands and feet are in there, since they are what set the silhouette's width.
    /// </summary>
    private static readonly HumanBodyBones[] MeasureBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
        HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm,  HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm,  HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand,      HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg,  HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftFoot,      HumanBodyBones.RightFoot,
    };

    /// <summary>
    /// How far the silhouette stands outside the skeleton, as a fraction of the skeleton's own
    /// height: bones are a centre-line, so flesh, hair, clothing and the top of the skull above
    /// the head bone all sit beyond them, and the foot bone is the ankle rather than the sole.
    /// A fraction rather than a fixed number of metres, so it does not swamp a 0.2-scale miniature.
    /// </summary>
    private const float BoneSilhouettePad = 0.06f;

    private static bool TryMeasureFromBones(Transform avatar, out Bounds bounds)
    {
        bounds = new Bounds();

        var animator = avatar.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return false;

        bool any = false;
        foreach (var id in MeasureBones)
        {
            var bone = animator.GetBoneTransform(id);
            if (bone == null) continue;
            if (!any) { bounds = new Bounds(bone.position, Vector3.zero); any = true; }
            else bounds.Encapsulate(bone.position);
        }
        if (!any) return false;

        // Expand() grows the SIZE by the amount given, i.e. half of it on each side.
        bounds.Expand(2f * Mathf.Max(bounds.size.y, 0.01f) * BoneSilhouettePad);
        return true;
    }

    /// <summary>
    /// Fallback for a non-humanoid avatar. Accurate only for static meshes — see the warning on
    /// MeasureAvatar about bind-pose bounds on skinned ones.
    /// </summary>
    private static bool TryMeasureFromRenderers(Transform avatar, out Bounds bounds)
    {
        bool any = false;
        bounds = new Bounds();
        foreach (var r in avatar.GetComponentsInChildren<Renderer>())
        {
            // Skinned/mesh renderers only — a world-space canvas or particle system parented
            // under the avatar would drag the bounds somewhere meaningless.
            if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    /// <summary>Half-width of an axis-aligned box along an arbitrary unit direction.</summary>
    private static float ExtentAlong(Bounds b, Vector3 dir) =>
        Mathf.Abs(b.extents.x * dir.x) + Mathf.Abs(b.extents.y * dir.y) + Mathf.Abs(b.extents.z * dir.z);

    /// <summary>Turn the panel so its front faces the participant (its forward points away).</summary>
    private void FaceParticipant()
    {
        if (cameraTransform == null) return;
        Vector3 toCamera = canvasRect.position - cameraTransform.position;
        if (toCamera.sqrMagnitude > 1e-4f)
            canvasRect.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
    }

    /// <summary>
    /// Held-paper placement: the panel sits at a fixed LOCAL offset from the controller and
    /// moves/rotates with it. With faceCamera it billboards to the participant (never shows its
    /// back); otherwise it rigidly follows the controller's orientation plus a reading tilt.
    /// </summary>
    private void PositionAtController(Transform controller)
    {
        canvasRect.position = controller.position + controller.rotation * controllerLocalOffset;

        if (faceCamera)
        {
            ResolveCamera();
            FaceParticipant();
        }
        else
        {
            canvasRect.rotation = controller.rotation * Quaternion.Euler(controllerTilt);
        }
    }

    private void PositionAtHead()
    {
        ResolveCamera();
        if (cameraTransform == null) return; // no head/camera to anchor to (shouldn't happen on Quest)

        // Anchor to the PARTICIPANT, not the avatar. Snapshot the head pose once per show
        // and world-lock the panel there: a fixed viewing distance keeps apparent text size
        // (= legibility) identical in every condition, and world-locking (vs. head-following)
        // lets the participant look straight at the panel to read it instead of it sliding
        // away with their gaze. Re-summoning re-snapshots, so it always returns in front.
        if (!placed)
        {
            Vector3 eye = cameraTransform.position;
            Vector3 fwd = cameraTransform.forward;
            fwd.y = 0f;                                     // yaw only -> stays level & at a stable height
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd); // participant's right

            panelWorldPos = eye
                          + fwd        * viewDistance
                          + right      * lateralOffset
                          + Vector3.up * heightOffset;
            placed = true;

            Log($"placed — eye(head)={eye}, panelWorldPos={panelWorldPos} " +
                $"(viewDistance={viewDistance}, lateralOffset={lateralOffset}, heightOffset={heightOffset}).");

            if (!faceCamera)
            {
                // Fixed orientation: face the participant (panel front points back at them).
                Vector3 lookDir = panelWorldPos - eye;
                if (lookDir.sqrMagnitude > 1e-4f)
                    canvasRect.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }

        canvasRect.position = panelWorldPos;

        if (faceCamera) FaceParticipant();
    }

    // =========================================================================
    //  UI construction (runtime, so no manual prefab wiring is needed)
    // =========================================================================

    private void EnsureBuilt()
    {
        if (canvasGO != null) return;

        // Standalone root object — NOT a child of the avatar, so the avatar's scale
        // manipulation never touches the panel's size.
        canvasGO = new GameObject("TaskPanel_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();

        canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(RefWidth, RefHeight);
        float metersPerPixel = panelWidthMeters / RefWidth;
        canvasRect.localScale = Vector3.one * metersPerPixel;

        // Background
        MakePanel(canvasRect, Vector2.zero, Vector2.one, BackColor);

        // Title, then the 職務 / 任務 row. Both are filled in Populate.
        //
        // NO BOLD ANYWHERE ON THIS PANEL — see the CJK note on BuildCell. Size and position
        // carry the hierarchy instead, so the title is a little larger than it was when bold.
        float right = 1f - SideMargin;
        titleText = MakeText(canvasRect, "Title", new Vector2(SideMargin, TitleBottom), new Vector2(right, 1f),
            TitleSize, TitleColor, TextAlignmentOptions.Left);

        // Role and task are two separate texts rather than one line with a separator: the
        // separator would be one more character to have to be present in the static font atlas,
        // and two rects keep 任務 starting at the same x on every sheet however long the role is.
        roleText = MakeText(canvasRect, "Role", new Vector2(SideMargin, MetaBottom), new Vector2(0.52f, TitleBottom),
            MetaSize, MetaColor, TextAlignmentOptions.Left);
        taskTypeText = MakeText(canvasRect, "TaskType", new Vector2(0.52f, MetaBottom), new Vector2(right, TitleBottom),
            MetaSize, MetaColor, TextAlignmentOptions.Left);

        // 2x2 cell grid in the lower ~73%.
        var grid = MakePanel(canvasRect, new Vector2(SideMargin, GridBottom), new Vector2(right, GridTop),
            new Color(0, 0, 0, 0)); // transparent container
        var gridRect = grid.rectTransform;

        BuildCell(gridRect, 0, new Vector2(0.0f,  0.52f), new Vector2(0.49f, 1.0f));
        BuildCell(gridRect, 1, new Vector2(0.51f, 0.52f), new Vector2(1.0f,  1.0f));
        BuildCell(gridRect, 2, new Vector2(0.0f,  0.0f),  new Vector2(0.49f, 0.48f));
        BuildCell(gridRect, 3, new Vector2(0.51f, 0.0f),  new Vector2(1.0f,  0.48f));

        canvasGO.SetActive(false);
    }

    /// <summary>
    /// Fill the sheet. The four cell HEADERS are set here rather than at build time because they
    /// are what distinguishes a SWOT sheet from an incident sheet — everything else about the two
    /// is identical, which is the whole point of having one component.
    /// </summary>
    private void Populate(TaskContent c)
    {
        titleText.text = c.title;

        // Built here rather than stored so the labels can never disagree between tasks. An empty
        // role/task yields an empty line rather than a dangling "職務：".
        roleText.text = string.IsNullOrEmpty(c.role) ? "" : "職務：" + c.role;
        taskTypeText.text = string.IsNullOrEmpty(c.taskType) ? "" : "任務：" + c.taskType;

        string[] headers = c.kind == PanelKind.Incident ? IncidentHeaders : SwotHeaders;
        for (int i = 0; i < CellCount; i++)
        {
            cellHeaders[i].text = headers[i];
            cellBodies[i].text = c.Cell(i);
        }
    }

    /// <summary>
    /// Builds one cell of the 2x2 grid. Both of its texts are left EMPTY here and written in
    /// Populate, since the headers depend on the task family.
    ///
    /// WHY THE HEADER IS NOT BOLD: the TMP asset (msjh SDF) contains a single weight, so
    /// FontStyles.Bold is FAUX bold — TMP dilates the SDF by boldStyle (0.75). Latin glyphs
    /// absorb that fine, but a dense CJK glyph (應, 顯, 潛在公司損失's 潛) has stroke gaps narrower
    /// than the dilation, so the strokes merge into a blob. Enlarging the panel does not help:
    /// the dilation scales with the glyph. Size and position carry the hierarchy instead.
    /// </summary>
    private void BuildCell(RectTransform parent, int index, Vector2 aMin, Vector2 aMax)
    {
        var cell = MakePanel(parent, aMin, aMax, CellColor);
        cell.gameObject.name = "Cell" + index;
        var cellRect = cell.rectTransform;

        cellHeaders[index] = MakeText(cellRect, "Header", new Vector2(0f, CellHeaderTop), new Vector2(1f, 1f),
            CellHeaderSize, CellHeaderColor, TextAlignmentOptions.TopLeft);

        cellBodies[index] = MakeText(cellRect, "Body", new Vector2(0f, 0f), new Vector2(1f, CellHeaderTop),
            CellBodySize, CellBodyColor, TextAlignmentOptions.TopLeft);
    }

    private Image MakePanel(RectTransform parent, Vector2 aMin, Vector2 aMax, Color color)
    {
        var go = new GameObject("Panel", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private TextMeshProUGUI MakeText(RectTransform parent, string name, Vector2 aMin, Vector2 aMax,
        float fontSize, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = new Vector2(TextPad, TextPad);
        rt.offsetMax = new Vector2(-TextPad, -TextPad);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (fontOverride != null) tmp.font = fontOverride;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = FontStyles.Normal;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    private void OnDestroy()
    {
        if (connection != null) connection.OnTaskAdvanced -= HandleTaskAdvanced;
        if (canvasGO != null) Destroy(canvasGO);
    }

    // =========================================================================
    //  Task content — keyed identically to ElevenLabsConnection.TASK_BLOCKS.
    //  Sources: swot_chinese.md (P1..P4) and incident_chinese.md (I1..I4),
    //  the zh-TW study build. Risk labels intentionally omitted (class header).
    //
    //  WHAT IS DROPPED FROM THE SOURCE DOCUMENTS: the 任務摘要 paragraph. The
    //  participant is briefed on the scenario by the researcher and by the agent
    //  (it is ElevenLabsConnection.TASK_BLOCKS that carries it), so on the sheet
    //  it was a wall of prose above the part they actually work from. The sheet
    //  now shows only what they refer back to mid-task: who they are, what they
    //  are producing, and the four components.
    //
    //  The cell text is VERBATIM from the source documents, minus the
    //  "優勢 (Strengths)：" / "事故細節：" prefix that the cell header already
    //  states. Keep it that way: the documents are the study's record of what
    //  participants were shown.
    //
    //  CJK strings are kept on ONE line each: splitting them across concatenated
    //  lines risks silently inserting/losing a space inside a run of characters.
    //
    //  FONT: these glyphs need a TMP font asset with CJK coverage — assign it to
    //  Font Override in the Inspector, or every character renders as a blank box.
    // =========================================================================

    /// <summary>Which set of four cell headers a task uses. This is the ONLY difference
    /// between the two families as far as this component is concerned.</summary>
    private enum PanelKind { Swot, Incident }

    private static readonly string[] SwotHeaders =
        { "優勢 (Strengths)", "劣勢 (Weaknesses)", "機會 (Opportunities)", "威脅 (Threats)" };

    private static readonly string[] IncidentHeaders =
        { "事故細節", "立即影響", "造成原因", "潛在公司損失" };

    private struct TaskContent
    {
        public PanelKind kind;
        public string title;
        public string role;      // 職務 — rendered with its label by Populate
        public string taskType;  // 任務 — likewise
        public string cell0, cell1, cell2, cell3;

        /// <summary>Cell body i, in the order of this task's headers.</summary>
        public string Cell(int i)
        {
            switch (i)
            {
                case 0:  return cell0;
                case 1:  return cell1;
                case 2:  return cell2;
                default: return cell3;
            }
        }
    }

    // The two factories exist so the content table below reads in each family's own vocabulary
    // (strengths/weaknesses/… vs details/impact/…) while still producing one shared struct. Call
    // them with named arguments — that is what keeps a four-string block honest.
    private static TaskContent Swot(string title, string role, string taskType,
        string strengths, string weaknesses, string opportunities, string threats) =>
        new TaskContent
        {
            kind = PanelKind.Swot, title = title, role = role, taskType = taskType,
            cell0 = strengths, cell1 = weaknesses, cell2 = opportunities, cell3 = threats,
        };

    private static TaskContent Incident(string title, string role, string taskType,
        string details, string immediateImpact, string cause, string potentialLoss) =>
        new TaskContent
        {
            kind = PanelKind.Incident, title = title, role = role, taskType = taskType,
            cell0 = details, cell1 = immediateImpact, cell2 = cause, cell3 = potentialLoss,
        };

    /// <summary>
    /// Practice-build placeholder (see <see cref="demoBlankContent"/>). Deliberately carries NO
    /// task material: the participant learns the button, the layout and the four cell headers,
    /// while the scenario itself stays unseen until the trial. The cells are left empty rather
    /// than filled with dummy prose so nothing here can be mistaken for a real task.
    ///
    /// It uses the SWOT headers because a practice sheet has to pick one of the two families and
    /// the shape of the sheet — which is what is being practised — is the same either way.
    /// </summary>
    private static readonly TaskContent DemoContent = Swot(
        title: "練習模式",
        role: "（正式實驗時顯示）",
        taskType: "（正式實驗時顯示）",
        strengths: "",
        weaknesses: "",
        opportunities: "",
        threats: ""
    );

    private static readonly Dictionary<string, TaskContent> TASKS = new Dictionary<string, TaskContent>
    {
        // ============ SWOT PROPOSALS (swot_chinese.md) ============

        ["P1"] = Swot(
            title: "任務 P1：客戶到職專案",
            role: "客戶營運總監",
            taskType: "專案提案",
            strengths: "已有試行階段數據支持，客戶入駐時間成功從 30 天縮短至 18 天，並創造約新台幣 1,600 萬的成本效益。",
            weaknesses: "需要新台幣 6,400 萬的投資，且全面推廣至全公司時，將面臨顯著的系統導入與變革管理挑戰。",
            opportunities: "成功推廣可帶動營收成長、提高客戶留存率，並大幅提升團隊在企業內部的能見度與影響力。",
            threats: "客戶需求的變動、法規遵循要求，或外部整合的相依性，都可能延後計畫時程，並降低預期可帶來的效益。"
        ),

        ["P2"] = Swot(
            title: "任務 P2：國際擴張試行計畫",
            role: "國際策略總監",
            taskType: "專案提案",
            strengths: "試行計畫的架構可在公司承諾全面擴張前，先測試市場可行性，並蒐集在地佐證。",
            weaknesses: "該提案需要新台幣 9,600 萬，並在兩個性質不同的國際市場之間產生龐大的協調與資源需求。",
            opportunities: "兩個目標城市約 20% 的年度市場成長，可望帶來新的營收、更廣的市場觸及，以及更高的團隊能見度。",
            threats: "法規變動、匯率波動、當地競爭或文化落差，都可能損害成效，並造成財務或商譽上的損失。"
        ),

        ["P3"] = Swot(
            title: "任務 P3：企業軟體升級",
            role: "企業科技總監",
            taskType: "專案提案",
            strengths: "此提案將核心系統整合至單一平台，統一各項作業流程，預估可節省約 NT$640 萬。",
            weaknesses: "需要 NT$4,800 萬的投資，並涉及廠商評選、系統遷移、教育訓練與變革管理等複雜工作。",
            opportunities: "一旦整合成功，可望降低停機時間、提升全公司生產力、精簡跨團隊協作，並確立團隊作為高效變革推動人物的地位。",
            threats: "廠商表現不如預期、導入時程延宕、資安問題或整合失敗，都可能干擾營運，並削弱外界對領導層的信任。"
        ),

        ["P4"] = Swot(
            title: "任務 P4：策略合作夥伴關係",
            role: "策略合作總監",
            taskType: "專案提案",
            strengths: "相較於公司花時間與成本自己從頭來，這項合作能讓公司更快取得溫莎銀行既有的資源、能力與市場觸角。",
            weaknesses: "需要投入 NT$8,000 萬，短期回報並不確定，且因成敗結果有一部分取決於合作夥伴，而降低公司的自主掌控權。",
            opportunities: "該合作可望帶動約 25% 的市場成長，擴大公司的觸及範圍，並強化其競爭地位。",
            threats: "雙方目標不一致、合作表現不如預期、法規上的複雜問題，或涉及溫莎銀行的商譽風險，都可能降低這項合作關係的價值。"
        ),

        // ============ INCIDENT REPORTS (incident_chinese.md) ============

        ["I1"] = Incident(
            title: "任務 I1：包裝機具傷害事故",
            role: "生產營運經理",
            taskType: "事故報告",
            details: "該名員工按下一般停止按鈕後，將手伸入機器內清除卡住的包裝材料。機器意外啟動，造成該名員工手部嚴重受傷。",
            immediateImpact: "緊急救護人員將該名員工送往醫院。生產線停止運作，客戶訂單交貨延誤，並已啟動正式的安全調查程序。",
            cause: "規定的停機上鎖與掛牌程序未確實執行。該機器存在間歇性的安全連鎖裝置故障，且前一週曾發生類似的材料卡住的事件，但未提交正式的未遂事故通報。",
            potentialLoss: "醫療費用、職業災害補償、主管機關裁罰、法律求償、設備維修、生產損失，以及商譽損害，總計可能達到約新台幣 4,500 萬至 9,000 萬元。"
        ),

        ["I2"] = Incident(
            title: "任務 I2：勒索軟體攻擊",
            role: "資訊技術營運總監",
            taskType: "事故報告",
            details: "一封夾帶惡意附件的郵件在某名員工的電腦上安裝了勒索軟體，並將共用網路資料夾加密。攻擊者要求支付贖金以換取解密金鑰。",
            immediateImpact: "內部排程、客戶服務、帳務作業及文件存取中斷約兩個工作日。資訊技術部門已隔離受影響的系統，並開始還原備份資料。",
            cause: "該工作站未安裝近期的資安更新，網路存取未進行充分的區隔，且該名員工在系統已顯示外部寄件者警示的情況下仍開啟了附件。",
            potentialLoss: "系統還原、營收損失、數位鑑識調查、法律審查、客戶賠償、營運中斷，以及商譽損害，估計將使公司支出約新台幣 9,000 萬至 1 億 8,000 萬元。"
        ),

        ["I3"] = Incident(
            title: "任務 I3：化學品洩漏與員工暴露事故",
            role: "維修部門主管",
            taskType: "事故報告",
            details: "一個受損的化學品容器自儲放架上掉落，洩漏出液體與蒸氣。兩名鄰近的員工在該區域完成疏散前已暴露於化學品中。",
            immediateImpact: "一名員工因呼吸道刺激症狀住院治療，另一名員工則接受現場處置。廠區受影響的區域已關閉，以進行環境檢測與清理作業。",
            cause: "該容器外部有明顯的破損痕跡，較重的化學品容器被存放在超過建議高度的層架上，且最近一次的儲放區稽核並未完成。",
            potentialLoss: "醫療費用、環境清理、主管機關裁罰、廠區停工、法律求償、材料損失，以及商譽影響，總計可能達到約新台幣 4,500 萬至 1 億 2,000 萬元。"
        ),

        ["I4"] = Incident(
            title: "任務 I4：客戶資料外洩",
            role: "雲端基礎架構總監",
            taskType: "事故報告",
            details: "該資料庫包含客戶姓名、聯絡資訊、帳戶歷史紀錄，以及部分付款資訊。存取紀錄顯示，一名不明的外部使用者下載了部分資料庫內容。",
            immediateImpact: "該資料庫已下線，外部存取已封鎖，數位鑑識調查亦已展開。客戶端服務中斷約十二小時。",
            cause: "某項雲端權限設定在系統移轉過程中遭到變更，該項變更未經獨立審查，且自動監控機制未能即時偵測出公開存取的設定狀態。",
            potentialLoss: "主管機關裁罰、法律求償、客戶通知、身分保護服務、數位鑑識調查、系統修復、客戶流失，以及商譽損害，估計將使公司支出約新台幣 1 億 5,000 萬至 3 億元。"
        ),
    };
}
