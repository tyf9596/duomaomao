using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The matchmaking lobby: a small open-top room floating high above (and off to the
/// side of) the arena, built entirely at runtime like everything else. Everyone
/// spawns here to warm up and volunteer for hunting; when the match starts the
/// hiders are teleported down into the map while the hunter stays behind until the
/// seek phase. The four walls each carry a different pattern — a first taste of the
/// camo meta. An invisible lid keeps wall-climbers inside; renderers cast no shadows
/// so the room doesn't print a rectangle onto the map far below.
/// </summary>
public class LobbyRoom : MonoBehaviour
{
    public Vector3 center;
    public Vector2 inner = new Vector2(15f, 11f); // walkable x/z

    const float WallHeight = 4.2f;
    const float WallThickness = 0.4f;

    public static LobbyRoom Build(ArenaMap map)
    {
        var existing = FindFirstObjectByType<LobbyRoom>();
        if (existing != null) return existing;

        var root = new GameObject("LobbyRoom");
        var room = root.AddComponent<LobbyRoom>();
        room.center = map.areaCenter + new Vector3(-(map.areaSize.x * 0.5f + 42f), 55f, 0f);
        root.transform.position = room.center;

        float w = room.inner.x, d = room.inner.y, h = WallHeight, t = WallThickness;

        Box(root, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(w + t * 2f, 0.5f, d + t * 2f),
            Mat(Checker(new Color(0.82f, 0.80f, 0.76f), new Color(0.71f, 0.69f, 0.65f)), new Vector2(w * 0.5f, d * 0.5f)));

        // walls — north rainbow, south B/W stripes, east warm tiles, west cool tiles
        Box(root, "WallN", new Vector3(0f, h * 0.5f, d * 0.5f + t * 0.5f), new Vector3(w + t * 2f, h, t),
            Mat(Rainbow(), new Vector2(2f, 1f)));
        Box(root, "WallS", new Vector3(0f, h * 0.5f, -d * 0.5f - t * 0.5f), new Vector3(w + t * 2f, h, t),
            Mat(Stripes(new Color(0.93f, 0.93f, 0.93f), new Color(0.13f, 0.13f, 0.15f)), new Vector2(11f, 1f)));
        Box(root, "WallE", new Vector3(w * 0.5f + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d),
            Mat(Checker(new Color(0.85f, 0.62f, 0.32f), new Color(0.72f, 0.48f, 0.22f)), new Vector2(6f, 2.4f)));
        Box(root, "WallW", new Vector3(-w * 0.5f - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, d),
            Mat(Checker(new Color(0.45f, 0.62f, 0.78f), new Color(0.32f, 0.48f, 0.65f)), new Vector2(6f, 2.4f)));

        // invisible lid: collider only, so climbers stay in but the sun still gets through
        var lid = new GameObject("Lid");
        lid.transform.SetParent(root.transform, false);
        lid.transform.localPosition = new Vector3(0f, h + 0.2f, 0f);
        var lidCol = lid.AddComponent<BoxCollider>();
        lidCol.size = new Vector3(w + 2f, 0.4f, d + 2f);

        // props: crate cluster to climb + a bench to sit near
        var wood = MatPlain(new Color(0.55f, 0.40f, 0.26f));
        var woodDark = MatPlain(new Color(0.47f, 0.33f, 0.20f));
        Box(root, "Crate1", new Vector3(w * 0.5f - 1.3f, 0.45f, d * 0.5f - 1.6f), Vector3.one * 0.9f, wood);
        Box(root, "Crate2", new Vector3(w * 0.5f - 2.3f, 0.45f, d * 0.5f - 1.3f), Vector3.one * 0.9f, woodDark);
        Box(root, "Crate3", new Vector3(w * 0.5f - 1.5f, 1.15f, d * 0.5f - 1.4f), Vector3.one * 0.5f, wood);
        Box(root, "Bench", new Vector3(-w * 0.5f + 0.9f, 0.22f, 0f), new Vector3(0.6f, 0.45f, 2.2f), woodDark);

        // wall sign (TextMesh reads correctly from inside the room)
        var sign = new GameObject("Sign");
        sign.transform.SetParent(root.transform, false);
        sign.transform.localPosition = new Vector3(0f, h - 0.8f, d * 0.5f - 0.12f);
        var tm = sign.AddComponent<TextMesh>();
        tm.text = "PAINT - HIDE - SURVIVE";
        tm.font = UiKit.DefaultFont;
        tm.fontSize = 64;
        tm.characterSize = 0.045f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.white;
        var tmr = sign.GetComponent<MeshRenderer>();
        if (UiKit.DefaultFont != null) tmr.sharedMaterial = UiKit.DefaultFont.material;
        tmr.shadowCastingMode = ShadowCastingMode.Off;

        return room;
    }

    /// <summary>Random standing spot inside the room (keeps clear of the walls).</summary>
    public Vector3 SpawnPoint()
    {
        return center + new Vector3(
            (Random.value - 0.5f) * (inner.x - 3f),
            0.05f,
            (Random.value - 0.5f) * (inner.y - 3f));
    }

    // ---------------- builders ----------------

    static void Box(GameObject root, string name, Vector3 localPos, Vector3 localScale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    static Material Mat(Texture2D tex, Vector2 tiling)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", tiling);
        m.SetFloat("_Smoothness", 0.05f);
        return m;
    }

    static Material MatPlain(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.05f);
        return m;
    }

    static Texture2D Checker(Color a, Color b)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.SetPixels(new[] { a, b, b, a });
        Finish(tex);
        return tex;
    }

    static Texture2D Stripes(Color a, Color b)
    {
        var tex = new Texture2D(2, 1, TextureFormat.RGBA32, false);
        tex.SetPixels(new[] { a, b });
        Finish(tex);
        return tex;
    }

    static Texture2D Rainbow()
    {
        var tex = new Texture2D(7, 1, TextureFormat.RGBA32, false);
        var cols = new Color[7];
        for (int i = 0; i < 7; i++) cols[i] = Color.HSVToRGB(i / 7f * 0.87f, 0.72f, 0.95f);
        tex.SetPixels(cols);
        Finish(tex);
        return tex;
    }

    static void Finish(Texture2D tex)
    {
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Apply(false);
    }
}
