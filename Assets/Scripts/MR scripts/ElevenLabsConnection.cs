using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Manages WebSocket connection to ElevenLabs Conversational AI Agent.
/// Create and configure your agent in the ElevenLabs dashboard,
/// then just paste the Agent ID here.
///
/// Setup:
/// 1. Attach to a GameObject alongside AgentVoiceController.
/// 2. Set your Agent ID (from ElevenLabs dashboard).
/// 3. Set taskContext BEFORE calling Connect() (your experiment controller
///    assigns it per trial from a TASK_BLOCKS dictionary).
/// 4. Call Connect() to start (auto-called by AvatarDeskPlacer).
/// </summary>
public class ElevenLabsConnection : MonoBehaviour
{
    [Header("ElevenLabs Configuration")]
    [Tooltip("Your ElevenLabs Agent ID from the dashboard.")]
    public string agentId = "";

    [Tooltip("Optional: API key for private agents. Leave empty for public agents.")]
    public string apiKey = "";

    [Header("Audio Settings")]
    [Tooltip("Input sample rate. ElevenLabs expects 16000 Hz.")]
    public int inputSampleRate = 16000;

    [Tooltip("Output sample rate. Set to match your agent's TTS output format (usually 16000).")]
    public int outputSampleRate = 16000;

    [Header("Interruption (barge-in)")]
    [Tooltip("Allow the user to interrupt the agent WHILE it is speaking. When ON, the mic keeps " +
             "streaming during agent speech and ElevenLabs 'interruption' events are honored, so " +
             "talking over the agent stops it. IMPORTANT: on Quest, use HEADPHONES — otherwise the " +
             "open mic hears the avatar's own voice and the agent self-interrupts. When OFF, the old " +
             "half-duplex behavior is kept (mic muted during speech; no barge-in). Requires the " +
             "'interruption' client event to be enabled on the agent in the ElevenLabs dashboard.")]
    public bool allowInterruption = true;

    [Header("Task Configuration")]
    [Tooltip("Optional: select the task scenario by key from the built-in TASK_BLOCKS table " +
             "(e.g. low_A, low_B, high_E …). If non-empty, it OVERRIDES taskContext when Connect() " +
             "runs. Leave empty to type the scenario into taskContext directly.")]
    public string taskKey = "";

    [Tooltip("The current trial's task scenario. Filled into {{task_context}} in the agent's " +
             "dashboard system prompt. Set this directly, or let taskKey populate it. Read once at " +
             "conversation initiation — changing the task mid-session requires RestartWithTask().")]
    [TextArea(4, 12)]
    public string taskContext = "";

    // WebSocket
    private ClientWebSocket ws;
    private CancellationTokenSource cts;
    public bool IsConnected { get; private set; }

    // Thread-safe queue for main thread dispatch
    private ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    // Events — matches the same pattern as RealtimeAPIConnection
    // so AgentVoiceController can work with either
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<string> OnAudioDelta;           // Base64 audio chunk from agent
    public event Action OnAudioDone;                     // Agent finished speaking
    public event Action OnNewResponse;                   // New agent response starting (buffer should reset)
    public event Action<string> OnTranscriptDelta;       // Partial agent transcript
    public event Action<string> OnTranscriptDone;        // Full agent response text
    public event Action<string> OnUserTranscript;        // What the user said
    public event Action<string> OnError;
    public event Action OnInterruption;                  // User interrupted the agent
    public event Action<JObject> OnAnyServerEvent;       // Raw event for debugging

    // Track if agent is currently speaking
    private bool agentIsSpeaking;

