using UnityEngine;

/// <summary>
/// TEMPORARY / DEBUG SCRIPT.
/// Floats a PNG image next to the avatar's head. Assign the PNG yourself
/// in the Inspector (the <see cref="image"/> field).
///
/// Setup:
/// 1. Attach this script to your avatar prefab root (the same prefab that
///    AvatarPlacer spawns), or to any GameObject that has an Animator
///    in its children.
/// 2. Drag a PNG onto the "Image" field in the Inspector. A default-imported
///    Texture2D is fine — you do NOT need to change its import type to Sprite.
/// 3. Tweak Offset / Height / Face Camera to taste.
///
/// The head bone is auto-detected from the Animator (HumanBodyBones.Head),
/// matching how HeadLookAt finds it. The image follows the head's position
/// but uses the body's facing for the offset, so it doesn't swing around
/// when HeadLookAt rotates the head.
/// </summary>
[DisallowMultipleComponent]
public class HeadImageTag : MonoBehaviour
{
    [Header("Image")]
    [Tooltip("Drag your PNG here. A default-imported Texture2D works fine.")]
    public Texture2D image;

    [Header("Placement")]
    [Tooltip("Offset from the head bone, from the USER's point of view. " +
             "X = the user's right (negative = user's left), Y = up, Z = away from the user.")]
    public Vector3 offset = new Vector3(0.25f, 0.15f, 0f);

    [Tooltip("World height of the image in meters. Width auto-scales to keep the PNG's aspect ratio.")]
    public float heightMeters = 0.2f;

    [Tooltip("If true, the image always rotates to face the camera (billboard).")]
    public bool faceCamera = true;

    [Header("References (optional)")]
    [Tooltip("Head bone to anchor near. Auto-detected from the Animator if left empty.")]
    public Transform headBone;

    [Tooltip("Camera the billboard faces. Falls back to Camera.main if left empty.")]
    public Transform cameraTransform;

    private Transform imageHolder;
    private SpriteRenderer spriteRenderer;

    private void Start()
    {
        // Auto-detect the head bone the same way HeadLookAt does.
        if (headBone == null)
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            }
        }

        if (headBone == null)
        {
            Debug.LogWarning("HeadImageTag: No head bone found. Disabling.");
            enabled = false;
            return;
        }

        if (image == null)
        {
            Debug.LogWarning("HeadImageTag: No image assigned in the Inspector. Disabling.");
            enabled = false;
            return;
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        BuildImage();
    }

    private void BuildImage()
    {
        imageHolder = new GameObject("HeadImageTag_Image").transform;
        spriteRenderer = imageHolder.gameObject.AddComponent<SpriteRenderer>();

        // Build a sprite from the assigned texture. Pixels-per-unit is chosen so
        // the sprite's world height matches heightMeters; width follows aspect ratio.
        float ppu = image.height / Mathf.Max(0.0001f, heightMeters);
        spriteRenderer.sprite = Sprite.Create(
            image,
            new Rect(0f, 0f, image.width, image.height),
            new Vector2(0.5f, 0.5f),
            ppu);

        // Render on top so it isn't hidden inside the avatar mesh.
        spriteRenderer.sortingOrder = 100;
    }

    private void LateUpdate()
    {
        if (headBone == null || imageHolder == null) return;

        // The OVR camera may not be ready at Start; keep trying until we find it,
        // otherwise we'd fall back to the avatar's right (the user's left).
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        // Place the tag in a USER-relative frame so positive X is always to the
        // user's right (the avatar faces the user, so its own "right" is the user's
        // left). Uses world up + the camera, not the head bone, so it doesn't swing
        // when HeadLookAt rotates the head.
        Vector3 anchor = headBone.position;
        Vector3 up = Vector3.up;
        Vector3 right;

        if (cameraTransform != null)
        {
            // Horizontal direction from the user to the head, flattened to the ground plane.
            Vector3 toHead = anchor - cameraTransform.position;
            toHead.y = 0f;
            if (toHead.sqrMagnitude < 1e-4f) toHead = transform.forward;
            right = Vector3.Cross(up, toHead.normalized);   // points to the user's right
        }
        else
        {
            right = transform.right;
        }

        Vector3 forward = Vector3.Cross(right, up);          // away from the user
        imageHolder.position = anchor + right * offset.x + up * offset.y + forward * offset.z;

        if (faceCamera && cameraTransform != null)
        {
            // Face the camera; flip forward so the sprite's front side shows.
            Vector3 toCamera = imageHolder.position - cameraTransform.position;
            if (toCamera.sqrMagnitude > 0.0001f)
            {
                imageHolder.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
            }
        }
        else
        {
            imageHolder.rotation = transform.rotation;
        }
    }

    private void OnDisable()
    {
        if (imageHolder != null)
        {
            Destroy(imageHolder.gameObject);
        }
    }
}
