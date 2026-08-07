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
/// 3. Set firstTaskKey and secondTaskKey BEFORE calling Connect() (STUDY CONTROL does this).
///    A session runs TWO tasks in ONE conversation: both are sent at initiation as
///    {{first_task_context}} and {{second_task_context}}, and the agent hands over between
///    them. Dynamic variables are read once at initiation and cannot be updated on a live
///    session, which is why the second task cannot simply be sent later.
/// 4. Call Connect() to start (auto-called by AvatarPlacer).
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
    public bool allowInterruption = true;

    [Header("Task Configuration")]
    [Tooltip("Key of the task the participant does FIRST (P1..P4 = SWOT proposals, " +
             "I1..I4 = incident reports). Filled into {{first_task_context}}. Normally pushed " +
             "here by STUDY CONTROL rather than typed — set it directly only for solo testing.")]
    public string firstTaskKey = "";

    [Tooltip("Key of the task the participant does SECOND. Filled into {{second_task_context}}. " +
             "BOTH tasks are handed to the agent up front, in one conversation — the agent is the " +
             "one that moves the session from the first to the second.")]
    public string secondTaskKey = "";

    [Tooltip("Resolved text sent as {{first_task_context}}. Populated from firstTaskKey when " +
             "Connect() runs; type into it directly only if you leave the key empty. Read once at " +
             "conversation initiation — dynamic variables cannot be changed on a live session.")]
    [TextArea(4, 10)]
    public string firstTaskContext = "";

    [Tooltip("Resolved text sent as {{second_task_context}}. Same rules as the first.")]
    [TextArea(4, 10)]
    public string secondTaskContext = "";

    [Header("Task Switch Trigger")]
    [Tooltip("The agent is prompted to say a fixed line when it moves the participant from the " +
             "first task to the second. When any of these phrases appears in the agent's " +
             "transcript, the session advances: CurrentTaskKey flips to the second task and the " +
             "participant's sheet follows it.\n\n" +
             "MATCH IS SUBSTRING, AFTER STRIPPING WHITESPACE AND PUNCTUATION, so wording around " +
             "the phrase and any drift in commas or full stops does not matter. Keep each entry " +
             "to a distinctive FRAGMENT rather than the whole sentence — the shorter and more " +
             "specific it is, the less there is to drift. List Simplified AND Traditional " +
             "variants: the agent's script is Simplified but the study build is zh-TW, and the " +
             "model does not reliably keep one script.")]
    public string[] taskSwitchPhrases =
    {
        // Only 个/個 and 报/報 differ between scripts here, so these four cover every way the
        // fragment can come back. The mixed pair is not paranoia: the agent prompt itself is
        // written in mixed script (那第一個…任务…), so the model has no consistent script to copy.
        "另一个报告需要完成",   // Simplified, as scripted in the agent prompt
        "另一個報告需要完成",   // Traditional, in case the model localizes it
    };

    [Tooltip("Turn OFF to disable automatic switching (the trigger phrase is then ignored and " +
             "only AdvanceToSecondTask() moves the session on). Useful when piloting the wording.")]
    public bool switchTaskOnPhrase = true;

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
        // ============ SWOT PROPOSALS ============
        ["P1"] = "我被要求準備一份論證報告，為用於推動「企業客戶入駐流程最佳化」計畫爭取新台幣 6,400 萬元的資金。此計畫若能成功，可望大幅縮短營收實現時間、提升客戶留存率，並強化公司的競爭表現。由於我將參與領導這項計畫的執行，亮眼的成果可以提高我在高階主管面前的能見度、升遷機會，以及未來擔任領導職務的可能性；反之，若發生成本超支或執行失敗，則可能損害我的信譽，並降低我未來主導策略型計畫的機會。",

        ["P2"] = "我被要求準備一份論證報告，為一項將營運據點拓展至倫敦與北京的試行計畫爭取新台幣 9,600 萬的資金。此案若獲核准，可望帶來重大的成長機會，並讓我確立在公司國際策略中的領導地位。試辦計畫若成功，可以提高我在高階主管面前的能見度、決策權限與升遷機會；反之，若成效不彰，則可能造成重大損失、使更大規模的擴張計畫停滯，並損害管理階層對我策略判斷力的信心。",

        ["P3"] = "我被要求準備一份論證報告，為將公司核心系統遷移至新的企業軟體平台爭取新台幣 4,800 萬。此案若獲核准，可望提升全公司的生產力，並確立我與我的團隊為推動全公司轉型的領導者。遷移作業若能順利完成，可以強化我在高階主管心目中的公信力，並提高我對未來科技投資的決策權；反之，若出現延宕、資安問題或營運中斷，則可能損害各方對我領導能力的信任，並降低我的升遷機會。",

        ["P4"] = "我被要求準備一份論證報告，建議公司投入新台幣 8,000 萬元與溫莎銀行建立策略合作夥伴關係。此案若獲核准，可讓公司更快取得寶貴的資源、能力與市場觸及範圍。合作若能成功，可大幅提升我在高階主管心目中的聲譽、增強我對公司策略的影響力，並讓我有機會承擔更重要的領導職責；反之，若合作夥伴表現不如預期或投資報酬令人失望，則可能損害我的信譽，並限制我的升遷發展。",

        // ============ INCIDENT REPORTS ============
        ["I1"] = "在我所監督的生產線上，一名員工在試圖清除自動包裝機內卡住的材料時，手部遭受嚴重傷害。該名員工需接受手術治療，生產線已停線。我必須撰寫一份詳細報告，說明事發經過、造成原因，以及防止再次發生所需採取的行動。由於我所負責的區域內先前可能已存在設備問題與安全程序執行不落實的情形，這次調查可能影響我的專業可信度、績效評估、升遷機會，以及後續的監督職責。",

        ["I2"] = "在我所負責的部門中，勒索軟體從一台員工工作站擴散至公司共用系統，導致數個部門的作業中斷。我必須撰寫一份資安事故報告，說明這起攻擊是如何發生的、為何會擴散，以及必須採取哪些立即與長期的應變措施。高階領導層可能會調查我的部門是否確實遵循規定的資安作業程序，調查結果可能影響我的聲譽、職涯發展、預算權限，以及是否能繼續擔任部門主管職務。",

        ["I3"] = "在我所管轄的維修作業區內，一個受損的工業清潔化學品容器破裂，導致兩名員工暴露於化學品中，廠區部分區域被迫關閉。其中一名員工需要住院治療。我必須撰寫一份報告，說明事件經過、找出造成原因，並提出矯正與預防措施。由於儲放區稽核與危害控制屬於我的職責範圍，這次調查可能導致我的決策受到正式審查，並可能影響我的績效評估、專業聲譽、升遷機會，或監督職位。",

        ["I4"] = "我所監督的技術團隊發現，一項雲端設定錯誤導致客戶資料暴露給未經授權的外部使用者。我必須撰寫一份資安事故報告，說明這起資料外洩事件、促成其發生的因素，以及後續應採取的矯正措施。由於可能衍生法律、財務及商譽方面的後果，高階領導層可能會嚴格檢視我對團隊的監督情形。這次調查可能影響我在高層心目中的可信度、未來的升遷、決策權限，或是否能繼續領導該部門。",
    };

    private static readonly Dictionary<string, string> TASK_TYPES = new Dictionary<string, string>
    {
        ["P1"] = "專案提案", ["P2"] = "專案提案", ["P3"] = "專案提案", ["P4"] = "專案提案",
        ["I1"] = "事故報告", ["I2"] = "事故報告", ["I3"] = "事故報告", ["I4"] = "事故報告",
    };

    public async void Connect()
    {
        if (string.IsNullOrEmpty(agentId))
        {
            Debug.LogError("ElevenLabsConnection: Agent ID is not set!");
            return;
        }

        // Resolve both task keys -> contexts now, right before initiation, so there's no
        // ordering race with AvatarPlacer's automatic Connect() call.
        ResolveTaskContexts();
        CurrentTaskIndex = 0;   // every fresh conversation starts on the first task

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
    /// per-trial variation is the task scenarios, injected via the {{first_task_context}}
    /// and {{second_task_context}} dynamic variables.
    /// </summary>
    private void SendConversationInitiation()
    {
        var initEvent = new
        {
            type = "conversation_initiation_client_data",
            // Keep empty — do NOT override the system prompt per condition.
            conversation_config_override = new { },
            // BOTH tasks go up front, in one shot. Dynamic variables are read once at
            // initiation and cannot be changed on a live session, so there is no way to feed
            // the second task in later without dropping the conversation and its history —
            // which is exactly what the study must not do between the two tasks.
            dynamic_variables = new Dictionary<string, object>
            {
                { "first_task_context", firstTaskContext },
                { "second_task_context", secondTaskContext }
            }
        };
        SendRawJson(JsonConvert.SerializeObject(initEvent));

        WarnIfEmpty(firstTaskContext, "first_task_context", firstTaskKey);
        WarnIfEmpty(secondTaskContext, "second_task_context", secondTaskKey);

        if (!string.IsNullOrEmpty(firstTaskContext) && !string.IsNullOrEmpty(secondTaskContext))
        {
            Debug.Log($"ElevenLabsConnection: Initiation sent. " +
                      $"first_task_context = '{firstTaskKey}' ({firstTaskContext.Length} chars), " +
                      $"second_task_context = '{secondTaskKey}' ({secondTaskContext.Length} chars).");
        }
    }

    private static void WarnIfEmpty(string context, string variableName, string key)
    {
        if (!string.IsNullOrEmpty(context)) return;
        Debug.LogWarning($"ElevenLabsConnection: {{{{{variableName}}}}} is EMPTY at initiation " +
            $"(task key '{key}'). The agent will fall back to the dashboard placeholder default " +
            "for it — set one, or that half of the session has no scenario.");
    }

    // =========================================================================
    //  Task selection
    // =========================================================================

    private void ResolveTaskContexts()
    {
        if (!string.IsNullOrEmpty(firstTaskKey)) firstTaskContext = ComposeTaskContext(firstTaskKey) ?? firstTaskContext;
        if (!string.IsNullOrEmpty(secondTaskKey)) secondTaskContext = ComposeTaskContext(secondTaskKey) ?? secondTaskContext;

        if (!string.IsNullOrEmpty(firstTaskKey) && firstTaskKey == secondTaskKey)
        {
            Debug.LogWarning($"ElevenLabsConnection: first and second task are BOTH '{firstTaskKey}'. " +
                "The participant will be given the same scenario twice — check STUDY CONTROL.");
        }
    }
    private string ComposeTaskContext(string key)
    {
        if (!TASK_BLOCKS.TryGetValue(key, out string summary))
        {
            Debug.LogError($"ElevenLabsConnection: task key '{key}' not found in TASK_BLOCKS. " +
                $"Valid keys: {string.Join(", ", TASK_BLOCKS.Keys)}. Leaving that context unchanged.");
            return null;
        }

        if (!TASK_TYPES.TryGetValue(key, out string taskType))
        {
            Debug.LogError($"ElevenLabsConnection: task key '{key}' is in TASK_BLOCKS but missing " +
                "from TASK_TYPES — sending the scenario without its 任務 line. Add it.");
            return summary;
        }

        return $"任務：{taskType}\n\n{summary}";
    }

    /// <summary>All valid task keys, for counterbalancing / iteration by a controller.</summary>
    public IReadOnlyCollection<string> TaskKeys => TASK_BLOCKS.Keys;

    // =========================================================================
    //  Which task the session is currently on
    // =========================================================================

    /// <summary>0 while the participant is on the first task, 1 once it has moved to the second.</summary>
    public int CurrentTaskIndex { get; private set; }

    /// <summary>
    /// The key of the task the participant is working on NOW. This is what TaskPanel follows, so
    /// the sheet and the conversation cannot drift onto different tasks.
    /// </summary>
    public string CurrentTaskKey => CurrentTaskIndex == 1 ? secondTaskKey : firstTaskKey;

    /// <summary>Raised when the session moves to the second task. Argument is the new current key.</summary>
    public event Action<string> OnTaskAdvanced;

    /// <summary>
    /// Move the session from the first task to the second. Fired automatically when the agent
    /// speaks its scripted hand-over line (see taskSwitchPhrases), and safe to call by hand — wire
    /// it to a researcher button if the agent's wording ever drifts past the matcher.
    ///
    /// This does NOT touch the conversation: the agent was given both tasks at initiation and is
    /// the one driving the hand-over. All that changes here is which task the study considers
    /// current, i.e. which sheet the participant sees and what the log attributes to.
    ///
    /// Idempotent: returns false if there is no second task or the session is already on it.
    /// </summary>
    public bool AdvanceToSecondTask()
    {
        if (CurrentTaskIndex == 1) return false;

        if (string.IsNullOrEmpty(secondTaskKey))
        {
            Debug.LogWarning("ElevenLabsConnection: AdvanceToSecondTask() but no second task is set.");
            return false;
        }

        CurrentTaskIndex = 1;
        Debug.Log($"ElevenLabsConnection: advanced to the SECOND task '{secondTaskKey}'.");
        OnTaskAdvanced?.Invoke(secondTaskKey);
        return true;
    }
    private void CheckTaskSwitchPhrase(string agentText)
    {
        if (!switchTaskOnPhrase) return;
        if (CurrentTaskIndex == 1) return;              // already moved on; nothing to match
        if (string.IsNullOrEmpty(agentText)) return;
        if (taskSwitchPhrases == null || taskSwitchPhrases.Length == 0) return;

        string haystack = NormalizeForMatch(agentText);
        if (haystack.Length == 0) return;

        foreach (string phrase in taskSwitchPhrases)
        {
            if (string.IsNullOrEmpty(phrase)) continue;
            string needle = NormalizeForMatch(phrase);
            if (needle.Length == 0 || !haystack.Contains(needle)) continue;

            Debug.Log($"ElevenLabsConnection: task-switch phrase '{phrase}' heard in the agent's " +
                      "transcript — moving to the second task.");
            AdvanceToSecondTask();
            return;
        }
    }

    /// <summary>
    /// Strip everything that carries no meaning for the match: whitespace, and the ASCII and
    /// full-width punctuation the model sprinkles differently every time. Letters, digits and
    /// CJK survive.
    /// </summary>
    private static string NormalizeForMatch(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Restart the conversation with a new task PAIR. Dynamic variables are read only at
    /// initiation, so changing them requires a fresh conversation: this applies the new keys,
    /// disconnects, and reconnects, losing all history. Not used by the normal flow — the two
    /// tasks of a session are handed over within ONE conversation.
    /// </summary>
    public async void RestartWithTasks(string firstKey, string secondKey)
    {
        firstTaskKey = firstKey;
        secondTaskKey = secondKey;
        CurrentTaskIndex = 0;

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

                        // AFTER the transcript event, so the conversation log records the line
                        // itself before the task-switch marker it triggers — the two land in
                        // the order they actually happened.
                        CheckTaskSwitchPhrase(agentText);
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