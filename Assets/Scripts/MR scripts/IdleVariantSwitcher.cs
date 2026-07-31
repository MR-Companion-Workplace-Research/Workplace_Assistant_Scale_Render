using UnityEngine;

/// <summary>
/// Slowly rotates the avatar between its resting idle poses so it doesn't loop the same
/// idle for the whole session.
///
/// It does NOT drive the transition clips itself — the Animator Controller owns that. This
/// script only flips a single BOOL parameter (HandsOnThigh); the controller then plays the
/// authored transition clip in whichever direction is needed:
///
///     Sitting Idle  --[HandsOnThigh = true]-->  transition_to_hands_on_thigh --> Hand on thigh
///     Hand on thigh --[HandsOnThigh = false]--> transition_reverse           --> Sitting Idle
///
/// Both of those transitions have "Has Exit Time" ON, so a flip never cuts the idle clip
/// mid-cycle — the avatar finishes the loop it is in and then changes over. That means the
/// switch lands up to one idle-loop AFTER the timer fires, which is what you want visually.
///
/// Timing: every dwellSeconds (random in [min,max]) it rolls switchChance. On success it
/// flips to the other idle; on failure it stays put and waits another dwell. So switchChance
/// = 1 gives "change every 45-60s", and lower values make changes rarer and less predictable.
///
/// SETUP: drop on the same GameObject as ElevenLabsConnection / AvatarBodyGestures. The
/// avatar spawns at runtime, so the Animator is resolved lazily from AvatarPlacer.
/// Controllers without a HandsOnThigh parameter (e.g. the miniature one) are ignored.
/// </summary>
[DisallowMultipleComponent]
public class IdleVariantSwitcher : MonoBehaviour
{
    private const string BoolName = "HandsOnThigh";

    [Header("References")]
    [Tooltip("The avatar's EmoterController. Auto-resolved from AvatarPlacer's spawned avatar.")]
    public EmoterController emoter;

    [Header("Timing")]
    [Tooltip("Turn the idle rotation on/off without removing the component.")]
    public bool enableSwitching = true;

    [Tooltip("Shortest time to stay in one idle before considering a change.")]
    public float minDwellSeconds = 45f;

    [Tooltip("Longest time to stay in one idle before considering a change.")]
    public float maxDwellSeconds = 60f;

    [Tooltip("Chance (0-1) of actually changing idle when the dwell timer elapses. 1 = always " +
             "change; lower values make the avatar sometimes hold its current pose longer.")]
    [Range(0f, 1f)] public float switchChance = 1f;

    [Header("Debug")]
    [Tooltip("Log each idle change.")]
    public bool verbose = true;

    private AvatarPlacer avatarPlacer;
    private Animator animator;
    private float nextDecisionTime;
    private bool handsOnThigh;

    // Cached "does the CURRENT controller know HandsOnThigh?" — AvatarPlacer swaps the
    // controller per Scale condition, so re-check whenever it changes.
    private RuntimeAnimatorController checkedController;
    private bool controllerHasBool;

    private void Start()
    {
        avatarPlacer = FindObjectOfType<AvatarPlacer>();
        ScheduleNextDecision();
    }

    private void Update()
    {
        if (!enableSwitching) return;
        if (Time.time < nextDecisionTime) return;

        ScheduleNextDecision();

        if (!ResolveAnimator()) return;

        if (Random.value > switchChance)
        {
            if (verbose) Debug.Log("IdleVariantSwitcher: holding the current idle this round.");
            return;
        }

        handsOnThigh = !handsOnThigh;
        animator.SetBool(BoolName, handsOnThigh);

        if (verbose)
            Debug.Log($"IdleVariantSwitcher: {BoolName} -> {handsOnThigh} " +
                      $"({(handsOnThigh ? "Hand on thigh" : "Sitting Idle")}). The controller plays " +
                      "the transition clip at the end of the current idle loop.");
    }

    private void ScheduleNextDecision()
    {
        float min = Mathf.Max(1f, minDwellSeconds);
        float max = Mathf.Max(min, maxDwellSeconds);
        nextDecisionTime = Time.time + Random.Range(min, max);
    }

    /// <summary>Finds the spawned avatar's Animator and confirms the attached controller
    /// actually declares the bool — otherwise SetBool is a silent no-op.</summary>
    private bool ResolveAnimator()
    {
        if (emoter == null)
        {
            if (avatarPlacer == null) avatarPlacer = FindObjectOfType<AvatarPlacer>();
            var avatar = avatarPlacer != null ? avatarPlacer.GetSpawnedAvatar() : null;
            if (avatar != null) emoter = avatar.GetComponentInChildren<EmoterController>();
        }

        animator = emoter != null && emoter.animatorCtrl != null ? emoter.animatorCtrl.Anim : null;
        if (animator == null || animator.runtimeAnimatorController == null) return false;

        if (animator.runtimeAnimatorController != checkedController)
        {
            checkedController = animator.runtimeAnimatorController;
            controllerHasBool = false;
            foreach (var p in animator.parameters)
                if (p.type == AnimatorControllerParameterType.Bool && p.name == BoolName)
                { controllerHasBool = true; break; }

            // The controller was just swapped in — its bool starts false, so match our state
            // to it rather than carrying the previous condition's pose over.
            handsOnThigh = false;

            if (!controllerHasBool && verbose)
                Debug.Log($"IdleVariantSwitcher: '{checkedController.name}' has no {BoolName} " +
                          "parameter — idle switching is inactive in this condition.");
        }
        return controllerHasBool;
    }
}
