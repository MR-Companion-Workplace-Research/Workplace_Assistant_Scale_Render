using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Floating SWOT panel shown next to the avatar on demand.
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
///    manipulation, and its flat style does not change with realistic/toon. Only the
///    anchor HEIGHT tracks the avatar so the panel sits beside the head.
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
/// 3. (Optional) tune Display Seconds, Offset, and Panel Width Meters in the Inspector.
///
/// The avatar is found automatically (via AvatarDeskPlacer's spawned instance) at
/// show-time. The task is chosen from ElevenLabsConnection.taskKey automatically. For
/// solo testing without that script, set Task Key Override (e.g. "low_A").
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

    [Header("Timing")]
    [Tooltip("Seconds the panel stays up before auto-hiding. Each button press re-shows and " +
             "refreshes this timer. Tune to taste.")]
    public float displaySeconds = 10f;

    [Header("Placement (offset from the avatar, USER-relative)")]
    [Tooltip("X = the user's right (negative = left), Y = up, Z = away from the user. " +
             "Anchored to the avatar ROOT (+ head height), NOT the live head bone, so it does " +
             "not swing when the head animates.")]
    public Vector3 offset = new Vector3(0.55f, 0.05f, 0f);

    [Tooltip("If true, the head-height anchor is measured once from the avatar's Head bone at " +
             "Start (auto-adapts to the miniature/human-sized scale). If false, Anchor Height is used.")]
    public bool autoAnchorHeight = true;

    [Tooltip("World height above the avatar root to anchor the panel, used when Auto Anchor Height is off.")]
    public float anchorHeight = 1.5f;

    [Tooltip("If true, the panel always turns to face the user (billboard).")]
    public bool faceCamera = true;

    [Header("Panel size & style")]
    [Tooltip("Physical width of the panel in meters. Height follows the reference aspect. " +
             "CONSTANT across all conditions — do not drive this from the avatar scale.")]
    public float panelWidthMeters = 0.5f;

    [Tooltip("Optional TMP font. Leave empty to use the project's default TMP font.")]
    public TMP_FontAsset fontOverride;

    [Header("Input (optional fallback)")]
    [Tooltip("Leave OFF when using the Controller Buttons Mapper building block. If ON, this " +
             "component polls OVRInput directly so it works without the building block (handy for testing).")]
    public bool useBuiltInInput = false;

    [Header("References (optional)")]
    [Tooltip("The avatar to anchor beside. Leave empty to auto-find the avatar AvatarDeskPlacer " +
             "spawned at runtime. Set explicitly only if you anchor to a fixed avatar in the scene.")]
    public Transform avatarRoot;

    [Tooltip("Camera the panel faces / measures from. Falls back to Camera.main.")]
    public Transform cameraTransform;

    private AvatarDeskPlacer deskPlacer;
    private Transform anchorTarget; // resolved avatar root in use this show
    private bool anchorMeasured;

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
        if (deskPlacer == null) deskPlacer = FindObjectOfType<AvatarDeskPlacer>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        // Head-height is measured lazily on first show: the avatar is spawned at runtime,
        // so it may not exist yet here.
    }

    /// <summary>
    /// Resolve the avatar to anchor beside: explicit reference, else the instance
    /// AvatarDeskPlacer spawned, else this object (degenerate fallback).
    /// </summary>
    private Transform ResolveAvatarRoot()
    {
        if (avatarRoot != null) return avatarRoot;
        if (deskPlacer == null) deskPlacer = FindObjectOfType<AvatarDeskPlacer>();
        var spawned = deskPlacer != null ? deskPlacer.GetSpawnedAvatar() : null;
        return spawned != null ? spawned.transform : transform;
    }

    /// <summary>Measure head height once (auto-adapts to the avatar's scale) for the anchor.</summary>
    private void MeasureAnchorHeight(Transform root)
    {
        if (!autoAnchorHeight || anchorMeasured || root == null) return;
        var animator = root.GetComponentInChildren<Animator>();
        var head = animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null)
        {
            // World vertical distance root -> head, so it already reflects the avatar's scale.
            anchorHeight = head.position.y - root.position.y;
            anchorMeasured = true;
        }
    }

    // =========================================================================
    //  PUBLIC API — wire ShowSwot() to the Controller Buttons Mapper callback.
    // =========================================================================

    /// <summary>Show (or refresh) the panel for the current task and restart the auto-hide timer.</summary>
    public void ShowSwot()
    {
        string key = ResolveTaskKey();
        if (string.IsNullOrEmpty(key) || !SWOT.TryGetValue(key, out var content))
        {
            Debug.LogWarning($"SwotPanel: no SWOT content for task key '{key}'. " +
                "Set ElevenLabsConnection.taskKey (or Task Key Override) to one of: " +
                string.Join(", ", SWOT.Keys));
            return;
        }

        anchorTarget = ResolveAvatarRoot();
        MeasureAnchorHeight(anchorTarget);

        EnsureBuilt();
        Populate(content);
        canvasGO.SetActive(true);
        isShowing = true;
        hideAtTime = Time.time + Mathf.Max(0.1f, displaySeconds);
        PositionPanel(); // place immediately so it doesn't pop in at a stale spot
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

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        Transform root = anchorTarget != null ? anchorTarget : transform;

        // User-relative frame: positive X is always the user's right, regardless of which
        // way the avatar faces (same approach as HeadImageTag), anchored to the avatar ROOT
        // + head height so head animation doesn't make it swing.
        Vector3 anchor = root.position + Vector3.up * anchorHeight;
        Vector3 up = Vector3.up;
        Vector3 right;

        if (cameraTransform != null)
        {
            Vector3 toHead = anchor - cameraTransform.position;
            toHead.y = 0f;
            if (toHead.sqrMagnitude < 1e-4f) toHead = root.forward;
            right = Vector3.Cross(up, toHead.normalized);
        }
        else
        {
            right = root.right;
        }

        Vector3 forward = Vector3.Cross(right, up); // away from the user
        canvasRect.position = anchor + right * offset.x + up * offset.y + forward * offset.z;

        if (faceCamera && cameraTransform != null)
        {
            Vector3 toCamera = canvasRect.position - cameraTransform.position;
            if (toCamera.sqrMagnitude > 1e-4f)
                canvasRect.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }
        else
        {
            canvasRect.rotation = root.rotation;
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

        // Title + scenario
        titleText = MakeText(canvasRect, "Title", new Vector2(0.03f, 0.88f), new Vector2(0.97f, 0.99f),
            46f, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
        scenarioText = MakeText(canvasRect, "Scenario", new Vector2(0.03f, 0.63f), new Vector2(0.97f, 0.87f),
            27f, new Color(0.82f, 0.86f, 0.92f), TextAlignmentOptions.TopLeft, FontStyles.Normal);

        // 2x2 quadrant grid in the lower ~60%.
        var grid = MakePanel(canvasRect, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.61f),
            new Color(0, 0, 0, 0)); // transparent container
        var gridRect = grid.rectTransform;

        strengthsText     = BuildQuadrant(gridRect, "STRENGTHS",     new Color(0.45f, 0.85f, 0.55f),
            new Vector2(0.0f, 0.52f), new Vector2(0.49f, 1.0f));
        weaknessesText    = BuildQuadrant(gridRect, "WEAKNESSES",    new Color(0.95f, 0.5f, 0.5f),
            new Vector2(0.51f, 0.52f), new Vector2(1.0f, 1.0f));
        opportunitiesText = BuildQuadrant(gridRect, "OPPORTUNITIES", new Color(0.55f, 0.72f, 0.97f),
            new Vector2(0.0f, 0.0f), new Vector2(0.49f, 0.48f));
        threatsText       = BuildQuadrant(gridRect, "THREATS",       new Color(0.97f, 0.8f, 0.45f),
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

    private TextMeshProUGUI BuildQuadrant(RectTransform parent, string header, Color headerColor,
        Vector2 aMin, Vector2 aMax)
    {
        var cell = MakePanel(parent, aMin, aMax, new Color(1f, 1f, 1f, 0.05f));
        var cellRect = cell.rectTransform;

        // Quadrant label (STRENGTHS / WEAKNESSES / OPPORTUNITIES / THREATS). This is a
        // fixed header, so set its text here (the body text is filled later in Populate).
        var headerText = MakeText(cellRect, header + "_h", new Vector2(0f, 0.74f), new Vector2(1f, 1f),
            30f, headerColor, TextAlignmentOptions.TopLeft, FontStyles.Bold);
        headerText.text = header;

        return MakeText(cellRect, header + "_b", new Vector2(0f, 0f), new Vector2(1f, 0.74f),
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
    //  (low_A … high_H). Source: swot_scenarios.md (Schlesener 2025, Appendix H).
    //  Risk labels intentionally omitted (see class header).
    // =========================================================================

    private struct SwotContent
    {
        public string title, scenario, strengths, weaknesses, opportunities, threats;
    }

    private static readonly Dictionary<string, SwotContent> SWOT = new Dictionary<string, SwotContent>
    {
        ["low_A"] = new SwotContent {
            title = "Office Chairs",
            scenario = "Prepare a report requesting $500 to replace the chairs in the 3rd-floor meeting room. " +
                       "Not an urgent safety concern, but it affects comfort and professionalism in cross-team meetings.",
            strengths = "Save money buying in bulk from ULINE ($50/chair).",
            weaknesses = "Five maintenance reports filed on the current chairs this past quarter.",
            opportunities = "Improves comfort and professionalism in cross-team meetings.",
            threats = "Delaying could lead to minor safety complaints or poor client impressions.",
        },
        ["low_B"] = new SwotContent {
            title = "Printer Supplies",
            scenario = "Prepare a report requesting $800 to restock the shared printer supply cabinet. " +
                       "Routine and necessary for daily operations.",
            strengths = "Save money buying in bulk from HP ($15/unit).",
            weaknesses = "Four maintenance requests on the current system in the past week.",
            opportunities = "Reduce annual costs and prevent future shortages.",
            threats = "Printer interruptions may affect report deadlines if the order is delayed.",
        },
        ["low_C"] = new SwotContent {
            title = "Coffee Machine",
            scenario = "Prepare a brief report requesting $1,200 from the facilities budget to replace the " +
                       "worn coffee machine in the employee lounge.",
            strengths = "Corporate discount from Nespresso ($1,200/unit).",
            weaknesses = "Three maintenance requests this past month from the machine breaking down.",
            opportunities = "A new machine could boost morale and efficiency during work breaks.",
            threats = "Ongoing issues may lower staff satisfaction and reflect poor facilities management.",
        },
        ["low_D"] = new SwotContent {
            title = "Conference-Room Whiteboards",
            scenario = "Draft a report requesting $1,500 to replace damaged whiteboards in two conference rooms.",
            strengths = "STAPLES is offering a bulk discount ($100/unit).",
            weaknesses = "Current boards are stained and hard to read during meetings.",
            opportunities = "New boards improve visibility, collaboration, and brainstorming quality.",
            threats = "Poor visuals continue to waste meeting time and frustrate teams.",
        },
        ["high_E"] = new SwotContent {
            title = "Client Onboarding Project",
            scenario = "Prepare a justification report requesting $2 million to streamline enterprise client onboarding. " +
                       "Success reduces time-to-revenue and improves retention.",
            strengths = "Pilot cut onboarding 30 → 18 days; ~$500K savings shown.",
            weaknesses = "High scaling costs → $2 million.",
            opportunities = "Full rollout could increase revenue, retention, and team recognition.",
            threats = "Leadership may question the project's credibility and your management skills.",
        },
        ["high_F"] = new SwotContent {
            title = "International Expansion Pilot",
            // NOTE: source thesis is internally inconsistent ($3M ask vs $2.5M weakness). Reproduced
            // verbatim from swot_scenarios.md; align to one figure in your own stimuli if desired.
            scenario = "Prepare a justification report requesting $3 million for a pilot to expand operations " +
                       "into a new international market.",
            strengths = "Market growth ~20% annually in two new cities (London and Beijing).",
            weaknesses = "High entry costs → $2.5 million.",
            opportunities = "Enters a new market as a competitor and raises your visibility.",
            threats = "Financial losses and reputational damage could occur if executed poorly.",
        },
        ["high_G"] = new SwotContent {
            title = "Enterprise Software Upgrade",
            scenario = "Prepare a justification report requesting $1.5 million to migrate core company systems " +
                       "to a new enterprise software platform.",
            strengths = "Reduces current inefficiencies, saving ~$200K.",
            weaknesses = "Competing vendor proposals (Lucky and Barber) complicate selection and budgeting.",
            opportunities = "Reduce downtime and streamline cross-team collaboration.",
            threats = "Failure could harm productivity and trust in leadership.",
        },
        ["high_H"] = new SwotContent {
            title = "Strategic Partnership",
            scenario = "Prepare a justification report recommending a $2.5 million investment in a strategic " +
                       "partnership with a leading industry firm.",
            strengths = "Potential partner Windsor Banking offers ~25% market growth.",
            weaknesses = "Requires $2.5 million commitment with uncertain short-term returns.",
            opportunities = "Expands market reach and strengthens competitive standing.",
            threats = "Goal misalignment could cause reputational risk.",
        },
    };
}
