using UnityEngine;

/// <summary>
/// Partially rotates the avatar's head bone toward a target (e.g., the user's head).
/// Runs in LateUpdate so it blends on top of whatever animation is playing.
/// 
/// Setup:
/// - This gets added automatically by AvatarDeskPlacer after spawning.
/// - Or attach manually and assign Target and HeadBone.
/// </summary>
public class HeadLookAt : MonoBehaviour
{
    [Tooltip("The transform to look at (e.g., CenterEyeAnchor from OVRCameraRig).")]
    public Transform Target;

    [Tooltip("The head bone. Auto-detected from Animator if not set.")]
    public Transform HeadBone;

    [Tooltip("How much the head turns toward the target. 0 = no turn, 1 = fully faces target.")]
    [Range(0f, 1f)]
    public float weight = 0.6f;

    [Tooltip("How fast the head blends toward the target rotation (degrees per second). Lower = smoother.")]
    public float smoothSpeed = 6f;

    [Tooltip("Max horizontal angle (degrees) the head will turn. Beyond this, it stops rotating.")]
    public float maxHorizontalAngle = 90f;

    [Tooltip("Max vertical angle (degrees) the head will tilt.")]
    public float maxVerticalAngle = 40f;

    private Quaternion currentBlendedRotation;
    private bool initialized;

    // Blend-out: smoothly return to animation pose when tracking is disabled
    private bool isBlendingOut;
    private float currentWeight;

    private void Start()
    {
        // Auto-detect head bone if not assigned
        if (HeadBone == null)
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                HeadBone = animator.GetBoneTransform(HumanBodyBones.Head);
            }
        }

        if (HeadBone == null)
        {
            Debug.LogWarning("HeadLookAt: No head bone found. Disabling.");
            enabled = false;
            return;
        }
    }

    private void LateUpdate()
    {
        if (HeadBone == null) return;

        // Get the current animation rotation
        Quaternion animationRotation = HeadBone.rotation;

        if (!initialized)
        {
            currentBlendedRotation = animationRotation;
            currentWeight = 0f;
            initialized = true;
        }

        if (isBlendingOut)
        {
            // Blend weight down to zero, then stop
            currentWeight = Mathf.MoveTowards(currentWeight, 0f, Time.deltaTime * smoothSpeed * 0.3f);
            Quaternion blendOutRotation = Quaternion.Slerp(animationRotation, currentBlendedRotation, currentWeight);
            currentBlendedRotation = blendOutRotation;
            HeadBone.rotation = currentBlendedRotation;

            if (currentWeight <= 0.001f)
            {
                isBlendingOut = false;
                initialized = false;
                enabled = false;
            }
            return;
        }

        if (Target == null) return;

        // Ramp weight up when first enabled
        currentWeight = Mathf.MoveTowards(currentWeight, 1f, Time.deltaTime * smoothSpeed * 0.3f);

        // Calculate direction from head to target
        Vector3 directionToTarget = Target.position - HeadBone.position;

        // Check angle limits using parent forward as reference
        Vector3 referenceForward = HeadBone.parent != null
            ? HeadBone.parent.forward
            : transform.forward;

        float horizontalAngle = Vector3.SignedAngle(
            Vector3.ProjectOnPlane(referenceForward, Vector3.up),
            Vector3.ProjectOnPlane(directionToTarget, Vector3.up),
            Vector3.up
        );

        float verticalAngle = Vector3.SignedAngle(
            Vector3.ProjectOnPlane(directionToTarget, Vector3.up),
            directionToTarget,
            Vector3.Cross(directionToTarget, Vector3.up)
        );

        float angleWeight = 1f;
        if (Mathf.Abs(horizontalAngle) > maxHorizontalAngle)
        {
            angleWeight = 0f;
        }
        else if (Mathf.Abs(horizontalAngle) > maxHorizontalAngle * 0.7f)
        {
            angleWeight = 1f - Mathf.InverseLerp(maxHorizontalAngle * 0.7f, maxHorizontalAngle, Mathf.Abs(horizontalAngle));
        }

        if (Mathf.Abs(verticalAngle) > maxVerticalAngle)
        {
            angleWeight *= 0f;
        }

        Quaternion lookRotation = Quaternion.LookRotation(directionToTarget, Vector3.up);

        float effectiveWeight = weight * angleWeight * currentWeight;
        Quaternion targetRotation = Quaternion.Slerp(animationRotation, lookRotation, effectiveWeight);

        currentBlendedRotation = Quaternion.Slerp(currentBlendedRotation, targetRotation, Time.deltaTime * smoothSpeed);

        HeadBone.rotation = currentBlendedRotation;
    }

    /// <summary>
    /// Enable or disable head tracking at runtime.
    /// When disabled, the head returns to pure animation control.
    /// </summary>
    public void SetEnabled(bool enable)
    {
        if (enable)
        {
            isBlendingOut = false;
            this.enabled = true;
        }
        else
        {
            // Don't disable immediately — let LateUpdate blend back to animation pose
            isBlendingOut = true;
        }
    }
}