    private static readonly Dictionary<string, string> TASK_BLOCKS = new Dictionary<string, string>
    {
        // ============ LOW RISK / LOW REWARD ============
        ["low_A"]  = "I've been asked to prepare a short internal report requesting a $500 reallocation " +
            "from the underused Q3 event planning budget to purchase replacement chairs for the 3rd floor " +
            "meeting room. The report should document the problem statement, confirm funding availability, " +
            "and facilitate quick approval. The problem statement should include information about any recent " +
            "maintenance requests and recommend a potential vendor. While not an urgent safety concern, the " +
            "situation affects comfort and professionalism during cross-team collaboration.",

        ["low_B"]  = "I've been asked to prepare a report requesting $800 to restock the shared printer supply " +
            "cabinet. The report should include a problem statement, identify the most cost-effective vendor, " +
            "and show that the expense can be covered under the office supply budget. The problem statement " +
            "should include information about any recent maintenance requests and recommend a potential vendor. " +
            "While necessary for daily operations, the request is routine and unlikely to face any pushback.",

        ["low_C"]  = "I've been asked to prepare a brief report requesting $1,200 from the facilities budget to " +
            "replace the worn coffee machine in the employee lounge. The report should include a problem " +
            "statement, confirm warranty expiration, and verify cost coverage. The problem statement should " +
            "include information about any recent maintenance requests and recommend a potential vendor. " +
            "While appreciated by staff, the decision carries minimal consequence for broader operations.",

        ["low_D"]  = "I've been asked to draft a report requesting $1,500 to replace damaged whiteboards in two " +
            "conference rooms. The report should include a problem statement, confirm budget coverage, and " +
            "provide vendor options. The problem statement should include information about any recent " +
            "maintenance requests and recommend a potential vendor. While helpful for meetings, the request is " +
            "small in scope and unlikely to be controversial.",

        // ============ HIGH RISK / HIGH REWARD ============
        ["high_E"] = "I've been asked to prepare a justification report requesting $2 million in funding for an " +
            "initiative to streamline enterprise client onboarding. The report should include a problem statement " +
            "and must present a strong financial and strategic case to secure executive approval. The problem " +
            "statement should include information about reduction in onboarding time and potential return on " +
            "investment. Success would reduce time-to-revenue, improve customer retention, and significantly " +
            "advance both the project and my team's career progression.",

        ["high_F"] = "I've been asked to prepare a justification report requesting $3 million in funding for a " +
            "pilot program to expand operations into a new international market. The report should include a " +
            "problem statement and outline projected revenue gains, compliance requirements, and anticipated " +
            "risks. The problem statement should include information about specific pilot cities and potential " +
            "return on investment. Approval would open major growth opportunities and increase visibility for me " +
            "and my team, though a weak case could stall expansion efforts.",

        ["high_G"] = "I've been asked to prepare a justification report requesting $1.5 million to migrate core " +
            "company systems to a new enterprise software platform. The report should include a problem statement " +
            "and must address financial costs, vendor comparisons, and long-term efficiency gains. The problem " +
            "statement should include information about current system downtime, potential vendors, and potential " +
            "return on investment. Approval would improve company-wide productivity and position my team as key " +
            "change leaders, though any gaps could raise concerns about feasibility.",

        ["high_H"] = "I've been asked to prepare a justification report recommending a $2.5 million investment in " +
            "a strategic partnership with a leading industry firm. The report should include a problem statement " +
            "and make the financial, technical, and cultural case for collaboration. The problem statement should " +
            "include information about the potential partner, details in cross-company collaboration, and " +
            "potential return on investment. Approval would secure valuable resources and strengthen my " +
            "professional standing, but failure to justify the proposal could damage my credibility at the " +
            "executive level.",
    };

    public async void Connect()
    {
        if (string.IsNullOrEmpty(agentId))
        {
            Debug.LogError("ElevenLabsConnection: Agent ID is not set!");
            return;
        }

        // Resolve taskKey -> taskContext now, right before initiation, so there's no
        // ordering race with AvatarDeskPlacer's automatic Connect() call.
        ResolveTaskContext();

        string url = $"wss://api.elevenlabs.io/v1/convai/conversation?agent_id={agentId}";

        ws = new ClientWebSocket();

        // If using a private agent, get a signed URL instead
        // For public agents, just connect directly
        if (!string.IsNullOrEmpty(apiKey))
        {
            // For private agents you'd need to get a signed URL from your server
            // For now, we support public agents directly
            Debug.LogWarning("ElevenLabsConnection: Private agent auth not implemented. Using public agent connection.");
        }

        cts = new CancellationTokenSource();

        try
        {
            Debug.Log("ElevenLabsConnection: Connecting...");
            await ws.ConnectAsync(new Uri(url), cts.Token);

            Debug.Log("ElevenLabsConnection: WebSocket connected. Sending initiation data...");
            IsConnected = true;

            // Send conversation initiation data — this tells ElevenLabs to use
            // the agent's dashboard configuration (voice, first message, language, etc.)
            SendConversationInitiation();

            // Start receive loop
            _ = ReceiveLoop();
        }
        catch (Exception e)
        {
            Debug.LogError($"ElevenLabsConnection: Connection failed: {e.Message}");
            mainThreadActions.Enqueue(() => OnError?.Invoke(e.Message));
        }
    }

