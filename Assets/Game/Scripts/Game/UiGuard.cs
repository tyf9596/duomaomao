using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Shared "did this pointer land on UI?" check that works for both the mouse and
/// the primary touch under the Input System UI module (touch needs its touchId).
/// </summary>
public static class UiGuard
{
    public static bool IsPointerOverUI()
    {
        var es = EventSystem.current;
        if (es == null) return false;
        var ts = Touchscreen.current;
        if (ts != null && ts.primaryTouch.press.isPressed)
            return es.IsPointerOverGameObject(ts.primaryTouch.touchId.ReadValue());
        return es.IsPointerOverGameObject();
    }
}
