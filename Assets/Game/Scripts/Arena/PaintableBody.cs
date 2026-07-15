using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A character's paintable skin: pure white body, one runtime Texture2D shared by all
/// body-part renderers. The Kenney blocky FBX UVs are unusable for painting (per-face
/// islands with wildly different texel density, mirrored limbs, a nearly unmapped
/// head), so for box-like parts we REBUILD the UVs at runtime: every box face gets its
/// own cell in a 6x6 atlas with one uniform texel density — a brush stroke is the same
/// physical width on every face of every part. Stamps write to a Color32[]; the
/// texture is uploaded at most once per frame in LateUpdate.
/// </summary>
public class PaintableBody : MonoBehaviour
{
    public int textureSize = 256; // fallback size; rebuilt bodies use 1024

    static readonly Color32 BaseSkin = new Color32(242, 242, 240, 255);

    Renderer[] _renderers;
    Texture2D _tex;
    Color32[] _pixels;
    Material _mat;
    bool _dirty;
    int _w, _h;
    readonly List<Mesh> _runtimeMeshes = new List<Mesh>();

    // CPU-readable copies of sampled scene textures (many kit textures aren't
    // import-flagged readable — blit once, cache forever)
    static readonly Dictionary<Texture2D, Texture2D> ReadableCache = new Dictionary<Texture2D, Texture2D>();

    void Awake()
    {
        var all = GetComponentsInChildren<MeshRenderer>(true);
        var list = new List<Renderer>();
        foreach (var r in all)
            if (r.name != "Eye" && r.name != "Gun" && r.name != "GunPart") list.Add(r);
        _renderers = list.ToArray();

        bool rebuilt = RebuildBoxUVs();
        _w = _h = rebuilt ? 1024 : textureSize;
        textureSize = _w;

        _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false);
        _tex.wrapMode = TextureWrapMode.Clamp;
        _pixels = new Color32[_w * _h];
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = BaseSkin;
        _tex.SetPixels32(_pixels);
        _tex.Apply(false);

