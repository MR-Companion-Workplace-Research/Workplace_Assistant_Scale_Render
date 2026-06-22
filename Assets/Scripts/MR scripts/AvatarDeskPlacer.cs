using UnityEngine;
using Meta.XR.MRUtilityKit;

/// <summary>
/// Places an avatar prefab on a desk/table anchor detected by MRUK,
/// with configurable position offset and automatic facing toward the user.
/// 
/// Setup:
/// 1. Attach this script to any GameObject in your scene.
/// 2. Assign your avatar prefab and OVRCameraRig reference in the Inspector.
/// 3. Make sure MRUK is in your scene and Scene Support is enabled.
/// 4. Adjust offset and scale in the Inspector to your liking.
/// </summary>
public class AvatarDeskPlacer : MonoBehaviour
{
    public enum RenderStyle { Realistic, Toon }

    [Header("References")]
    [Tooltip("Fallback avatar prefab. Used only if the matching render-style prefab below is not assigned.")]
    public GameObject avatarPrefab;

    [Header("Render Style (study IV)")]
    [Tooltip("Which render style to spawn. The realistic vs toon prefab is chosen from the two fields below.")]
    public RenderStyle renderStyle = RenderStyle.Realistic;

    [Tooltip("Realistic prefab (e.g. female_real).")]
    public GameObject avatarPrefabRealistic;

    [Tooltip("Toon-shaded prefab variant (e.g. female_toon).")]
    public GameObject avatarPrefabToon;

    [Tooltip("Reference to the OVRCameraRig in your scene.")]
    public OVRCameraRig cameraRig;

    [Header("Desk Placement Settings")]
    [Tooltip("Offset from the desk anchor center (local space). X = left/right, Y = up (above desk surface), Z = forward/back.")]
    public Vector3 positionOffset = new Vector3(0f, 0f, 0f);

    [Tooltip("If true, the avatar will rotate to face the user's head on spawn.")]
    public bool faceUser = true;

    [Tooltip("If true, only rotates on the Y axis (keeps avatar upright).")]
    public bool constrainToYAxis = true;

    [Header("Scale")]
    [Tooltip("Scale of the spawned avatar. Use (1,1,1) for human-sized, smaller values like (0.2, 0.2, 0.2) for miniature.")]
    public Vector3 avatarScale = Vector3.one;

    [Header("Face Mesh Settings")]
    [Tooltip("Name of the mesh object containing facial blendshapes.")]
    public string faceMeshName = "F_HeadSlot";

    [Tooltip("Name of the blendshape used for lip sync mouth opening.")]
    public string mouthBlendShapeName = "phoneme_Ah";

    [Header("Lip Sync")]
    [Tooltip("If true, the SALSA component already on the prefab drives the mouth, and " +
             "AgentVoiceController's built-in RMS lip sync is disabled (so the two don't fight). " +
             "Leave this ON for the Reallusion prefab, which ships with SALSA.")]
    public bool useSalsaLipSync = true;

    private GameObject spawnedAvatar;

    // Shader global the toon outline reads so its world thickness scales with the
    // avatar. A skinned mesh scaled at runtime bakes the scale into the verts, not
    // unity_ObjectToWorld, so the shader can't recover it — we hand it over here.
    private static readonly int OutlineScaleId = Shader.PropertyToID("_OutlineScale");

    private void ApplyOutlineScale(float scale)
    {
        Shader.SetGlobalFloat(OutlineScaleId, scale);
    }

    private void Start()
    {
        MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
    }

