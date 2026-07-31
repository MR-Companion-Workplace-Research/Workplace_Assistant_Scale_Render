using UnityEngine;
using Meta.XR.MRUtilityKit;

/// <summary>
/// Builds an invisible depth-only box matching a real MR scene anchor (by default the
/// TABLE / study desk), so virtual content behind it is hidden by real geometry.
///
/// WHY, given the project already has Depth API occlusion on every avatar shader:
/// the Depth API re-estimates environment depth per frame from the cameras. On a flat,
/// low-texture desk that estimate is noisy, and because the shaders resolve it with a
/// binary clip() the noise shows up as the seated avatar's lower body FLASHING into
/// view at the desk edge, with a ragged boundary. The desk is static and already
/// captured in the room scan, so we can occlude it geometrically instead — giving a
/// perfectly straight, completely stable edge.
///
/// This does not replace the Depth API; the two coexist. Depth API still handles
/// everything dynamic (hands, the participant's own body, objects moved during the
/// session). This just takes the one dominant, static occluder out of its hands.
///
/// Setup:
/// 1. Attach to any GameObject in the MR scene (alongside AvatarPlacer is fine).
/// 2. Make sure MRUK is in the scene with Scene Support enabled — same requirement
///    AvatarPlacer already has.
/// 3. Leave Occluder Anchor Label on TABLE for the seated desk study.
/// 4. Build to device. Like all MR features this does nothing meaningful in the
///    Editor, where there is no room scan.
///
/// Tick Debug Visualise to render the box as a visible magenta shape in a build when
/// you need to check its alignment on-site.
/// </summary>
public class SceneDepthOccluder : MonoBehaviour
{
    [Header("Target Anchor")]
    [Tooltip("Which MR scene anchor to build the depth occluder from. TABLE is the study " +
             "desk. You can tick several types — every matching anchor in the room gets " +
             "its own occluder box, so ticking TABLE + COUCH occludes both.")]
    public MRUKAnchor.SceneLabels occluderAnchorLabel = MRUKAnchor.SceneLabels.TABLE;

    [Header("Occluder Shape")]
    [Tooltip("Extend the occluder box down to the floor. IMPORTANT for the seated study: " +
             "MRUK usually captures a desk as just the tabletop SLAB, and a seated avatar's " +
             "legs are BELOW that slab — so a slab-only occluder would not hide them at all. " +
             "Extending to the floor makes the desk block the whole lower body, which is the " +
             "behaviour you want. Turn off only if you specifically want the space under the " +
             "desk to stay visible.")]
    public bool extendToFloor = true;

    [Tooltip("Grows (or with a negative value, shrinks) the occluder horizontally in meters, " +
             "on each side. A small positive value hides the seam where the depth estimate and " +
             "the scanned anchor disagree slightly at the desk edge. Keep this small — too much " +
             "and the occluder eats into the avatar's visible torso above the desk.")]
    public float horizontalPadding = 0.02f;

    [Tooltip("Raises (positive) or lowers (negative) the top surface of the occluder in meters. " +
             "Nudge NEGATIVE if the desk edge is clipping too much of the avatar's torso; " +
             "nudge POSITIVE if a sliver of lower body still shows above the desk.")]
    public float topSurfaceAdjust = 0f;

    [Header("Material")]
    [Tooltip("Material using the QuestOcclusion/DepthOnlyOccluder shader. STRONGLY RECOMMENDED " +
             "to assign one: nothing else in the scene references that shader, so without an " +
             "asset reference here Unity STRIPS it from the Android build and occlusion silently " +
             "does nothing on device. Leave empty only if you have instead added the shader to " +
             "Project Settings > Graphics > Always Included Shaders.")]
    public Material occluderMaterialAsset;

    [Header("Debug")]
    [Tooltip("Render the occluder as a visible solid box instead of an invisible one, so you " +
             "can verify its alignment against the real desk on-site. Turn OFF for the study.")]
    public bool debugVisualise = false;

    [Tooltip("Colour used by Debug Visualise. Magenta reads clearly against most real rooms.")]
    public Color debugColour = Color.magenta;

    [Tooltip("Log the anchor name and resolved box dimensions for each occluder built.")]
    public bool verboseLogging = true;

    private const string ShaderName = "QuestOcclusion/DepthOnlyOccluder";

    private static readonly int ColorMaskId = Shader.PropertyToID("_ColorMask");
    private static readonly int DebugColorId = Shader.PropertyToID("_DebugColor");

    private Material occluderMaterial;
    private Transform occluderRoot;

