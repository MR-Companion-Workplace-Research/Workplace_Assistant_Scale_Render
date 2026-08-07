using UnityEngine;
using Meta.XR.MRUtilityKit;

/// <summary>
/// Places an avatar prefab on a chosen MR scene anchor detected by MRUK
/// (e.g. a TABLE/desk, a COUCH, a BED), with configurable position offset
/// and automatic facing toward the user.
///
/// Setup:
/// 1. Attach this script to any GameObject in your scene.
/// 2. Assign your avatar prefab and OVRCameraRig reference in the Inspector.
/// 3. Make sure MRUK is in your scene and Scene Support is enabled.
/// 4. Pick which anchor type to spawn on (Spawn Anchor Label).
/// 5. Adjust offset and scale in the Inspector to your liking.
/// </summary>
public class AvatarPlacer : MonoBehaviour
{
    /// <summary>How the avatar makes contact with the anchor surface.</summary>
    public enum SurfaceContact
    {
        FeetOnSurface,   // standing: the soles rest on the surface (e.g. miniature on a TABLE).
        SeatedOnSurface, // sitting: the hips rest on the cushion (e.g. human-sized on a COUCH).
    }

    /// <summary>Which avatar (gender x render style) this session uses. Independent of the
    /// Scale condition below — the two are chosen separately per participant.</summary>
    public enum AvatarVariant
    {
        FemaleReal,
        FemaleToon,
        MaleReal,
        MaleToon,
    }

    [Header("Study Condition — Avatar")]
    [Tooltip("Which avatar variant this session uses (gender x render style). The matching " +
             "prefab slot below is spawned. Separate from the Scale condition — set both " +
             "per participant.")]
    public AvatarVariant avatarVariant = AvatarVariant.FemaleReal;

    [Tooltip("female_real prefab (realistic female).")]
    public GameObject femaleRealPrefab;

    [Tooltip("female_toon prefab (toon female — the Variant of female_real).")]
    public GameObject femaleToonPrefab;

    [Tooltip("male_real prefab (realistic male).")]
    public GameObject maleRealPrefab;

    [Tooltip("male_toon prefab (toon male — the Variant of male_real).")]
    public GameObject maleToonPrefab;

    [Header("References")]
    [Tooltip("LEGACY fallback: spawned only when the prefab slot selected by Avatar Variant " +
             "above is empty. Prefer assigning all four variant slots and choosing via Avatar Variant.")]
    public GameObject avatarPrefab;

    [Tooltip("Reference to the OVRCameraRig in your scene.")]
    public OVRCameraRig cameraRig;

    /// <summary>The study's Scale condition for this session. Selects which Animator
    /// Controller is attached to the spawned avatar (and is sanity-checked against
    /// Avatar Scale at spawn so a mismatched session setup is caught in the log).</summary>
    public enum ScaleCondition
    {
        HumanSized, // avatarScale ~1.0 — e.g. seated across the table
        Miniature,  // avatarScale ~0.2 — e.g. standing on the desk
    }

    [Header("Study Condition — Scale")]
    [Tooltip("Which Scale condition this session runs. Human Sized attaches the Human-Sized " +
             "Animator, Miniature attaches the Miniature Animator. Set this together with " +
             "Avatar Scale per participant — a mismatch (e.g. Miniature condition with scale 1) " +
             "is warned about in the Console at spawn.")]
    public ScaleCondition scaleCondition = ScaleCondition.HumanSized;

    [Tooltip("Animator Controller attached to the avatar at spawn in the HUMAN-SIZED condition " +
             "(e.g. the seated animator). Leave empty to keep the controller already on the prefab.")]
    public RuntimeAnimatorController humanSizedAnimator;

    [Tooltip("Animator Controller attached to the avatar at spawn in the MINIATURE condition " +
             "(e.g. the standing-on-desk animator). Leave empty to keep the controller already on the prefab.")]
    public RuntimeAnimatorController miniatureAnimator;

    [Header("Placement Target")]
    [Tooltip("Which MR scene anchor to spawn the avatar on. Pick the surface type from your room scan " +
             "(e.g. TABLE for a desk, COUCH for a sofa, BED, FLOOR…). The first anchor in the room whose " +
             "labels match this selection is used. You can tick more than one type as a fallback set.")]
    public MRUKAnchor.SceneLabels spawnAnchorLabel = MRUKAnchor.SceneLabels.TABLE;

