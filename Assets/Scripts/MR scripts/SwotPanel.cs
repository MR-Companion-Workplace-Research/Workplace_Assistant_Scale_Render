using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Floating SWOT panel summoned in front of the participant on demand.
///
/// WHY THIS REPLACES THE PNG (HeadImageTag): a baked PNG is rasterized, so at a
/// small world size viewed through Quest passthrough the text is always soft.
/// This panel renders REAL text with TextMeshPro (SDF), which stays crisp at any
/// scale or distance.
///
/// RESEARCH-INSTRUMENT NOTES (Scale x Render Style study):
///  - The panel is a measurement instrument, so it must look IDENTICAL in every
///    condition. The canvas is its OWN root object (never parented to the avatar),
///    sized in absolute meters, so it does NOT scale with the miniature/human-sized
///    manipulation, and its flat style does not change with realistic/toon.
///  - CONTROLLER-ANCHORED (default): when summoned the panel rides the participant's
///    controller, like a sheet of paper held in the hand. It FOLLOWS the controller
///    every frame, so the participant brings it closer to read it more clearly and
///    moves it aside when done — the apparent text size is under their own control via
///    a natural physical gesture rather than a fixed distance. (Earlier versions
///    anchored it to the avatar, which drifted far away in the human-sized "seated
///    across the table" cell, and then to a fixed point in front of the head; the team
///    preferred the held-paper metaphor after the demo.) A Head anchor mode is kept as
///    a fallback that world-locks the panel in front of the participant.
///  - The HIGH/LOW risk label is deliberately NOT shown — per swot_scenarios.md it
///    was never shown to participants and would cue the Risk manipulation.
///  - Content is keyed by the SAME task keys as ElevenLabsConnection.TASK_BLOCKS
///    (low_A … high_H). The panel reads ElevenLabsConnection.taskKey at show-time so
///    the panel and the agent can never drift onto different tasks.
///
/// SETUP (put this on a PERSISTENT scene object, NOT the avatar prefab):
/// 1. Create an empty GameObject in the scene (e.g. "SWOT Panel") and attach this.
///    It must live in the scene at edit time so the Controller Buttons Mapper can
///    reference it — the avatar is spawned at runtime, so a component on the avatar
///    prefab could NOT be wired into an inspector UnityEvent.
/// 2. Add a "Controller Buttons Mapper" building block to the scene. In its Button
///    Actions, set Button = Button.Two (B on the right controller / Y on the left, so
///    both controllers work), Mode = Button Down, and wire the callback to this
///    component's ShowSwot().
/// 3. REQUIRED (Chinese build): assign Font Override a TMP font asset that has Traditional
///    Chinese glyphs. The content below is zh-TW and the project's default TMP font
///    (LiberationSans) is Latin-only, so without this every character renders as a blank box.
/// 4. (Optional) tune the Controller Local Offset / Panel Width Meters in the Inspector,
///    and pick which hand it appears in via Anchor Mode.
///
/// Placement rides the chosen controller (the OVR hand anchor), so the avatar is NOT
/// involved in positioning. The task is chosen from ElevenLabsConnection.taskKey
/// automatically. For solo testing without that script, set Task Key Override (e.g. "low_A").
/// </summary>
[DisallowMultipleComponent]
public class SwotPanel : MonoBehaviour
{
    [Header("Task source")]
    [Tooltip("Reads its taskKey to pick which SWOT to show. Auto-found in the scene if left empty.")]
    public ElevenLabsConnection connection;

    [Tooltip("Forces a specific task key (e.g. low_A, high_E) regardless of the connection. " +
             "Leave EMPTY in the real study so the panel always follows the agent's taskKey.")]
    public string taskKeyOverride = "";

    [Tooltip("PRACTICE BUILD ONLY — must stay OFF in SampleScene. When ON, the panel shows its " +
             "four headers with EMPTY quadrants instead of any task content, so a participant can " +
             "rehearse the button and the held-paper gesture without previewing a real scenario. " +
             "Set automatically by Tools > Study > Create or Refresh Demo Scene.")]
    public bool demoBlankContent = false;

    [Tooltip("Optional: the conversation logger to notify when the panel is summoned (drops a " +
             "timestamped 'swot_shown' marker into the log, with the task key as detail). " +
             "Auto-found in the scene if left empty; leave empty for solo testing.")]
    public ConversationLogger conversationLogger;

