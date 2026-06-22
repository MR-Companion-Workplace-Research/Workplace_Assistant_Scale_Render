using System.Collections;
using UnityEngine;

/// <summary>
/// Simple auto-blink script for avatars.
/// Controls a blink blendshape by lerping from the eyes-open value to the eyes-closed value.
///
/// Standard avatars: closedValue = 100, openValue = 0 (blendshape 0 = neutral/open, 100 = closed).
/// Inverted avatars: closedValue = 0, openValue = some non-zero value.
///
/// Attach to the avatar root. It will search children for the face mesh automatically.
/// </summary>
public class SimpleAutoBlinker : MonoBehaviour
{
    [Header("Target Mesh Settings")]
    [Tooltip("Name of the mesh object containing the blink blendshape.")]
    public string faceMeshName = "Avater_Female_GEO";

    [Tooltip("Name of the blink blendshape.")]
    public string blinkShapeName = "Expression_Blink";

    [Header("Blendshape Values")]
    [Tooltip("Blendshape weight when eyes are OPEN. Standard avatars: 0. Inverted avatars: non-zero.")]
    public float openValue = 0f;

    [Tooltip("Blendshape weight when eyes are CLOSED. Standard avatars: 100. Inverted avatars: 0.")]
    public float closedValue = 100f;

    [Header("Blink Timing (seconds)")]
    [Tooltip("Minimum time between blinks.")]
    public float minInterval = 2.0f;
    [Tooltip("Maximum time between blinks.")]
    public float maxInterval = 6.0f;

    [Tooltip("Time to close the eyes.")]
    public float closeSeconds = 0.05f;
    [Tooltip("Time eyes stay fully closed.")]
    public float closedHoldTime = 0.02f;
    [Tooltip("Time to open the eyes.")]
    public float openSeconds = 0.08f;

    // Runtime references
    private SkinnedMeshRenderer faceMeshRenderer;
    private int blinkIndex = -1;

    private Coroutine blinkCoroutine;
    private bool initialized = false;

    private void Start()
    {
        StartCoroutine(InitializeNextFrame());
    }

    private IEnumerator InitializeNextFrame()
    {
        yield return null; // 等一幀，讓 Animator 先跑
        Initialize();
    }

    private void OnEnable()
    {
        if (initialized && blinkCoroutine == null)
        {
            blinkCoroutine = StartCoroutine(BlinkRoutine());
        }
    }

    private void OnDisable()
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }
    }

    private void Initialize()
    {
        // 1. Search by name in children
        var allRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();
        foreach (var renderer in allRenderers)
        {
            if (renderer.gameObject.name == faceMeshName)
            {
                faceMeshRenderer = renderer;
                break;
            }
        }

        // 2. Fallback: find any mesh with the blink blendshape
        if (faceMeshRenderer == null)
        {
            foreach (var renderer in allRenderers)
            {
                if (renderer.sharedMesh != null &&
                    renderer.sharedMesh.GetBlendShapeIndex(blinkShapeName) != -1)
                {
                    faceMeshRenderer = renderer;
                    Debug.LogWarning($"[SimpleAutoBlinker] '{faceMeshName}' not found. " +
                        $"Using '{renderer.gameObject.name}' which has '{blinkShapeName}'.");
                    break;
                }
            }
        }

        if (faceMeshRenderer == null)
        {
            Debug.LogError($"[SimpleAutoBlinker] Could not find a mesh with '{blinkShapeName}'. Disabling.");
            return;
        }

        // 3. Get blendshape index
        blinkIndex = faceMeshRenderer.sharedMesh.GetBlendShapeIndex(blinkShapeName);
        if (blinkIndex == -1)
        {
            Debug.LogError($"[SimpleAutoBlinker] '{blinkShapeName}' not found on {faceMeshRenderer.gameObject.name}. Disabling.");
            return;
        }

        float runtimeOpen = faceMeshRenderer.GetBlendShapeWeight(blinkIndex);
        if (runtimeOpen != openValue)
        {
            Debug.Log($"[SimpleAutoBlinker] openValue 從 {openValue} 修正為實際 runtime 值 {runtimeOpen}");
            openValue = runtimeOpen;
        }

        Debug.Log($"[SimpleAutoBlinker] Initialized. Target: {faceMeshRenderer.gameObject.name}, " +
            $"BlendShape: {blinkShapeName} (index {blinkIndex}), " +
            $"Open={openValue}, Closed={closedValue}, " +
            $"Runtime weight at init={faceMeshRenderer.GetBlendShapeWeight(blinkIndex)}");

        initialized = true;

        if (blinkCoroutine == null)
        {
            blinkCoroutine = StartCoroutine(BlinkRoutine());
        }
    }

    private void SetBlinkValue(float value)
    {
        if (!initialized) return;
        faceMeshRenderer.SetBlendShapeWeight(blinkIndex, Mathf.Clamp(value, 0f, 100f));
    }

    IEnumerator BlinkRoutine()
    {
        float range = closedValue - openValue; // positive = standard (0→100), negative = inverted

        while (true)
        {
            // Wait random interval
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));

            // Close eyes: lerp from openValue toward closedValue
            float current = openValue;
            if (closeSeconds > 0 && range != 0f)
            {
                float speed = range / closeSeconds;
                while ((range > 0f) ? (current < closedValue) : (current > closedValue))
                {
                    current += Time.deltaTime * speed;
                    SetBlinkValue(current);
                    yield return null;
                }
            }
            SetBlinkValue(closedValue);

            // Hold closed
            yield return new WaitForSeconds(closedHoldTime);

            // Open eyes: lerp from closedValue back to openValue (reverse direction)
            current = closedValue;
            if (openSeconds > 0 && range != 0f)
            {
                float speed = range / openSeconds;
                while ((range > 0f) ? (current > openValue) : (current < openValue))
                {
                    current -= Time.deltaTime * speed;
                    SetBlinkValue(current);
                    yield return null;
                }
            }
            SetBlinkValue(openValue);
        }
    }
}