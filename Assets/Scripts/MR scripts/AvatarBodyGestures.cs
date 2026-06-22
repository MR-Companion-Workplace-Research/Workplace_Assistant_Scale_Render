using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the avatar's BODY animations (wave / gesture / head-nod) from ElevenLabs
/// conversation events, through the prefab's EmoterController -> animDriver -> Animator
/// triggers (Reallusion_Animator.controller: WaveTrigger / GestureTrigger / HeadNodTrigger).
///
///  - Greeting WAVE: fires once each time a conversation connects (OnConnected).
///  - Speech gestures: each time the agent begins a response there is a configurable
///    chance to play a RANDOM gesture (Gesturing or Head Nod) — or nothing at all, so
///    the avatar isn't constantly gesturing.
///
/// Facial emotes are handled separately by ElevenLabsEmoteBridge; this component only
/// fires the BODY triggers that nothing was invoking after the OpenAI -> ElevenLabs move.
///
/// SETUP: drop this on a persistent scene object (e.g. the same GameObject as
/// ElevenLabsConnection). The avatar spawns at runtime, so the EmoterController is
/// resolved lazily from AvatarDeskPlacer's spawned instance — no manual wiring needed.
/// </summary>
[DisallowMultipleComponent]
public class AvatarBodyGestures : MonoBehaviour
{
    [Header("References")]
    [Tooltip("ElevenLabs connection to listen to. Auto-found if left empty.")]
    public ElevenLabsConnection connection;

    [Tooltip("The avatar's EmoterController. Auto-resolved from AvatarDeskPlacer's spawned avatar.")]
    public EmoterController emoter;

    [Header("Greeting")]
    [Tooltip("Play a wave once when the conversation connects.")]
    public bool waveOnConnect = true;

    [Header("Speech gestures")]
    [Tooltip("Chance (0-1) that a gesture plays when the agent begins a response. The rest " +
             "of the time the avatar stays still, so gesturing isn't constant.")]
    [Range(0f, 1f)] public float gestureChance = 0.6f;

    [Tooltip("Include the 'Gesturing' animation in the random pool.")]
    public bool includeGesture = true;

    [Tooltip("Include the 'Head Nod' animation in the random pool.")]
    public bool includeHeadNod = true;

    [Tooltip("Suppress speech gestures for this many seconds after the greeting wave so the " +
             "first gesture doesn't stomp the wave.")]
    public float postWaveCooldown = 2.5f;

    [Header("Debug")]
    [Tooltip("Log when the wave/gestures fire, to diagnose wiring.")]
    public bool verbose = true;

    private AvatarDeskPlacer deskPlacer;
    private float lastWaveTime = -999f;
    private int lastPick = -1; // avoid repeating the same gesture back-to-back

    private void Start()
    {
        if (connection == null) connection = FindObjectOfType<ElevenLabsConnection>();
        if (deskPlacer == null) deskPlacer = FindObjectOfType<AvatarDeskPlacer>();

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

        if (Random.value > gestureChance) return; // stayed still this turn

        int pick = ChooseGesture();
        switch (pick)
        {
            case 0: if (verbose) Debug.Log("AvatarBodyGestures: speech gesture -> Gesturing."); emoter.gestureAnim?.Invoke(); break;
            case 1: if (verbose) Debug.Log("AvatarBodyGestures: speech gesture -> Head Nod."); emoter.headnodAnim?.Invoke(); break;
            default: return; // nothing enabled
        }
        lastPick = pick;
    }

    // Returns 0 = Gesturing, 1 = Head Nod, -1 = nothing enabled. Avoids immediate repeats.
    private int ChooseGesture()
    {
        var options = new List<int>(2);
        if (includeGesture) options.Add(0);
        if (includeHeadNod) options.Add(1);

        if (options.Count == 0) return -1;
        if (options.Count == 1) return options[0];

        int pick;
        do { pick = options[Random.Range(0, options.Count)]; }
        while (pick == lastPick);
        return pick;
    }

    private EmoterController ResolveEmoter()
    {
        if (emoter != null) return emoter;
        if (deskPlacer == null) deskPlacer = FindObjectOfType<AvatarDeskPlacer>();
        var avatar = deskPlacer != null ? deskPlacer.GetSpawnedAvatar() : null;
        if (avatar != null) emoter = avatar.GetComponentInChildren<EmoterController>();
        if (emoter == null)
            Debug.LogWarning("AvatarBodyGestures: EmoterController not found on the spawned avatar yet.");
        return emoter;
    }
}