        // one instanced material shared by all body parts
        if (_renderers.Length > 0 && _renderers[0].sharedMaterial != null)
            _mat = new Material(_renderers[0].sharedMaterial);
        else
            _mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", _tex);
        _mat.mainTexture = _tex;
        if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", Color.white);
        foreach (var r in _renderers) r.sharedMaterial = _mat;
    }

    void OnDestroy()
    {
        if (_tex != null) Destroy(_tex);
        if (_mat != null) Destroy(_mat);
        foreach (var m in _runtimeMeshes) if (m != null) Destroy(m);
    }

    void LateUpdate()
    {
        if (!_dirty) return;
        _tex.SetPixels32(_pixels);
        _tex.Apply(false);
        _dirty = false;
    }

    // ---------------- runtime UV rebuild ----------------

    /// <summary>Give every box face its own uniformly-scaled atlas cell (part = row,
    /// face = column). Returns false for non-box bodies (capsule fallback).</summary>
    bool RebuildBoxUVs()
    {
        var mfs = new List<MeshFilter>();
        foreach (var r in _renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount <= 40) mfs.Add(mf);
        }
        if (mfs.Count < 4 || mfs.Count > 6) return false;

        try
        {
            // Pass 1: per-part, per-face-bucket bounds in that face's plane, using
            // PER-TRIANGLE bucketing (source verts can be shared between faces, so
            // vertex-level bucketing corrupts UVs — we split vertices in pass 2).
            const float cell = 1f / 6f;
            const float margin = 0.012f;
            float maxDim = 0.01f;
            var partMins = new List<Vector2[]>();

            foreach (var mf in mfs)
            {
                var mesh = mf.sharedMesh;
                var verts = mesh.vertices;
                var tris = mesh.triangles;
                // work in world units — node scales differ per part (the head is a
                // 10x mesh on a 0.1x bone in the Kenney rig)
                float ws = mf.transform.lossyScale.x;
                var mins = new Vector2[6];
                var maxs = new Vector2[6];
                for (int b = 0; b < 6; b++)
                {
                    mins[b] = new Vector2(float.MaxValue, float.MaxValue);
                    maxs[b] = new Vector2(float.MinValue, float.MinValue);
                }
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 n = Vector3.Cross(verts[tris[t + 1]] - verts[tris[t]], verts[tris[t + 2]] - verts[tris[t]]);
                    int b = DominantAxis(n);
                    for (int c = 0; c < 3; c++)
                    {
                        Vector2 p = PlanarCoord(verts[tris[t + c]], b) * ws;
                        mins[b] = Vector2.Min(mins[b], p);
                        maxs[b] = Vector2.Max(maxs[b], p);
                    }
                }
                for (int b = 0; b < 6; b++)
                    if (maxs[b].x > mins[b].x)
                        maxDim = Mathf.Max(maxDim, Mathf.Max(maxs[b].x - mins[b].x, maxs[b].y - mins[b].y));
                partMins.Add(mins);
            }

            float scale = (cell - margin * 2f) / maxDim; // ONE texel density for every face

            // Pass 2: rebuild each part with split vertices (one per triangle corner)
            // so every face owns its corners and lives alone in its atlas cell.
            for (int m = 0; m < mfs.Count; m++)
            {
                var src = mfs[m].sharedMesh;
                var verts = src.vertices;
                var tris = src.triangles;
                var mins = partMins[m];
                float ws = mfs[m].transform.lossyScale.x;

                int n = tris.Length;
                var nv = new Vector3[n];
                var nn = new Vector3[n];
                var nuv = new Vector2[n];
                var nt = new int[n];
                for (int t = 0; t < n; t += 3)
                {
                    Vector3 faceN = Vector3.Cross(verts[tris[t + 1]] - verts[tris[t]], verts[tris[t + 2]] - verts[tris[t]]).normalized;
                    int b = DominantAxis(faceN);
                    for (int c = 0; c < 3; c++)
                    {
                        Vector3 v = verts[tris[t + c]];
                        Vector2 p = PlanarCoord(v, b) * ws;
                        nv[t + c] = v;
                        nn[t + c] = faceN;
                        nuv[t + c] = new Vector2(
                            b * cell + margin + (p.x - mins[b].x) * scale,
                            m * cell + margin + (p.y - mins[b].y) * scale);
                        nt[t + c] = t + c;
                    }
                }
                var clone = new Mesh();
                clone.name = src.name + "-repainted";
                clone.vertices = nv;
                clone.normals = nn;
                clone.uv = nuv;
                clone.triangles = nt;
                clone.RecalculateBounds();
                mfs[m].sharedMesh = clone;
                var col = mfs[m].GetComponent<MeshCollider>();
                if (col != null) col.sharedMesh = clone; // textureCoord must use the new UVs
                _runtimeMeshes.Add(clone);
            }
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("PaintableBody UV rebuild failed, keeping original UVs: " + e.Message);
            return false;
        }
    }

    static int DominantAxis(Vector3 n)
    {
        float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
        if (ax >= ay && ax >= az) return n.x >= 0f ? 0 : 1;
        if (ay >= ax && ay >= az) return n.y >= 0f ? 2 : 3;
        return n.z >= 0f ? 4 : 5;
    }

    static Vector2 PlanarCoord(Vector3 v, int b)
    {
        if (b <= 1) return new Vector2(v.z, v.y);
        if (b <= 3) return new Vector2(v.x, v.z);
        return new Vector2(v.x, v.y);
    }

    // ---------------- painting ----------------

    /// <summary>Stamp a soft circle at a UV coordinate.</summary>
    public void PaintAt(Vector2 uv, Color color, float radiusUV, float hardness)
    {
        int cx = Mathf.RoundToInt(uv.x * _w);
        int cy = Mathf.RoundToInt(uv.y * _h);
        int r = Mathf.Max(1, Mathf.RoundToInt(radiusUV * _w));
        int r2 = r * r;
        Color32 c = color;

        int x0 = Mathf.Clamp(cx - r, 0, _w - 1);
        int x1 = Mathf.Clamp(cx + r, 0, _w - 1);
        int y0 = Mathf.Clamp(cy - r, 0, _h - 1);
        int y1 = Mathf.Clamp(cy + r, 0, _h - 1);

        for (int y = y0; y <= y1; y++)
        {
            int dy = y - cy;
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - cx;
                int d2 = dx * dx + dy * dy;
                if (d2 > r2) continue;
                float edge = 1f - Mathf.Sqrt((float)d2) / r;
                float a = Mathf.Clamp01(edge / (1f - hardness + 0.0001f));
                int idx = y * _w + x;
                _pixels[idx] = Color32.Lerp(_pixels[idx], c, a);
            }
        }
        _dirty = true;
    }

    public void Fill(Color color)
    {
        Color32 c = color;
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = c;
        _dirty = true;
    }

    /// <summary>Back to the pure white base skin.</summary>
    public void Clear()
    {
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = BaseSkin;
        _dirty = true;
    }

    /// <summary>Two-colour stripe camo — bots use it against patterned surfaces (tiles, zebra…).</summary>
    public void FillStripes(Color a, Color b, int stripes)
    {
        Color32 ca = a, cb = b;
        for (int y = 0; y < _h; y++)
        {
            Color32 c = ((y * stripes) / _h) % 2 == 0 ? ca : cb;
            int row = y * _w;
            for (int x = 0; x < _w; x++) _pixels[row + x] = c;
        }
        _dirty = true;
    }

    /// <summary>Bot camouflage: base coat in the environment colour plus darker/lighter blotches.</summary>
    public void FillCamo(Color baseColor)
    {
        Fill(baseColor);
        int blotches = Random.Range(14, 24);
        for (int i = 0; i < blotches; i++)
        {
            float v = (Random.value - 0.5f) * 0.25f;
            Color blotch = new Color(
                Mathf.Clamp01(baseColor.r + v),
                Mathf.Clamp01(baseColor.g + v),
                Mathf.Clamp01(baseColor.b + v));
            var uv = new Vector2(Random.value, Random.value);
            PaintAt(uv, blotch, Random.Range(0.04f, 0.12f), 0.35f);
        }
    }

    /// <summary>
    /// What colour is this surface at the hit point? Prefers the texture pixel at the
    /// hit UV (Kenney kits keep colour in the colormap texture, tint stays white);
    /// falls back to the material tint. Used by bot camo, hunter AI and the eyedropper.
    /// </summary>
    public static bool SampleSurfaceColor(RaycastHit hit, out Color color)
    {
        color = Color.gray;
        if (hit.collider == null) return false;
        var rend = hit.collider.GetComponent<Renderer>();
        if (rend == null || rend.sharedMaterial == null) return false;
        var m = rend.sharedMaterial;

        Color tint = Color.white;
        if (m.HasProperty("_BaseColor")) tint = m.GetColor("_BaseColor");
        else if (m.HasProperty("_Color")) tint = m.color;

        var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") as Texture2D : m.mainTexture as Texture2D;
        if (tex != null && hit.collider is MeshCollider)
        {
            var readable = GetReadable(tex);
            if (readable != null)
            {
                color = readable.GetPixelBilinear(hit.textureCoord.x, hit.textureCoord.y) * tint;
                color.a = 1f;
                return true;
            }
        }
        color = tint;
        color.a = 1f;
        return true;
    }

    static Texture2D GetReadable(Texture2D src)
    {
        if (src.isReadable) return src;
        Texture2D copy;
        if (ReadableCache.TryGetValue(src, out copy) && copy != null) return copy;
        try
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            ReadableCache[src] = copy;
            return copy;
        }
        catch (System.Exception)
        {
            ReadableCache[src] = null;
            return null;
        }
    }

    /// <summary>Average skin colour — used by hunter AI to judge how well someone blends in.</summary>
    public Color AverageColor()
    {
        long r = 0, g = 0, b = 0;
        int step = 16;
        int n = 0;
        for (int i = 0; i < _pixels.Length; i += step) { r += _pixels[i].r; g += _pixels[i].g; b += _pixels[i].b; n++; }
        return n == 0 ? Color.white : new Color(r / (255f * n), g / (255f * n), b / (255f * n));
    }
}
