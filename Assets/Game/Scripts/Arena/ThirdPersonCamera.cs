using UnityEngine;

/// <summary>
/// Camera for the arena: third-person orbit for hiders (spherecast keeps it out of
/// walls, paint mode pulls in close) and a first-person mode for hunters — standard
/// FPS aiming where the crosshair is exactly the shot direction.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public float yaw;
    public float pitch = 18f;
    public float minPitch = -25f;
    public float maxPitch = 75f;
    public float lookSpeed = 0.18f;   // degrees per pixel
    public float distance = 3.1f;
    public float paintDistance = 1.4f;
    public float pivotHeight = 1.1f;
    public float eyeHeight = 1.18f;   // first-person eye level on the 1.35m body
    public bool paintMode;
    public bool firstPerson;

    float _currentDistance;

    void Awake()
    {
        _currentDistance = distance;
        // tight near plane so first-person view doesn't clip into walls you stand against
        var cam = GetComponent<Camera>();
        if (cam != null) cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, 0.05f);
    }

    public void AddLook(Vector2 pixelDelta)
    {
        yaw += pixelDelta.x * lookSpeed;
        pitch = Mathf.Clamp(pitch - pixelDelta.y * lookSpeed, minPitch, maxPitch);
    }

    /// <summary>Horizontal forward for camera-relative movement.</summary>
    public Vector3 FlatForward()
    {
        var f = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        return f;
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (firstPerson)
        {
            Quaternion fpRot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target.position + Vector3.up * eyeHeight + fpRot * Vector3.forward * 0.1f;
            transform.rotation = fpRot;
            return;
        }

        Vector3 pivot = target.position + Vector3.up * pivotHeight;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        float wanted = paintMode ? paintDistance : distance;

        // Pull in when a wall is between the pivot and the camera.
        Vector3 back = rot * Vector3.back;
        RaycastHit hit;
        if (Physics.SphereCast(pivot, 0.2f, back, out hit, wanted))
        {
            var ch = hit.collider.GetComponentInParent<Character>();
            if (ch == null) wanted = Mathf.Max(0.5f, hit.distance - 0.05f);
        }

        _currentDistance = Mathf.Lerp(_currentDistance, wanted, 12f * Time.deltaTime);
        transform.position = pivot + back * _currentDistance;
        transform.rotation = rot;
    }
}
