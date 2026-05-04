using UnityEngine;

/// <summary>
/// Auto-frames CharacterCamera to show the full character mesh at runtime.
/// Attach to CharacterCamera. Set target to HatWanderer root transform.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CharacterView : MonoBehaviour
{
    public Transform target;

    void Start()
    {
        if (target == null)
        {
            Debug.LogWarning("[CharacterView] target not assigned — add HatWanderer as target.");
            return;
        }

        var renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("[CharacterView] no Renderers found under target.");
            return;
        }

        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        var cam = GetComponent<Camera>();
        float halfFovRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        // Aspect: RT is portrait (256x512), so vertical FOV governs height
        float dist = (bounds.size.y * 0.58f) / Mathf.Tan(halfFovRad);

        // Character faces -Z → camera at min.z side, LookAt center
        transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - dist);
        transform.LookAt(bounds.center);

        Debug.Log($"[CharacterView] Framed: center={bounds.center} size.y={bounds.size.y:F0} dist={dist:F0}");
    }
}
