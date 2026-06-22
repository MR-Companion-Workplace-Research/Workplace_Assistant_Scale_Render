using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Manages WebSocket connection to OpenAI Realtime API.
/// Uses .NET built-in ClientWebSocket — no external packages required (except Newtonsoft JSON).
/// 
/// Setup:
/// - Attach to a GameObject.
/// - Set your API key in the Inspector.
/// - Call Connect() to start.
/// </summary>
public class RealtimeAPIConnection : MonoBehaviour
{
    [Header("API Configuration")]
    [Tooltip("Your OpenAI API key. Keep this secure.")]
    public string apiKey = "";

    [Tooltip("Model to use.")]
    public string model = "gpt-realtime-mini";

    [Tooltip("Voice for the AI. Options: alloy, echo, shimmer, ash, ballad, coral, sage, verse")]
    public string voice = "alloy";

    [Tooltip("System instructions that define the agent's personality.")]
    [TextArea(3, 10)]
    public string instructions = "You are a friendly assistant. Keep responses brief and conversational. You're sitting on the user's desk in mixed reality.";

    // WebSocket
    private ClientWebSocket ws;
    private CancellationTokenSource cts;
    public bool IsConnected { get; private set; }

    // Thread-safe queue to dispatch events to Unity's main thread
    private ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    // Events that other scripts can subscribe to
    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<string> OnAudioDelta;          // Base64 audio chunk
    public event Action OnAudioDone;                    // Response audio finished
    public event Action<string> OnTranscriptDelta;      // Partial transcript
    public event Action<string> OnTranscriptDone;       // Full transcript
    public event Action<string> OnUserTranscript;       // What the user said
    public event Action<string> OnError;
    public event Action<JObject> OnAnyServerEvent;      // Raw event for debugging

    public async void Connect()
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("RealtimeAPIConnection: API key is not set!");
            return;
        }

        string url = $"wss://api.openai.com/v1/realtime?model={model}";

        ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");

        cts = new CancellationTokenSource();

        try
        {
            Debug.Log("RealtimeAPIConnection: Connecting...");
            await ws.ConnectAsync(new Uri(url), cts.Token);

            Debug.Log("RealtimeAPIConnection: Connected to OpenAI Realtime API.");
            IsConnected = true;

            mainThreadActions.Enqueue(() => OnConnected?.Invoke());

            ConfigureSession();

            // Start receive loop on background thread
            _ = ReceiveLoop();
        }
        catch (Exception e)
        {
            Debug.LogError($"RealtimeAPIConnection: Connection failed: {e.Message}");
            mainThreadActions.Enqueue(() => OnError?.Invoke(e.Message));
        }
    }

    private void Update()
    {
        // Dispatch queued actions on the main thread
        while (mainThreadActions.TryDequeue(out Action action))
        {
            action?.Invoke();
        }
    }

    private void ConfigureSession()
    {
        var sessionUpdate = new
        {
            type = "session.update",
            session = new
            {
                instructions = this.instructions,
                voice = this.voice,
                input_audio_format = "pcm16",
                output_audio_format = "pcm16",
                input_audio_transcription = new
                {
                    model = "whisper-1"
                },
                turn_detection = new
                {
                    type = "server_vad",
                    threshold = 0.5f,
                    prefix_padding_ms = 300,
                    silence_duration_ms = 500
                }
            }
        };

        SendEvent(sessionUpdate);
        Debug.Log("RealtimeAPIConnection: Session configured.");
    }

    private async Task ReceiveLoop()
    {
        byte[] buffer = new byte[1024 * 64]; // 64KB buffer for large audio chunks
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
                            Debug.Log("RealtimeAPIConnection: Server closed connection.");
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
                Debug.LogError($"RealtimeAPIConnection: WebSocket error: {e.Message}");
                IsConnected = false;
                OnError?.Invoke(e.Message);
                OnDisconnected?.Invoke();
            });
        }
        catch (Exception e)
        {
            mainThreadActions.Enqueue(() =>
            {
                Debug.LogError($"RealtimeAPIConnection: Receive error: {e.Message}");
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
                case "session.created":
                    Debug.Log("RealtimeAPIConnection: Session created.");
                    break;

                case "session.updated":
                    Debug.Log("RealtimeAPIConnection: Session updated.");
                    break;

                case "response.audio.delta":
                    string audioDelta = evt["delta"]?.ToString();
                    if (!string.IsNullOrEmpty(audioDelta))
                    {
                        OnAudioDelta?.Invoke(audioDelta);
                    }
                    break;

                case "response.audio.done":
                    OnAudioDone?.Invoke();
                    break;

                case "response.audio_transcript.delta":
                    string transcriptDelta = evt["delta"]?.ToString();
                    OnTranscriptDelta?.Invoke(transcriptDelta);
                    break;

                case "response.audio_transcript.done":
                    string fullTranscript = evt["transcript"]?.ToString();
                    OnTranscriptDone?.Invoke(fullTranscript);
                    Debug.Log($"AI said: {fullTranscript}");
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    string userText = evt["transcript"]?.ToString();
                    OnUserTranscript?.Invoke(userText);
                    Debug.Log($"User said: {userText}");
                    break;

                case "error":
                    string errorMsg = evt["error"]?["message"]?.ToString();
                    Debug.LogError($"RealtimeAPIConnection: API error: {errorMsg}");
                    OnError?.Invoke(errorMsg);
                    break;

                case "input_audio_buffer.speech_started":
                    Debug.Log("RealtimeAPIConnection: User started speaking.");
                    break;

                case "input_audio_buffer.speech_stopped":
                    Debug.Log("RealtimeAPIConnection: User stopped speaking.");
                    break;

                default:
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"RealtimeAPIConnection: Error parsing event: {e.Message}");
        }
    }

    /// <summary>
    /// Send a JSON event to the API.
    /// </summary>
    public async void SendEvent(object eventObj)
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            Debug.LogWarning("RealtimeAPIConnection: Cannot send — not connected.");
            return;
        }

        string json = JsonConvert.SerializeObject(eventObj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
        }
        catch (Exception e)
        {
            Debug.LogError($"RealtimeAPIConnection: Send error: {e.Message}");
        }
    }

    /// <summary>
    /// Send raw audio data (PCM16, base64 encoded) to the API.
    /// </summary>
    public void SendAudio(string base64Audio)
    {
        var audioEvent = new
        {
            type = "input_audio_buffer.append",
            audio = base64Audio
        };
        SendEvent(audioEvent);
    }

    /// <summary>
    /// Make the AI initiate a conversation.
    /// Use this for your RQ3 — the transition from passive co-presence to active engagement.
    /// </summary>
    public void MakeAISpeak(string prompt = "Casually check in with the user. Say something brief and friendly.")
    {
        var createItem = new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "message",
                role = "user",
                content = new[]
                {
                    new { type = "input_text", text = prompt }
                }
            }
        };
        SendEvent(createItem);

        var createResponse = new
        {
            type = "response.create"
        };
        SendEvent(createResponse);

        Debug.Log($"RealtimeAPIConnection: AI initiation triggered with prompt: {prompt}");
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
                Debug.LogWarning($"RealtimeAPIConnection: Close error: {e.Message}");
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