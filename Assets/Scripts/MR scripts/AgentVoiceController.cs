using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls the agent's voice interaction: mic capture, audio playback, and lip sync.
/// Works with either RealtimeAPIConnection (OpenAI) or ElevenLabsConnection.
///
/// Setup:
/// 1. Attach to the same GameObject as your chosen connection script.
/// 2. Set the Backend field to match which connection you're using.
/// 3. After avatar spawns, call Initialize() with the avatar's AudioSource and face mesh.
/// </summary>
public class AgentVoiceController : MonoBehaviour
{
    public enum VoiceBackend { OpenAI, ElevenLabs }

    [Header("Backend")]
    [Tooltip("Which voice API to use.")]
    public VoiceBackend backend = VoiceBackend.ElevenLabs;

    [Header("References")]
    public RealtimeAPIConnection openAIConnection;
    public ElevenLabsConnection elevenLabsConnection;

    [Header("Microphone Settings")]
    [Tooltip("Mic sample rate. 24000 for OpenAI, 16000 for ElevenLabs.")]
    public int sampleRate = 16000;

    [Tooltip("If true, mic is always on. If false, use StartListening()/StopListening() for push-to-talk.")]
    public bool alwaysListening = true;

    [Tooltip("Length of mic buffer in seconds.")]
    public int micBufferLengthSec = 1;

    [Tooltip("Gain multiplier applied to mic input before sending. Increase if your voice is too quiet for ElevenLabs to detect. " +
             "Watch 'Mic RMS' in console — boosted RMS should be well above the noise gate threshold when speaking.")]
    [Range(1f, 20f)]
    public float micGain = 1f;

    [Tooltip("Minimum RMS level to send mic audio to the API. Raise to filter background noise — " +
             "if ElevenLabs thinks you're still speaking, increase this. Check the console for 'Mic RMS' logs to calibrate. 0 = no gate.")]
    [Range(0f, 0.05f)]
    public float noiseGateThreshold = 0.02f;

    [Header("Audio Playback")]
    [Tooltip("The AudioSource on the avatar that plays AI responses. Set via Initialize().")]
    public AudioSource avatarAudioSource;

    [Header("Lip Sync")]
    [Tooltip("The SkinnedMeshRenderer containing mouth blend shapes (e.g., the Face mesh).")]
    public SkinnedMeshRenderer faceMesh;

    [Tooltip("Index of the mouth-open blend shape.")]
    public int mouthBlendShapeIndex = 0;

    [Tooltip("How much the mouth opens. Higher = wider mouth movement.")]
    public float mouthSensitivity = 300f;

    [Tooltip("How fast the mouth closes when audio stops.")]
    public float mouthSmoothSpeed = 20f;

    // =========================================================================
    //  Mic Mute (for experimenter remote control)
    // =========================================================================

    /// <summary>
    /// When true, mic audio is NOT sent to the voice API.
    /// The Unity Microphone keeps recording (avoids restart latency),
    /// but ProcessMicAudio() silently discards all samples.
    /// </summary>
    [Header("Experimenter Control")]
    [Tooltip("When true, mic input is suppressed — no audio is sent to the API. " +
             "The mic hardware stays active to avoid restart latency.")]
    public bool isMicMuted = false;

    /// <summary>
    /// Mute or unmute the user's mic (for experimenter remote control).
    /// Does NOT stop the Unity Microphone — just stops sending audio to the API.
    /// </summary>
    public void SetMicMuted(bool muted)
    {
        isMicMuted = muted;
        Debug.Log($"AgentVoiceController: Mic {(muted ? "MUTED" : "UNMUTED")}.");
    }

    // Mic state
    private AudioClip micClip;
    private string micDevice;
    private int lastMicPosition;
    private bool isRecording;

    // Audio playback state — queue-based streaming
    private readonly System.Collections.Concurrent.ConcurrentQueue<float> audioQueue =
        new System.Collections.Concurrent.ConcurrentQueue<float>();
    private AudioClip playbackClip;
    private bool isPlaying;
    private bool isBuffering; // True while accumulating initial buffer before playback
    private float currentMouthValue;

