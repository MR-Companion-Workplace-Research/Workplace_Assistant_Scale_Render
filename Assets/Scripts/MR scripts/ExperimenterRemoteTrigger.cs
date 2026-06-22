using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// Listens for UDP commands from the experimenter's laptop/PC.
/// Allows remote triggering of study events (e.g., agent initiation).
///
/// Setup:
/// 1. Attach to the same GameObject as ElevenLabsConnection.
/// 2. Assign the ElevenLabsConnection reference (or let it auto-detect).
/// 3. Run the experimenter Python script on the laptop.
/// 4. Both devices must be on the same WiFi network.
///
/// Commands the experimenter can send:
///   INITIATE                  — Trigger agent to start talking (default prompt)
///   INITIATE:custom text      — Trigger with custom context
///   CONTEXT:some info         — Send a silent contextual update
///   PING                      — Activity ping to keep session alive
///   END_CONVERSATION          — AI wraps up the conversation
///   STATUS                    — Quest replies with current connection status
///   MIC:ON / MIC:OFF          — Mute or unmute user's microphone
///   GAZE:ON / GAZE:OFF        — Enable or disable avatar gaze + head tracking (eyes + head)
///
/// The Quest's IP address is logged to the console on start.
/// </summary>
public class ExperimenterRemoteTrigger : MonoBehaviour
{
    [Header("Network")]
    [Tooltip("UDP port to listen on. Must match the port in the experimenter's script.")]
    public int listenPort = 9100;

    [Header("References")]
    [Tooltip("Auto-detected if on the same GameObject.")]
    public ElevenLabsConnection elevenLabs;

    [Tooltip("Auto-detected via FindObjectOfType if not assigned.")]
    public AgentVoiceController voiceController;

    [Tooltip("Auto-detected via FindObjectOfType if not assigned.")]
    public AvatarDeskPlacer avatarPlacer;

    [Header("Session Keep-Alive")]
    [Tooltip("Automatically send activity pings during passive phase to prevent timeout.")]
    public bool autoActivityPing = true;

    [Tooltip("Seconds between automatic activity pings.")]
    public float activityPingInterval = 25f;

    // UDP
    private UdpClient udpListener;
    private Thread listenerThread;
    private bool isRunning;

    // Thread-safe command queue (UDP runs on a background thread)
    private ConcurrentQueue<string> commandQueue = new ConcurrentQueue<string>();

    // For sending replies back to the experimenter
    private IPEndPoint lastSenderEndpoint;

    // Auto-ping timer
    private float pingTimer;

    private void Start()
    {
        if (elevenLabs == null)
            elevenLabs = GetComponent<ElevenLabsConnection>();

        if (voiceController == null)
            voiceController = FindObjectOfType<AgentVoiceController>();

        if (avatarPlacer == null)
            avatarPlacer = FindObjectOfType<AvatarDeskPlacer>();

        if (elevenLabs == null)
        {
            Debug.LogError("ExperimenterRemoteTrigger: No ElevenLabsConnection found!");
            enabled = false;
            return;
        }

        if (voiceController == null)
            Debug.LogWarning("ExperimenterRemoteTrigger: No AgentVoiceController found. MIC commands will be unavailable.");

        if (avatarPlacer == null)
            Debug.LogWarning("ExperimenterRemoteTrigger: No AvatarDeskPlacer found. GAZE commands will be unavailable.");

        LogDeviceIP();
        StartListener();
    }

    private void LogDeviceIP()
    {
        // Show the Quest's IP so the experimenter knows where to send commands
        try
        {
            string hostName = Dns.GetHostName();
            var addresses = Dns.GetHostAddresses(hostName);
            foreach (var addr in addresses)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                {
                    Debug.Log($"=== EXPERIMENTER REMOTE TRIGGER ===");
                    Debug.Log($"    Quest IP: {addr}");
                    Debug.Log($"    Port:     {listenPort}");
                    Debug.Log($"===================================");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ExperimenterRemoteTrigger: Could not resolve IP: {e.Message}");
        }
    }

    private void StartListener()
    {
        try
        {
            udpListener = new UdpClient(listenPort);
            isRunning = true;

            listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "ExperimenterUDPListener"
            };
            listenerThread.Start();

            Debug.Log($"ExperimenterRemoteTrigger: Listening on UDP port {listenPort}.");
        }
        catch (Exception e)
        {
            Debug.LogError($"ExperimenterRemoteTrigger: Failed to start UDP listener: {e.Message}");
        }
    }

    private void ListenLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