    private void OnSceneLoaded()
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null)
        {
            Debug.LogWarning("AvatarDeskPlacer: No room found.");
            return;
        }

        MRUKAnchor deskAnchor = null;

        foreach (var anchor in room.Anchors)
        {
            Debug.Log($"  Anchor: {anchor.name}, Labels: {anchor.Label}");

            if (anchor.Label.HasFlag(MRUKAnchor.SceneLabels.TABLE))
            {
                deskAnchor = anchor;
                break;
            }
        }

        if (deskAnchor == null)
        {
            Debug.LogWarning("AvatarDeskPlacer: No TABLE anchor found in room. " +
                "Make sure you've scanned your desk in Quest Space Setup.");
            return;
        }

        SpawnOnAnchor(deskAnchor);
    }

    private void SpawnOnAnchor(MRUKAnchor anchor)
    {
        Vector3 anchorPosition = anchor.transform.position;
        Vector3 spawnPosition = anchorPosition + positionOffset;

        GameObject prefabToSpawn = ResolvePrefab();
        if (prefabToSpawn == null)
        {
            Debug.LogWarning("AvatarDeskPlacer: No avatar prefab assigned for the selected render style.");
            return;
        }

        spawnedAvatar = Instantiate(prefabToSpawn, spawnPosition, Quaternion.identity);
        spawnedAvatar.transform.localScale = avatarScale;
        ApplyOutlineScale(avatarScale.x);   // keep toon outline proportional to the avatar

        if (faceUser && cameraRig != null)
        {
            FaceTarget(cameraRig.centerEyeAnchor.position);
        }
        else
        {
            spawnedAvatar.transform.rotation = anchor.transform.rotation;
        }

        // Gaze: point SALSA's Eyes module at the user. This replaces the old HeadLookAt
        // component, which fought SALSA over the head bone and could mis-orient it
        // (the "looking top-right" issue).
        SetupEyeGaze();

        // Set up voice
        SetupVoice();

        Debug.Log($"AvatarDeskPlacer: Avatar spawned at {spawnPosition} on anchor '{anchor.name}'");
    }

    /// <summary>
    /// Finds the face SkinnedMeshRenderer containing blendshapes.
    /// </summary>
    private SkinnedMeshRenderer FindFaceMesh()
    {
        // 1. Try direct child
        Transform directChild = spawnedAvatar.transform.Find(faceMeshName);
        if (directChild != null)
        {
            var renderer = directChild.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null) return renderer;
        }

        // 2. Recursive search by name
        var allRenderers = spawnedAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var renderer in allRenderers)
        {
            if (renderer.gameObject.name == faceMeshName)
            {
                return renderer;
            }
        }

        // 3. Last resort: find any mesh that has the mouth blendshape
        foreach (var renderer in allRenderers)
        {
            if (renderer.sharedMesh != null &&
                renderer.sharedMesh.GetBlendShapeIndex(mouthBlendShapeName) != -1)
            {
                Debug.LogWarning($"AvatarDeskPlacer: '{faceMeshName}' not found by name, " +
                    $"but found '{mouthBlendShapeName}' on '{renderer.gameObject.name}'. Using that.");
                return renderer;
            }
        }

        return null;
    }

    private void SetupVoice()
    {
        var voiceController = FindObjectOfType<AgentVoiceController>();
        if (voiceController == null)
        {
            Debug.LogWarning("AvatarDeskPlacer: No AgentVoiceController found in scene. Voice disabled.");
            return;
        }

        // AudioSource that plays the AI's voice. Reuse one already on the prefab
        // (e.g. the one SALSA reads) instead of blindly adding a second source.
        var audioSource = spawnedAvatar.GetComponentInChildren<AudioSource>();
        if (audioSource == null)
            audioSource = spawnedAvatar.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1.0f;
        audioSource.minDistance = 0.5f;
        audioSource.maxDistance = 10f;
        audioSource.playOnAwake = false;

        // Lip sync: when SALSA owns the mouth we pass a null face mesh to
        // AgentVoiceController, which disables its built-in RMS lip sync so the
        // two systems don't fight over the mouth blendshape.
        SkinnedMeshRenderer faceMesh = null;
        int mouthIndex = 0;

        if (!useSalsaLipSync)
        {
            faceMesh = FindFaceMesh();

            if (faceMesh != null && faceMesh.sharedMesh != null)
            {
                mouthIndex = faceMesh.sharedMesh.GetBlendShapeIndex(mouthBlendShapeName);

                if (mouthIndex != -1)
                {
                    Debug.Log($"AvatarDeskPlacer: Found '{mouthBlendShapeName}' on {faceMesh.gameObject.name}, index {mouthIndex}.");
                }
                else
                {
                    Debug.LogError($"AvatarDeskPlacer: No '{mouthBlendShapeName}' blendshape on {faceMesh.gameObject.name}!");
                    Mesh mesh = faceMesh.sharedMesh;
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                        Debug.Log($"  Available blendshape [{i}]: {mesh.GetBlendShapeName(i)}");
                    mouthIndex = 0;
                }
            }
            else
            {
                Debug.LogError("AvatarDeskPlacer: Could not find face mesh with blendshapes! " +
                    $"Searched for '{faceMeshName}'. Check your avatar hierarchy.");
            }
        }

        voiceController.Initialize(audioSource, faceMesh, mouthIndex);

        // Bind SALSA to the playback AudioSource, and link the emote bridge to this avatar.
        if (useSalsaLipSync)
            SetupSalsaLipSync(audioSource);
        SetupEmoteBridge();

        // Auto-connect to the voice API
        if (voiceController.backend == AgentVoiceController.VoiceBackend.ElevenLabs)
        {
            var elConnection = voiceController.elevenLabsConnection;
            if (elConnection == null)
                elConnection = FindObjectOfType<ElevenLabsConnection>();

            if (elConnection != null)
            {
                voiceController.elevenLabsConnection = elConnection;
                elConnection.Connect();
                Debug.Log("AvatarDeskPlacer: Connecting to ElevenLabs...");
            }
            else
            {
                Debug.LogWarning("AvatarDeskPlacer: No ElevenLabsConnection found. Voice disabled.");
            }
        }
        else
        {
            var apiConnection = voiceController.openAIConnection;
            if (apiConnection == null)
                apiConnection = FindObjectOfType<RealtimeAPIConnection>();

            if (apiConnection != null)
            {
                voiceController.openAIConnection = apiConnection;
                apiConnection.Connect();
                Debug.Log("AvatarDeskPlacer: Connecting to OpenAI Realtime API...");
            }
            else
            {
                Debug.LogWarning("AvatarDeskPlacer: No RealtimeAPIConnection found. Voice disabled.");
            }
        }
    }

    /// <summary>
    /// Binds the prefab's SALSA component to the AudioSource that plays the AI's voice,
    /// so SALSA analyses that audio to drive the mouth visemes.
    /// </summary>
    private void SetupSalsaLipSync(AudioSource audioSource)
    {
        var salsa = spawnedAvatar.GetComponentInChildren<CrazyMinnow.SALSA.Salsa>();
        if (salsa == null)
        {
            Debug.LogWarning("AvatarDeskPlacer: useSalsaLipSync is ON but no SALSA component " +
                "was found on the avatar. The mouth will not move.");
            return;
        }

        // AgentVoiceController streams PCM through a read-callback AudioClip, which SALSA
        // cannot read directly (it samples the clip buffer at the playhead — nonexistent
        // for streamed audio). So switch SALSA to external analysis and feed it the live
        // output amplitude via SalsaExternalAudioFeed.
        salsa.audioSrc = audioSource;
        salsa.useExternalAnalysis = true;
        salsa.configReady = true;
        salsa.Initialize();

        var feed = spawnedAvatar.GetComponent<SalsaExternalAudioFeed>();
        if (feed == null) feed = spawnedAvatar.AddComponent<SalsaExternalAudioFeed>();
        feed.Init(salsa, audioSource);

        Debug.Log("AvatarDeskPlacer: SALSA external audio feed wired (mouth driven by live amplitude).");
    }

    /// <summary>
    /// Points the prefab's SALSA Eyes module at the user so the head/eyes track them.
    /// The look target can't be set in the inspector because the avatar is spawned at
    /// runtime, so we assign it here. Affinity is 1.0 so the avatar stays locked on the
    /// user during the conversation instead of darting off to random points (the
    /// remaining % is what made it "look into nothing"). Lower it slightly (e.g. 0.95)
    /// if you want occasional natural glances back.
    /// </summary>
    private void SetupEyeGaze()
    {
        if (cameraRig == null) return;

        var eyes = spawnedAvatar.GetComponentInChildren<CrazyMinnow.SALSA.Eyes>();
        if (eyes == null)
        {
            Debug.LogWarning("AvatarDeskPlacer: No SALSA Eyes component found; gaze disabled.");
            return;
        }

        eyes.lookTarget = cameraRig.centerEyeAnchor;
        eyes.useAffinity = true;
        eyes.affinityPercentage = 1.0f;
        Debug.Log("AvatarDeskPlacer: SALSA Eyes look target locked to the user (affinity 1.0).");
    }

    /// <summary>
    /// Links the scene's ElevenLabsEmoteBridge (if present) to this avatar's
    /// EmoterController, so agent emotion tags can drive facial emotes.
    /// </summary>
    private void SetupEmoteBridge()
    {
        var bridge = FindObjectOfType<ElevenLabsEmoteBridge>();
        if (bridge == null) return; // optional component

        var emoter = spawnedAvatar.GetComponentInChildren<EmoterController>();
        if (emoter == null)
        {
            Debug.LogWarning("AvatarDeskPlacer: ElevenLabsEmoteBridge is in the scene but the " +
                "avatar has no EmoterController. Emotes disabled.");
            return;
        }

        bridge.SetEmoter(emoter);
        Debug.Log("AvatarDeskPlacer: Emote bridge linked to avatar EmoterController.");
    }

    private void FaceTarget(Vector3 targetPosition)
    {
        if (spawnedAvatar == null) return;

        Vector3 direction = targetPosition - spawnedAvatar.transform.position;

        if (constrainToYAxis)
        {
            direction.y = 0f;
        }

        if (direction.sqrMagnitude > 0.001f)
        {
            spawnedAvatar.transform.rotation = Quaternion.LookRotation(direction.normalized);
        }
    }

    public void UpdateFacing()
    {
        if (spawnedAvatar != null && cameraRig != null)
        {
            FaceTarget(cameraRig.centerEyeAnchor.position);
        }
    }

    public void SetScale(Vector3 newScale)
    {
        avatarScale = newScale;
        ApplyOutlineScale(avatarScale.x);
        if (spawnedAvatar != null)
        {
            spawnedAvatar.transform.localScale = avatarScale;
        }
    }

    /// <summary>Picks the prefab for the selected render style, falling back to avatarPrefab.</summary>
    private GameObject ResolvePrefab()
    {
        GameObject chosen = renderStyle == RenderStyle.Toon ? avatarPrefabToon : avatarPrefabRealistic;
        return chosen != null ? chosen : avatarPrefab;
    }

    /// <summary>Set the render style. Only takes effect on the next spawn (prefab choice is
    /// made at instantiate time), so call before the avatar is placed.</summary>
    public void SetRenderStyle(RenderStyle style)
    {
        renderStyle = style;
        if (spawnedAvatar != null)
            Debug.LogWarning("AvatarDeskPlacer: render style changed but avatar already spawned; " +
                "it applies on the next spawn only.");
    }

    public GameObject GetSpawnedAvatar()
    {
        return spawnedAvatar;
    }

    // =========================================================================
    //  Gaze & Head Tracking Control (for experimenter remote control)
    // =========================================================================

    /// <summary>
    /// Enable or disable gaze tracking by setting/clearing the SALSA Eyes look target.
    /// </summary>
    public void SetGazeTracking(bool enabled)
    {
        if (spawnedAvatar == null) return;

        var eyes = spawnedAvatar.GetComponentInChildren<CrazyMinnow.SALSA.Eyes>();
        if (eyes != null)
        {
            eyes.lookTarget = (enabled && cameraRig != null) ? cameraRig.centerEyeAnchor : null;
        }

        Debug.Log($"AvatarDeskPlacer: Gaze tracking {(enabled ? "enabled" : "disabled")}.");
    }
}