    /// <summary>
    /// Sends the conversation_initiation_client_data event.
    /// This ensures the agent uses its dashboard configuration.
    ///
    /// We keep conversation_config_override EMPTY on purpose: the persona/tone/
    /// guardrails must be byte-identical across every Scale x Render Style cell so
    /// the visual manipulation is not confounded by prompt differences. The ONLY
    /// per-trial variation is the task scenario, injected via the {{task_context}}
    /// dynamic variable (the within-subject Risk Level manipulation lives entirely
    /// in that string).
    /// </summary>
    private void SendConversationInitiation()
    {
        var initEvent = new
        {
            type = "conversation_initiation_client_data",
            // Keep empty — do NOT override the system prompt per condition.
            conversation_config_override = new { },
            // Runtime value filled into {{task_context}} in the dashboard prompt.
            dynamic_variables = new Dictionary<string, object>
            {
                { "task_context", taskContext }
            }
        };
        SendRawJson(JsonConvert.SerializeObject(initEvent));

        if (string.IsNullOrEmpty(taskContext))
        {
            Debug.LogWarning("ElevenLabsConnection: taskContext is EMPTY at initiation. " +
                "The agent will fall back to the dashboard placeholder default (set one!).");
        }
        else
        {
            Debug.Log($"ElevenLabsConnection: Initiation sent. task_context = {taskContext.Length} chars.");
        }
    }

    // =========================================================================
    //  Task selection
    // =========================================================================

    /// <summary>
    /// If taskKey is set, copy the matching scenario from TASK_BLOCKS into taskContext.
    /// Called automatically at the start of Connect() so the value is always current
    /// when the initiation event is sent — no ordering race with the auto-connect.
    /// </summary>
    private void ResolveTaskContext()
    {
        if (string.IsNullOrEmpty(taskKey)) return; // using taskContext directly

        if (TASK_BLOCKS.TryGetValue(taskKey, out string ctx))
        {
            taskContext = ctx;
            Debug.Log($"ElevenLabsConnection: Resolved taskContext from key '{taskKey}' ({ctx.Length} chars).");
        }
        else
        {
            Debug.LogError($"ElevenLabsConnection: taskKey '{taskKey}' not found in TASK_BLOCKS. " +
                $"Valid keys: {string.Join(", ", TASK_BLOCKS.Keys)}. Leaving taskContext unchanged.");
        }
    }

