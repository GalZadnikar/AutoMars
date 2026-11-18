using UnityEngine;

/// Keeps world-space labels facing the camera.
public class Billboard : MonoBehaviour
{
    Transform cam;

    void LateUpdate()
    {
        if (cam == null)
        {
            if (Camera.main == null) return;
            cam = Camera.main.transform;
        }
        transform.rotation = cam.rotation;
    }
}