    [Header("Placement Settings")]
    [Tooltip("Offset from the anchor center (world space). X = left/right, Z = forward/back. " +
             "Y = up — but Y is IGNORED when Snap To Surface is on (the surface snap owns the " +
             "vertical; use Contact Height Adjust instead).")]
    public Vector3 positionOffset = new Vector3(0f, 0f, 0f);

    [Tooltip("If true, the avatar will rotate to face the user's head on spawn.")]
    public bool faceUser = true;

    [Tooltip("If true, only rotates on the Y axis (keeps avatar upright).")]
    public bool constrainToYAxis = true;

    [Header("Surface Contact")]
    [Tooltip("If true, the avatar is vertically snapped so its contact point rests ON the anchor " +
             "surface, AFTER scaling. This makes placement scale-independent (1.0 / 0.8 / 0.2 all " +
             "sit correctly) and lets a seated avatar sit IN the couch instead of standing on it. " +
             "Turn off to use the raw root-at-anchor placement.")]
    public bool snapToSurface = true;

    [Tooltip("Which point rests on the surface. FeetOnSurface = standing on a TABLE (miniature); " +
             "SeatedOnSurface = hips on the cushion of a COUCH (human-sized).")]
    public SurfaceContact surfaceContact = SurfaceContact.FeetOnSurface;

    [Tooltip("Vertical fine-tune applied to the contact point, in meters (after scaling). " +
             "Positive lifts the avatar; a small NEGATIVE value sinks a seated avatar into the " +
             "cushion so it doesn't look like it's hovering. Use this instead of Position Offset Y " +
             "when Snap To Surface is on.")]
    public float contactHeightAdjust = 0f;

    [Header("Scale")]
    [Tooltip("Scale of the spawned avatar. Use (1,1,1) for human-sized, smaller values like (0.2, 0.2, 0.2) for miniature.")]
    public Vector3 avatarScale = Vector3.one;

    [Header("Toon Key Light")]
    [Tooltip("Direction the toon light bands come FROM, relative to the avatar (X = avatar's " +
             "local right, Y = up, Z = the way the avatar faces). The posterized toon shaders " +
             "band against this instead of the scene's directional light, so the band layout on " +
             "the face is the same wherever MRUK spawns the avatar and whichever way it faces. " +
             "Default (-0.5, 0.7, 0.9): because the avatar is spawned facing the participant, " +
             "this reads to the PARTICIPANT as lit from THEIR top-right, shaded toward their " +
             "bottom-left (the previous_toon reference look). Flip the X sign to mirror it. " +
             "Set to zero to band against the real scene light instead.")]
    public Vector3 toonKeyLocalDirection = new Vector3(-0.5f, 0.7f, 0.9f);

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
    private float lastSurfaceY;     // anchor surface height, kept so SetScale can re-snap
    private bool hasSurfaceY;

    // Shader global the toon outline reads so its world thickness scales with the
    // avatar. A skinned mesh scaled at runtime bakes the scale into the verts, not
    // unity_ObjectToWorld, so the shader can't recover it — we hand it over here.
    private static readonly int OutlineScaleId = Shader.PropertyToID("_OutlineScale");

    private void ApplyOutlineScale(float scale)
    {
        Shader.SetGlobalFloat(OutlineScaleId, scale);
    }

    // Shader global the posterized toon shaders band against instead of the scene light:
    // the avatar spawns at an arbitrary orientation per room, so real-light N.L gives a
    // different (often flat) band layout on the face every run. This is the avatar-local
    // key direction transformed into world space at the avatar's FINAL rotation — call
    // again whenever that rotation changes. Zero clears it -> shaders use the scene light.
    private static readonly int ToonKeyDirId = Shader.PropertyToID("_ToonKeyDir");

    private void ApplyToonKeyDirection()
    {
        if (spawnedAvatar == null) return;
        Vector3 world = toonKeyLocalDirection.sqrMagnitude > 0.0001f
            ? spawnedAvatar.transform.rotation * toonKeyLocalDirection.normalized
            : Vector3.zero;
        Shader.SetGlobalVector(ToonKeyDirId, world);
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
            Debug.LogWarning("AvatarPlacer: No room found.");
            return;
        }

