using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Writes a per-session conversation log to a FILE ON THE HEADSET, so you get the full
/// transcript + timing even when the app runs as a standalone APK (no Quest Link / no
/// live Unity console).
///
/// WHY THIS EXISTS: on a standalone build there is no editor console. This component
/// records the conversation to <see cref="Application.persistentDataPath"/>, which on
/// Quest is the app's private external folder:
///     /sdcard/Android/data/&lt;package&gt;/files/ConversationLogs/&lt;participant&gt;/
/// You retrieve it AFTER the session WITHOUT Link — just plug the headset in over USB:
///   * Tools/pull-logs.ps1            (pull to ./StudyLogs, verify, optionally wipe the headset)
///   * adb pull "/sdcard/Android/data/com.DefaultCompany.MRWorkplaceAssistant/files/ConversationLogs" .
///   * or the Meta Quest Developer Hub "Device Files" browser,
///   * or Windows Explorer (the headset shows up as an MTP drive).
/// (adb is bundled with Unity at
///  Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe.)
///
/// WHAT IT LOGS (one JSON object per line — JSONL — so it's both human-readable and
/// trivial to parse in Python/R with pandas.read_json(lines=True)):
///   session_start / session_end, connected / disconnected,
///   conversation_init (ElevenLabs conversation_id + audio formats + task key),
///   user_message  (what the participant said),
///   agent_message (what the agent said),
///   agent_audio_start / agent_audio_end (precise speaking interval from the audio stream),
///   interruption  (participant barged in over the agent),
///   agent_correction (text the agent retracted after being interrupted),
///   error, and optional free-form markers via LogMarker().
/// Every record carries a wall-clock ISO-8601 timestamp ("t") AND milliseconds since
/// session start ("ms"), so you can measure e.g. interruption latency directly.
///
/// SETUP: drop this on the SAME GameObject as ElevenLabsConnection (the "AgentVoice"
/// object), or anywhere in the scene — it auto-finds the connection. Participant Id and
/// Condition Label are read from the StudyControlPanel (the single per-trial control
/// surface), NOT set here. No changes to ElevenLabsConnection are needed; it just
/// subscribes to that script's public events.
/// </summary>
[DisallowMultipleComponent]
public class ConversationLogger : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("The voice connection to record. Auto-found in the scene if left empty.")]
    public ElevenLabsConnection connection;

    [Tooltip("The study control panel that holds Participant ID / Condition Label. Auto-found in " +
             "the scene if left empty. Set those fields THERE, not here.")]
    public StudyControlPanel study;

    [Header("What to capture")]
    [Tooltip("Log the agent's precise speaking interval (agent_audio_start/end) from the " +
             "audio stream, so you can see exactly when it was talking and when a barge-in landed.")]
    public bool logAgentAudioTiming = true;

    [Tooltip("Also dump EVERY raw server event (except the noisy 'audio'/'ping') as " +
             "server_event lines. Verbose — handy for debugging the ElevenLabs protocol, " +
             "off for clean study logs.")]
    public bool includeRawServerEvents = false;

    [Tooltip("ALSO mirror the entire Unity console (every Debug.Log/Warning/Error, from any " +
             "script) to a second '<name>_console.log' file. This is the catch-all that " +
             "recovers what you'd normally read in the editor console on a tethered run.")]
    public bool mirrorAllDebugLogs = true;

    [Header("Output")]
    [Tooltip("Subfolder under Application.persistentDataPath where logs are written.")]
    public string logFolderName = "ConversationLogs";

    [Tooltip("Give each participant their OWN subfolder (ConversationLogs/P07/...) instead of " +
             "dumping every session into one flat directory. Keeps repeated runs for the same " +
             "participant together and makes 'adb pull' of a single participant trivial.")]
    public bool folderPerParticipant = true;

    // ---- runtime state ----
    private StreamWriter jsonl;            // structured conversation log (.jsonl)
    private StreamWriter console;          // full Debug.Log mirror (.log), optional
    private readonly object gate = new object();
    private System.Diagnostics.Stopwatch sw;
    private bool bound;                    // subscribed to connection events?
    private bool agentAudioActive;         // currently inside an agent speaking interval?
    private string jsonlPath;

    /// <summary>Absolute path of the current structured log, for display/debugging.</summary>
    public string CurrentLogPath => jsonlPath;

    // Identity comes from the StudyControlPanel (the single per-trial control surface), with
    // safe fallbacks if the panel is missing so logging never breaks.
    private string ParticipantId => study != null ? study.participantId : "P00";
    private string ConditionLabel => study != null ? study.ResolvedConditionLabel : "";

    private void Start()
    {
        // Resolve the panel FIRST — OpenFiles() uses the participant id in the file name.
        if (study == null) study = FindObjectOfType<StudyControlPanel>();

        OpenFiles();

        if (mirrorAllDebugLogs)
            Application.logMessageReceivedThreaded += OnUnityLog;

        if (connection == null) connection = FindObjectOfType<ElevenLabsConnection>();
        TryBind();

        Write("session_start", r =>
        {
            r["participant"] = ParticipantId;
            r["condition"] = ConditionLabel;
            r["task_key"] = connection != null ? connection.taskKey : "";
            r["device"] = SystemInfo.deviceModel;
            r["app_version"] = Application.version;
            r["file"] = jsonlPath;
        });

        // Echo the path so it also lands in adb logcat / the console mirror — makes the
        // file easy to locate after the run.
        Debug.Log($"ConversationLogger: logging to {jsonlPath}");
    }

    private void Update()
    {
        // The connection may not have existed yet at Start (e.g. added late); bind once it does.
        if (!bound)
        {
            if (connection == null) connection = FindObjectOfType<ElevenLabsConnection>();
            TryBind();
        }
    }

    private void OpenFiles()
    {
        try
        {
            string participant = Sanitize(ParticipantId);

            string dir = Path.Combine(Application.persistentDataPath, logFolderName);
            // One folder per participant keeps their repeated sessions together and lets you
            // pull (or delete) a single participant's data on its own.
            if (folderPerParticipant) dir = Path.Combine(dir, participant);
            Directory.CreateDirectory(dir);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            // Condition goes in the name too, so a file is identifiable without opening it.
            string condition = Sanitize(ConditionLabel);
            string baseName = string.IsNullOrEmpty(ConditionLabel)
                ? $"{participant}_{stamp}"
                : $"{participant}_{condition}_{stamp}";

            jsonlPath = Path.Combine(dir, baseName + ".jsonl");
            jsonl = NewWriter(jsonlPath);

            if (mirrorAllDebugLogs)
                console = NewWriter(Path.Combine(dir, baseName + "_console.log"));

            sw = System.Diagnostics.Stopwatch.StartNew();
        }
        catch (Exception e)
        {
            // Never let logging failure take down the study session.
            Debug.LogError($"ConversationLogger: could NOT open log files: {e.Message}");
        }
    }

    private static StreamWriter NewWriter(string path)
    {
        // Append + AutoFlush so a crash, force-quit, or battery pull never loses the
        // already-written lines (conversation volume is low, so per-line flush is cheap).
        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
    }

    // =========================================================================
    //  Binding to the connection's public events
    // =========================================================================

    private void TryBind()
    {
        if (bound || connection == null) return;

        connection.OnConnected      += HandleConnected;
        connection.OnDisconnected   += HandleDisconnected;
        connection.OnUserTranscript += HandleUserTranscript;
        connection.OnTranscriptDone += HandleAgentTranscript;
        connection.OnInterruption   += HandleInterruption;
        connection.OnError          += HandleError;
        connection.OnAudioDelta     += HandleAudioDelta;
        connection.OnAudioDone      += HandleAudioDone;
        connection.OnAnyServerEvent += HandleAnyServerEvent;

        bound = true;
        Write("logger_bound", null);
    }

    private void Unbind()
    {
        if (!bound || connection == null) return;

        connection.OnConnected      -= HandleConnected;
        connection.OnDisconnected   -= HandleDisconnected;
        connection.OnUserTranscript -= HandleUserTranscript;
        connection.OnTranscriptDone -= HandleAgentTranscript;
        connection.OnInterruption   -= HandleInterruption;
        connection.OnError          -= HandleError;
        connection.OnAudioDelta     -= HandleAudioDelta;
        connection.OnAudioDone      -= HandleAudioDone;
        connection.OnAnyServerEvent -= HandleAnyServerEvent;

        bound = false;
    }

    private void HandleConnected()    => Write("connected", null);
    private void HandleDisconnected() => Write("disconnected", null);

    private void HandleUserTranscript(string text) =>
        Write("user_message", r => { r["speaker"] = "user"; r["text"] = text; });

    private void HandleAgentTranscript(string text) =>
        Write("agent_message", r => { r["speaker"] = "agent"; r["text"] = text; });

    private void HandleInterruption()
    {
        Write("interruption", r => { r["speaker"] = "user"; });
        // The connection raises OnAudioDone right after OnInterruption; HandleAudioDone
        // closes the speaking interval, so we don't double-close it here.
    }

    private void HandleError(string msg) =>
        Write("error", r => r["text"] = msg);

    private void HandleAudioDelta(string _)
    {
        if (!logAgentAudioTiming) return;
        // First chunk of a speaking turn → mark the onset. We never store the audio itself.
        if (!agentAudioActive)
        {
            agentAudioActive = true;
            Write("agent_audio_start", r => r["speaker"] = "agent");
        }
    }

    private void HandleAudioDone()
    {
        if (!logAgentAudioTiming) return;
        if (agentAudioActive)
        {
            agentAudioActive = false;
            Write("agent_audio_end", r => r["speaker"] = "agent");
        }
    }

    private void HandleAnyServerEvent(JObject evt)
    {
        string type = evt["type"]?.ToString();
        if (string.IsNullOrEmpty(type)) return;

        switch (type)
        {
            case "conversation_initiation_metadata":
            {
                var m = evt["conversation_initiation_metadata_event"];
                Write("conversation_init", r =>
                {
                    r["conversation_id"] = m?["conversation_id"]?.ToString() ?? "unknown";
                    r["agent_output_format"] = m?["agent_output_audio_format"]?.ToString() ?? "unknown";
                    r["user_input_format"] = m?["user_input_audio_format"]?.ToString() ?? "unknown";
                    r["task_key"] = connection != null ? connection.taskKey : "";
                });
                break;
            }
            case "agent_response_correction":
            {
                string corrected = evt["agent_response_correction_event"]?["agent_response"]?.ToString();
                Write("agent_correction", r => { r["speaker"] = "agent"; r["text"] = corrected; });
                break;
            }
            default:
                // Everything else only when explicitly asked for, and never the firehose
                // 'audio' chunks or keepalive 'ping's.
                if (includeRawServerEvents && type != "audio" && type != "ping")
                    Write("server_event", r => { r["server_type"] = type; r["raw"] = evt.ToString(Formatting.None); });
                break;
        }
    }

    // =========================================================================
    //  Public API — call from other scripts to drop labelled markers into the log
    //  (e.g. SwotPanel.ShowSwot, trial start/stop from ExperimenterRemoteTrigger).
    // =========================================================================

    /// <summary>Write a free-form, timestamped marker (e.g. "swot_shown", "trial_start").</summary>
    public void LogMarker(string label, string detail = null) =>
        Write("marker", r => { r["label"] = label; if (detail != null) r["detail"] = detail; });

    // =========================================================================
    //  Writing
    // =========================================================================

    private void Write(string ev, Action<Dictionary<string, object>> fill)
    {
        if (jsonl == null) return;

        var rec = new Dictionary<string, object>
        {
            ["t"]  = DateTime.Now.ToString("o"),                 // ISO-8601 wall clock w/ offset
            ["ms"] = sw != null ? sw.ElapsedMilliseconds : 0L,    // monotonic ms since session start
            ["ev"] = ev,
        };
        fill?.Invoke(rec);

        string line;
        try { line = JsonConvert.SerializeObject(rec); }
        catch (Exception e) { line = $"{{\"ev\":\"{ev}\",\"serialize_error\":\"{e.Message}\"}}"; }

        lock (gate)
        {
            try { jsonl.WriteLine(line); } catch { /* disk gone / closed — drop the line, never throw */ }
        }
    }

    // Console mirror. Runs on the THREADED callback so it also catches logs from the
    // WebSocket receive thread; the lock keeps file writes safe across threads.
    private void OnUnityLog(string logString, string stackTrace, LogType type)
    {
        if (console == null) return;
        string ts = DateTime.Now.ToString("HH:mm:ss.fff");
        lock (gate)
        {
            try
            {
                console.WriteLine($"[{ts}] {type}: {logString}");
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    console.WriteLine(stackTrace);
            }
            catch { /* never throw from a log handler */ }
        }
    }

    // =========================================================================
    //  Lifecycle / teardown — flush and close so the file is complete & valid.
    // =========================================================================

    private void OnApplicationPause(bool paused)
    {
        // Quest fires this when the headset is removed / the app is backgrounded — a good
        // moment to make sure everything is on disk.
        if (!paused) return;
        lock (gate) { try { jsonl?.Flush(); console?.Flush(); } catch { } }
    }

    private void OnApplicationQuit() => CloseOut("application_quit");
    private void OnDestroy()         => CloseOut("destroyed");

    private bool closed;
    private void CloseOut(string reason)
    {
        if (closed) return;
        closed = true;

        if (mirrorAllDebugLogs)
            Application.logMessageReceivedThreaded -= OnUnityLog;

        Write("session_end", r => r["reason"] = reason);
        Unbind();

        lock (gate)
        {
            try { jsonl?.Flush(); jsonl?.Dispose(); } catch { }
            try { console?.Flush(); console?.Dispose(); } catch { }
            jsonl = null;
            console = null;
        }
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "anon";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append((char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '_');
        return sb.ToString();
    }
}
