using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the avatar's BODY animations (wave / gesture / head-nod) from ElevenLabs
/// conversation events, through the prefab's EmoterController -> animDriver -> Animator
/// triggers (Reallusion_Animator.controller: WaveTrigger / GestureTrigger / HeadNodTrigger).
///
///  - Greeting WAVE: fires once each time a conversation connects (OnConnected).
///  - Speech gestures: each time the agent begins a response there is a configurable
///    chance (gestureChance) to play a gesture at all — or nothing, so the avatar isn't
///    constantly gesturing. When one does play, headNodShare splits it between the Head
///    Nod and the talking gestures (Gesturing / Gesturing 2, picked alternately).
///
/// Facial emotes are handled separately by ElevenLabsEmoteBridge; this component only
/// fires the BODY triggers that nothing was invoking after the OpenAI -> ElevenLabs move.
///
/// SETUP: drop this on a persistent scene object (e.g. the same GameObject as
/// ElevenLabsConnection). The avatar spawns at runtime, so the EmoterController is
/// resolved lazily from AvatarPlacer's spawned instance — no manual wiring needed.
/// </summary>
[DisallowMultipleComponent]
public class AvatarBodyGestures : MonoBehaviour
{
    [Header("References")]
    [Tooltip("ElevenLabs connection to listen to. Auto-found if left empty.")]
    public ElevenLabsConnection connection;

    [Tooltip("The avatar's EmoterController. Auto-resolved from AvatarPlacer's spawned avatar.")]
    public EmoterController emoter;

    [Header("Greeting")]
    [Tooltip("Play a wave once when the conversation connects.")]
    public bool waveOnConnect = true;

    [Header("Speech gestures")]
    [Tooltip("Chance (0-1) that a gesture plays when the agent begins a response. The rest " +
             "of the time the avatar stays still, so gesturing isn't constant.")]
    [Range(0f, 1f)] public float gestureChance = 0.7f;

    [Tooltip("Of the turns that DO gesture, the share (0-1) that plays 'Head Nod'. The " +
             "remainder plays a talking gesture, split evenly across the enabled gesture " +
             "variants. 0.5 = half nods, half gestures. Ignored (forced to 1) while the avatar " +
             "is in an idle tagged IdleNodOnly, which has no talking gesture to play.")]
    [Range(0f, 1f)] public float headNodShare = 0.5f;

    [Tooltip("Include the 'Gesturing' animation in the random pool.")]
    public bool includeGesture = true;

    [Tooltip("Include the second 'Gesturing 2' animation in the random pool. Falls back to " +
             "'Gesturing' on controllers that have no Gesture2Trigger (e.g. the human-sized one).")]
    public bool includeGesture2 = true;

    [Tooltip("Include the 'Head Nod' animation in the random pool.")]
    public bool includeHeadNod = true;

    [Tooltip("Suppress speech gestures for this many seconds after the greeting wave so the " +
             "first gesture doesn't stomp the wave.")]
    public float postWaveCooldown = 2.5f;

    [Tooltip("Grace period for a fired trigger to be consumed by a transition. If the current " +
             "idle has no transition for it, the trigger is dropped after this long instead of " +
             "firing later. Must stay above one frame; 0.5s is plenty.")]
    public float staleTriggerTimeout = 0.5f;

    [Header("Debug")]
    [Tooltip("Log when the wave/gestures fire, to diagnose wiring.")]
    public bool verbose = true;

    private AvatarPlacer avatarPlacer;
    private float lastWaveTime = -999f;
    private int lastGestureVariant = -1; // alternate between the talking gestures

    // The spawned avatar's Animator, refreshed by ResolveEmoter().
    private Animator animator;

    // Cached "does the CURRENT controller know Gesture2Trigger?" — the controller is swapped
    // per Scale condition by AvatarPlacer, so re-check whenever it changes.
    private RuntimeAnimatorController checkedController;
    private bool controllerHasGesture2;

