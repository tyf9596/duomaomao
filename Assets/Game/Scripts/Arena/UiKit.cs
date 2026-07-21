using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime-UI toolkit shared by the arena scripts. All UI is built in code, scenes
/// stay dumb. Since the INK &amp; PAINT redesign (2026-07-21, design doc
/// MecchaChameleon-UI-Redesign 1a) this also owns the design tokens, the Baloo 2
/// fonts, the white tintable sprite/icon library and the sticker-button factory.
/// </summary>
public static class UiKit
{
    // ---------------- design tokens (spec section 1.1) ----------------

    public static readonly Color Ink = Hex("17171F");
    public static readonly Color Ink2 = Hex("232330");
    public static readonly Color Cream = Hex("FFF8EC");
    public static readonly Color CreamBg = Hex("FFF3DC");      // resultB poster bg
    public static readonly Color CreamBg2 = Hex("FFE9C4");     // resultB stripe
    public static readonly Color Blue = Hex("2E8FE0");
    public static readonly Color BlueEdge = Hex("1E5FA0");
    public static readonly Color HunterRed = Hex("E8442E");
    public static readonly Color HunterRedEdge = Hex("A81818");
    public static readonly Color HunterRedBright = Hex("FF5A42");
    public static readonly Color Gold = Hex("FFC431");
    public static readonly Color GoldEdge = Hex("C78F12");
    public static readonly Color GoldText = Hex("3A2A05");
    public static readonly Color Green = Hex("3FBF5C");
    public static readonly Color GreenEdge = Hex("2A8C40");
    public static readonly Color GreenHint = Hex("9BE8AF");
    public static readonly Color Purple = Hex("8A5CD6");
    public static readonly Color PurpleEdge = Hex("5F3AA0");
    public static readonly Color ClearRed = Hex("7A2418");
    public static readonly Color TextDim = new Color(1f, 1f, 1f, 0.72f);
    public static readonly Color[] Rainbow =
        { Hex("D63A2A"), Hex("F5822A"), Hex("FFC431"), Hex("3FBF5C"), Hex("2AB8A8"), Hex("2E8FE0"), Hex("8A5CD6") };

    public static Color Hex(string h)
    {
        Color c;
        return ColorUtility.TryParseHtmlString("#" + h, out c) ? c : Color.magenta;
    }

    // ---------------- fonts ----------------

    static Font _font, _fontBold, _legacy;

    /// <summary>Body font: Baloo 2 SemiBold (falls back to LegacyRuntime).</summary>
    public static Font DefaultFont
    {
        get
        {
            if (_font == null) _font = Resources.Load<Font>("Fonts/Baloo2-SemiBold");
            if (_font == null) _font = LegacyFont;
            return _font;
        }
    }

    /// <summary>Display font: Baloo 2 ExtraBold — titles, buttons, numbers.</summary>
    public static Font BoldFont
    {
        get
        {
            if (_fontBold == null) _fontBold = Resources.Load<Font>("Fonts/Baloo2-ExtraBold");
            if (_fontBold == null) _fontBold = LegacyFont;
            return _fontBold;
        }
    }