    [Header("Playback Tuning")]
    [Tooltip("Samples to buffer before starting playback. Lower = less latency. At 16kHz: 3200 = 0.2s.")]
    public int preBufferSamples = 3200;

    [Tooltip("Seconds of silent padding added after the AI finishes speaking. " +
             "This prevents hardware DSP latency from cutting off the last word. 0.5s is usually safe.")]
    public float drainDelaySeconds = 0.5f;

    // Timeout detection
    private float lastAudioChunkTime;
    private float audioTimeoutSeconds = 2.0f;

    // State
    private bool initialized;
    public bool IsAISpeaking { get; private set; }

    private int PlaybackSampleRate => backend == VoiceBackend.ElevenLabs ? 16000 : 24000;

    private void Start()
    {
        // Auto-detect connection if not assigned
        if (backend == VoiceBackend.ElevenLabs)
        {
            if (elevenLabsConnection == null)
                elevenLabsConnection = GetComponent<ElevenLabsConnection>();

            if (elevenLabsConnection == null)
            {
                Debug.LogError("AgentVoiceController: No ElevenLabsConnection found!");
                enabled = false;
                return;
            }

            elevenLabsConnection.OnConnected += OnAPIConnected;
            elevenLabsConnection.OnAudioDelta += OnAudioChunkReceived;
            elevenLabsConnection.OnAudioDone += OnAudioResponseDone;
            elevenLabsConnection.OnInterruption += OnInterruption;
            elevenLabsConnection.OnNewResponse += OnNewResponseStarting;
        }
        else
        {
            if (openAIConnection == null)
                openAIConnection = GetComponent<RealtimeAPIConnection>();

            if (openAIConnection == null)
            {
                Debug.LogError("AgentVoiceController: No RealtimeAPIConnection found!");
                enabled = false;
                return;
            }

            openAIConnection.OnConnected += OnAPIConnected;
            openAIConnection.OnAudioDelta += OnAudioChunkReceived;
            openAIConnection.OnAudioDone += OnAudioResponseDone;
        }

        sampleRate = backend == VoiceBackend.ElevenLabs ? 16000 : 24000;
    }

    public void Initialize(AudioSource audioSource, SkinnedMeshRenderer faceMeshRenderer, int mouthBlendIndex)
    {
        avatarAudioSource = audioSource;
        faceMesh = faceMeshRenderer;
        mouthBlendShapeIndex = mouthBlendIndex;
        initialized = true;

        int sr = PlaybackSampleRate;
        playbackClip = AudioClip.Create("AIResponse", sr * 2, 1, sr, true, OnAudioRead);
        avatarAudioSource.clip = playbackClip;
        avatarAudioSource.loop = true;

        Debug.Log($"AgentVoiceController: Initialized ({backend}, {sr}Hz, streaming mode).");
    }

    private void OnAudioRead(float[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (audioQueue.TryDequeue(out float sample))
            {
                data[i] = sample;
            }
            else
            {
                data[i] = 0f; // Silence when no data available
            }
        }
    }

    private void OnAPIConnected()
    {
        Debug.Log("AgentVoiceController: API connected.");
        if (alwaysListening)
        {
            StartListening();
        }
    }

    #region Microphone Capture

    public void StartListening()
    {
        if (isRecording) return;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("AgentVoiceController: No microphone found!");
            return;
        }

        micDevice = Microphone.devices[0];
        Debug.Log($"AgentVoiceController: Using microphone: {micDevice}");

        micClip = Microphone.Start(micDevice, true, micBufferLengthSec, sampleRate);
        lastMicPosition = 0;
        isRecording = true;