    [Header("Timing")]
    [Tooltip("Seconds the panel stays up before auto-hiding. Each button press re-shows and " +
             "refreshes this timer. Tune to taste.")]
    public float displaySeconds = 10f;

    public enum AnchorMode
    {
        RightController, // held in the right hand (B button), like a sheet of paper
        LeftController,  // held in the left hand (Y button)
        Head,            // fallback: world-locked in front of the participant
    }

    [Header("Placement")]
    [Tooltip("Where the panel appears. RightController / LeftController = it rides that " +
             "controller like a held sheet of paper, so the participant moves the controller " +
             "closer to read it. Head = the old behaviour: world-locked in front of the head.")]
    public AnchorMode anchorMode = AnchorMode.RightController;

    [Tooltip("If true, the panel always turns to face the participant (billboard) so they " +
             "never see its back. If false, in a controller mode it rigidly follows the " +
             "controller's orientation (a true clipboard) using Controller Tilt.")]
    public bool faceCamera = true;

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
    [Tooltip("Physical width of the panel in meters. Height follows the reference aspect. " +
             "CONSTANT across all conditions — do not drive this from the avatar scale.")]
    public float panelWidthMeters = 0.5f;

    [Tooltip("REQUIRED for the Chinese build: a TMP font asset with Traditional Chinese (CJK) " +
             "coverage, e.g. Noto Sans TC / Source Han Sans. The project's default TMP font " +
             "(LiberationSans) has NO CJK glyphs, so leaving this empty renders every character " +
             "as a blank box.")]
    public TMP_FontAsset fontOverride;

    [Header("Input (optional fallback)")]
    [Tooltip("Leave OFF when using the Controller Buttons Mapper building block. If ON, this " +
             "component polls OVRInput directly so it works without the building block (handy for testing).")]
    public bool useBuiltInInput = false;

    [Header("Debug")]
    [Tooltip("Logs each step of ShowSwot()/placement so you can see in the console (or adb logcat) " +
             "whether the button fired, the task key resolved, and where the panel was placed. " +
             "Turn OFF for the real study.")]
    public bool verboseLogging = true;

    [Header("References (optional)")]
    [Tooltip("The participant's head transform the panel anchors to / faces. " +
             "Falls back to Camera.main (the OVR center-eye).")]
    public Transform cameraTransform;

    // HEAD MODE: world position locked at show-time, so the panel doesn't slide away with
    // the participant's gaze while they read it. Re-summoning re-snapshots the head pose.
    // (Controller modes ignore this — they follow the controller live every frame.)
    private Vector3 panelWorldPos;
    private bool placed;

    // Cached OVR rig so we can read the controller (hand) anchors each frame.
    private OVRCameraRig cachedRig;

    // ---- Reference canvas resolution (pixels). Width maps to panelWidthMeters. ----
    private const float RefWidth = 1100f;
    private const float RefHeight = 750f;

    // Built lazily on first ShowSwot().
    private GameObject canvasGO;
    private RectTransform canvasRect;
    private TextMeshProUGUI titleText, scenarioText, strengthsText, weaknessesText, opportunitiesText, threatsText;

    private float hideAtTime;
    private bool isShowing;

    private void Start()
    {
        if (connection == null) connection = FindObjectOfType<ElevenLabsConnection>();
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
        Debug.LogWarning("SwotPanel: could NOT resolve any camera — the panel cannot be placed and will stay at world origin. Assign Camera Transform in the Inspector.");
    }

    /// <summary>
    /// The controller (OVR hand anchor) the panel currently rides, for the chosen anchorMode.
    /// Returns null in Head mode. Falls back to null (caller then uses head placement) if the
    /// OVR rig / hand anchor can't be found, so the panel never disappears.
    /// </summary>
    private Transform ResolveControllerTransform()
    {
        if (anchorMode == AnchorMode.Head) return null;
        if (cachedRig == null) cachedRig = FindObjectOfType<OVRCameraRig>();
        if (cachedRig == null) return null;
        return anchorMode == AnchorMode.LeftController ? cachedRig.leftHandAnchor : cachedRig.rightHandAnchor;
    }

    // =========================================================================
    //  PUBLIC API — wire ShowSwot() to the Controller Buttons Mapper callback.
    // =========================================================================

    /// <summary>Show (or refresh) the panel for the current task and restart the auto-hide timer.</summary>
    public void ShowSwot()
    {
        // STEP 1 — proves the button actually reached this method (rules out wiring).
        Log("ShowSwot() CALLED — the button event reached the panel.");

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
            $"connection.taskKey='{(connection != null ? connection.taskKey : "<none>")}').");