    static Font LegacyFont
    {
        get
        {
            if (_legacy == null)
            {
                try { _legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { _legacy = null; }
            }
            return _legacy;
        }
    }

    // ---------------- sprites ----------------

    static Sprite _circle;
    static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

    /// <summary>White tintable shape sprite from Resources/UI/Sprites.</summary>
    public static Sprite Shape(string name)
    {
        Sprite s;
        if (SpriteCache.TryGetValue(name, out s)) return s;
        s = Resources.Load<Sprite>("UI/Sprites/" + name);
        SpriteCache[name] = s;
        return s;
    }

    /// <summary>White silhouette icon from Resources/UI/Icons.</summary>
    public static Sprite Icon(string name)
    {
        string key = "i:" + name;
        Sprite s;
        if (SpriteCache.TryGetValue(key, out s)) return s;
        s = Resources.Load<Sprite>("UI/Icons/" + name);
        SpriteCache[key] = s;
        return s;
    }

    /// <summary>Soft white circle sprite generated once — legacy fallback + joystick.</summary>
    public static Sprite CircleSprite
    {
        get
        {
            if (_circle == null)
            {
                var loaded = Shape("btn-circle-base");
                if (loaded != null) { _circle = loaded; return _circle; }
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

    // ---------------- base builders ----------------

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

    public static Text MakeText(Transform parent, string text, int size, TextAnchor align, bool shadow = true, bool bold = true)
    {
        var go = new GameObject("Text", typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = bold ? BoldFont : DefaultFont;
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
            sh.effectColor = new Color(Ink.r, Ink.g, Ink.b, 0.6f);
            sh.effectDistance = new Vector2(0f, -4f);
        }
        return t;
    }

    /// <summary>Plain image child, never a raycast target.</summary>
    public static Image MakeImage(Transform parent, Sprite sprite, Color color, string name = "Img")
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        if (sprite != null && sprite.border.sqrMagnitude > 0f) img.type = Image.Type.Sliced;
        return img;
    }

    /// <summary>Rounded 9-slice panel (radius 32 sprite).</summary>
    public static Image MakePanel(Transform parent, Color color, string name = "Panel")
    {
        return MakeImage(parent, Shape("panel-round-32"), color, name);
    }

    /// <summary>Rounded 9-slice card (radius 24 sprite).</summary>
    public static Image MakeCard(Transform parent, Color color, string name = "Card")
    {
        return MakeImage(parent, Shape("card-cream-24"), color, name);
    }

    /// <summary>Capsule pill (chips, timers, badges).</summary>
    public static Image MakePill(Transform parent, Color color, string name = "Pill")
    {
        return MakeImage(parent, Shape("chip-pill"), color, name);
    }

    public static Image MakeIconImage(Transform parent, string icon, Color color, float size)
    {
        var img = MakeImage(parent, Icon(icon), color, "Icon_" + icon);
        SetRect(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(size, size));
        return img;
    }

    /// <summary>The 7-band rainbow strip used under banners (design: only progress,
    /// banner underline and celebrations may be rainbow).</summary>
    public static RectTransform MakeRainbowStrip(Transform parent, float height = 7f)
    {
        var root = new GameObject("Rainbow", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rt = (RectTransform)root.transform;
        int n = Rainbow.Length;
        for (int i = 0; i < n; i++)
        {
            var seg = MakeImage(root.transform, null, Rainbow[i], "seg" + i);
            SetRect(seg.rectTransform, new Vector2(i / (float)n, 0f), new Vector2((i + 1) / (float)n, 1f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }
        rt.sizeDelta = new Vector2(0f, height);
        return rt;
    }

    // ---------------- sticker buttons (spec section 2) ----------------

    public struct Sticker
    {
        public RectTransform root;
        public Button button;
        public Image body;
        public Image edge;
        public Image icon;
        public Text label;
        public Image shadow;
        public PressFx fx;
    }

    /// <summary>Round sticker action button: INK drop shadow + fill circle + darker
    /// bottom crescent + white silhouette icon + 800-weight label.</summary>
    public static Sticker MakeStickerButton(Transform parent, string label, string iconName,
        Color fill, Color content, float diameter, bool holdButton = false)
    {
        var s = new Sticker();
        var rootGo = new GameObject("Btn_" + label, typeof(RectTransform));
        rootGo.transform.SetParent(parent, false);
        s.root = (RectTransform)rootGo.transform;
        s.root.sizeDelta = new Vector2(diameter, diameter);

        s.shadow = MakeImage(rootGo.transform, Shape("btn-circle-base"), new Color(Ink.r, Ink.g, Ink.b, 0.35f), "Shadow");
        SetRect(s.shadow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), Vector2.zero);

        var bodyGo = new GameObject("Body", typeof(Image), typeof(Button));
        bodyGo.transform.SetParent(rootGo.transform, false);
        s.body = bodyGo.GetComponent<Image>();
        s.body.sprite = Shape("btn-circle-base");
        s.body.color = fill;
        SetRect((RectTransform)bodyGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        s.button = bodyGo.GetComponent<Button>();
        s.button.transition = Selectable.Transition.None;
        s.fx = bodyGo.AddComponent<PressFx>();
        s.fx.target = s.body;
        if (holdButton) bodyGo.AddComponent<HoldButton>();

        s.edge = MakeImage(bodyGo.transform, Shape("btn-circle-edge"), new Color(0f, 0f, 0f, 0.26f), "EdgeDark");
        SetRect(s.edge.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        bool hasLabel = !string.IsNullOrEmpty(label);
        float iconSize = diameter * (hasLabel ? 0.36f : 0.44f);
        if (!string.IsNullOrEmpty(iconName))
        {
            s.icon = MakeIconImage(bodyGo.transform, iconName, content, iconSize);
            s.icon.rectTransform.anchoredPosition = new Vector2(0f, hasLabel ? diameter * 0.10f : 0f);
        }
        if (hasLabel)
        {
            s.label = MakeText(bodyGo.transform, label, Mathf.RoundToInt(diameter * 0.135f), TextAnchor.MiddleCenter, false);
            s.label.color = content;
            SetRect(s.label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -diameter * (string.IsNullOrEmpty(iconName) ? 0f : 0.24f)), new Vector2(diameter, diameter * 0.3f));
        }
        return s;
    }

    /// <summary>Legacy rect button — now a rounded sticker tile.</summary>
    public static Button MakeButton(Transform parent, string label, Color bg, Color fg, int fontSize, bool round = false)
    {
        var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = bg;
        img.sprite = round ? Shape("btn-circle-base") : Shape("tile-round-12");
        if (!round && img.sprite != null) img.type = Image.Type.Sliced;
        var btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        var fx = go.AddComponent<PressFx>();
        fx.target = img;
        var t = MakeText(go.transform, label, fontSize, TextAnchor.MiddleCenter, false);
        t.color = fg;
        SetRect(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return btn;
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

/// <summary>Press feedback per design spec section 5: press = scale .92 + sink 4px +
/// darken 12% over 70ms; release = 180ms with a small overshoot.</summary>
public class PressFx : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public Image target;
    bool _down;
    float _t = 1f; // 0 = fully pressed, 1 = released
    Color _baseColor;
    Vector2 _basePos;
    bool _init;

    void Init()
    {
        if (_init) return;
        _init = true;
        if (target != null) _baseColor = target.color;
        _basePos = ((RectTransform)transform).anchoredPosition;
    }

    /// <summary>Call if the button gets recolored at runtime (context button swaps).</summary>
    public void RebaseColor(Color c) { _init = true; _baseColor = c; _basePos = ((RectTransform)transform).anchoredPosition; }

    public void OnPointerDown(PointerEventData e) { Init(); _down = true; }
    public void OnPointerUp(PointerEventData e) { _down = false; }
    void OnDisable()
    {
        _down = false;
        _t = 1f;
        if (!_init) return;
        Apply(1f);
    }

    void Update()
    {
        Init();
        float speed = _down ? 1f / 0.07f : 1f / 0.18f;
        _t = Mathf.MoveTowards(_t, _down ? 0f : 1f, Time.unscaledDeltaTime * speed);
        Apply(_t);
    }

    void Apply(float t)
    {
        // eased: released overshoots slightly (easeOutBack-ish)
        float s = Mathf.Lerp(0.92f, 1f, t);
        if (!_down && t > 0.7f && t < 1f) s += 0.03f * Mathf.Sin((t - 0.7f) / 0.3f * Mathf.PI);
        transform.localScale = new Vector3(s, s, 1f);
        ((RectTransform)transform).anchoredPosition = _basePos + new Vector2(0f, Mathf.Lerp(-4f, 0f, t));
        if (target != null)
        {
            float dark = Mathf.Lerp(0.88f, 1f, t);
            target.color = new Color(_baseColor.r * dark, _baseColor.g * dark, _baseColor.b * dark, _baseColor.a);
        }
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