        Debug.Log("AgentVoiceController: Mic recording started.");
    }

    public void StopListening()
    {
        if (!isRecording) return;

        Microphone.End(micDevice);
        isRecording = false;

        Debug.Log("AgentVoiceController: Mic recording stopped.");
    }

    private void ProcessMicAudio()
    {
        if (!isRecording || micClip == null) return;

        // ── Mic mute: keep reading the buffer (so it doesn't overflow)
        //    but do NOT send anything to the API.
        if (isMicMuted)
        {
            // Advance the read position so the ring buffer stays current.
            // When we unmute, we start from the live position — no stale audio.
            lastMicPosition = Microphone.GetPosition(micDevice);
            return;
        }

        bool connected = backend == VoiceBackend.ElevenLabs
            ? elevenLabsConnection != null && elevenLabsConnection.IsConnected
            : openAIConnection != null && openAIConnection.IsConnected;

        if (!connected) return;

        // While the agent speaks we normally mute the mic (half-duplex) so the Quest mic
        // doesn't feed the avatar's own voice back to the API. With barge-in enabled we KEEP
        // streaming so ElevenLabs can detect the user talking over the agent and interrupt it.
        bool allowBargeIn = backend == VoiceBackend.ElevenLabs
            && elevenLabsConnection != null
            && elevenLabsConnection.allowInterruption;

        if (IsAISpeaking && !allowBargeIn)
        {
            lastMicPosition = Microphone.GetPosition(micDevice);
            return;
        }

        int currentPosition = Microphone.GetPosition(micDevice);
        if (currentPosition == lastMicPosition) return;

        int samplesToRead;
        if (currentPosition > lastMicPosition)
        {
            samplesToRead = currentPosition - lastMicPosition;
        }
        else
        {
            samplesToRead = (micClip.samples - lastMicPosition) + currentPosition;
        }

        if (samplesToRead <= 0) return;

        float[] samples = new float[samplesToRead];
        micClip.GetData(samples, lastMicPosition);
        lastMicPosition = currentPosition;

        if (micGain != 1f)
        {
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Clamp(samples[i] * micGain, -1f, 1f);
        }

        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }
        float rms = Mathf.Sqrt(sum / samples.Length);

        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"AgentVoiceController: Mic RMS = {rms:F5}");
        }

        byte[] pcm16Bytes = FloatToPCM16(samples);
        string base64Audio = Convert.ToBase64String(pcm16Bytes);

        if (backend == VoiceBackend.ElevenLabs)
            elevenLabsConnection.SendAudio(base64Audio);
        else
            openAIConnection.SendAudio(base64Audio);
    }

    #endregion

    #region Audio Playback

    private void OnNewResponseStarting()
    {
        IsAISpeaking = true;
        Debug.Log("AgentVoiceController: New agent response detected.");
    }

    private void OnAudioChunkReceived(string base64Audio)
    {
        if (!initialized) return;

        lastAudioChunkTime = Time.time;
        IsAISpeaking = true;

        byte[] pcm16Bytes = Convert.FromBase64String(base64Audio);
        float[] samples = PCM16ToFloat(pcm16Bytes);

        for (int i = 0; i < samples.Length; i++)
        {
            audioQueue.Enqueue(samples[i]);
        }

        if (isBuffering)
        {
            if (audioQueue.Count >= preBufferSamples)
            {
                isBuffering = false;
                avatarAudioSource.Play();
                isPlaying = true;
                Debug.Log($"AgentVoiceController: Pre-buffer filled ({audioQueue.Count} samples). Playback started.");
            }
            return;
        }

        if (!isPlaying)
        {
            isBuffering = true;
            Debug.Log("AgentVoiceController: Buffering audio...");

            if (audioQueue.Count >= preBufferSamples)
            {
                isBuffering = false;
                avatarAudioSource.Play();
                isPlaying = true;
                Debug.Log("AgentVoiceController: Playback started.");
            }
        }
    }

    private void OnAudioResponseDone()
    {
        if (isBuffering && audioQueue.Count > 0)
        {
            isBuffering = false;
            avatarAudioSource.Play();
            isPlaying = true;
            Debug.Log("AgentVoiceController: Short response — playing without full buffer.");
        }

        // --- Zero-Padding: pad silence so hardware finishes playing all real audio ---
        int paddingSamples = Mathf.CeilToInt(PlaybackSampleRate * drainDelaySeconds);
        for (int i = 0; i < paddingSamples; i++)
        {
            audioQueue.Enqueue(0f);
        }

        IsAISpeaking = false;
        Debug.Log($"AgentVoiceController: AI audio stream ended. Added {drainDelaySeconds}s of silence padding.");
    }

    private void OnInterruption()
    {
        while (audioQueue.TryDequeue(out _)) { }
        avatarAudioSource.Stop();
        isPlaying = false;
        isBuffering = false;
        IsAISpeaking = false;
        Debug.Log("AgentVoiceController: Playback interrupted by user.");
    }

    private void CheckPlaybackCompletion()
    {
        if (isBuffering) return;

        if (!IsAISpeaking && isPlaying)
        {
            if (audioQueue.IsEmpty)
            {
                avatarAudioSource.Stop();
                isPlaying = false;
                Debug.Log("AgentVoiceController: Playback completed naturally.");
            }
        }
    }

    #endregion

    #region Lip Sync

    private void UpdateLipSync()
    {
        if (faceMesh == null) return;

        float targetMouth = 0f;

        if (isPlaying && !audioQueue.IsEmpty)
        {
            float[] samples = new float[256];
            avatarAudioSource.GetOutputData(samples, 0);

            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }
            float rms = Mathf.Sqrt(sum / samples.Length);
            targetMouth = Mathf.Clamp01(rms * mouthSensitivity);
        }

        currentMouthValue = Mathf.Lerp(currentMouthValue, targetMouth, Time.deltaTime * mouthSmoothSpeed);
        faceMesh.SetBlendShapeWeight(mouthBlendShapeIndex, currentMouthValue * 100f);
    }

    #endregion

    #region Audio Format Conversion

    private byte[] FloatToPCM16(float[] samples)
    {
        byte[] bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short pcm16 = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
            bytes[i * 2] = (byte)(pcm16 & 0xFF);
            bytes[i * 2 + 1] = (byte)((pcm16 >> 8) & 0xFF);
        }
        return bytes;
    }

    private float[] PCM16ToFloat(byte[] bytes)
    {
        float[] samples = new float[bytes.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short pcm16 = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
            samples[i] = pcm16 / 32768f;
        }
        return samples;
    }

    #endregion

    private void Update()
    {
        ProcessMicAudio();
        CheckAudioTimeout();
        CheckPlaybackCompletion();
        UpdateLipSync();
    }

    private void CheckAudioTimeout()
    {
        if (!IsAISpeaking) return;
        if (lastAudioChunkTime <= 0) return;

        if (Time.time - lastAudioChunkTime > audioTimeoutSeconds)
        {
            Debug.Log("AgentVoiceController: Audio timeout — no more chunks expected.");
            IsAISpeaking = false;
            lastAudioChunkTime = 0;

            if (isBuffering && audioQueue.Count > 0)
            {
                isBuffering = false;
                avatarAudioSource.Play();
                isPlaying = true;
                Debug.Log("AgentVoiceController: Timeout during buffering — playing partial buffer.");
            }

            int paddingSamples = Mathf.CeilToInt(PlaybackSampleRate * drainDelaySeconds);
            for (int i = 0; i < paddingSamples; i++)
            {
                audioQueue.Enqueue(0f);
            }
        }
    }

    private void OnDestroy()
    {
        if (isRecording) StopListening();

        if (elevenLabsConnection != null)
        {
            elevenLabsConnection.OnConnected -= OnAPIConnected;
            elevenLabsConnection.OnAudioDelta -= OnAudioChunkReceived;
            elevenLabsConnection.OnAudioDone -= OnAudioResponseDone;
            elevenLabsConnection.OnInterruption -= OnInterruption;
            elevenLabsConnection.OnNewResponse -= OnNewResponseStarting;
        }

        if (openAIConnection != null)
        {
            openAIConnection.OnConnected -= OnAPIConnected;
            openAIConnection.OnAudioDelta -= OnAudioChunkReceived;
            openAIConnection.OnAudioDone -= OnAudioResponseDone;
        }
    }
}