    private void Start()
    {
        if (connection == null) connection = FindObjectOfType<ElevenLabsConnection>();
        if (avatarPlacer == null) avatarPlacer = FindObjectOfType<AvatarPlacer>();

        if (connection != null)
        {
            connection.OnConnected += HandleConnected;
            connection.OnNewResponse += HandleNewResponse;
            if (verbose) Debug.Log("AvatarBodyGestures: subscribed to ElevenLabsConnection (OnConnected, OnNewResponse).");
        }
        else
        {
            Debug.LogWarning("AvatarBodyGestures: No ElevenLabsConnection found. Body gestures disabled.");
        }
    }

    private void OnDestroy()
    {
        if (connection != null)
        {
            connection.OnConnected -= HandleConnected;
            connection.OnNewResponse -= HandleNewResponse;
        }
    }

    // Greeting wave when the conversation connects.
    private void HandleConnected()
    {
        if (verbose) Debug.Log("AvatarBodyGestures: OnConnected fired.");
        if (!waveOnConnect) return;
        if (ResolveEmoter() == null) return;
        lastWaveTime = Time.time;
        if (verbose) Debug.Log("AvatarBodyGestures: invoking greeting wave.");
        emoter.waveAnim?.Invoke();
    }

    // Random gesture (or nothing) when the agent starts a new spoken response.
    private void HandleNewResponse()
    {
        if (Time.time < lastWaveTime + postWaveCooldown) return; // don't clobber the greeting
        if (ResolveEmoter() == null) return;

        // Discard anything left over from an earlier turn BEFORE deciding. Unity triggers
        // latch until a transition consumes them, so a trigger set while the avatar could
        // not react would otherwise fire much later, with the agent silent.
        ClearPendingTriggers();

        // Only fire when the avatar is actually resting in an idle it can gesture out of.
        if (!ReadyToGesture()) return;

        if (Random.value > gestureChance) return; // stayed still this turn

        bool gesturesOn = includeGesture || (includeGesture2 && HasGesture2());

        // An idle tagged IdleNodOnly (e.g. "Hand on thigh") has no talking-gesture transition
        // at all, so every gesturing turn there is a head nod rather than a dropped trigger.
        float nodShare = NodOnlyIdle() ? 1f : headNodShare;

        // Split by CATEGORY first (headNodShare), so adding gesture variants dilutes the
        // gestures among themselves rather than eating into the head-nod share.
        if (includeHeadNod && (!gesturesOn || Random.value < nodShare))
        {
            if (verbose) Debug.Log("AvatarBodyGestures: speech gesture -> Head Nod.");
            emoter.headnodAnim?.Invoke();
            StartCoroutine(DropTriggerIfUnused());
            return;
        }

        if (!gesturesOn) return; // nothing enabled

        PlayGestureVariant();
    }

    /// <summary>
    /// Drops a trigger that the current state had no transition for. Not every idle answers
    /// every trigger — "Hand on thigh" only has a Head Nod — and an unanswered trigger stays
    /// latched until some LATER state consumes it, which is what made gestures appear after
    /// an idle change. A transition that does exist has no exit time, so it is taken within a
    /// frame; anything still pending after this short grace period was never going to play.
    /// </summary>
    private IEnumerator DropTriggerIfUnused()
    {
        yield return new WaitForSeconds(staleTriggerTimeout);

        if (animator != null && animator.GetCurrentAnimatorStateInfo(GestureLayer()).IsTag("Idle")
            && !animator.IsInTransition(GestureLayer()))
        {
            if (verbose) Debug.Log("AvatarBodyGestures: gesture had no transition from this idle — dropping the trigger.");
            ClearPendingTriggers();
        }
    }

