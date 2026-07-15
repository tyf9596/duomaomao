using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Mobile orbit camera: one-finger drag rotates, two-finger pinch zooms.
/// Falls back to mouse (drag = rotate, wheel = zoom) so it can be tested in the Editor.
/// Gestures that begin over UI are ignored so dragging a slider never spins the camera.
/// </summary>
public class OrbitCamera : MonoBehaviour
{
    [Header("Pivot")]
    public Vector3 pivot = new Vector3(0f, 0.5f, 0f);

    [Header("Orbit")]
    public float yaw = 0f;
    public float pitch = 22f;
    public float minPitch = 5f;
    public float maxPitch = 82f;
    public float rotateSpeed = 0.2f;       // degrees per pixel of drag

    [Header("Zoom")]
    public float distance = 8f;
    public float minDistance = 3.5f;
    public float maxDistance = 14f;
    public float pinchZoomSpeed = 0.02f;   // units per pixel of pinch delta
    public float scrollZoomSpeed = 0.5f;   // units per scroll notch

    float _lastPinch = -1f;
    bool _uiGesture;
    float _homeYaw, _homePitch, _homeDistance;

    void Awake()
    {
        _homeYaw = yaw;
        _homePitch = pitch;
        _homeDistance = distance;
    }

    void OnEnable()
    {
        _lastPinch = -1f;
        _uiGesture = false;
    }

    /// <summary>Return to the framing the scene started with (used when the seeker takes over).</summary>
    public void ResetView()
    {
        yaw = _homeYaw;
        pitch = _homePitch;
        distance = _homeDistance;
        Apply();
    }

    void Update()
    {
        var ts = Touchscreen.current;
        var mouse = Mouse.current;
        int touchCount = 0;
        TouchControl t0 = null, t1 = null;
        if (ts != null)
        {
            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                if (t.press.isPressed)
                {
                    if (touchCount == 0) t0 = t;
                    else if (touchCount == 1) t1 = t;
                    touchCount++;
                }
            }
        }

        // A gesture that begins over UI belongs to the UI for its whole lifetime.
        bool began = (ts != null && ts.primaryTouch.press.wasPressedThisFrame)
                  || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        bool anyHeld = touchCount > 0 || (mouse != null && mouse.leftButton.isPressed);
        if (began) _uiGesture = UiGuard.IsPointerOverUI();
        if (!anyHeld) _uiGesture = false;
        if (_uiGesture)
        {
            _lastPinch = -1f;
            Apply();
            return;
        }

        if (touchCount >= 2)
        {
            float pinch = Vector2.Distance(t0.position.ReadValue(), t1.position.ReadValue());
            if (_lastPinch > 0f) distance -= (pinch - _lastPinch) * pinchZoomSpeed;
            _lastPinch = pinch;
        }
        else
        {
            _lastPinch = -1f;
            if (touchCount == 1)
            {
                Vector2 d = t0.delta.ReadValue();
                yaw += d.x * rotateSpeed;
                pitch -= d.y * rotateSpeed;
            }
            else if (mouse != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    yaw += d.x * rotateSpeed;
                    pitch -= d.y * rotateSpeed;
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !UiGuard.IsPointerOverUI())
                    distance -= Mathf.Sign(scroll) * scrollZoomSpeed;
            }
        }

        Apply();
    }

    void Apply()
    {
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = pivot - (rot * Vector3.forward) * distance;
        transform.rotation = rot;
    }

    void OnValidate()
    {
        Apply();
    }
}
