using System;
using UnityEngine;
using Meta.XR.MRUtilityKit;

/// <summary>
/// The per-participant configuration the RESEARCHER sets before a session: who this participant
/// is, and which visual cell they are in. Everything here is fixed for the whole session — the
/// only thing that varies per run is the task, which the participant picks on the launch menu.
///
/// WHY IT IS ITS OWN CLASS: the same twelve values are set on <see cref="StudySetup"/> in
/// Launch_Scene, carried across the scene load by <see cref="StudySession"/>, and applied by
/// <see cref="StudyControlPanel"/> in MR_Scene. Declaring them once and copying the whole object
/// means the three can never fall out of sync as fields are added — which is exactly what would
/// happen with three hand-maintained field lists.
/// </summary>
[Serializable]
public class StudyConfig
{
    [Header("Session Identity (written into every log)")]
    [Tooltip("Participant / run identifier. Shown on the launch menu (read-only to the " +
             "participant) and written into the log file name and header. e.g. P01. " +
             "Safe characters only — anything odd is replaced with '_' by the logger.")]
    public string participantId = "P00";

    [Tooltip("Label for the experimental cell, e.g. 'human_female_real'. Leave EMPTY to " +
             "auto-derive it from the Scale + Avatar conditions below (e.g. 'mini_femaletoon'). " +
             "NEVER displayed to the participant — it would reveal the manipulation.")]
    public string conditionLabel = "";

    [Header("Study Condition — Avatar")]
    [Tooltip("Which avatar variant this participant sees (gender x render style).")]
    public AvatarPlacer.AvatarVariant avatarVariant = AvatarPlacer.AvatarVariant.FemaleReal;

    [Header("Study Condition — Scale")]
    [Tooltip("Which Scale condition this participant runs. Human Sized attaches the human-sized " +
             "animator; Miniature attaches the miniature animator. Keep this consistent with " +
             "Avatar Scale below (a mismatch is warned about at spawn).")]
    public AvatarPlacer.ScaleCondition scaleCondition = AvatarPlacer.ScaleCondition.HumanSized;

    [Tooltip("Uniform scale of the spawned avatar. 1 = human-sized, ~0.2 = miniature.")]
    public float avatarScale = 1f;

    [Header("Placement Target")]
    [Tooltip("Which MR scene anchor to spawn the avatar on (TABLE for a desk, COUCH for a sofa). " +
             "The first room anchor matching this label is used.")]
    public MRUKAnchor.SceneLabels spawnAnchorLabel = MRUKAnchor.SceneLabels.TABLE;

    [Header("Placement Settings")]
    [Tooltip("Offset from the anchor center (world space). X = left/right, Z = forward/back. " +
             "Y is ignored when Snap To Surface is on — use Contact Height Adjust instead.")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("If true, the avatar rotates to face the user's head on spawn.")]
    public bool faceUser = true;

    [Tooltip("If true, facing only rotates on the Y axis (keeps the avatar upright).")]
    public bool constrainToYAxis = true;

    [Header("Surface Contact")]
    [Tooltip("If true, the avatar is vertically snapped so its contact point rests ON the anchor " +
             "surface after scaling (scale-independent placement).")]
    public bool snapToSurface = true;

    [Tooltip("Which point rests on the surface. FeetOnSurface = standing on a TABLE (miniature); " +
             "SeatedOnSurface = hips on the cushion of a COUCH (human-sized).")]
    public AvatarPlacer.SurfaceContact surfaceContact = AvatarPlacer.SurfaceContact.FeetOnSurface;

    [Tooltip("Vertical fine-tune of the contact point, in meters (after scaling). A small " +
             "NEGATIVE value sinks a seated avatar into the cushion so it doesn't hover.")]
    public float contactHeightAdjust = 0f;

    /// <summary>Condition label to log: the explicit one if set, else derived from the conditions.</summary>
    public string ResolvedConditionLabel =>
        string.IsNullOrEmpty(conditionLabel) ? AutoLabel() : conditionLabel;

    private string AutoLabel()
    {
        string scale = scaleCondition == AvatarPlacer.ScaleCondition.Miniature ? "mini" : "human";
        return $"{scale}_{avatarVariant.ToString().ToLowerInvariant()}";
    }

    /// <summary>
    /// Deep-ish copy. Used when handing the config to <see cref="StudySession"/> so a later edit
    /// to the source component (or a scene unload) cannot mutate what the session is carrying.
    /// </summary>
    public StudyConfig Clone() => (StudyConfig)MemberwiseClone();
}
