using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// On-headset diagnostics for the facial emote system — for when you can't see the logs.
///
///  - MANUAL TEST: call TestPositive / TestNegative / TestNeutral / TestThinking (bind them
///    to the Controller Buttons Mapper). Each fires the SAME EmoterController event the agent
///    path uses, so if the avatar's face changes, the whole emote->face chain works and the
///    only remaining question is whether the agent is emitting [happy]/[sad]/... tags.
///  - LIVE READOUT: a small floating label shows what ElevenLabsEmoteBridge detected from each
///    agent reply ("AGENT -> Positive", or "None" when no tag was found) and your manual tests
///    ("TEST -> Positive"). So you can also see whether tags are arriving at all.
///
/// Drop on a persistent scene object (e.g. the ElevenLabsConnection GameObject). The avatar is
/// resolved lazily from AvatarPlacer's spawned instance — no manual wiring needed.
/// Remove this component (or set Show Hud off) for real study runs.
/// </summary>
[DisallowMultipleComponent]
public class EmoteDebugger : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The emote bridge to watch. Auto-found if left empty.")]
    public ElevenLabsEmoteBridge bridge;

    [Header("Floating readout")]
    [Tooltip("Show a floating label above the avatar reporting each emote fired.")]
    public bool showHud = true;

    [Tooltip("Seconds the label stays visible after each emote.")]
    public float hideSeconds = 4f;

    [Tooltip("Physical width of the label in meters.")]
    public float labelWidthMeters = 0.3f;

    [Tooltip("Offset above the avatar's head, in meters.")]
    public Vector3 offset = new Vector3(0f, 0.35f, 0f);

    [Tooltip("Optional TMP font. Leave empty to use the project's default.")]
    public TMP_FontAsset fontOverride;

    private const float RefWidth = 600f;
    private const float RefHeight = 150f;

    private AvatarPlacer avatarPlacer;
    private EmoterController emoter;
    private Transform cam;

    private GameObject canvasGO;
    private RectTransform canvasRect;
    private TextMeshProUGUI label;
    private float hideAt;
    private bool showing;
    private float headHeight = 1.5f;
    private bool measured;

    private void Start()
    {
        if (bridge == null) bridge = FindObjectOfType<ElevenLabsEmoteBridge>();
        avatarPlacer = FindObjectOfType<AvatarPlacer>();
        if (Camera.main != null) cam = Camera.main.transform;

        if (bridge != null) bridge.OnEmotionTriggered += OnAgentEmotion;
        else Debug.LogWarning("EmoteDebugger: No ElevenLabsEmoteBridge found; live readout disabled (manual tests still work).");
    }

    private void OnDestroy()
    {
        if (bridge != null) bridge.OnEmotionTriggered -= OnAgentEmotion;
    }

    private void OnAgentEmotion(ElevenLabsEmoteBridge.Emotion e) => Show("AGENT → " + e);

    // ---- Manual tests: bind these to Controller Buttons Mapper buttons. ----
    public void TestPositive() => FireTest(ElevenLabsEmoteBridge.Emotion.Positive);
    public void TestNegative() => FireTest(ElevenLabsEmoteBridge.Emotion.Negative);
    public void TestNeutral()  => FireTest(ElevenLabsEmoteBridge.Emotion.Neutral);
    public void TestThinking() => FireTest(ElevenLabsEmoteBridge.Emotion.Thinking);

    // Cycle Positive -> Negative -> Neutral -> Thinking on each press, so a single button
    // can exercise every expression.
    private static readonly ElevenLabsEmoteBridge.Emotion[] cycle =
    {
        ElevenLabsEmoteBridge.Emotion.Positive,
        ElevenLabsEmoteBridge.Emotion.Negative,
        ElevenLabsEmoteBridge.Emotion.Neutral,
        ElevenLabsEmoteBridge.Emotion.Thinking,
    };
    private int cycleIndex = -1;
    public void CycleEmote()
    {
        cycleIndex = (cycleIndex + 1) % cycle.Length;
        FireTest(cycle[cycleIndex]);
    }

    private void FireTest(ElevenLabsEmoteBridge.Emotion e)
    {
        if (ResolveEmoter() == null)
        {
            Show("TEST → " + e + "  (no EmoterController!)");
            return;
        }

        switch (e)
        {
            case ElevenLabsEmoteBridge.Emotion.Positive: emoter.emoterEventPositive?.Invoke(); break;
            case ElevenLabsEmoteBridge.Emotion.Negative: emoter.emoterEventNegative?.Invoke(); break;
            case ElevenLabsEmoteBridge.Emotion.Neutral:  emoter.emoterEventNeutral?.Invoke();  break;
            case ElevenLabsEmoteBridge.Emotion.Thinking: emoter.emoterEventThinking?.Invoke(); break;
        }
        Show("TEST → " + e);
    }

    private EmoterController ResolveEmoter()
    {
        if (emoter != null) return emoter;
        var avatar = ResolveAvatarRoot();
        if (avatar != null) emoter = avatar.GetComponentInChildren<EmoterController>();
        return emoter;
    }

    private Transform ResolveAvatarRoot()
    {
        if (avatarPlacer == null) avatarPlacer = FindObjectOfType<AvatarPlacer>();
        var a = avatarPlacer != null ? avatarPlacer.GetSpawnedAvatar() : null;
        return a != null ? a.transform : null;
    }

    private void Show(string text)
    {
        Debug.Log("EmoteDebugger: " + text);
        if (!showHud) return;

        EnsureHud();
        label.text = text;
        canvasGO.SetActive(true);
        showing = true;
        hideAt = Time.time + Mathf.Max(0.5f, hideSeconds);
        Position();
    }

    private void LateUpdate()
    {
        if (!showing) return;
        if (Time.time >= hideAt) { canvasGO.SetActive(false); showing = false; return; }
        Position();
    }

    private void Position()
    {
        if (canvasRect == null) return;
        if (cam == null && Camera.main != null) cam = Camera.main.transform;

        var avatar = ResolveAvatarRoot();
        if (avatar == null) return;

        if (!measured)
        {
            var anim = avatar.GetComponentInChildren<Animator>();
            var head = anim != null ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head != null) { headHeight = head.position.y - avatar.position.y; measured = true; }
        }

        canvasRect.position = avatar.position + Vector3.up * headHeight + offset;
        if (cam != null)
        {
            Vector3 toCam = canvasRect.position - cam.position;
            if (toCam.sqrMagnitude > 1e-4f)
                canvasRect.rotation = Quaternion.LookRotation(toCam, Vector3.up);
        }
    }

    private void EnsureHud()
    {
        if (canvasGO != null) return;

        canvasGO = new GameObject("EmoteDebugger_HUD");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();
        canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(RefWidth, RefHeight);
        canvasRect.localScale = Vector3.one * (labelWidthMeters / RefWidth);

        var bg = new GameObject("BG", typeof(RectTransform));
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.SetParent(canvasRect, false);
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
        var img = bg.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.78f);
        img.raycastTarget = false;

        var t = new GameObject("Label", typeof(RectTransform));
        var trt = t.GetComponent<RectTransform>();
        trt.SetParent(canvasRect, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(12f, 8f); trt.offsetMax = new Vector2(-12f, -8f);
        label = t.AddComponent<TextMeshProUGUI>();
        if (fontOverride != null) label.font = fontOverride;
        label.fontSize = 48f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        label.raycastTarget = false;

        canvasGO.SetActive(false);
    }
}
