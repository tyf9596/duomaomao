using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Hider-only paint mode, tuned for careful mobile painting:
/// - the body FREEZES (movement locked + animator paused) so the canvas holds still
/// - the brush is defined in WORLD size (cm) and converted per-hit-triangle to UV
///   size, so every box face of the blocky skin gets the same physical stroke width
/// - a screen cursor ring previews exactly what the stamp will cover
/// - camera: drag off-body orbits, pinch or ZOOM buttons (or mouse wheel) dolly in/out
/// PICK samples any surface colour; strokes that start on the body paint.
/// </summary>
public class SelfPaintMode : MonoBehaviour
{
    public bool Active { get; private set; }

    // spec 1.1: the 8 brush colors keep their semantics, tuned to the new palette
    static readonly Color[] Palette =
    {
        UiKit.Hex("56A845"), UiKit.Hex("8C6239"),
        UiKit.Hex("2E6BD6"), UiKit.Hex("F5822A"),
        UiKit.Hex("D63A2A"), UiKit.Hex("2AB8A8"),
        UiKit.Hex("EFE7D2"), UiKit.Hex("1E1E24"),
    };

    const float SeamJump = 0.08f;      // UV jump treated as crossing islands, not a drag
    const float MinWorldRadius = 0.015f;
    const float MaxWorldRadius = 0.12f;
    const float MinZoom = 0.7f;
    const float MaxZoom = 2.8f;

    Character _self;
    ThirdPersonCamera _cam;
    PlayerRig _rig;

    Color _brush = new Color(0.36f, 0.62f, 0.30f);
    float _worldRadius = 0.04f;        // brush size in metres on the body surface
    float _hardness = 0.55f;
    bool _eyedropper;

    GameObject _root;
    RectTransform _barRt;
    Image _swatch;
    Image _pickBg;
    Image _pickIcon;
    Text _pickLabel;
    Image[] _swatchRings;
    RectTransform _sizeDot;
    Text _sizeText;
    RectTransform _cursor;
    Canvas _canvas;

    bool _stroke;
    bool _uiGesture;
    Vector2 _lastUV;
    bool _hasLastUV;
    Vector2 _lastPos;
    float _lastPinch = -1f;
    Animator _pausedAnim;

    static readonly RaycastHit[] HitBuf = new RaycastHit[16];

    // cached mesh data for UV-density lookups
    class MeshData { public int[] tris; public Vector2[] uv; public Vector3[] verts; }
    readonly Dictionary<Collider, MeshData> _meshCache = new Dictionary<Collider, MeshData>();

    public void Init(Character self, ThirdPersonCamera cam, PlayerRig rig)
    {
        _self = self;
        _cam = cam;
        _rig = rig;
    }

    public void Toggle()
    {
        if (Active) Exit();
        else Enter();
    }

    public void Enter()
    {
        if (Active) return;
        Active = true;
        _self.motor.paintLocked = true;
        // hold the pose completely still — a breathing idle makes fine painting impossible
        _pausedAnim = _self.motor.anim;
        if (_pausedAnim != null) _pausedAnim.speed = 0f;
        _cam.paintMode = true;
        _rig.SetControlsVisible(false);
        EnsureUI();
        _root.SetActive(true);
        // spec 5: the toolbar slides up from the bottom edge (260ms easeOutCubic)
        if (_barRt != null) StartCoroutine(BarIn());
    }

    System.Collections.IEnumerator BarIn()
    {
        Vector2 basePos = new Vector2(0f, -44f);
        float t = 0f;
        while (t < 0.26f && _barRt != null && Active)
        {
            t += Time.deltaTime;
            float k = UiKit.EaseOutCubic(t / 0.26f);
            _barRt.anchoredPosition = Vector2.LerpUnclamped(basePos + new Vector2(0f, -340f), basePos, k);
            yield return null;
        }
        if (_barRt != null) _barRt.anchoredPosition = basePos;
    }

    public void Exit()
    {
        if (!Active) return;
        Active = false;
        _self.motor.paintLocked = false;
        if (_pausedAnim != null) _pausedAnim.speed = 1f;
        _pausedAnim = null;
        _cam.paintMode = false;
        _rig.SetControlsVisible(true);
        if (_root != null) _root.SetActive(false);
        _stroke = false;
        _hasLastUV = false;
    }