        MRUKAnchor targetAnchor = null;

        foreach (var anchor in room.Anchors)
        {
            Debug.Log($"  Anchor: {anchor.name}, Labels: {anchor.Label}");

            // Match if the anchor carries any of the selected label(s). Using a bitwise
            // mask (rather than HasFlag of a single label) lets the inspector pick one
            // surface type or several as an ordered fallback set.
            if ((anchor.Label & spawnAnchorLabel) != 0)
            {
                targetAnchor = anchor;
                break;
            }
        }

        if (targetAnchor == null)
        {
            Debug.LogWarning($"AvatarPlacer: No anchor matching '{spawnAnchorLabel}' found in room. " +
                "Make sure you've scanned that surface in Quest Space Setup.");
            return;
        }

        SpawnOnAnchor(targetAnchor);
    }

    private void SpawnOnAnchor(MRUKAnchor anchor)
    {
        Vector3 anchorPosition = anchor.transform.position;
        Vector3 spawnPosition = anchorPosition + positionOffset;

        GameObject prefabToSpawn = ResolvePrefab();
        if (prefabToSpawn == null)
        {
            Debug.LogWarning($"AvatarPlacer: No prefab assigned for Avatar Variant '{avatarVariant}' " +
                "and no legacy Avatar Prefab fallback. Nothing spawned.");
            return;
        }

        spawnedAvatar = Instantiate(prefabToSpawn, spawnPosition, Quaternion.identity);
        spawnedAvatar.transform.localScale = avatarScale;
        ApplyOutlineScale(avatarScale.x);   // keep toon outline proportional to the avatar

        // Attach the condition's Animator Controller BEFORE the surface snap below:
        // AlignContactToSurface poses the rig with animator.Update(0f) to measure the
        // seated/standing contact point, so it must see the condition's pose (a seated
        // human-sized clip and a standing miniature clip give different contact points).
        ApplyConditionAnimator();

        if (faceUser && cameraRig != null)
        {
            FaceTarget(cameraRig.centerEyeAnchor.position);
        }
        else
        {
            spawnedAvatar.transform.rotation = anchor.transform.rotation;
        }
        ApplyToonKeyDirection();   // key light follows the avatar's final facing

        // Snap the chosen contact point onto the anchor surface. Done AFTER scale + rotation,
        // so it's measured at the avatar's final size/pose — this is what makes placement
        // scale-independent and lets a seated avatar sit IN the couch instead of standing on it.
        lastSurfaceY = anchorPosition.y;
        hasSurfaceY = true;
        if (snapToSurface)
            AlignContactToSurface(lastSurfaceY);

        // Gaze: point SALSA's Eyes module at the user. This replaces the old HeadLookAt
        // component, which fought SALSA over the head bone and could mis-orient it
        // (the "looking top-right" issue).
        SetupEyeGaze();

        // Set up voice
        SetupVoice();

        Debug.Log($"AvatarPlacer: Avatar spawned at {spawnedAvatar.transform.position} on anchor '{anchor.name}'");
    }

    /// <summary>
    /// The prefab for the selected Avatar Variant. Falls back to the legacy Avatar Prefab
    /// field (with a warning) if the selected slot is empty, so an old scene setup keeps
    /// working until the four variant slots are assigned.
    /// </summary>
    private GameObject ResolvePrefab()
    {
        GameObject chosen;
        switch (avatarVariant)
        {
            case AvatarVariant.FemaleReal: chosen = femaleRealPrefab; break;
            case AvatarVariant.FemaleToon: chosen = femaleToonPrefab; break;
            case AvatarVariant.MaleReal:   chosen = maleRealPrefab;   break;
            case AvatarVariant.MaleToon:   chosen = maleToonPrefab;   break;
            default:                       chosen = null;             break;
        }

        if (chosen != null)
        {
            Debug.Log($"AvatarPlacer: Avatar Variant = {avatarVariant} -> spawning '{chosen.name}'.");
            return chosen;
        }

        if (avatarPrefab != null)
            Debug.LogWarning($"AvatarPlacer: prefab slot for Avatar Variant '{avatarVariant}' is EMPTY — " +
                $"falling back to the legacy Avatar Prefab '{avatarPrefab.name}'. Assign the four variant " +
                "slots so the variant dropdown actually controls the condition.");
        return avatarPrefab;
    }

    /// <summary>
    /// Attaches the Animator Controller for the selected Scale condition to the spawned
    /// avatar's Animator. Only the CONTROLLER is swapped — the Animator component itself
    /// (and serialized references to it, e.g. animDriver.Anim on the prefab) stays intact.
    /// Warns if the chosen controller is missing the body-gesture trigger parameters that
    /// EmoterController/AvatarBodyGestures fire, and if Avatar Scale looks inconsistent
    /// with the selected condition (a session-setup mistake).
    /// </summary>
    private void ApplyConditionAnimator()
    {
        // Sanity-check the session setup: condition vs actual scale.
        bool looksMiniature = avatarScale.x < 0.5f;
        if ((scaleCondition == ScaleCondition.Miniature) != looksMiniature)
            Debug.LogWarning($"AvatarPlacer: CONDITION MISMATCH? Scale Condition = {scaleCondition} " +
                $"but Avatar Scale = {avatarScale.x:0.##}. Check the per-participant setup.");

        var controller = scaleCondition == ScaleCondition.Miniature ? miniatureAnimator : humanSizedAnimator;
        if (controller == null)
        {
            Debug.Log($"AvatarPlacer: no {scaleCondition} animator assigned — keeping the " +
                      "controller already on the prefab.");
            return;
        }

        var animator = spawnedAvatar.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            Debug.LogWarning("AvatarPlacer: avatar has no Animator component — cannot attach " +
                             $"the {scaleCondition} controller.");
            return;
        }

        animator.runtimeAnimatorController = controller;

        // The body-gesture path (AvatarBodyGestures -> EmoterController -> animDriver) fires
        // these triggers; a controller without them silently swallows every gesture.
        foreach (var trigger in new[] { "WaveTrigger", "GestureTrigger", "HeadNodTrigger" })
        {
            bool found = false;
            foreach (var p in animator.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == trigger) { found = true; break; }
            if (!found)
                Debug.LogWarning($"AvatarPlacer: controller '{controller.name}' has no '{trigger}' " +
                    "trigger — that body gesture will not play in this condition.");
        }

        Debug.Log($"AvatarPlacer: attached '{controller.name}' ({scaleCondition} condition) " +
                  $"to Animator '{animator.name}'.");
    }

    /// <summary>
    /// Shifts the spawned avatar vertically so its contact point (soles when standing,
    /// hips when seated) rests on the anchor surface at <paramref name="surfaceY"/>.
    /// Measured after scale + pose, so the result is the same at any avatarScale.
    /// </summary>
    private void AlignContactToSurface(float surfaceY)
    {
        if (spawnedAvatar == null) return;

        // Pose the rig once so bones/bounds reflect the seated (or idle) pose rather than
        // the import T-pose — the Animator hasn't ticked yet on the spawn frame.
        var animator = spawnedAvatar.GetComponentInChildren<Animator>();
        if (animator != null && animator.isActiveAndEnabled)
            animator.Update(0f);

        float targetY = surfaceY + contactHeightAdjust;

        float contactY;
        if (surfaceContact == SurfaceContact.SeatedOnSurface &&
            animator != null && animator.isHuman &&
            animator.GetBoneTransform(HumanBodyBones.Hips) != null)
        {
            // Seated: rest the hips (≈ where the body meets the cushion) on the surface.
            contactY = animator.GetBoneTransform(HumanBodyBones.Hips).position.y;
        }
        else
        {
            // Standing (or no humanoid rig): rest the lowest rendered point (the feet) on it.
            if (!TryGetRenderBoundsMinY(out contactY))
                return; // nothing to measure; leave placement as-is
        }

        float dy = targetY - contactY;
        spawnedAvatar.transform.position += Vector3.up * dy;
    }

    /// <summary>World-space lowest Y across all of the avatar's renderers. False if none.</summary>
    private bool TryGetRenderBoundsMinY(out float minY)
    {
        minY = 0f;
        var renderers = spawnedAvatar.GetComponentsInChildren<Renderer>();
        bool any = false;
        foreach (var r in renderers)
        {
            // Skip non-spatial renderers (e.g. world-space UI) that would skew the bounds.
            if (r is SkinnedMeshRenderer || r is MeshRenderer)
            {
                float rMin = r.bounds.min.y;
                if (!any || rMin < minY) minY = rMin;
                any = true;
            }
        }
        return any;
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
                Debug.LogWarning($"AvatarPlacer: '{faceMeshName}' not found by name, " +
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
            Debug.LogWarning("AvatarPlacer: No AgentVoiceController found in scene. Voice disabled.");
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
                    Debug.Log($"AvatarPlacer: Found '{mouthBlendShapeName}' on {faceMesh.gameObject.name}, index {mouthIndex}.");
                }
                else
                {
                    Debug.LogError($"AvatarPlacer: No '{mouthBlendShapeName}' blendshape on {faceMesh.gameObject.name}!");
                    Mesh mesh = faceMesh.sharedMesh;
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                        Debug.Log($"  Available blendshape [{i}]: {mesh.GetBlendShapeName(i)}");
                    mouthIndex = 0;
                }
            }
            else
            {
                Debug.LogError("AvatarPlacer: Could not find face mesh with blendshapes! " +
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
                Debug.Log("AvatarPlacer: Connecting to ElevenLabs...");
            }
            else
            {
                Debug.LogWarning("AvatarPlacer: No ElevenLabsConnection found. Voice disabled.");
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
                Debug.Log("AvatarPlacer: Connecting to OpenAI Realtime API...");
            }
            else
            {
                Debug.LogWarning("AvatarPlacer: No RealtimeAPIConnection found. Voice disabled.");
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
            Debug.LogWarning("AvatarPlacer: useSalsaLipSync is ON but no SALSA component " +
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

        Debug.Log("AvatarPlacer: SALSA external audio feed wired (mouth driven by live amplitude).");
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
            Debug.LogWarning("AvatarPlacer: No SALSA Eyes component found; gaze disabled.");
            return;
        }

        eyes.lookTarget = cameraRig.centerEyeAnchor;
        eyes.useAffinity = true;
        eyes.affinityPercentage = 1.0f;
        Debug.Log("AvatarPlacer: SALSA Eyes look target locked to the user (affinity 1.0).");
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
            Debug.LogWarning("AvatarPlacer: ElevenLabsEmoteBridge is in the scene but the " +
                "avatar has no EmoterController. Emotes disabled.");
            return;
        }

        bridge.SetEmoter(emoter);
        Debug.Log("AvatarPlacer: Emote bridge linked to avatar EmoterController.");
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
            ApplyToonKeyDirection();   // keep the band layout glued to the face
        }
    }

    /// <summary>
    /// The avatar instance spawned for this trial, or NULL until the MRUK OnSceneLoaded callback
    /// has run — which is well after every Awake/Start, so anything reading this must poll or be
    /// driven from a later event rather than caching it at startup.
    ///
    /// Exposed for <see cref="TaskPanel"/> in Avatar anchor mode, which places the participant's
    /// task sheet beside the avatar and therefore has to wait for it to exist.
    /// </summary>
    public GameObject SpawnedAvatar => spawnedAvatar;

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
            // Re-snap so the new scale doesn't shift the contact point off the surface.
            if (snapToSurface && hasSurfaceY)
                AlignContactToSurface(lastSurfaceY);
        }
    }

    /// <summary>Switch the Scale condition at runtime: re-attaches the matching animator to
    /// the spawned avatar and re-snaps it to the surface (the new pose moves the contact
    /// point). Normally the condition is just set in the Inspector before Play.</summary>
    public void SetScaleCondition(ScaleCondition condition)
    {
        scaleCondition = condition;
        if (spawnedAvatar != null)
        {
            ApplyConditionAnimator();
            if (snapToSurface && hasSurfaceY)
                AlignContactToSurface(lastSurfaceY);
        }
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

        Debug.Log($"AvatarPlacer: Gaze tracking {(enabled ? "enabled" : "disabled")}.");
    }
}