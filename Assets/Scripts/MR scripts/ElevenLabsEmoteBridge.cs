using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bridges ElevenLabs agent responses to the avatar's SALSA-driven EmoterController.
///
/// The ElevenLabs agent is instructed (via its dashboard system prompt) to lead a
/// sentence with an Eleven v3 emotion audio tag such as [happy] or [sad]. With the
/// "Eleven v3 Conversational" model and "strip audio tags" turned OFF, those tags:
///   - are spoken with the matching emotional delivery (handled by ElevenLabs), and
///   - still arrive in the agent transcript we receive here, so we can parse them
///     and trigger the matching facial emote on the avatar.
///
/// Setup:
/// 1. Attach to the same GameObject as ElevenLabsConnection (+ AgentVoiceController).
/// 2. AvatarDeskPlacer calls SetEmoter() with the spawned avatar's EmoterController.
///
/// Keep the tag list below in sync with the tags you configured on the agent.
/// </summary>
public class ElevenLabsEmoteBridge : MonoBehaviour
{
    public enum Emotion { None, Positive, Negative, Neutral, Thinking }

    [Header("References")]
    [Tooltip("ElevenLabs connection to listen to. Auto-found if left empty.")]
    public ElevenLabsConnection connection;

    [Tooltip("The avatar's EmoterController. Assigned at runtime by AvatarDeskPlacer after spawn.")]
    public EmoterController emoter;

    [Header("Behaviour")]
    [Tooltip("Emote to fire when an agent reply contains no recognised tag. Set to None to do nothing.")]
    public Emotion fallbackEmotion = Emotion.Neutral;

    [Tooltip("Log each detected tag and the emote fired.")]
    public bool verbose = true;

    /// <summary>Fires whenever an emote is triggered from an agent response (including the
    /// fallback and None-when-no-tag-found). Lets a debug HUD show what the bridge detected.</summary>
    public event System.Action<Emotion> OnEmotionTriggered;

    // Tag (without brackets) -> emotion. MUST match the tags set up on the agent.
    private readonly Dictionary<string, Emotion> tagMap = new Dictionary<string, Emotion>(StringComparer.OrdinalIgnoreCase)
    {
        // RECOGNISED Eleven v3 audio tags — interpreted as delivery and rarely spoken aloud.
        // Make the agent's dashboard system prompt emit ONLY these.
        { "happy",       Emotion.Positive },
        { "excited",     Emotion.Positive },
        { "excitedly",   Emotion.Positive },
        { "cheerful",    Emotion.Positive },
        { "laughs",      Emotion.Positive },
        { "laughing",    Emotion.Positive },
        { "sad",            Emotion.Negative },
        { "sighs",          Emotion.Negative },
        { "empathetic",     Emotion.Negative },  // ElevenLabs default delivery tag (replaces [sympathetic])
        { "empathetically", Emotion.Negative },

        // LEGACY / non-standard tags. NOT well-recognised v3 audio tags, so the TTS
        // occasionally reads them aloud (e.g. "[curious]"). Kept only so older agent configs
        // still map to an emote — prefer the recognised tags above, or a tool call.
        { "sympathetic", Emotion.Negative },
        { "curious",     Emotion.Neutral  },
        { "thoughtful",  Emotion.Thinking },
    };

    private void Start()
    {
        if (connection == null) connection = GetComponent<ElevenLabsConnection>();
        if (connection == null) connection = FindObjectOfType<ElevenLabsConnection>();

        if (connection != null)
        {
            connection.OnTranscriptDone += HandleAgentText;
        }
        else
        {
            Debug.LogWarning("ElevenLabsEmoteBridge: No ElevenLabsConnection found. Emotes disabled.");
        }
    }

    private void OnDestroy()
    {
        if (connection != null)
            connection.OnTranscriptDone -= HandleAgentText;
    }

    /// <summary>Called by AvatarDeskPlacer once the avatar (with EmoterController) exists.</summary>
    public void SetEmoter(EmoterController controller)
    {
        emoter = controller;
        if (verbose) Debug.Log("ElevenLabsEmoteBridge: EmoterController linked.");
    }

    private void HandleAgentText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Emotion emotion = DetectEmotion(text);
        if (emotion == Emotion.None) emotion = fallbackEmotion;

        if (verbose) Debug.Log($"ElevenLabsEmoteBridge: emotion={emotion}  text=\"{text}\"");

        TriggerEmote(emotion);
    }

    /// <summary>Returns the emotion of the FIRST recognised "[tag]" found in the text.</summary>
    private Emotion DetectEmotion(string text)
    {
        int bestIndex = int.MaxValue;
        Emotion found = Emotion.None;

        foreach (var pair in tagMap)
        {
            int idx = text.IndexOf("[" + pair.Key + "]", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < bestIndex)
            {
                bestIndex = idx;
                found = pair.Value;
            }
        }
        return found;
    }

    /// <summary>Fires the matching EmoterController event (also callable from other scripts).</summary>
    public void TriggerEmote(Emotion emotion)
    {
        // Notify debug listeners first, so the readout shows what was detected even if the
        // emoter isn't linked yet (or the emotion is None / fallback).
        OnEmotionTriggered?.Invoke(emotion);

        if (emoter == null)
        {
            if (verbose) Debug.LogWarning("ElevenLabsEmoteBridge: No EmoterController linked yet; skipping emote.");
            return;
        }

        switch (emotion)
        {
            case Emotion.Positive: emoter.emoterEventPositive?.Invoke(); break;
            case Emotion.Negative: emoter.emoterEventNegative?.Invoke(); break;
            case Emotion.Neutral:  emoter.emoterEventNeutral?.Invoke();  break;
            case Emotion.Thinking: emoter.emoterEventThinking?.Invoke(); break;
            default: break; // Emotion.None -> do nothing
        }
    }
}