        if (string.IsNullOrEmpty(key) || !SWOT.TryGetValue(key, out var content))
        {
            Debug.LogWarning($"SwotPanel: ABORT — no SWOT content for task key '{key}'. " +
                "Set Task Key Override (e.g. low_A) for testing, or make sure the agent set " +
                "ElevenLabsConnection.taskKey. Valid keys: " + string.Join(", ", SWOT.Keys));
            return;
        }

        Show(content, key);
    }

    /// <summary>
    /// Build (once), fill, place and time out the panel. Shared by the study path and the
    /// practice path so the two can never diverge in HOW the panel behaves — the practice
    /// build differs only in WHAT it contains.
    /// </summary>
    private void Show(SwotContent content, string key)
    {
        EnsureBuilt();
        Populate(content);
        canvasGO.SetActive(true);
        isShowing = true;
        placed = false;  // Head mode: re-snapshot the head pose each summon (ignored in controller modes)
        hideAtTime = Time.time + Mathf.Max(0.1f, displaySeconds);
        PositionPanel(); // place immediately so it doesn't pop in at a stale spot

        LogSwotShown(key); // drop a 'swot_shown' marker into the conversation log

        Log($"shown — canvas active={canvasGO.activeInHierarchy}, " +
            $"worldPos={canvasGO.transform.position}, camera={(cameraTransform != null ? cameraTransform.name : "NULL")}.");
    }

    /// <summary>
    /// Records a timestamped 'swot_shown' marker in the conversation log (with the task key as
    /// detail) each time the panel is successfully summoned, so the analysis can line up SWOT
    /// views against what the agent was saying. Optional: no-op if no ConversationLogger exists.
    /// </summary>
    private void LogSwotShown(string key)
    {
        if (conversationLogger == null)
        {
            // The logger usually lives on the same object as the connection (AgentVoice).
            if (connection != null) conversationLogger = connection.GetComponent<ConversationLogger>();
            if (conversationLogger == null) conversationLogger = FindObjectOfType<ConversationLogger>();
        }

        if (conversationLogger != null) conversationLogger.LogMarker("swot_shown", key);
    }

    private void Log(string msg)
    {
        if (verboseLogging) Debug.Log("SwotPanel: " + msg);
    }

    /// <summary>Hide the panel immediately.</summary>
    public void HideSwot()
    {
        if (canvasGO != null) canvasGO.SetActive(false);
        isShowing = false;
    }

    /// <summary>Toggle the panel (show with a fresh timer, or hide if already up).</summary>
    public void ToggleSwot()
    {
        if (isShowing) HideSwot();
        else ShowSwot();
    }

    private string ResolveTaskKey()
    {
        if (!string.IsNullOrEmpty(taskKeyOverride)) return taskKeyOverride;
        return connection != null ? connection.taskKey : "";
    }

    // =========================================================================
    //  Update / placement
    // =========================================================================

    private void Update()
    {
        if (useBuiltInInput && OVRInput.GetDown(OVRInput.Button.Two))
            ShowSwot();
    }

    private void LateUpdate()
    {
        if (!isShowing) return;

        if (Time.time >= hideAtTime)
        {
            HideSwot();
            return;
        }

        PositionPanel();
    }

    private void PositionPanel()
    {
        if (canvasRect == null) return;

        // CONTROLLER MODE: the panel rides the controller every frame, like a sheet of
        // paper in the hand — the participant pulls it closer to read it more clearly.
        Transform controller = ResolveControllerTransform();
        if (controller != null)
        {
            PositionAtController(controller);
            return;
        }

        // HEAD MODE (or controller-resolve failure): world-lock in front of the head.
        PositionAtHead();
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
            if (cameraTransform != null)
            {
                Vector3 toCamera = canvasRect.position - cameraTransform.position;
                if (toCamera.sqrMagnitude > 1e-4f)
                    canvasRect.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
            }
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

        if (faceCamera)
        {
            Vector3 toCamera = canvasRect.position - cameraTransform.position;
            if (toCamera.sqrMagnitude > 1e-4f)
                canvasRect.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }
    }

    // =========================================================================
    //  UI construction (runtime, so no manual prefab wiring is needed)
    // =========================================================================

    private void EnsureBuilt()
    {
        if (canvasGO != null) return;

        // Standalone root object — NOT a child of the avatar, so the avatar's scale
        // manipulation never touches the panel's size.
        canvasGO = new GameObject("SwotPanel_Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();

        canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(RefWidth, RefHeight);
        float metersPerPixel = panelWidthMeters / RefWidth;
        canvasRect.localScale = Vector3.one * metersPerPixel;

        // Background
        MakePanel(canvasRect, Vector2.zero, Vector2.one, new Color(0.07f, 0.09f, 0.13f, 0.94f));

        // Title + scenario.
        // NO BOLD ANYWHERE ON THIS PANEL — see the CJK note on BuildQuadrant. Size and colour
        // carry the hierarchy instead, so the title is a little larger than it was when bold.
        titleText = MakeText(canvasRect, "Title", new Vector2(0.03f, 0.88f), new Vector2(0.97f, 0.99f),
            50f, Color.white, TextAlignmentOptions.Left, FontStyles.Normal);
        scenarioText = MakeText(canvasRect, "Scenario", new Vector2(0.03f, 0.63f), new Vector2(0.97f, 0.87f),
            27f, new Color(0.82f, 0.86f, 0.92f), TextAlignmentOptions.TopLeft, FontStyles.Normal);

        // 2x2 quadrant grid in the lower ~60%.
        var grid = MakePanel(canvasRect, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.61f),
            new Color(0, 0, 0, 0)); // transparent container
        var gridRect = grid.rectTransform;

        strengthsText     = BuildQuadrant(gridRect, "STRENGTHS",     "優勢 (Strengths)",     new Color(0.45f, 0.85f, 0.55f),
            new Vector2(0.0f, 0.52f), new Vector2(0.49f, 1.0f));
        weaknessesText    = BuildQuadrant(gridRect, "WEAKNESSES",    "劣勢 (Weaknesses)",    new Color(0.95f, 0.5f, 0.5f),
            new Vector2(0.51f, 0.52f), new Vector2(1.0f, 1.0f));
        opportunitiesText = BuildQuadrant(gridRect, "OPPORTUNITIES", "機會 (Opportunities)", new Color(0.55f, 0.72f, 0.97f),
            new Vector2(0.0f, 0.0f), new Vector2(0.49f, 0.48f));
        threatsText       = BuildQuadrant(gridRect, "THREATS",       "威脅 (Threats)",       new Color(0.97f, 0.8f, 0.45f),
            new Vector2(0.51f, 0.0f), new Vector2(1.0f, 0.48f));

        canvasGO.SetActive(false);
    }

    private void Populate(SwotContent c)
    {
        titleText.text = c.title;
        scenarioText.text = c.scenario;
        strengthsText.text = c.strengths;
        weaknessesText.text = c.weaknesses;
        opportunitiesText.text = c.opportunities;
        threatsText.text = c.threats;
    }

    /// <summary>
    /// Builds one SWOT quadrant. <paramref name="name"/> stays ASCII (STRENGTHS, …) so the
    /// runtime hierarchy is still readable while debugging; <paramref name="label"/> is the
    /// localized text the participant actually reads.
    ///
    /// WHY THE HEADER IS NOT BOLD: the TMP asset (msjh SDF) contains a single weight, so
    /// FontStyles.Bold is FAUX bold — TMP dilates the SDF by boldStyle (0.75). Latin glyphs
    /// absorb that fine, but a dense CJK glyph (應, 顯, threats' 威脅) has stroke gaps narrower
    /// than the dilation, so the strokes merge into a blob. Enlarging the panel does not help:
    /// the dilation scales with the glyph. Size + the per-quadrant colour carry the hierarchy.
    /// </summary>
    private TextMeshProUGUI BuildQuadrant(RectTransform parent, string name, string label, Color headerColor,
        Vector2 aMin, Vector2 aMax)
    {
        var cell = MakePanel(parent, aMin, aMax, new Color(1f, 1f, 1f, 0.05f));
        var cellRect = cell.rectTransform;

        // Fixed header, so set its text here (the body text is filled later in Populate).
        var headerText = MakeText(cellRect, name + "_h", new Vector2(0f, 0.74f), new Vector2(1f, 1f),
            33f, headerColor, TextAlignmentOptions.TopLeft, FontStyles.Normal);
        headerText.text = label;

        return MakeText(cellRect, name + "_b", new Vector2(0f, 0f), new Vector2(1f, 0.74f),
            26f, new Color(0.93f, 0.94f, 0.96f), TextAlignmentOptions.TopLeft, FontStyles.Normal);
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
        float fontSize, Color color, TextAlignmentOptions align, FontStyles style)
    {
        const float pad = 14f;
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (fontOverride != null) tmp.font = fontOverride;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = align;
        tmp.fontStyle = style;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    private void OnDestroy()
    {
        if (canvasGO != null) Destroy(canvasGO);
    }

    // =========================================================================
    //  SWOT content — keyed identically to ElevenLabsConnection.TASK_BLOCKS
    //  (low_A … high_H). Source: swot_chinese.md — the zh-TW study build.
    //  Risk labels intentionally omitted (see class header).
    //
    //  PERSON: the panel is what the PARTICIPANT reads, so it stays in the second
    //  person (你被要求…), unlike ElevenLabsConnection.TASK_BLOCKS which is written
    //  first-person for the agent. Both drop the source doc's closing
    //  "你尋求AI 職場助理的協助…" line — that line describes the study setup itself.
    //
    //  CJK strings are kept on ONE line each: splitting them across concatenated
    //  lines risks silently inserting/losing a space inside a run of characters.
    //
    //  FONT: these glyphs need a TMP font asset with CJK coverage — assign it to
    //  Font Override in the Inspector, or every character renders as a blank box.
    // =========================================================================

    private struct SwotContent
    {
        public string title, scenario, strengths, weaknesses, opportunities, threats;
    }

    /// <summary>
    /// Practice-build placeholder (see <see cref="demoBlankContent"/>). Deliberately carries NO
    /// task material: the participant learns the button, the layout and the four quadrant labels,
    /// while the scenario itself stays unseen until the trial. The quadrants are left empty rather
    /// than filled with dummy prose so nothing here can be mistaken for a real task.
    /// </summary>
    private static readonly SwotContent DemoContent = new SwotContent
    {
        title = "練習模式",
        scenario = "這是操作練習。正式實驗時，此處會顯示你的情境說明，下方四格則會顯示對應的分析內容。",
        strengths = "",
        weaknesses = "",
        opportunities = "",
        threats = "",
    };

    private static readonly Dictionary<string, SwotContent> SWOT = new Dictionary<string, SwotContent>
    {
        ["low_A"] = new SwotContent {
            title = "辦公椅",
            scenario = "你被要求準備一份報告，申請 NT$16,000 採購會議室的替換座椅，用於三樓會議室。雖然這並非緊急的安全問題，但該提案可以改善跨團隊合作時的舒適度與專業形象。",
            strengths = "可以採用 ULINE 的批量購買的價格，每張椅子 NT$1,600，可在 NT$16,000 的預算內更換十張椅子。",
            weaknesses = "將 NT$16,000 投入一項非緊急、範圍有限的設施改善項目。",
            opportunities = "更好的座椅可以提升跨團隊及客戶會議中的舒適度、專業形象與協作品質。",
            threats = "ULINE 的價格或庫存可能在這份報告核准前發生變動，導致成本增加或更換延遲。",
        },
        ["low_B"] = new SwotContent {
            title = "印表機碳粉",
            scenario = "你被要求準備一份報告，申請 NT$25,600 補充共用印表機及碳粉耗材。但該提案屬於例行性質且為日常營運所需，不太可能遭遇反對。",
            strengths = "若向 HP 批量採購，可以得到每單位 NT$480 的優惠，降低單位成本並建立備用庫存。",
            weaknesses = "將 NT$25,600 用於補充耗材，未增加新的營運能力。",
            opportunities = "穩定的供應可以減少印表機中斷、保障報告截止期限，並降低緊急採購成本。",
            threats = "HP 的價格上漲、供應短缺或運送延遲，可能削弱預期節省的效益，使耗材庫存不足。",
        },
        ["low_C"] = new SwotContent {
            title = "咖啡機",
            scenario = "你被要求準備一份簡要報告，從設施預算中申請 NT$38,400 以更換員工休息室中老舊的咖啡機。雖然更換咖啡機受到員工支持，但該提案對更廣泛的營運影響有限。",
            strengths = "可以利用 Nespresso 的企業折扣，將更換成本控制在 NT$38,400。",
            weaknesses = "將 NT$38,400 投入小型員工福祉設施，對營運的直接影響不大。",
            opportunities = "一台可靠的新咖啡機可以提升員工士氣，也使休息時間更有效率。",
            threats = "Nespresso 可能在報告核准前更改企業價格或停產所選型號。",
        },
        ["low_D"] = new SwotContent {
            title = "會議室白板",
            scenario = "你被要求寫一份報告，申請 NT$48,000 更換兩間會議室中損壞的白板。該提案規模較小，不太可能引起爭議。",
            strengths = "可以用 STAPLES 的批量價格，得到每單位 NT$3,200 的優惠，使更換白板具有成本效益。",
            weaknesses = "需要 NT$48,000 用於一項有用但非營運必需的小型設施升級。",
            opportunities = "更清晰的白板能讓會議討論更順暢，同時大幅提升團隊協作與腦力激盪的成效。",
            threats = "STAPLES 的定價、庫存或配送時程可能在採購前發生變動，導致成本增加或安裝延遲。",
        },
        ["high_E"] = new SwotContent {
            title = "客戶導入專案",
            scenario = "你被要求準備一份說明報告，申請 NT$6,400 萬的資金，用於推動「企業客戶入駐流程最佳化」計畫。此計畫若成功，將能縮短營收實現週期、提升客戶留存率，並顯著推動該專案與團隊的職涯發展。",
            strengths = "已有試行階段數據支持，客戶入駐時間成功從 30 天縮短至 18 天，並創造約 NT$1,600 萬的成本效益。",
            weaknesses = "需要 NT$6,400 萬的投資，且全面推廣至全公司時，將面臨顯著的系統導入與變革管理挑戰。",
            opportunities = "成功推廣可帶動營收成長、提高客戶留存率，並大幅提升團隊在企業內部的能見度與影響力。",
            threats = "客戶需求的變動、法規遵循要求，或外部整合的相依性，都可能延後計畫時程，並降低預期可帶來的效益。",
        },
        ["high_F"] = new SwotContent {
            title = "國際擴張試行計畫",
            scenario = "你被要求準備一份說明報告，申請 NT$9,600 萬的資金，用於一項將營運據點拓展至倫敦與北京的試行計畫。若獲核准，可望開啟重大的成長契機，並提升你與團隊的能見度；但若成效不佳，則可能使更大規模的擴張停滯。",
            strengths = "試行計畫的架構可在公司承諾全面擴張前，先測試市場可行性，並蒐集在地佐證。",
            weaknesses = "該提案需要 NT$9,600 萬，並在兩個性質不同的國際市場之間產生龐大的協調與資源需求。",
            opportunities = "兩個目標城市約 20% 的年度市場成長，可望帶來新的營收、更廣的市場觸及，以及更高的團隊能見度。",
            threats = "法規變動、匯率波動、當地競爭或文化落差，都可能損害成效，並造成財務或商譽上的損失。",
        },
        ["high_G"] = new SwotContent {
            title = "企業軟體升級",
            scenario = "你被要求準備一份說明報告，申請 NT$4,800 萬將公司核心系統遷移至新的企業軟體平台。若獲批准，可望提升全公司的生產力，並使你的團隊成為重要變革的推動人物；但若導入過程出現問題，則可能引發外界對可行性的疑慮。",
            strengths = "此提案將核心系統整合至單一平台，統一各項作業流程，預估可節省約 NT$640 萬。",
            weaknesses = "需要 NT$4,800 萬的投資，並涉及廠商評選、系統遷移、教育訓練與變革管理等複雜工作。",
            opportunities = "一旦整合成功，可望降低停機時間、提升全公司生產力、精簡跨團隊協作，並確立團隊作為高效變革推動人物的地位。",
            threats = "廠商表現不如預期、導入時程延宕、資安問題或整合失敗，都可能干擾營運，並削弱外界對領導層的信任。",
        },
        ["high_H"] = new SwotContent {
            title = "策略合作夥伴關係",
            scenario = "你被要求準備一份專案論證報告，建議公司投入 NT$8,000 萬與溫莎銀行建立策略夥伴關係。若獲批准，可望取得寶貴的資源，並增強你的專業地位；但若成效不佳，則可能在高階主管層面損及你的信譽。",
            strengths = "該提案使公司能比自行建構更快地取得溫莎銀行的資源、能力與市場覆蓋範圍。",
            weaknesses = "需要投入 NT$8,000 萬，短期回報並不確定，且因成敗結果有一部分取決於合作夥伴，而降低公司的自主掌控權。",
            opportunities = "該合作可望帶動約 25% 的市場成長，擴大公司的觸及範圍，並強化其競爭地位。",
            threats = "雙方目標不一致、合作表現不如預期、法規上的複雜問題，或涉及溫莎銀行的商譽風險，都可能降低合作的價值。",
        },
    };
}