    /// <summary>
    /// Programmatically select the trial's task by key (e.g. "low_A", "high_E").
    /// Sets both taskKey and taskContext. Returns false if the key is unknown.
    /// Call BEFORE Connect(), or use RestartWithTask() to apply it to a live session.
    /// </summary>
    public bool SetTaskByKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("ElevenLabsConnection: SetTaskByKey called with an empty key.");
            return false;
        }

        if (!TASK_BLOCKS.TryGetValue(key, out string ctx))
        {
            Debug.LogError($"ElevenLabsConnection: Unknown task key '{key}'. " +
                $"Valid keys: {string.Join(", ", TASK_BLOCKS.Keys)}.");
            return false;
        }

        taskKey = key;
        taskContext = ctx;
        return true;
    }

    /// <summary>All valid task keys, for counterbalancing / iteration by a controller.</summary>
    public IReadOnlyCollection<string> TaskKeys => TASK_BLOCKS.Keys;

    /// <summary>
    /// Switch the task on a LIVE session. Dynamic variables are read only at conversation
    /// initiation, so changing the task requires a fresh conversation: this applies the new
    /// task, disconnects, and reconnects. Each call starts a brand-new conversation with no
    /// carried-over history. No-op if the key is unknown.
    /// </summary>
    public async void RestartWithTask(string key)
    {
        if (!SetTaskByKey(key)) return;

        if (ws != null)
        {
            Disconnect();
            // Let the old socket close and its receive loop unwind before reconnecting.
            await Task.Delay(300);
        }

        Connect();
    }

    private void Update()
    {
        while (mainThreadActions.TryDequeue(out Action action))
        {
            action?.Invoke();
        }
    }

    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[1024 * 64];
        StringBuilder messageBuilder = new StringBuilder();

        try
        {
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                messageBuilder.Clear();

                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        mainThreadActions.Enqueue(() =>
                        {
                            Debug.Log("ElevenLabsConnection: Server closed connection.");
                            IsConnected = false;
                            OnDisconnected?.Invoke();
                        });
                        return;
                    }

                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                } while (!result.EndOfMessage);

                string message = messageBuilder.ToString();
                mainThreadActions.Enqueue(() => HandleServerEvent(message));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (WebSocketException e)
        {
            mainThreadActions.Enqueue(() =>
            {
                Debug.LogError($"ElevenLabsConnection: WebSocket error: {e.Message}");
                IsConnected = false;
                OnError?.Invoke(e.Message);
                OnDisconnected?.Invoke();
            });
        }
        catch (Exception e)
        {
            mainThreadActions.Enqueue(() =>
            {
                Debug.LogError($"ElevenLabsConnection: Receive error: {e.Message}");
                IsConnected = false;
                OnError?.Invoke(e.Message);
            });
        }
    }

    private void HandleServerEvent(string json)
    {
        try
        {
            var evt = JObject.Parse(json);
            string type = evt["type"]?.ToString();

            OnAnyServerEvent?.Invoke(evt);

            switch (type)
            {
                case "conversation_initiation_metadata":
                    // Log the full metadata so we can see the actual audio format
                    var metadata = evt["conversation_initiation_metadata_event"];
                    string outputFormat = metadata?["agent_output_audio_format"]?.ToString() ?? "unknown";
                    string inputFormat = metadata?["user_input_audio_format"]?.ToString() ?? "unknown";
                    string convId = metadata?["conversation_id"]?.ToString() ?? "unknown";
                    Debug.Log($"ElevenLabsConnection: Conversation initiated." +
                        $"\n  Conversation ID: {convId}" +
                        $"\n  Agent output format: {outputFormat}" +
                        $"\n  User input format: {inputFormat}");
                    mainThreadActions.Enqueue(() => OnConnected?.Invoke());
                    break;

                case "audio":
                    // Agent is sending audio chunks
                    string audioBase64 = evt["audio_event"]?["audio_base_64"]?.ToString();
                    if (!string.IsNullOrEmpty(audioBase64))
                    {
                        if (!agentIsSpeaking)
                        {
                            agentIsSpeaking = true;
                            Debug.Log("ElevenLabsConnection: Agent started speaking.");
                        }
                        // Log chunk sizes periodically to detect issues
                        byte[] rawBytes = Convert.FromBase64String(audioBase64);
                        if (Time.frameCount % 30 == 0)
                        {
                            Debug.Log($"ElevenLabsConnection: Audio chunk - {rawBytes.Length} bytes ({audioBase64.Length} base64 chars)");
                        }
                        OnAudioDelta?.Invoke(audioBase64);
                    }
                    break;

                case "agent_response":
                    // Agent's text response — signals a new response is starting
                    string agentText = evt["agent_response_event"]?["agent_response"]?.ToString();
                    if (!string.IsNullOrEmpty(agentText))
                    {
                        OnNewResponse?.Invoke();
                        OnTranscriptDone?.Invoke(agentText);
                        Debug.Log($"Agent said: {agentText}");
                    }
                    break;

                case "agent_response_correction":
                    // Corrected agent response (if agent was interrupted)
                    string correctedText = evt["agent_response_correction_event"]?["agent_response"]?.ToString();
                    if (!string.IsNullOrEmpty(correctedText))
                    {
                        Debug.Log($"Agent corrected: {correctedText}");
                    }
                    break;

                case "user_transcript":
                    // What the user said
                    string userText = evt["user_transcription_event"]?["user_transcript"]?.ToString();
                    if (!string.IsNullOrEmpty(userText))
                    {
                        OnUserTranscript?.Invoke(userText);
                        Debug.Log($"User said: {userText}");
                    }
                    break;

                case "interruption":
                    // When barge-in is ENABLED, always honor the interruption (stop the agent).
                    // When DISABLED, keep the half-duplex echo guard: ignore interruptions that
                    // arrive while the agent is speaking, because the open Quest mic hears the
                    // avatar's own voice and ElevenLabs would misread it as the user talking.
                    if (allowInterruption || !agentIsSpeaking)
                    {
                        Debug.Log("ElevenLabsConnection: User interrupted agent.");
                        agentIsSpeaking = false;
                        OnInterruption?.Invoke();
                        OnAudioDone?.Invoke();
                    }
                    else
                    {
                        Debug.Log("ElevenLabsConnection: Ignoring interruption — likely echo from agent's own speech (barge-in disabled).");
                    }
                    break;

                case "ping":
                    // Respond to keep connection alive
                    var pongEvent = evt["ping_event"];
                    SendPong(pongEvent?["event_id"]?.ToObject<int>() ?? 0);
                    break;

                case "agent_response_end":
                    // Agent finished its response
                    agentIsSpeaking = false;
                    OnAudioDone?.Invoke();
                    Debug.Log("ElevenLabsConnection: Agent finished speaking (agent_response_end).");
                    break;

                case "mode_change":
                    // ElevenLabs signals when the agent switches between speaking and listening
                    string mode = evt["mode_change_event"]?["mode"]?.ToString()
                               ?? evt["mode"]?.ToString();
                    Debug.Log($"ElevenLabsConnection: Mode changed to: {mode}");

                    if (mode == "listening" && agentIsSpeaking)
                    {
                        agentIsSpeaking = false;
                        OnAudioDone?.Invoke();
                        Debug.Log("ElevenLabsConnection: Agent switched to listening — audio done.");
                    }
                    else if (mode == "speaking")
                    {
                        agentIsSpeaking = true;
                    }
                    break;

                default:
                    // Log unknown events for debugging
                    if (!string.IsNullOrEmpty(type))
                    {
                        Debug.Log($"ElevenLabsConnection: Unhandled event type: {type}");
                    }
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ElevenLabsConnection: Error parsing event: {e.Message}\nJSON: {json}");
        }
    }

    // =========================================================================
    //  PUBLIC API — Client-to-Server Events
    // =========================================================================

    /// <summary>
    /// Send raw audio data (PCM16, base64 encoded) to the agent.
    /// Called by AgentVoiceController each frame with mic data.
    /// </summary>
    public void SendAudio(string base64Audio)
    {
        var audioEvent = new
        {
            user_audio_chunk = base64Audio
        };
        SendRawJson(JsonConvert.SerializeObject(audioEvent));
    }

    /// <summary>
    /// Send a text message to the agent as user input.
    /// This TRIGGERS a response — the agent will reply as if the user spoke.
    ///
    /// Per ElevenLabs docs, the correct format is:
    ///   { "type": "user_message", "text": "..." }
    /// </summary>
    public void SendTextMessage(string text)
    {
        var textEvent = new
        {
            type = "user_message",
            text = text
        };
        SendRawJson(JsonConvert.SerializeObject(textEvent));
        Debug.Log($"ElevenLabsConnection: Sent user_message: {text}");
    }

    /// <summary>
    /// Send contextual information that won't trigger a response.
    /// Use this to silently inform the agent about the current situation.
    ///
    /// Per ElevenLabs docs, the correct format is:
    ///   { "type": "contextual_update", "text": "..." }
    /// </summary>
    public void SendContextualUpdate(string context)
    {
        var contextEvent = new
        {
            type = "contextual_update",
            text = context
        };
        SendRawJson(JsonConvert.SerializeObject(contextEvent));
        Debug.Log($"ElevenLabsConnection: Sent contextual_update: {context}");
    }

    /// <summary>
    /// Send a user_activity ping to prevent session timeout.
    /// Does NOT affect conversation content — just resets the turn timeout timer.
    /// Call every ~30s during passive co-presence phases where nobody is speaking.
    /// </summary>
    public void SendActivityPing()
    {
        var activityEvent = new
        {
            type = "user_activity"
        };
        SendRawJson(JsonConvert.SerializeObject(activityEvent));
        // Don't spam the console — only log occasionally
    }

    /// <summary>
    /// Trigger the AI agent to initiate a conversation.
    /// Used for the RQ3 transition from passive co-presence to active engagement.
    ///
    /// Step 1: contextual_update primes the agent with the situation.
    /// Step 2: user_message triggers the agent to actually respond.
    ///
    /// The agent's system prompt should instruct it to speak naturally
    /// as if it noticed the user is done, NOT as if it received a command.
    /// </summary>
    public void TriggerAgentInitiation(string context = null, string trigger = null)
    {
        context ??= "【系統】使用者剛完成他們的分類任務，現在有空了。" +
                     "你應該像是注意到他已經忙完一樣，自然地開啟一段對話。" +
                     "不要提到你有收到任何指示。";
        trigger ??= "(使用者跳過)";

        SendContextualUpdate(context);
        SendTextMessage(trigger);
        Debug.Log("ElevenLabsConnection: Triggered agent-initiated conversation.");
    }

    // =========================================================================
    //  Internal helpers
    // =========================================================================

    /// <summary>
    /// Respond to server ping to keep connection alive.
    /// </summary>
    private void SendPong(int eventId)
    {
        var pongEvent = new
        {
            type = "pong",
            event_id = eventId
        };
        SendRawJson(JsonConvert.SerializeObject(pongEvent));
    }

    /// <summary>
    /// Send raw JSON string over WebSocket.
    /// </summary>
    private async void SendRawJson(string json)
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
        catch (Exception e)
        {
            Debug.LogError($"ElevenLabsConnection: Send error: {e.Message}");
        }
    }

    public async void Disconnect()
    {
        if (ws != null && ws.State == WebSocketState.Open)
        {
            try
            {
                cts?.Cancel();
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ElevenLabsConnection: Close error: {e.Message}");
            }
        }

        IsConnected = false;
    }

    private void OnDestroy()
    {
        Disconnect();
        cts?.Dispose();
    }

    private void OnApplicationQuit()
    {
        Disconnect();
    }
}