    private void Start()
    {
        MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded);
    }

    private void OnSceneLoaded()
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();
        if (room == null)
        {
            Debug.LogWarning("SceneDepthOccluder: No room found — no occluders built.");
            return;
        }

        if (!TryCreateMaterial())
            return;

        // The floor height is needed before we start building, since extendToFloor
        // stretches each box down to meet it.
        float floorY = ResolveFloorY(room);

        int built = 0;
        foreach (var anchor in room.Anchors)
        {
            if ((anchor.Label & occluderAnchorLabel) == 0)
                continue;

            if (BuildOccluderFor(anchor, floorY))
                built++;
        }

        if (built == 0)
        {
            Debug.LogWarning($"SceneDepthOccluder: No anchor matching '{occluderAnchorLabel}' had " +
                "usable geometry. The desk will fall back to Depth API occlusion only (the " +
                "flickering edge this component exists to fix). Check the surface is captured " +
                "in Quest Space Setup.");
        }
        else if (verboseLogging)
        {
            Debug.Log($"SceneDepthOccluder: built {built} depth occluder(s) for '{occluderAnchorLabel}'.");
        }
    }

    /// <summary>
    /// Resolves the depth-only material. Prefers the assigned asset, because that reference is
    /// also what keeps the shader from being stripped out of the build. Falls back to
    /// Shader.Find so the component still works in the Editor with nothing wired up.
    ///
    /// Fails loudly rather than silently doing nothing: a stripped shader looks exactly like
    /// "the fix didn't help", which is the worst thing to be debugging on-site.
    /// </summary>
    private bool TryCreateMaterial()
    {
        if (occluderMaterialAsset != null)
        {
            occluderMaterial = occluderMaterialAsset;
            return true;
        }

        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"SceneDepthOccluder: shader '{ShaderName}' not found and no Occluder " +
                "Material Asset assigned, so no occluders were built. On a device build this " +
                "almost certainly means the shader was STRIPPED: assign a material using it to " +
                "the Occluder Material Asset slot, or add the shader to Project Settings > " +
                "Graphics > Always Included Shaders.");
            return false;
        }

        Debug.LogWarning("SceneDepthOccluder: no Occluder Material Asset assigned — falling back " +
            "to Shader.Find. This works in the Editor but risks the shader being stripped from a " +
            "device build. Assign the material before building for the study.");

        occluderMaterial = new Material(shader) { name = "SceneDepthOccluder (runtime)" };
        return true;
    }

    /// <summary>
    /// Floor height for <see cref="extendToFloor"/>. Uses the room's FLOOR anchor; if the scan
    /// has none, falls back to a fixed drop below the surface, which is enough to cover the
    /// legs of a seated avatar.
    /// </summary>
    private float ResolveFloorY(MRUKRoom room)
    {
        if (room.FloorAnchor != null)
            return room.FloorAnchor.transform.position.y;

        Debug.LogWarning("SceneDepthOccluder: room has no FLOOR anchor — occluders will extend a " +
                         "fixed 2m below each surface instead.");
        return float.NaN;   // signals "use the fixed fallback drop" in BuildOccluderFor
    }

    /// <summary>
    /// Creates one occluder box for an anchor. Returns false if the anchor carries no usable
    /// plane or volume geometry.
    ///
    /// The box is built UNPARENTED in a yaw-aligned world frame rather than parented to the
    /// anchor with a local scale. That matters because the two anchor kinds orient their local
    /// axes differently: a VOLUME's local Y is roughly world-up, but a horizontal PLANE's local
    /// Z is its NORMAL (pointing up), which leaves its local Y lying flat in the world. Scaling
    /// "local Y" to extend the box downward would therefore stretch a plane-derived occluder
    /// SIDEWAYS. Deriving world-space corners first and rebuilding upright sidesteps that
    /// entirely and handles both kinds identically.
    ///
    /// Desks are upright, so keeping only the anchor's yaw loses nothing and keeps the box
    /// snug against a desk that sits at an angle to the world axes.
    /// </summary>
    private bool BuildOccluderFor(MRUKAnchor anchor, float floorY)
    {
        Vector3 localCenter;
        Vector3 localSize;

        if (anchor.VolumeBounds.HasValue)
        {
            // Preferred: the scan captured the desk as a solid volume.
            var bounds = anchor.VolumeBounds.Value;
            localCenter = bounds.center;
            localSize = bounds.size;
        }
        else if (anchor.PlaneRect.HasValue)
        {
            // Only a flat quad was captured (the tabletop). Nominal thickness along the plane's
            // local Z (its normal); extendToFloor does the real work of making it tall enough.
            var rect = anchor.PlaneRect.Value;
            localCenter = new Vector3(rect.center.x, rect.center.y, 0f);
            localSize = new Vector3(rect.size.x, rect.size.y, 0.02f);
        }
        else
        {
            return false;
        }

        // Yaw-aligned frame: take whichever anchor axis lies flattest in the world as the
        // box's forward. For a volume that is typically its own forward; for an upward-facing
        // plane it is one of the in-plane axes. Either way the result is an upright frame that
        // matches how the furniture is actually turned in the room.
        float yaw = ResolveYaw(anchor);
        Quaternion boxRotation = Quaternion.Euler(0f, yaw, 0f);
        Quaternion toBoxFrame = Quaternion.Inverse(boxRotation);

        // Project the eight world corners into that frame to get the true footprint and top.
        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            var corner = localCenter + Vector3.Scale(localSize * 0.5f, CornerSign(i));
            var inFrame = toBoxFrame * anchor.transform.TransformPoint(corner);
            min = Vector3.Min(min, inFrame);
            max = Vector3.Max(max, inFrame);
        }

        float topY = max.y + topSurfaceAdjust;
        // NaN floorY means the scan had no FLOOR anchor — drop a fixed 2m instead, which
        // comfortably clears a seated figure.
        float bottomY = extendToFloor
            ? (float.IsNaN(floorY) ? topY - 2f : floorY)
            : min.y;

        float height = Mathf.Max(topY - bottomY, 0.001f);
        var size = new Vector3(
            max.x - min.x + horizontalPadding * 2f,
            height,
            max.z - min.z + horizontalPadding * 2f);

        var centreInFrame = new Vector3(
            (min.x + max.x) * 0.5f,
            bottomY + height * 0.5f,
            (min.z + max.z) * 0.5f);

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = $"DepthOccluder_{anchor.name}";
        // Parented to a guaranteed-identity root, NOT to this component's GameObject: any
        // rotation or non-unit scale on the host would otherwise be baked into the box and
        // silently corrupt its world size.
        box.transform.SetParent(GetOccluderRoot(), worldPositionStays: false);
        box.transform.SetPositionAndRotation(boxRotation * centreInFrame, boxRotation);
        box.transform.localScale = size;

        // A collider here would put an invisible wall in front of the desk for hand rays and
        // any scene physics. This is purely a rendering aid — drop it.
        Destroy(box.GetComponent<Collider>());

        var renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = occluderMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        if (debugVisualise)
            MakeVisibleForDebug(renderer);

        if (verboseLogging)
        {
            Debug.Log($"SceneDepthOccluder: '{anchor.name}' -> size {size}, yaw {yaw:0.#}deg, " +
                      $"top y={topY:0.###}, bottom y={bottomY:0.###}.");
        }

        return true;
    }

    /// <summary>
    /// Lazily-created identity-transform parent that all occluder boxes hang under, so they
    /// are easy to find in the runtime hierarchy without inheriting any transform from the
    /// component's own GameObject.
    /// </summary>
    private Transform GetOccluderRoot()
    {
        if (occluderRoot == null)
        {
            occluderRoot = new GameObject("SceneDepthOccluders").transform;
            occluderRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            occluderRoot.localScale = Vector3.one;
        }
        return occluderRoot;
    }

    /// <summary>Sign pattern for corner <paramref name="i"/> of a box, i in [0,8).</summary>
    private static Vector3 CornerSign(int i)
    {
        return new Vector3(
            (i & 1) == 0 ? -1f : 1f,
            (i & 2) == 0 ? -1f : 1f,
            (i & 4) == 0 ? -1f : 1f);
    }

    /// <summary>
    /// The anchor's rotation about world-up, taken from whichever of its local axes lies
    /// flattest in the world. Using the flattest axis is what makes this work for both volume
    /// anchors (local Z is already horizontal) and upward-facing plane anchors (local Z points
    /// up, so an in-plane axis is chosen instead).
    /// </summary>
    private static float ResolveYaw(MRUKAnchor anchor)
    {
        Vector3 best = Vector3.forward;
        float flattest = float.PositiveInfinity;

        foreach (var axis in new[] { anchor.transform.right, anchor.transform.up, anchor.transform.forward })
        {
            float verticality = Mathf.Abs(axis.y);
            if (verticality < flattest)
            {
                flattest = verticality;
                best = axis;
            }
        }

        best.y = 0f;
        if (best.sqrMagnitude < 1e-6f)
            return 0f;   // degenerate; world-aligned is as good a guess as any

        return Quaternion.LookRotation(best.normalized, Vector3.up).eulerAngles.y;
    }

    /// <summary>
    /// Makes the box visible by opening up the occluder shader's own colour mask, so it can be
    /// eyeballed against the real desk during on-site setup.
    ///
    /// This deliberately does NOT swap in a separate debug shader. An earlier version used
    /// Shader.Find("Unlit/Color"), which Unity STRIPS from the Android build (nothing references
    /// it from an asset) — so the debug view silently did nothing on device, the one place it is
    /// actually needed. Driving a property of the shader we already ship avoids that entirely.
    ///
    /// Uses renderer.material (an instance) rather than sharedMaterial so debugging never writes
    /// through to the DepthOnlyOccluder.mat asset on disk.
    /// </summary>
    private void MakeVisibleForDebug(MeshRenderer renderer)
    {
        var instance = renderer.material;
        instance.SetFloat(ColorMaskId, 15f);   // 15 = RGBA, i.e. fully visible
        instance.SetColor(DebugColorId, debugColour);
    }
}