    void Update()
    {
        if (!Active) return;

        // --- two-finger pinch = zoom (never paints) ---
        var ts = Touchscreen.current;
        int touchCount = 0;
        Vector2 t0 = Vector2.zero, t1 = Vector2.zero;
        if (ts != null)
        {
            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                if (!touches[i].press.isPressed) continue;
                if (touchCount == 0) t0 = touches[i].position.ReadValue();
                else if (touchCount == 1) t1 = touches[i].position.ReadValue();
                touchCount++;
            }
        }
        if (touchCount >= 2)
        {
            float pinch = Vector2.Distance(t0, t1);
            if (_lastPinch > 0f) Zoom((_lastPinch - pinch) * 0.004f);
            _lastPinch = pinch;
            _stroke = false;
            _hasLastUV = false;
            UpdateCursor(Vector2.Lerp(t0, t1, 0.5f), false);
            return;
        }
        _lastPinch = -1f;

        // --- mouse wheel zoom (editor) ---
        var mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !UiGuard.IsPointerOverUI()) Zoom(-Mathf.Sign(scroll) * 0.15f);
        }

        Vector2 pos; bool pressed, held;
        ReadPointer(out pos, out pressed, out held);

        if (pressed)
        {
            _uiGesture = UiGuard.IsPointerOverUI();
            _lastPos = pos;
            if (_uiGesture) { UpdateCursor(pos, false); return; }

            if (_eyedropper)
            {
                Color sampled;
                if (SampleAt(pos, out sampled)) _brush = sampled;
                SetEyedropper(false);
                return;
            }

            RaycastHit hit;
            if (RaycastOwnBody(pos, out hit))
            {
                _stroke = true;
                _lastUV = hit.textureCoord;
                _hasLastUV = true;
                _self.SkinPaintAt(hit.textureCoord, _brush, UvRadiusFor(hit), _hardness);
            }
        }
        else if (held && !_uiGesture)
        {
            if (_stroke)
            {
                RaycastHit hit;
                if (RaycastOwnBody(pos, out hit))
                {
                    float uvR = UvRadiusFor(hit);
                    Vector2 uv = hit.textureCoord;
                    if (_hasLastUV) PaintSegment(_lastUV, uv, uvR);
                    else _self.SkinPaintAt(uv, _brush, uvR, _hardness);
                    _lastUV = uv;
                    _hasLastUV = true;
                }
                else _hasLastUV = false;
            }
            else
            {
                _cam.AddLook((pos - _lastPos) * 0.7f); // slower orbit for precision
            }
            _lastPos = pos;
        }
        else
        {
            _stroke = false;
            _hasLastUV = false;
            _uiGesture = false;
        }

        UpdateCursor(pos, held || Mouse.current != null);
        if (_swatch != null) _swatch.color = _brush;
        RefreshPaletteSelection();
        UpdateSizeIndicator();
    }

    void Zoom(float delta)
    {
        _cam.paintDistance = Mathf.Clamp(_cam.paintDistance + delta, MinZoom, MaxZoom);
    }

    /// <summary>Convert the world-space brush radius to UV space for the face we hit,
    /// using that triangle's texel density — small UV islands get small UV brushes.</summary>
    float UvRadiusFor(RaycastHit hit)
    {
        var mc = hit.collider as MeshCollider;
        if (mc == null || mc.sharedMesh == null || hit.triangleIndex < 0) return _worldRadius * 0.5f;
        try
        {
            MeshData md;
            if (!_meshCache.TryGetValue(hit.collider, out md))
            {
                var mesh = mc.sharedMesh;
                md = new MeshData { tris = mesh.triangles, uv = mesh.uv, verts = mesh.vertices };
                _meshCache[hit.collider] = md;
            }
            int t = hit.triangleIndex * 3;
            var tr = mc.transform;
            Vector3 w0 = tr.TransformPoint(md.verts[md.tris[t]]);
            Vector3 w1 = tr.TransformPoint(md.verts[md.tris[t + 1]]);
            Vector3 w2 = tr.TransformPoint(md.verts[md.tris[t + 2]]);
            Vector2 u0 = md.uv[md.tris[t]];
            Vector2 u1 = md.uv[md.tris[t + 1]];
            Vector2 u2 = md.uv[md.tris[t + 2]];
            float worldArea = Vector3.Cross(w1 - w0, w2 - w0).magnitude * 0.5f;
            float uvArea = Mathf.Abs((u1.x - u0.x) * (u2.y - u0.y) - (u2.x - u0.x) * (u1.y - u0.y)) * 0.5f;
            if (worldArea < 1e-8f || uvArea < 1e-10f) return _worldRadius * 0.5f;
            return _worldRadius * Mathf.Sqrt(uvArea / worldArea);
        }
        catch (System.Exception)
        {
            return _worldRadius * 0.5f; // mesh not readable — rough fallback
        }
    }

    void PaintSegment(Vector2 from, Vector2 to, float uvR)
    {
        float dist = Vector2.Distance(from, to);
        if (dist > SeamJump) { _self.SkinPaintAt(to, _brush, uvR, _hardness); return; }
        float step = Mathf.Max(uvR * 0.4f, 0.0015f);
        for (float t = step; t < dist; t += step)
            _self.SkinPaintAt(Vector2.LerpUnclamped(from, to, t / dist), _brush, uvR, _hardness);
        _self.SkinPaintAt(to, _brush, uvR, _hardness);
    }

    bool RaycastOwnBody(Vector2 screenPos, out RaycastHit bestHit)
    {
        bestHit = default;
        var cam = _cam.GetComponent<Camera>();
        if (cam == null) return false;
        Ray ray = cam.ScreenPointToRay(screenPos);
        int n = Physics.RaycastNonAlloc(ray, HitBuf, 10f);
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (HitBuf[i].collider.GetComponentInParent<PaintableBody>() != _self.skin) continue;
            if (best < 0 || HitBuf[i].distance < HitBuf[best].distance) best = i;
        }
        if (best < 0) return false;
        bestHit = HitBuf[best];
        return true;
    }

    bool SampleAt(Vector2 screenPos, out Color color)
    {
        color = _brush;
        var cam = _cam.GetComponent<Camera>();
        if (cam == null) return false;
        Ray ray = cam.ScreenPointToRay(screenPos);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, 30f)) return false;
        return PaintableBody.SampleSurfaceColor(hit, out color);
    }

    void ReadPointer(out Vector2 pos, out bool pressed, out bool held)
    {
        pos = Vector2.zero; pressed = false; held = false;

        var ts = Touchscreen.current;
        if (ts != null)
        {
            var pt = ts.primaryTouch;
            if (pt.press.isPressed || pt.press.wasPressedThisFrame)
            {
                pos = pt.position.ReadValue();
                pressed = pt.press.wasPressedThisFrame;
                held = pt.press.isPressed;
                return;
            }
        }

        var m = Mouse.current;
        if (m != null)
        {
            pos = m.position.ReadValue();
            pressed = m.leftButton.wasPressedThisFrame;
            held = m.leftButton.isPressed;
        }
    }

    void SetEyedropper(bool on)
    {
        _eyedropper = on;
        if (_pickBg != null) _pickBg.color = on ? UiKit.Gold : UiKit.Hex("2A2A36");
        if (_pickIcon != null) _pickIcon.color = on ? UiKit.GoldText : Color.white;
        if (_pickLabel != null) _pickLabel.color = on ? UiKit.GoldText : Color.white;
    }

    /// <summary>Gold ring on whichever palette swatch matches the current brush.</summary>
    void RefreshPaletteSelection()
    {
        if (_swatchRings == null) return;
        for (int i = 0; i < _swatchRings.Length; i++)
        {
            bool sel = !_eyedropper
                && Mathf.Abs(Palette[i].r - _brush.r) + Mathf.Abs(Palette[i].g - _brush.g) + Mathf.Abs(Palette[i].b - _brush.b) < 0.01f;
            if (_swatchRings[i].gameObject.activeSelf != sel) _swatchRings[i].gameObject.SetActive(sel);
        }
    }

    // ---------------- UI ----------------

    /// <summary>Screen-space ring that previews the brush footprint on the body.</summary>
    void UpdateCursor(Vector2 screenPos, bool show)
    {
        if (_cursor == null) return;
        RaycastHit hit = default(RaycastHit);
        bool onBody = show && RaycastOwnBody(screenPos, out hit);
        _cursor.gameObject.SetActive(onBody);
        if (!onBody) return;

        var cam = _cam.GetComponent<Camera>();
        float px = _worldRadius * 2f * Screen.height /
                   (2f * hit.distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        float canvasUnits = px / _canvas.scaleFactor;
        _cursor.sizeDelta = new Vector2(canvasUnits, canvasUnits);
        Vector2 lp;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_canvas.transform, screenPos, null, out lp);
        _cursor.anchoredPosition = lp;
    }

    void UpdateSizeIndicator()
    {
        if (_sizeText != null) _sizeText.text = Mathf.RoundToInt(_worldRadius * 200f) + "cm"; // diameter
        if (_sizeDot != null)
        {
            float k = Mathf.InverseLerp(MinWorldRadius, MaxWorldRadius, _worldRadius);
            float d = Mathf.Lerp(18f, 95f, k);
            _sizeDot.sizeDelta = new Vector2(d, d);
        }
    }

    void EnsureUI()
    {
        if (_root != null) return;
        _canvas = UiKit.MakeCanvas("PaintMode", 55, transform);
        _root = _canvas.gameObject;
        Transform root = _canvas.transform;

        // brush cursor ring (whole-canvas anchored center)
        var cur = new GameObject("BrushCursor", typeof(Image));
        cur.transform.SetParent(root, false);
        var curImg = cur.GetComponent<Image>();
        curImg.sprite = UiKit.CircleSprite;
        curImg.color = new Color(1f, 1f, 1f, 0.45f);
        curImg.raycastTarget = false;
        _cursor = (RectTransform)cur.transform;
        UiKit.SetRect(_cursor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));
        cur.SetActive(false);

        // zoom: icon stickers on the right edge (design 07)
        var zin = UiKit.MakeStickerButton(root, "", "zoom-in", new Color(UiKit.Ink2.r, UiKit.Ink2.g, UiKit.Ink2.b, 0.88f), Color.white, 130);
        UiKit.SetRect(zin.root, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-100, 110), new Vector2(130, 130));
        zin.button.onClick.AddListener(() => Zoom(-0.25f));
        var zout = UiKit.MakeStickerButton(root, "", "zoom-out", new Color(UiKit.Ink2.r, UiKit.Ink2.g, UiKit.Ink2.b, 0.88f), Color.white, 130);
        UiKit.SetRect(zout.root, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-100, -60), new Vector2(130, 130));
        zout.button.onClick.AddListener(() => Zoom(0.25f));

        // bottom toolbar: INK, rounded 44 at the top only (bottom corners off-screen).
        // Laid out by hand — runtime-built nested layout groups proved unreliable.
        var bar = new GameObject("Bar", typeof(Image));
        bar.transform.SetParent(root, false);
        var barImg = bar.GetComponent<Image>();
        barImg.sprite = UiKit.Shape("panel-round-32");
        barImg.type = Image.Type.Sliced;
        barImg.color = new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.95f);
        UiKit.SetRect((RectTransform)bar.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0f, -44f), new Vector2(0, 374));
        _barRt = (RectTransform)bar.transform;
        Transform barT = bar.transform;
        float rowY1 = -89f, rowY2 = -227f; // row centers measured from the bar's top

        // current color swatch (white ring) leads the palette row
        var curSwatch = new GameObject("Current", typeof(RectTransform));
        curSwatch.transform.SetParent(barT, false);
        BarRect((RectTransform)curSwatch.transform, 115f, rowY1, 130f, 130f);
        var curRing = UiKit.MakeImage(curSwatch.transform, UiKit.Shape("tile-round-12"), Color.white, "Ring");
        UiKit.SetRect(curRing.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _swatch = UiKit.MakeImage(curSwatch.transform, UiKit.Shape("tile-round-12"), _brush, "Fill");
        UiKit.SetRect(_swatch.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-12f, -12f));

        _swatchRings = new Image[Palette.Length];
        for (int i = 0; i < Palette.Length; i++)
        {
            Color c = Palette[i];
            var swGo = new GameObject("Swatch" + i, typeof(Image), typeof(Button));
            swGo.transform.SetParent(barT, false);
            var swImg = swGo.GetComponent<Image>();
            swImg.sprite = UiKit.Shape("tile-round-12");
            swImg.type = Image.Type.Sliced;
            swImg.color = c;
            BarRect((RectTransform)swGo.transform, 240f + i * 106f, rowY1, 96f, 120f);
            var swBtn = swGo.GetComponent<Button>();
            swBtn.transition = Selectable.Transition.None;
            swGo.AddComponent<PressFx>().target = swImg;
            // selection ring (gold, design: 6px)
            var ring = UiKit.MakeImage(swGo.transform, UiKit.Shape("tile-round-12"), UiKit.Gold, "Sel");
            UiKit.SetRect(ring.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f));
            var ringHole = UiKit.MakeImage(ring.transform, UiKit.Shape("tile-round-12"), c, "Hole");
            UiKit.SetRect(ringHole.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-12f, -12f));
            ring.gameObject.SetActive(false);
            _swatchRings[i] = ring;
            swBtn.onClick.AddListener(() => { _brush = c; SetEyedropper(false); });
        }

        // tools row: PICK / - size + / CLEAR / DONE  (total 924 wide, centred)
        var pick = IconToolButton(barT, "PICK", "eyedropper", UiKit.Hex("2A2A36"), Color.white, 153f, rowY2, 150, 110, 44, 22);
        _pickBg = pick.GetComponent<Image>();
        _pickIcon = pick.transform.Find("Icon_eyedropper").GetComponent<Image>();
        _pickLabel = pick.GetComponentInChildren<Text>();
        pick.onClick.AddListener(() => SetEyedropper(!_eyedropper));

        var stepper = new GameObject("Stepper", typeof(Image));
        stepper.transform.SetParent(barT, false);
        var stImg = stepper.GetComponent<Image>();
        stImg.sprite = UiKit.Shape("tile-round-12");
        stImg.type = Image.Type.Sliced;
        stImg.color = UiKit.Hex("2A2A36");
        BarRect((RectTransform)stepper.transform, 414f, rowY2, 348f, 110f);
        var minus = StepperButton(stepper.transform, "-", -121f);
        minus.onClick.AddListener(() => { _worldRadius = Mathf.Clamp(_worldRadius - 0.01f, MinWorldRadius, MaxWorldRadius); });
        var sizeBox = new GameObject("Size", typeof(RectTransform));
        sizeBox.transform.SetParent(stepper.transform, false);
        UiKit.SetRect((RectTransform)sizeBox.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 94f));
        var dot = new GameObject("Dot", typeof(Image));
        dot.transform.SetParent(sizeBox.transform, false);
        var dotImg = dot.GetComponent<Image>();
        dotImg.sprite = UiKit.CircleSprite;
        dotImg.raycastTarget = false;
        _sizeDot = (RectTransform)dot.transform;
        UiKit.SetRect(_sizeDot, new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));
        _sizeText = UiKit.MakeText(sizeBox.transform, "", 22, TextAnchor.LowerCenter, false);
        _sizeText.color = UiKit.TextDim;
        UiKit.SetRect(_sizeText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0, 6), Vector2.zero);
        var plus = StepperButton(stepper.transform, "+", 121f);
        plus.onClick.AddListener(() => { _worldRadius = Mathf.Clamp(_worldRadius + 0.01f, MinWorldRadius, MaxWorldRadius); });

        var clear = IconToolButton(barT, "CLEAR", "clear-x", UiKit.ClearRed, UiKit.Hex("FFD9D1"), 680f, rowY2, 160, 110, 44, 22);
        clear.onClick.AddListener(() => { _self.SkinClear(); });

        var done = IconToolButton(barT, "DONE", "check", UiKit.Green, Color.white, 887f, rowY2, 230, 110, 48, 34, horizontal: true);
        done.onClick.AddListener(Exit);

        _swatch.color = _brush;
        UpdateSizeIndicator();
        RefreshPaletteSelection();
    }

    /// <summary>Place an element inside the toolbar: x from the bar's left edge,
    /// y (negative) from the bar's top edge, both to the element's centre.</summary>
    static void BarRect(RectTransform rt, float x, float y, float w, float h)
    {
        UiKit.SetRect(rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(w, h));
    }

    /// <summary>Rounded tool tile with a silhouette icon + label (design 07 tools row).</summary>
    Button IconToolButton(Transform parent, string label, string icon, Color bg, Color content,
        float x, float y, float w, float h, float iconSize, int labelSize, bool horizontal = false)
    {
        var go = new GameObject("Tool_" + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = UiKit.Shape("tile-round-12");
        img.type = Image.Type.Sliced;
        img.color = bg;
        BarRect((RectTransform)go.transform, x, y, w, h);
        var btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        go.AddComponent<PressFx>().target = img;
        var ic = UiKit.MakeIconImage(go.transform, icon, content, iconSize);
        var lbl = UiKit.MakeText(go.transform, label, labelSize, TextAnchor.MiddleCenter, false);
        lbl.color = content;
        if (horizontal)
        {
            ic.rectTransform.anchoredPosition = new Vector2(-w * 0.28f, 0f);
            UiKit.SetRect(lbl.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(iconSize * 0.42f, 0f), Vector2.zero);
        }
        else
        {
            ic.rectTransform.anchoredPosition = new Vector2(0f, h * 0.12f);
            UiKit.SetRect(lbl.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(0f, labelSize + 8f));
        }
        return btn;
    }

    Button StepperButton(Transform parent, string label, float x)
    {
        var go = new GameObject("Step" + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = UiKit.Shape("tile-round-12");
        img.type = Image.Type.Sliced;
        img.color = UiKit.Ink2;
        UiKit.SetRect((RectTransform)go.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(90f, 94f));
        var btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        go.AddComponent<PressFx>().target = img;
        var t = UiKit.MakeText(go.transform, label, 52, TextAnchor.MiddleCenter, false);
        UiKit.SetRect(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return btn;
    }
}
