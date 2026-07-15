using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>
/// The hide-phase paint toolbar. Built entirely in code at runtime: a colour palette,
/// current-colour readout, eyedropper (PICK) and relocate (MOVE) toggles, brush size
/// +/- and a clear button. Also guarantees an EventSystem exists (with the Input
/// System UI module) so buttons work. GameFlow shows/hides the whole bar via Root.
/// </summary>
public class PaintUI : MonoBehaviour
{
    public ChameleonPainter painter;

    static readonly Color[] Palette =
    {
        new Color(0.36f, 0.62f, 0.30f), // leaf green
        new Color(0.55f, 0.40f, 0.28f), // wood brown
        new Color(0.20f, 0.35f, 0.75f), // blue
        new Color(0.90f, 0.50f, 0.15f), // orange
        new Color(0.70f, 0.15f, 0.15f), // red
        new Color(0.10f, 0.50f, 0.50f), // teal
        new Color(0.85f, 0.82f, 0.75f), // wall beige
        new Color(0.12f, 0.12f, 0.14f), // near black
    };

    static readonly Color ToggleOn = new Color(0.98f, 0.85f, 0.30f);
    static readonly Color ToggleOff = new Color(0.75f, 0.75f, 0.78f);

    GameObject _root;
    Image _currentSwatch;
    Image _pickBg;
    Image _moveBg;
    Font _font;

    /// <summary>Root canvas of the toolbar (available after EnsureBuilt).</summary>
    public GameObject Root
    {
        get { EnsureBuilt(); return _root; }
    }

    void Start()
    {
        EnsureBuilt();
    }

    // Poll the painter so the swatch and toggles always reflect reality — the
    // eyedropper both changes the colour and switches itself off from inside
    // ChameleonPainter, where no UI callback runs.
    void Update()
    {
        if (painter == null) return;
        if (_currentSwatch != null) _currentSwatch.color = painter.brushColor;
        if (_pickBg != null) _pickBg.color = painter.tool == BrushTool.Eyedropper ? ToggleOn : ToggleOff;
        if (_moveBg != null) _moveBg.color = painter.tool == BrushTool.Move ? ToggleOn : new Color(0.30f, 0.30f, 0.33f);
    }

    /// <summary>Idempotent build so GameFlow can force the bar to exist before its own Start.</summary>
    public void EnsureBuilt()
    {
        if (_root != null) return;
        if (painter == null) painter = FindFirstObjectByType<ChameleonPainter>();
        try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { _font = null; }
        EnsureEventSystem();
        BuildUI();
    }

    public static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
            es.AddComponent<InputSystemUIInputModule>();
        }
    }

    void BuildUI()
    {
        var canGo = new GameObject("PaintCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _root = canGo;
        var canvas = canGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        // bottom bar with two rows
        var bar = new GameObject("Bar", typeof(Image), typeof(VerticalLayoutGroup));
        bar.transform.SetParent(canGo.transform, false);
        bar.GetComponent<Image>().color = new Color(0.10f, 0.09f, 0.08f, 0.82f);
        var barRt = bar.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0, 0);
        barRt.anchorMax = new Vector2(1, 0);
        barRt.pivot = new Vector2(0.5f, 0);
        barRt.anchoredPosition = Vector2.zero;
        barRt.sizeDelta = new Vector2(0, 340);
        var vlg = bar.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 18, 18);
        vlg.spacing = 14;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        var row1 = MakeRow(bar.transform);
        var row2 = MakeRow(bar.transform);

        // Row 1: palette + current colour
        foreach (var col in Palette)
        {
            Color c = col;
            var b = MakeButton(row1, c, 84);
            b.onClick.AddListener(() =>
            {
                painter.brushColor = c;
                painter.tool = BrushTool.Paint;
            });
        }
        var cur = new GameObject("Current", typeof(Image), typeof(LayoutElement));
        cur.transform.SetParent(row1, false);
        _currentSwatch = cur.GetComponent<Image>();
        var curLe = cur.GetComponent<LayoutElement>();
        curLe.minWidth = 120; curLe.preferredWidth = 120; curLe.preferredHeight = 130;

        // Row 2: tools
        var pick = MakeButton(row2, ToggleOff, 190);
        _pickBg = pick.GetComponent<Image>();
        AddLabel(pick, "PICK", new Color(0.1f, 0.1f, 0.1f));
        pick.onClick.AddListener(() =>
        {
            painter.tool = painter.tool == BrushTool.Eyedropper ? BrushTool.Paint : BrushTool.Eyedropper;
        });

        var move = MakeButton(row2, new Color(0.30f, 0.30f, 0.33f), 190);
        _moveBg = move.GetComponent<Image>();
        AddLabel(move, "MOVE", Color.white);
        move.onClick.AddListener(() =>
        {
            painter.tool = painter.tool == BrushTool.Move ? BrushTool.Paint : BrushTool.Move;
        });

        var minus = MakeButton(row2, new Color(0.30f, 0.30f, 0.33f), 120);
        AddLabel(minus, "-", Color.white);
        minus.onClick.AddListener(() => { painter.brushRadiusUV = Mathf.Clamp(painter.brushRadiusUV - 0.02f, 0.02f, 0.2f); });

        var plus = MakeButton(row2, new Color(0.30f, 0.30f, 0.33f), 120);
        AddLabel(plus, "+", Color.white);
        plus.onClick.AddListener(() => { painter.brushRadiusUV = Mathf.Clamp(painter.brushRadiusUV + 0.02f, 0.02f, 0.2f); });

        var clear = MakeButton(row2, new Color(0.55f, 0.20f, 0.20f), 190);
        AddLabel(clear, "CLEAR", Color.white);
        clear.onClick.AddListener(() =>
        {
            if (painter != null) painter.ClearCanvas();
        });
    }

    RectTransform MakeRow(Transform parent)
    {
        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        row.GetComponent<LayoutElement>().preferredHeight = 130;
        return row.GetComponent<RectTransform>();
    }

    Button MakeButton(Transform parent, Color bg, float width)
    {
        var go = new GameObject("Btn", typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bg;
        var le = go.GetComponent<LayoutElement>();
        le.minWidth = width; le.preferredWidth = width; le.preferredHeight = 130;
        return go.GetComponent<Button>();
    }

    void AddLabel(Button b, string text, Color col)
    {
        if (_font == null) return;
        var go = new GameObject("Label", typeof(Text));
        go.transform.SetParent(b.transform, false);
        var t = go.GetComponent<Text>();
        t.font = _font; t.text = text; t.color = col;
        t.alignment = TextAnchor.MiddleCenter; t.fontSize = 46; t.raycastTarget = false;
        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }
}
