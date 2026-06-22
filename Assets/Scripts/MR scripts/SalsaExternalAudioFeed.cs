using UnityEngine;
using CrazyMinnow.SALSA;

/// <summary>
/// Feeds live audio amplitude into SALSA's external-analysis input.
///
/// AgentVoiceController plays the AI's voice through a STREAMING AudioClip
/// (PCM read-callback), and SALSA cannot read streaming clips — it samples the
/// AudioClip buffer at the playhead, which doesn't exist for streamed audio, so
/// the mouth never moves. Per Crazy Minnow's docs, the fix is external analysis:
/// set salsa.useExternalAnalysis = true and push a normalized [0..1] amplitude
/// into salsa.analysisValue. We compute that from the AudioSource's live output
/// (GetOutputData), which DOES work for streaming clips.
///
/// Added and wired at runtime by AvatarDeskPlacer.
/// </summary>
public class SalsaExternalAudioFeed : MonoBehaviour
{
    [Tooltip("The SALSA instance to drive.")]
    public Salsa salsa;

    [Tooltip("The AudioSource playing the AI's voice.")]
    public AudioSource source;

    [Tooltip("Amplitude gain. Raise if the mouth barely moves, lower if it's always wide open.")]
    [Range(1f, 50f)]
    public float gain = 12f;

    [Tooltip("Number of output samples to RMS each frame.")]
    public int sampleWindow = 256;

    private float[] buffer;

    /// <summary>Wire up references and switch SALSA into external-analysis mode.</summary>
    public void Init(Salsa salsaInstance, AudioSource audioSource)
    {
        salsa = salsaInstance;
        source = audioSource;
        buffer = new float[sampleWindow];
        if (salsa != null) salsa.useExternalAnalysis = true;
    }

    private void Awake()
    {
        if (buffer == null) buffer = new float[sampleWindow];
    }

    private void Update()
    {
        if (salsa == null || source == null) return;

        if (!source.isPlaying)
        {
            salsa.analysisValue = 0f;
            return;
        }

        source.GetOutputData(buffer, 0);

        float sum = 0f;
        for (int i = 0; i < buffer.Length; i++)
            sum += buffer[i] * buffer[i];
        float rms = Mathf.Sqrt(sum / buffer.Length);

        salsa.analysisValue = Mathf.Clamp01(rms * gain);
    }
}
