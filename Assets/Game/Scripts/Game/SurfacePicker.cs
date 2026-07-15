using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Phase 1 probe: a tap (press + release without dragging) raycasts from the camera
/// into the scene. On a hit it drops a small marker at the point and logs the object
/// and UV — proving touch-to-surface works before we build painting on top of it.
/// </summary>
public class SurfacePicker : MonoBehaviour
{
    public Camera cam;
    public float tapMoveThreshold = 12f;   // px; beyond this the gesture is a drag, not a tap
    public float rayLength = 100f;
    public float markerScale = 0.14f;
    public int maxMarkers = 12;

    Vector2 _downPos;
    bool _down;
    bool _moved;
    readonly System.Collections.Generic.Queue<GameObject> _markers = new System.Collections.Generic.Queue<GameObject>();

    void Awake()
    {
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        Vector2 pos;
        bool pressed, released;
        ReadPointer(out pos, out pressed, out released);

        if (pressed)
        {
            _down = true;
            _moved = false;
            _downPos = pos;
        }
        else if (_down)
        {
            if ((pos - _downPos).magnitude > tapMoveThreshold) _moved = true;
            if (released)
            {
                if (!_moved) DoPick(pos);
                _down = false;
            }
        }
    }

    void ReadPointer(out Vector2 pos, out bool pressed, out bool released)
    {
        pos = Vector2.zero; pressed = false; released = false;

        var ts = Touchscreen.current;
        if (ts != null)
        {
            var pt = ts.primaryTouch;
            if (pt.press.isPressed || pt.press.wasPressedThisFrame || pt.press.wasReleasedThisFrame)
            {
                pos = pt.position.ReadValue();
                pressed = pt.press.wasPressedThisFrame;
                released = pt.press.wasReleasedThisFrame;
                return;
            }
        }

        var m = Mouse.current;
        if (m != null)
        {
            pos = m.position.ReadValue();
            pressed = m.leftButton.wasPressedThisFrame;
            released = m.leftButton.wasReleasedThisFrame;
        }
    }

    void DoPick(Vector2 screenPos)
    {
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(screenPos);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, rayLength))
        {
            Debug.Log($"[SurfacePicker] hit '{hit.collider.name}' at {hit.point} uv={hit.textureCoord}");

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = marker.GetComponent<Collider>();
            if (col != null) Destroy(col);
            marker.name = "TapMarker";
            marker.transform.position = hit.point;
            marker.transform.localScale = Vector3.one * markerScale;

            _markers.Enqueue(marker);
            while (_markers.Count > maxMarkers)
            {
                var old = _markers.Dequeue();
                if (old != null) Destroy(old);
            }
        }
        else
        {
            Debug.Log("[SurfacePicker] no hit");
        }
    }
}
