using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Tiny runtime-UI toolkit shared by the arena scripts (HUD, touch controls, paint
/// palette). Same philosophy as the old demo: all UI is built in code, scenes stay dumb.
/// </summary>
public static class UiKit
{
    static Font _font;
    static Sprite _circle;

    public static Font DefaultFont
    {
        get
        {
            if (_font == null)
            {
                try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { _font = null; }
            }
            return _font;
        }
    }

    /// <summary>Soft white circle sprite generated once — used for joystick and round buttons.</summary>
    public static Sprite CircleSprite
    {
        get
        {
            if (_circle == null)
            {
                const int s = 64;
                var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
                var px = new Color32[s * s];
                float r = s * 0.5f - 1f;
                for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = x - s * 0.5f + 0.5f, dy = y - s * 0.5f + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)(255f * Mathf.Clamp01(r - d + 1f));
                    px[y * s + x] = new Color32(255, 255, 255, a);
                }
                tex.SetPixels32(px);
                tex.Apply(false);
                _circle = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
            }
            return _circle;
        }
    }

    public static Canvas MakeCanvas(string name, int sortingOrder, Transform parent = null)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        if (parent != null) go.transform.SetParent(parent, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    public static Text MakeText(Transform parent, string text, int size, TextAnchor align, bool shadow = true)
    {
        var go = new GameObject("Text", typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = DefaultFont;
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        if (shadow)
        {
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.7f);
            sh.effectDistance = new Vector2(2f, -2f);
        }
        return t;
    }

    public static Button MakeButton(Transform parent, string label, Color bg, Color fg, int fontSize, bool round = false)
    {
        var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = bg;
        if (round) img.sprite = CircleSprite;
        var t = MakeText(go.transform, label, fontSize, TextAnchor.MiddleCenter);
        t.color = fg;
        SetRect(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return go.GetComponent<Button>();
    }

    public static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
    }
}

/// <summary>Button that reports press-and-hold (for climb) as well as taps.</summary>
public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public bool Held { get; private set; }
    public System.Action onDown;

    public void OnPointerDown(PointerEventData eventData)
    {
        Held = true;
        if (onDown != null) onDown();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Held = false;
    }

    void OnDisable() { Held = false; }
}
