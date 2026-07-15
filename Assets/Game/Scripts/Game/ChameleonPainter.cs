using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Which gesture a touch on the chameleon performs.</summary>
public enum BrushTool { Paint, Eyedropper, Move }

/// <summary>
/// The signature mechanic: paint directly onto the chameleon's body.
/// A runtime Texture2D is the albedo (_BaseMap); UVs come from the MeshCollider
/// (RaycastHit.textureCoord). This is the single gesture arbiter for the hide
/// phase: a stroke that starts on the chameleon belongs to the active tool
/// (paint / move), everything else is left to the OrbitCamera, which we disable
/// only while a stroke is in progress. GameFlow shuts all of this off with
/// <see cref="interactionEnabled"/> outside the hide phase.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class ChameleonPainter : MonoBehaviour
{
    public Camera cam;
    public Renderer targetRenderer;
    public Collider targetCollider;

    [Header("Canvas")]
    public int textureSize = 512;

    [Header("Brush")]
    public Color brushColor = new Color(0.36f, 0.62f, 0.30f); // leafy green to start
    [Range(0.01f, 0.25f)] public float brushRadiusUV = 0.05f;
    [Range(0.1f, 1f)] public float brushHardness = 0.6f;
    public float rayLength = 100f;

    [Header("Tools")]
    public BrushTool tool = BrushTool.Paint;
    [Tooltip("Lift along the surface normal when repositioning. Negative = auto (half the renderer's height).")]
    public float surfaceOffset = -1f;

    /// <summary>Master switch — GameFlow turns this off outside the hide phase.</summary>
    [HideInInspector] public bool interactionEnabled = true;

    /// <summary>The transform the move tool drags around (the collider's body).</summary>
    public Transform Body => targetCollider != null ? targetCollider.transform : transform;

    const float SeamJump = 0.25f; // UV distance treated as a seam crossing, not a drag

    static readonly Color32 BaseSkin = new Color32(245, 245, 235, 255);
    static readonly RaycastHit[] HitBuf = new RaycastHit[16];

    Texture2D _tex;
    Color32[] _pixels;
    Material _mat;
    OrbitCamera _orbit;
    bool _stroke;       // a paint/move gesture owns the pointer right now
    bool _uiGesture;    // current press began over UI — ignore it entirely
    Vector2 _lastUV;
    bool _hasLastUV;
    bool _dirty;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        if (targetCollider == null) targetCollider = GetComponent<Collider>();
        if (cam != null) _orbit = cam.GetComponent<OrbitCamera>();

        _tex = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        _pixels = new Color32[textureSize * textureSize];
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = BaseSkin;
        _tex.SetPixels32(_pixels);
        _tex.Apply(false);

        // Instance material so we don't touch the shared asset.
        _mat = targetRenderer.material;
        _mat.SetColor("_BaseColor", Color.white);
        _mat.SetTexture("_BaseMap", _tex);
    }

    void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
        if (_mat != null) Destroy(_mat);
    }

    void Update()
    {
        Vector2 pos; bool pressed, held;
        ReadPointer(out pos, out pressed, out held);

        if (!interactionEnabled)
        {
            EndStroke();
            _uiGesture = false;
            return;
        }

        if (pressed) BeginGesture(pos);
        else if (held) ContinueGesture(pos);
        else { EndStroke(); _uiGesture = false; } // release or cancel
    }

    // Stamps only write into _pixels; the texture is uploaded at most once per frame.
    void LateUpdate()
    {
        if (!_dirty) return;
        _tex.SetPixels32(_pixels);
        _tex.Apply(false);
        _dirty = false;
    }

    void BeginGesture(Vector2 pos)
    {
        _uiGesture = UiGuard.IsPointerOverUI();
        if (_uiGesture) return;

        if (tool == BrushTool.Eyedropper)
        {
            Color sampled;
            if (SampleColorAt(pos, out sampled)) brushColor = sampled;
            tool = BrushTool.Paint; // one-shot
            return;
        }

        RaycastHit hit;
        if (!RaycastBody(pos, out hit)) return;

        _stroke = true;
        if (_orbit != null) _orbit.enabled = false; // this gesture belongs to the tool

        if (tool == BrushTool.Move)
        {
            MoveBody(pos);
        }
        else
        {
            _lastUV = hit.textureCoord;
            _hasLastUV = true;
            PaintAt(hit.textureCoord);
        }
    }

    void ContinueGesture(Vector2 pos)
    {
        if (_uiGesture || !_stroke) return;

        if (tool == BrushTool.Move)
        {
            MoveBody(pos);
            return;
        }

        RaycastHit hit;
        if (RaycastBody(pos, out hit))
        {
            Vector2 uv = hit.textureCoord;
            if (_hasLastUV) PaintSegment(_lastUV, uv);
            else PaintAt(uv);
            _lastUV = uv;
            _hasLastUV = true;
        }
        else
        {
            _hasLastUV = false; // slid off the body — don't smear when it returns
        }
    }

    void EndStroke()
    {
        if (!_stroke) return;
        _stroke = false;
        _hasLastUV = false;
        // Outside the hide phase GameFlow owns the camera — don't fight it.
        if (_orbit != null && interactionEnabled) _orbit.enabled = true;
    }

    bool RaycastBody(Vector2 screenPos, out RaycastHit hit)
    {
        hit = default;
        if (cam == null || targetCollider == null) return false;
        Ray ray = cam.ScreenPointToRay(screenPos);
        return Physics.Raycast(ray, out hit, rayLength) && hit.collider == targetCollider;
    }

    /// <summary>Drag the chameleon along whatever environment surface is under the finger.</summary>
    void MoveBody(Vector2 screenPos)
    {
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(screenPos);
        int n = Physics.RaycastNonAlloc(ray, HitBuf, rayLength);
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (HitBuf[i].collider == targetCollider) continue; // ignore ourselves
            if (best < 0 || HitBuf[i].distance < HitBuf[best].distance) best = i;
        }
        if (best < 0) return;

        RaycastHit h = HitBuf[best];
        float lift = surfaceOffset >= 0f ? surfaceOffset
            : (targetRenderer != null ? targetRenderer.bounds.extents.y : 0.3f);
        Body.position = h.point + h.normal * lift;
        Body.rotation = Quaternion.FromToRotation(Body.up, h.normal) * Body.rotation;
    }

    /// <summary>Stamp a line of brush circles so fast drags don't leave gaps.</summary>
    void PaintSegment(Vector2 from, Vector2 to)
    {
        float dist = Vector2.Distance(from, to);
        if (dist > SeamJump) { PaintAt(to); return; } // crossed a UV seam
        float step = Mathf.Max(brushRadiusUV * 0.4f, 1f / textureSize);
        for (float t = step; t < dist; t += step)
            PaintAt(Vector2.LerpUnclamped(from, to, t / dist));
        PaintAt(to);
    }

    /// <summary>Stamp a soft circle of the current brush colour at a UV coordinate.</summary>
    public void PaintAt(Vector2 uv)
    {
        int cx = Mathf.RoundToInt(uv.x * textureSize);
        int cy = Mathf.RoundToInt(uv.y * textureSize);
        int r = Mathf.Max(1, Mathf.RoundToInt(brushRadiusUV * textureSize));
        int r2 = r * r;
        Color32 c = brushColor;

        int x0 = Mathf.Clamp(cx - r, 0, textureSize - 1);
        int x1 = Mathf.Clamp(cx + r, 0, textureSize - 1);
        int y0 = Mathf.Clamp(cy - r, 0, textureSize - 1);
        int y1 = Mathf.Clamp(cy + r, 0, textureSize - 1);

        for (int y = y0; y <= y1; y++)
        {
            int dy = y - cy;
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - cx;
                int d2 = dx * dx + dy * dy;
                if (d2 > r2) continue;
                float edge = 1f - Mathf.Sqrt((float)d2) / r; // 1 at centre, 0 at rim
                float a = Mathf.Clamp01(edge / (1f - brushHardness + 0.0001f));
                int idx = y * textureSize + x;
                _pixels[idx] = Color32.Lerp(_pixels[idx], c, a);
            }
        }
        _dirty = true;
    }

    public void ClearCanvas()
    {
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = BaseSkin;
        _dirty = true;
    }

    public bool SampleColorAt(Vector2 screenPos, out Color color)
    {
        color = brushColor;
        if (cam == null) return false;
        Ray ray = cam.ScreenPointToRay(screenPos);
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, rayLength)) return false;

        if (targetCollider != null && hit.collider == targetCollider)
        {
            int px = Mathf.Clamp(Mathf.RoundToInt(hit.textureCoord.x * textureSize), 0, textureSize - 1);
            int py = Mathf.Clamp(Mathf.RoundToInt(hit.textureCoord.y * textureSize), 0, textureSize - 1);
            color = _pixels[py * textureSize + px];
            return true;
        }

        var rend = hit.collider.GetComponent<Renderer>();
        if (rend != null && rend.sharedMaterial != null)
        {
            var m = rend.sharedMaterial;
            color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            return true;
        }
        return false;
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
}
