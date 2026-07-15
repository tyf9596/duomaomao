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

    static readonly Color[] Palette =
    {
        new Color(0.36f, 0.62f, 0.30f), new Color(0.55f, 0.40f, 0.28f),
        new Color(0.20f, 0.35f, 0.75f), new Color(0.90f, 0.50f, 0.15f),
        new Color(0.70f, 0.15f, 0.15f), new Color(0.10f, 0.50f, 0.50f),
        new Color(0.85f, 0.82f, 0.75f), new Color(0.12f, 0.12f, 0.14f),
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
    Image _swatch;
    Image _pickBg;
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
        _self.motor.movementLocked = true;
        // hold the pose completely still — a breathing idle makes fine painting impossible
        _pausedAnim = _self.motor.anim;
        if (_pausedAnim != null) _pausedAnim.speed = 0f;
        _cam.paintMode = true;
        _rig.SetControlsVisible(false);
        EnsureUI();
        _root.SetActive(true);
    }

    public void Exit()
    {
        if (!Active) return;
        Active = false;
        _self.motor.movementLocked = false;
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
                _self.skin.PaintAt(hit.textureCoord, _brush, UvRadiusFor(hit), _hardness);
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
                    else _self.skin.PaintAt(uv, _brush, uvR, _hardness);
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
        if (dist > SeamJump) { _self.skin.PaintAt(to, _brush, uvR, _hardness); return; }
        float step = Mathf.Max(uvR * 0.4f, 0.0015f);
        for (float t = step; t < dist; t += step)
            _self.skin.PaintAt(Vector2.LerpUnclamped(from, to, t / dist), _brush, uvR, _hardness);
        _self.skin.PaintAt(to, _brush, uvR, _hardness);
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
        if (_pickBg != null) _pickBg.color = on ? new Color(0.98f, 0.85f, 0.30f) : new Color(0.75f, 0.75f, 0.78f);
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

        // zoom buttons on the right edge
        var zin = UiKit.MakeButton(root, "ZOOM+", new Color(0.22f, 0.22f, 0.26f, 0.85f), Color.white, 34, round: true);
        UiKit.SetRect((RectTransform)zin.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-100, 110), new Vector2(150, 150));
        zin.onClick.AddListener(() => Zoom(-0.25f));
        var zout = UiKit.MakeButton(root, "ZOOM-", new Color(0.22f, 0.22f, 0.26f, 0.85f), Color.white, 34, round: true);
        UiKit.SetRect((RectTransform)zout.transform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-100, -60), new Vector2(150, 150));
        zout.onClick.AddListener(() => Zoom(0.25f));

        // bottom bar
        var bar = new GameObject("Bar", typeof(Image), typeof(VerticalLayoutGroup));
        bar.transform.SetParent(root, false);
        bar.GetComponent<Image>().color = new Color(0.10f, 0.09f, 0.08f, 0.82f);
        UiKit.SetRect((RectTransform)bar.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 330));
        var vlg = bar.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 16, 16);
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        var row1 = MakeRow(bar.transform);
        var row2 = MakeRow(bar.transform);

        foreach (var col in Palette)
        {
            Color c = col;
            var b = SizedButton(row1, "", c, 84);
            b.onClick.AddListener(() => { _brush = c; SetEyedropper(false); });
        }
        var curSwatch = new GameObject("Current", typeof(Image), typeof(LayoutElement));
        curSwatch.transform.SetParent(row1, false);
        _swatch = curSwatch.GetComponent<Image>();
        var curLe = curSwatch.GetComponent<LayoutElement>();
        curLe.minWidth = 110; curLe.preferredWidth = 110; curLe.preferredHeight = 125;

        var pick = SizedButton(row2, "PICK", new Color(0.75f, 0.75f, 0.78f), 150, new Color(0.1f, 0.1f, 0.1f));
        _pickBg = pick.GetComponent<Image>();
        pick.onClick.AddListener(() => SetEyedropper(!_eyedropper));

        var minus = SizedButton(row2, "-", new Color(0.30f, 0.30f, 0.33f), 100);
        minus.onClick.AddListener(() => { _worldRadius = Mathf.Clamp(_worldRadius - 0.01f, MinWorldRadius, MaxWorldRadius); });

        // live size indicator: dot grows with brush size, label shows cm
        var sizeBox = new GameObject("Size", typeof(Image), typeof(LayoutElement));
        sizeBox.transform.SetParent(row2, false);
        sizeBox.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.18f);
        var sbLe = sizeBox.GetComponent<LayoutElement>();
        sbLe.minWidth = 140; sbLe.preferredWidth = 140; sbLe.preferredHeight = 125;
        var dot = new GameObject("Dot", typeof(Image));
        dot.transform.SetParent(sizeBox.transform, false);
        var dotImg = dot.GetComponent<Image>();
        dotImg.sprite = UiKit.CircleSprite;
        dotImg.raycastTarget = false;
        _sizeDot = (RectTransform)dot.transform;
        UiKit.SetRect(_sizeDot, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));
        _sizeText = UiKit.MakeText(sizeBox.transform, "", 26, TextAnchor.LowerCenter, false);
        UiKit.SetRect(_sizeText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0, 4), Vector2.zero);

        var plus = SizedButton(row2, "+", new Color(0.30f, 0.30f, 0.33f), 100);
        plus.onClick.AddListener(() => { _worldRadius = Mathf.Clamp(_worldRadius + 0.01f, MinWorldRadius, MaxWorldRadius); });

        var clear = SizedButton(row2, "CLEAR", new Color(0.55f, 0.20f, 0.20f), 150);
        clear.onClick.AddListener(() => { _self.skin.Clear(); });

        var done = SizedButton(row2, "DONE", new Color(0.20f, 0.55f, 0.25f), 170);
        done.onClick.AddListener(Exit);

        _swatch.color = _brush;
        UpdateSizeIndicator();
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
        row.GetComponent<LayoutElement>().preferredHeight = 125;
        return (RectTransform)row.transform;
    }

    Button SizedButton(Transform parent, string label, Color bg, float width, Color? fg = null)
    {
        var b = UiKit.MakeButton(parent, label, bg, fg ?? Color.white, 42);
        var le = b.gameObject.AddComponent<LayoutElement>();
        le.minWidth = width; le.preferredWidth = width; le.preferredHeight = 125;
        return b;
    }
}