        while (isRunning)
        {
            try
            {
                // This blocks until a datagram arrives
                byte[] data = udpListener.Receive(ref remoteEP);
                string message = Encoding.UTF8.GetString(data).Trim();
                lastSenderEndpoint = remoteEP;

                if (!string.IsNullOrEmpty(message))
                {
                    commandQueue.Enqueue(message);
                    Debug.Log($"ExperimenterRemoteTrigger: Received [{message}] from {remoteEP}");
                }
            }
            catch (SocketException)
            {
                // Expected when socket is closed during shutdown
                break;
            }
            catch (Exception e)
            {
                if (isRunning)
                    Debug.LogError($"ExperimenterRemoteTrigger: Receive error: {e.Message}");
            }
        }
    }

    private void Update()
    {
        // Process queued commands on the main thread
        while (commandQueue.TryDequeue(out string command))
        {
            HandleCommand(command);
        }

        // Auto activity ping during passive phase
        if (autoActivityPing && elevenLabs.IsConnected)
        {
            pingTimer += Time.deltaTime;
            if (pingTimer >= activityPingInterval)
            {
                elevenLabs.SendActivityPing();
                pingTimer = 0f;
            }
        }
    }

    private void HandleCommand(string raw)
    {
        // Split command and payload on first ':'
        string command;
        string payload = null;
        int colonIndex = raw.IndexOf(':');
        if (colonIndex >= 0)
        {
            command = raw.Substring(0, colonIndex).Trim().ToUpper();
            payload = raw.Substring(colonIndex + 1).Trim();
        }
        else
        {
            command = raw.Trim().ToUpper();
        }

        // --- Commands that DON'T require ElevenLabs connection ---
        switch (command)
        {
            case "MIC":
                HandleMicCommand(payload);
                return;

            case "GAZE":
                HandleGazeCommand(payload);
                return;
        }

        // --- Commands that require ElevenLabs connection ---
        if (!elevenLabs.IsConnected)
        {
            Debug.LogWarning("ExperimenterRemoteTrigger: ElevenLabs not connected. Ignoring command.");
            SendReply("ERROR: ElevenLabs not connected");
            return;
        }

        switch (command)
        {
            case "INITIATE":
                // Trigger agent to start talking
                if (!string.IsNullOrEmpty(payload))
                {
                    elevenLabs.TriggerAgentInitiation(context: payload);
                }
                else
                {
                    elevenLabs.TriggerAgentInitiation();
                }
                SendReply("OK: INITIATE");
                Debug.Log("ExperimenterRemoteTrigger: >>> Agent initiation triggered.");
                break;

            case "CONTEXT":
                // Silent contextual update (no response triggered)
                if (!string.IsNullOrEmpty(payload))
                {
                    elevenLabs.SendContextualUpdate(payload);
                    SendReply("OK: CONTEXT");
                }
                else
                {
                    SendReply("ERROR: CONTEXT requires payload");
                }
                break;

            case "PING":
                // Manual activity ping
                elevenLabs.SendActivityPing();
                SendReply("OK: PING");
                break;

            case "END_CONVERSATION":
                // Tell the AI to wrap up
                string endContext = payload ?? "The experiment session is ending. Wrap up the conversation naturally and say goodbye.";
                elevenLabs.SendContextualUpdate(endContext);
                elevenLabs.SendTextMessage("(session ending)");
                SendReply("OK: END_CONVERSATION");
                Debug.Log("ExperimenterRemoteTrigger: >>> End conversation triggered.");
                break;

            case "STATUS":
                string micStatus = voiceController != null ? (voiceController.isMicMuted ? "muted" : "live") : "N/A";
                string status = $"Connected={elevenLabs.IsConnected}, Mic={micStatus}";
                SendReply($"STATUS: {status}");
                break;

            default:
                Debug.LogWarning($"ExperimenterRemoteTrigger: Unknown command: {command}");
                SendReply($"ERROR: Unknown command '{command}'");
                break;
        }
    }

    // =========================================================================
    //  Mic Control
    // =========================================================================

    private void HandleMicCommand(string payload)
    {
        if (voiceController == null)
        {
            SendReply("ERROR: No AgentVoiceController found");
            return;
        }

        string action = (payload ?? "").ToUpper();
        switch (action)
        {
            case "ON":
                voiceController.SetMicMuted(false);
                SendReply("OK: MIC ON (unmuted)");
                break;
            case "OFF":
                voiceController.SetMicMuted(true);
                SendReply("OK: MIC OFF (muted)");
                break;
            default:
                // Toggle if no argument
                bool newState = !voiceController.isMicMuted;
                voiceController.SetMicMuted(newState);
                SendReply($"OK: MIC {(newState ? "OFF (muted)" : "ON (unmuted)")}");
                break;
        }
    }

    // =========================================================================
    //  Gaze & Head Tracking Control
    // =========================================================================

    private void HandleGazeCommand(string payload)
    {
        if (avatarPlacer == null)
        {
            SendReply("ERROR: No AvatarDeskPlacer found");
            return;
        }

        string action = (payload ?? "").ToUpper();
        switch (action)
        {
            case "ON":
                avatarPlacer.SetGazeTracking(true);
                SendReply("OK: GAZE ON");
                break;
            case "OFF":
                avatarPlacer.SetGazeTracking(false);
                SendReply("OK: GAZE OFF");
                break;
            default:
                SendReply("ERROR: GAZE requires ON or OFF");
                break;
        }
    }

    // =========================================================================
    //  Reply
    // =========================================================================

    /// <summary>
    /// Send a reply back to the experimenter's script.
    /// </summary>
    private void SendReply(string message)
    {
        if (lastSenderEndpoint == null) return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            udpListener.Send(data, data.Length, lastSenderEndpoint);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ExperimenterRemoteTrigger: Reply send failed: {e.Message}");
        }
    }

    private void OnDestroy()
    {
        isRunning = false;
        udpListener?.Close();
        listenerThread?.Join(1000);
    }

    private void OnApplicationQuit()
    {
        isRunning = false;
        udpListener?.Close();
    }
}