    // Picks one of the enabled talking gestures, alternating so the same one doesn't repeat.
    private void PlayGestureVariant()
    {
        var variants = new List<int>(2);
        if (includeGesture) variants.Add(0);
        if (includeGesture2 && HasGesture2()) variants.Add(1);

        int pick = variants[0];
        if (variants.Count > 1)
        {
            do { pick = variants[Random.Range(0, variants.Count)]; }
            while (pick == lastGestureVariant);
        }
        lastGestureVariant = pick;

        if (pick == 1)
        {
            if (verbose) Debug.Log("AvatarBodyGestures: speech gesture -> Gesturing 2.");
            emoter.gesture2Anim?.Invoke();
        }
        else
        {
            if (verbose) Debug.Log("AvatarBodyGestures: speech gesture -> Gesturing.");
            emoter.gestureAnim?.Invoke();
        }

        StartCoroutine(DropTriggerIfUnused());
    }

    /// <summary>The layer the gesture states live on ("Upper Body" when present, else 0).</summary>
    private int GestureLayer()
    {
        int i = animator.GetLayerIndex("Upper Body");
        return i >= 0 ? i : 0;
    }

    /// <summary>
    /// True only when the avatar is sitting in a state tagged "Idle" and not already
    /// blending somewhere. Firing at any other moment is what caused gestures to appear
    /// AFTER an idle change: the trigger could not be consumed at the time (the idle
    /// transition clips have no gesture transitions), Unity latched it, and it fired the
    /// instant the avatar reached an idle again — long after the agent stopped talking.
    ///
    /// States with no tag at all are allowed through, so a controller that has not been
    /// tagged still gestures as before.
    /// </summary>
    private bool ReadyToGesture()
    {
        if (animator == null) return false;

        int layer = GestureLayer();
        if (animator.IsInTransition(layer))
        {
            if (verbose) Debug.Log("AvatarBodyGestures: mid-transition — skipping this turn.");
            return false;
        }

        var state = animator.GetCurrentAnimatorStateInfo(layer);
        if (state.tagHash != 0 && !state.IsTag("Idle") && !state.IsTag("IdleNodOnly"))
        {
            if (verbose) Debug.Log("AvatarBodyGestures: avatar is not in an Idle state — skipping this turn.");
            return false;
        }
        return true;
    }

    /// <summary>True while resting in an idle whose only gesture transition is the head nod.</summary>
    private bool NodOnlyIdle()
    {
        return animator != null &&
               animator.GetCurrentAnimatorStateInfo(GestureLayer()).IsTag("IdleNodOnly");
    }

    /// <summary>Drops any gesture trigger that was set but never consumed, so it cannot fire
    /// late. Resetting an already-consumed trigger is a harmless no-op.</summary>
    private void ClearPendingTriggers()
    {
        if (animator == null) return;
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger)
                animator.ResetTrigger(p.nameHash);
    }

    /// <summary>True if the Animator Controller currently attached to the avatar declares
    /// Gesture2Trigger. The miniature controller does; the human-sized one may not, and a
    /// missing trigger would silently swallow the gesture and leave the turn dead.</summary>
    private bool HasGesture2()
    {
        var anim = animator;
        if (anim == null || anim.runtimeAnimatorController == null) return false;

        if (anim.runtimeAnimatorController != checkedController)
        {
            checkedController = anim.runtimeAnimatorController;
            controllerHasGesture2 = false;
            foreach (var p in anim.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "Gesture2Trigger")
                { controllerHasGesture2 = true; break; }

            if (!controllerHasGesture2 && verbose)
                Debug.Log($"AvatarBodyGestures: '{checkedController.name}' has no Gesture2Trigger — " +
                          "the second gesture falls back to 'Gesturing' in this condition.");
        }
        return controllerHasGesture2;
    }

    private EmoterController ResolveEmoter()
    {
        if (emoter == null)
        {
            if (avatarPlacer == null) avatarPlacer = FindObjectOfType<AvatarPlacer>();
            var avatar = avatarPlacer != null ? avatarPlacer.GetSpawnedAvatar() : null;
            if (avatar != null) emoter = avatar.GetComponentInChildren<EmoterController>();
            if (emoter == null)
            {
                Debug.LogWarning("AvatarBodyGestures: EmoterController not found on the spawned avatar yet.");
                return null;
            }
        }

        animator = emoter.animatorCtrl != null ? emoter.animatorCtrl.Anim : null;
        return emoter;
    }
}
