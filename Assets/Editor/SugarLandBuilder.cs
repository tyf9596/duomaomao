using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds Arena07 "Sugar Land" — the saturated-colour / repeated-props map from the
/// reference game's design axes (Sugar Land: "dessert diorama; repeated props,
/// merciless colors"). Giant Food Kit desserts become terrain; a gingerbread-man
/// crowd doubles as decoy cover at exactly hider height. Driven by a menu item
/// because execute_code is dead. Idempotent: rebuilds the scene from scratch.
/// </summary>
public static class SugarLandBuilder
{
    const string ScenePath = "Assets/Game/Scenes/Arena07.unity";
    const string P = "Assets/Game/Art/Patterns/";
    const string FD = "Assets/Game/Art/Kits/FoodKit/";

    static Transform _env;

    [MenuItem("Tools/Sugar Land/Build All")]
    public static void BuildAll()
    {
        MakeTextures();
        MakeScene();
        BuildTerrain();
        BuildZones();
        BuildDecoys();
        RegisterScene();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[SugarLand] build complete, children=" + _env.childCount);
    }

    // ---------------------------------------------------------------- materials

    static Material M(string n) { return AssetDatabase.LoadAssetAtPath<Material>(P + n + ".mat"); }

    static Material Plain(string name, Color c, float smooth)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(P + name + ".mat");
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        AssetDatabase.CreateAsset(m, P + name + ".mat");
        return m;
    }

    static void Tex(string name, int w, int h, System.Func<int, int, Color> px)
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(P + name + ".png") != null) return;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, y, px(x, y));
        tex.Apply();
        System.IO.File.WriteAllBytes(P + name + ".png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.Refresh();
        var imp = (TextureImporter)AssetImporter.GetAtPath(P + name + ".png");
        imp.isReadable = true;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.filterMode = FilterMode.Point;
        imp.SaveAndReimport();
        var t2 = AssetDatabase.LoadAssetAtPath<Texture2D>(P + name + ".png");
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetTexture("_BaseMap", t2);
        mat.SetFloat("_Smoothness", 0.08f);
        AssetDatabase.CreateAsset(mat, P + name + ".mat");
    }

    static void MakeTextures()
    {
        var rng = new System.Random(99);
        // diagonal red/white candy stripe
        Tex("CandyStripe", 256, 256, (x, y) => ((x + y) / 32) % 2 == 0
            ? new Color(0.92f, 0.18f, 0.22f) : new Color(0.97f, 0.95f, 0.93f));
        // cream base + colourful sprinkle dashes (irregular camo)
        var sprinkles = new Color[] {
            new Color(0.92f,0.3f,0.35f), new Color(0.3f,0.65f,0.9f), new Color(0.4f,0.8f,0.4f),
            new Color(0.98f,0.8f,0.25f), new Color(0.75f,0.4f,0.85f), new Color(0.98f,0.55f,0.7f) };
        var spr = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(P + "Sprinkles.png") == null)
        {
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                spr.SetPixel(x, y, new Color(0.96f, 0.92f, 0.86f));
            for (int s = 0; s < 260; s++)
            {
                var col = sprinkles[rng.Next(sprinkles.Length)];
                int cx = rng.Next(4, 252), cy = rng.Next(4, 252);
                int dx = rng.Next(-1, 2), dy = rng.Next(-1, 2);
                if (dx == 0 && dy == 0) dx = 1;
                for (int i = 0; i < 7; i++)
                    for (int wpx = -1; wpx <= 1; wpx++)
                    {
                        int qx = cx + dx * i + (dy != 0 ? wpx : 0);
                        int qy = cy + dy * i + (dx != 0 ? wpx : 0);
                        if (qx < 0 || qx >= 256 || qy < 0 || qy >= 256) continue;
                        spr.SetPixel(qx, qy, col);
                    }
            }
            spr.Apply();
            System.IO.File.WriteAllBytes(P + "Sprinkles.png", spr.EncodeToPNG());
            AssetDatabase.Refresh();
            var imp = (TextureImporter)AssetImporter.GetAtPath(P + "Sprinkles.png");
            imp.isReadable = true; imp.wrapMode = TextureWrapMode.Repeat; imp.filterMode = FilterMode.Point;
            imp.SaveAndReimport();
            var t2 = AssetDatabase.LoadAssetAtPath<Texture2D>(P + "Sprinkles.png");
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", t2);
            mat.SetFloat("_Smoothness", 0.08f);
            AssetDatabase.CreateAsset(mat, P + "Sprinkles.mat");
        }
        Object.DestroyImmediate(spr);
        // chocolate bar segments
        Tex("ChocBar", 256, 256, (x, y) => (x % 64) < 6 || (y % 64) < 6
            ? new Color(0.22f, 0.12f, 0.07f) : new Color(0.35f, 0.2f, 0.11f));
        // waffle grid
        Tex("Waffle", 256, 256, (x, y) => (x % 42) < 7 || (y % 42) < 7
            ? new Color(0.62f, 0.42f, 0.2f) : new Color(0.85f, 0.64f, 0.35f));
        // cookie dough + chips
        var dough = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(P + "CookieDough.png") == null)
        {
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                dough.SetPixel(x, y, new Color(0.82f, 0.6f, 0.34f));
            for (int c = 0; c < 40; c++)
            {
                int cx = rng.Next(8, 248), cy = rng.Next(8, 248), r = rng.Next(5, 11);
                for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    int qx = cx + dx, qy = cy + dy;
                    if (qx < 0 || qx >= 256 || qy < 0 || qy >= 256) continue;
                    dough.SetPixel(qx, qy, new Color(0.28f, 0.16f, 0.09f));
                }
            }
            dough.Apply();
            System.IO.File.WriteAllBytes(P + "CookieDough.png", dough.EncodeToPNG());
            AssetDatabase.Refresh();
            var imp = (TextureImporter)AssetImporter.GetAtPath(P + "CookieDough.png");
            imp.isReadable = true; imp.wrapMode = TextureWrapMode.Repeat; imp.filterMode = FilterMode.Point;
            imp.SaveAndReimport();
            var t2 = AssetDatabase.LoadAssetAtPath<Texture2D>(P + "CookieDough.png");
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", t2);
            mat.SetFloat("_Smoothness", 0.08f);
            AssetDatabase.CreateAsset(mat, P + "CookieDough.mat");
        }
        Object.DestroyImmediate(dough);
        // plain palette
        Plain("FrostingPink", new Color(0.96f, 0.8f, 0.84f), 0.12f);
        Plain("FrostingCream", new Color(0.97f, 0.93f, 0.85f), 0.15f);
        Plain("MintGreen", new Color(0.6f, 0.88f, 0.75f), 0.15f);
        Plain("BerryRed", new Color(0.85f, 0.2f, 0.3f), 0.25f);
        Plain("ChocDark", new Color(0.28f, 0.16f, 0.1f), 0.3f);
        Plain("PastelBlue", new Color(0.62f, 0.78f, 0.95f), 0.15f);
        Plain("CandyWhite", new Color(0.97f, 0.95f, 0.93f), 0.3f);
        AssetDatabase.SaveAssets();
    }

    // ---------------------------------------------------------------- scene shell

    static void MakeScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera");
        camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        camGo.tag = "MainCamera";
        camGo.transform.position = new Vector3(0, 12, -24);
        camGo.transform.rotation = Quaternion.Euler(25, 0, 0);
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.shadows = LightShadows.Soft;
        light.color = new Color(1f, 0.97f, 0.9f);
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.52f, 0.55f);

        var mapGo = new GameObject("Arena");
        var map = mapGo.AddComponent<ArenaMap>();
        map.areaCenter = Vector3.zero;
        map.areaSize = new Vector2(44f, 38f);
        map.floorNormalMinY = 0.85f;
        map.maxSpawnY = 5f;
        map.characterCountOverride = 10;
        map.hideSecondsOverride = 50f;
        map.seekSecondsOverride = 240f;

        _env = new GameObject("Env").transform;
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    // ---------------------------------------------------------------- helpers

    static Transform Box(string name, Vector3 c, Vector3 s, Vector3 e, Material mat)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        var mc = g.AddComponent<MeshCollider>();
        mc.sharedMesh = g.GetComponent<MeshFilter>().sharedMesh;
        g.transform.SetParent(_env, false);
        g.transform.localPosition = c;
        g.transform.localEulerAngles = e;
        g.transform.localScale = s;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g.transform;
    }

    static Transform Cyl(string name, Vector3 c, float r, float h, Vector3 e, Material mat)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        var mc = g.AddComponent<MeshCollider>();
        mc.sharedMesh = g.GetComponent<MeshFilter>().sharedMesh;
        g.transform.SetParent(_env, false);
        g.transform.localPosition = c;
        g.transform.localEulerAngles = e;
        g.transform.localScale = new Vector3(r * 2f, h / 2f, r * 2f);
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g.transform;
    }

    static Transform Ball(string name, Vector3 c, Vector3 s, Material mat)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        var mc = g.AddComponent<MeshCollider>();
        mc.sharedMesh = g.GetComponent<MeshFilter>().sharedMesh;
        g.transform.SetParent(_env, false);
        g.transform.localPosition = c;
        g.transform.localScale = s;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g.transform;
    }

    /// <summary>Spawn a Food Kit model scaled so its LARGEST dimension equals
    /// targetSize, grounded so its base sits on baseY. Handles unknown pivots.</summary>
    static Transform Food(string file, string nm, Vector3 pos, float rotY, float targetSize, float baseY, Vector3 extraEuler)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FD + file + ".fbx");
        if (prefab == null) { Debug.LogWarning("[SugarLand] missing " + file); return null; }
        var g = Object.Instantiate(prefab, _env);
        g.name = nm;
        g.transform.position = pos;
        g.transform.rotation = Quaternion.Euler(extraEuler.x, rotY, extraEuler.z);
        g.transform.localScale = Vector3.one;
        var rends = g.GetComponentsInChildren<Renderer>();
        var b = new Bounds(g.transform.position, Vector3.zero);
        bool first = true;
        foreach (var r in rends) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        float biggest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (biggest > 0.0001f) g.transform.localScale = Vector3.one * (targetSize / biggest);
        // re-measure and drop to the floor
        b = new Bounds(g.transform.position, Vector3.zero); first = true;
        foreach (var r in rends) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        g.transform.position += Vector3.up * (baseY - b.min.y);
        foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            if (mf.GetComponent<Collider>() == null)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
        return g.transform;
    }

    // ---------------------------------------------------------------- terrain

    static void BuildTerrain()
    {
        // frosting meadow base
        Box("GroundSlab", new Vector3(0, -0.15f, 0), new Vector3(46, 0.3f, 40), Vector3.zero, M("FrostingPink"));

        // wafer perimeter walls + corner candy canes
        Box("WaferWallN", new Vector3(0, 1.1f, 19.7f), new Vector3(46, 2.2f, 0.5f), Vector3.zero, M("Waffle"));
        Box("WaferWallS", new Vector3(0, 1.1f, -19.7f), new Vector3(46, 2.2f, 0.5f), Vector3.zero, M("Waffle"));
        Box("WaferWallW", new Vector3(-22.7f, 1.1f, 0), new Vector3(0.5f, 2.2f, 40), Vector3.zero, M("Waffle"));
        Box("WaferWallE", new Vector3(22.7f, 1.1f, 0), new Vector3(0.5f, 2.2f, 40), Vector3.zero, M("Waffle"));
        float[] cxs = { -21.5f, 21.5f };
        float[] czs = { -18.5f, 18.5f };
        foreach (float cx in cxs)
            foreach (float cz in czs)
            {
                Cyl("CandyCane", new Vector3(cx, 1.6f, cz), 0.18f, 3.2f, Vector3.zero, M("CandyStripe"));
                Cyl("CandyCaneArm", new Vector3(cx + 0.4f, 3.1f, cz), 0.18f, 0.9f, new Vector3(0, 0, 90), M("CandyStripe"));
            }

        // zone floor patches (MeshCollider cubes -> eyedropper-friendly)
        Box("F_CookiePathN", new Vector3(0, 0.02f, 10f), new Vector3(2.2f, 0.05f, 16f), Vector3.zero, M("CookieDough"));
        Box("F_CookiePathS", new Vector3(0, 0.02f, -11f), new Vector3(2.2f, 0.05f, 14f), Vector3.zero, M("CookieDough"));
        Box("F_CookiePathE", new Vector3(11.5f, 0.02f, 0f), new Vector3(19f, 0.05f, 2.2f), Vector3.zero, M("CookieDough"));
        Box("F_CookiePathW", new Vector3(-11.5f, 0.02f, 0f), new Vector3(19f, 0.05f, 2.2f), Vector3.zero, M("CookieDough"));
        Box("F_Sprinkles", new Vector3(13.5f, 0.02f, 12f), new Vector3(14f, 0.05f, 11f), Vector3.zero, M("Sprinkles"));
        Box("F_Waffle", new Vector3(-14f, 0.02f, 12.5f), new Vector3(13f, 0.05f, 10f), Vector3.zero, M("Waffle"));
        Box("F_ChocBar", new Vector3(-14f, 0.02f, -12f), new Vector3(14f, 0.05f, 11f), Vector3.zero, M("ChocBar"));
        Box("F_MintIce", new Vector3(13.5f, 0.02f, -12f), new Vector3(14f, 0.05f, 11f), Vector3.zero, M("MintGreen"));
        Cyl("F_GingerGrove", new Vector3(3f, 0.03f, 8.5f), 3.4f, 0.06f, Vector3.zero, M("CookieDough"));

        // ---- central cake landmark on a hollow podium (mouse-hole crawl inside) ----
        var cream = M("FrostingCream");
        // podium ring with a 1.3w x 1.2h tunnel through (E-W), interior den 2.4x2.4
        Box("PodiumN", new Vector3(0, 0.55f, 2.35f), new Vector3(7f, 1.1f, 2.3f), Vector3.zero, cream);
        Box("PodiumS", new Vector3(0, 0.55f, -2.35f), new Vector3(7f, 1.1f, 2.3f), Vector3.zero, cream);
        Box("PodiumE", new Vector3(2.85f, 0.55f, 0f), new Vector3(1.3f, 1.1f, 2.4f), Vector3.zero, cream);
        Box("PodiumW", new Vector3(-2.85f, 0.55f, 0f), new Vector3(1.3f, 1.1f, 2.4f), Vector3.zero, cream);
        Box("PodiumLid", new Vector3(0, 1.15f, 0f), new Vector3(7f, 0.1f, 7f), Vector3.zero, M("CandyStripe"));
        // giant birthday cake on top
        Food("cake-birthday", "GiantCake", new Vector3(0, 0, 0), 20, 4.6f, 1.2f, Vector3.zero);
        // strawberries + cream blobs around the podium edge
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            Food("strawberry", "strawberry", new Vector3(Mathf.Cos(a) * 3.1f, 0, Mathf.Sin(a) * 3.1f), i * 60, 0.75f, 1.2f, Vector3.zero);
        }
        Food("whipped-cream", "cream", new Vector3(4.6f, 0, 2.6f), 40, 1.1f, 0f, Vector3.zero);
        Food("whipped-cream", "cream", new Vector3(-4.4f, 0, -3f), 190, 1.3f, 0f, Vector3.zero);
    }

    // ---------------------------------------------------------------- zones

    static void BuildZones()
    {
        var rng = new System.Random(7);
        Material[] candy = { M("BerryRed"), M("PastelBlue"), M("MintGreen"), Plain("SunYellow", new Color(0.98f, 0.8f, 0.25f), 0.25f), Plain("GrapePurple", new Color(0.7f, 0.45f, 0.85f), 0.25f) };

        // ---- NE candy forest: lollipop trees + gumdrop bushes + canes ----
        for (int i = 0; i < 8; i++)
        {
            float x = 8f + (i % 4) * 3.6f + (float)rng.NextDouble() * 1.4f;
            float z = 8f + (i / 4) * 6f + (float)rng.NextDouble() * 2f;
            Cyl("LolliStick", new Vector3(x, 1.1f, z), 0.09f, 2.2f, Vector3.zero, M("CandyWhite"));
            var head = Ball("LolliHead", new Vector3(x, 2.55f, z), new Vector3(1.5f, 1.5f, 0.45f), candy[i % candy.Length]);
            head.localEulerAngles = new Vector3(0, rng.Next(0, 180), 0);
        }
        for (int i = 0; i < 10; i++)
        {
            float x = 7.5f + (float)rng.NextDouble() * 12f;
            float z = 6.8f + (float)rng.NextDouble() * 11f;
            float s = 0.8f + (float)rng.NextDouble() * 0.7f;
            Ball("Gumdrop", new Vector3(x, s * 0.38f, z), new Vector3(s, s * 0.8f, s), candy[rng.Next(candy.Length)]);
        }
        for (int i = 0; i < 3; i++)
        {
            float x = 19.5f - i * 1.6f;
            Cyl("CandyCane", new Vector3(x, 1.3f, 17.5f), 0.14f, 2.6f, Vector3.zero, M("CandyStripe"));
            Cyl("CandyCaneArm", new Vector3(x + 0.35f, 2.5f, 17.5f), 0.14f, 0.8f, new Vector3(0, 0, 90), M("CandyStripe"));
        }
        Food("popsicle", "popsicle", new Vector3(20.5f, 0, 8f), 15, 2.2f, 0f, new Vector3(0, 0, -12));

        // ---- NW pastry district: donut family + cupcakes + pancake perch ----
        Food("donut", "donutFlat", new Vector3(-16f, 0, 15f), 0, 2.8f, 0f, Vector3.zero);                 // lying = low perch
        Food("donut-sprinkles", "donutLean", new Vector3(-11f, 0, 15.5f), 25, 2.8f, 0.45f, new Vector3(-62, 0, 0)); // leaning: crawl-under gap
        Food("donut-chocolate", "donutFlat2", new Vector3(-19.5f, 0, 9f), 70, 2.4f, 0f, Vector3.zero);
        Food("cupcake", "cupcake", new Vector3(-8.5f, 0, 11f), 10, 2.1f, 0f, Vector3.zero);
        Food("muffin", "muffin", new Vector3(-13.5f, 0, 8.5f), 130, 1.8f, 0f, Vector3.zero);
        Food("muffin", "muffin", new Vector3(-6.8f, 0, 16.5f), 260, 1.6f, 0f, Vector3.zero);
        Food("croissant", "croissant", new Vector3(-17.5f, 0, 12f), 200, 1.7f, 0f, Vector3.zero);
        Food("pancakes", "pancakeStack", new Vector3(-20f, 0, 16.5f), 0, 2.2f, 0f, Vector3.zero);          // ~1m perch
        Food("waffle", "waffleSlab", new Vector3(-9f, 0, 14f), 45, 2.2f, 0f, Vector3.zero);
        // wafer lean-to tent (crawl-in)
        Box("WaferLean", new Vector3(-15.5f, 0.75f, 18.2f), new Vector3(2.6f, 0.12f, 2.1f), new Vector3(-52, 0, 0), M("Waffle"));
        Box("WaferLean", new Vector3(-13.4f, 0.75f, 18.2f), new Vector3(2.6f, 0.12f, 2.1f), new Vector3(52, 0, 0), M("Waffle"));

        // ---- SW chocolate quarter: slabs, boulders, pond, wafer platform perch ----
        Food("chocolate", "chocSlab", new Vector3(-18f, 0, -9f), 20, 2.2f, 0f, Vector3.zero);
        Food("chocolate", "chocSlabLean", new Vector3(-16.4f, 0, -9.6f), 20, 2.2f, 0.5f, new Vector3(-58, 0, 0)); // leans on the first
        Food("candy-bar", "candyBar", new Vector3(-11f, 0, -16f), 300, 2.0f, 0f, Vector3.zero);
        for (int i = 0; i < 4; i++)
            Box("ChocBoulder", new Vector3(-20f + i * 3.4f, 0.55f, -14.5f + (i % 2) * 1.8f),
                Vector3.one * (1.0f + (i % 3) * 0.25f), new Vector3(rng.Next(-20, 20), rng.Next(0, 90), rng.Next(-15, 15)), M("ChocDark"));
        // melted chocolate pond + cookie stepping stones
        Cyl("ChocPond", new Vector3(-9f, 0.02f, -9.5f), 2.6f, 0.05f, Vector3.zero, Plain("ChocGloss", new Color(0.24f, 0.13f, 0.08f), 0.85f));
        Food("cookie", "cookieStep", new Vector3(-11.2f, 0, -9.5f), 20, 1.15f, 0.05f, Vector3.zero);
        Food("cookie-chocolate", "cookieStep", new Vector3(-9f, 0, -9.2f), 140, 1.15f, 0.05f, Vector3.zero);
        Food("cookie", "cookieStep", new Vector3(-6.9f, 0, -9.8f), 250, 1.15f, 0.05f, Vector3.zero);
        // wafer platform perch on chocolate pillars + candy-bar steps
        Box("WaferPlat", new Vector3(-19f, 1.85f, -18f), new Vector3(3.4f, 0.16f, 2.6f), Vector3.zero, M("Waffle"));
        Box("ChocPillar", new Vector3(-20.3f, 0.9f, -18.9f), new Vector3(0.3f, 1.8f, 0.3f), Vector3.zero, M("ChocDark"));
        Box("ChocPillar", new Vector3(-17.7f, 0.9f, -18.9f), new Vector3(0.3f, 1.8f, 0.3f), Vector3.zero, M("ChocDark"));
        Box("ChocPillar", new Vector3(-20.3f, 0.9f, -17.1f), new Vector3(0.3f, 1.8f, 0.3f), Vector3.zero, M("ChocDark"));
        Box("ChocPillar", new Vector3(-17.7f, 0.9f, -17.1f), new Vector3(0.3f, 1.8f, 0.3f), Vector3.zero, M("ChocDark"));
        Box("ChocStep", new Vector3(-16.4f, 0.3f, -18f), new Vector3(1.2f, 0.6f, 1.4f), Vector3.zero, M("ChocBar"));
        Box("ChocStep", new Vector3(-17.3f, 0.9f, -18f), new Vector3(1.0f, 1.8f - 0.6f, 1.4f), Vector3.zero, M("ChocBar"));

        // ---- SE ice cream corner: scoops, sundae, popsicle fence ----
        Food("sundae", "sundae", new Vector3(18.5f, 0, -15.5f), 200, 2.6f, 0f, Vector3.zero);
        Food("ice-cream-cup", "iceCup", new Vector3(9f, 0, -16.5f), 320, 1.9f, 0f, Vector3.zero);
        Food("ice-cream", "iceCream", new Vector3(13f, 0, -17.5f), 40, 1.8f, 0f, Vector3.zero);
        string[] scoops = { "ice-cream-scoop-mint", "ice-cream-scoop-chocolate", "ice-cream-scoop-mint" };
        for (int i = 0; i < 3; i++)
            Food(scoops[i], "scoop", new Vector3(8.5f + i * 1.3f, 0, -10f + (i % 2) * 1.1f), i * 80, 1.5f, 0f, Vector3.zero);
        Food("ice-cream-scoop-chocolate", "scoopTop", new Vector3(9.4f, 0, -9.6f), 30, 1.3f, 1.05f, Vector3.zero); // stacked
        for (int i = 0; i < 5; i++)
            Food(i % 2 == 0 ? "popsicle" : "popsicle-chocolate", "popsicleFence",
                new Vector3(15f + i * 1.5f, 0, -8.2f), 0, 2.3f, 0f, new Vector3(0, 0, (i % 2 == 0) ? -6 : 7));
        Food("ice-cream-cne", "cone", new Vector3(20.5f, 0, -11.5f), 70, 2.0f, 0f, new Vector3(0, 0, 78)); // fallen cone

        // ---- gingerbread grove: a crowd of real gingerbread men at hider height ----
        float[] gx = { 1.6f, 3.2f, 4.6f, 2.2f, 4.0f };
        float[] gz = { 7.4f, 9.8f, 8.0f, 9.4f, 6.9f };
        for (int i = 0; i < 5; i++)
            Food("ginger-bread", "gingerMan", new Vector3(gx[i], 0, gz[i]), rng.Next(0, 360), 1.05f, 0.06f, Vector3.zero);

        // ---- cookie-box fort (like the crate fort, but a biscuit box) ----
        Box("BoxWallW", new Vector3(11.5f, 0.8f, 4.5f), new Vector3(0.15f, 1.6f, 2.4f), Vector3.zero, M("CookieDough"));
        Box("BoxWallE", new Vector3(13.9f, 0.8f, 4.5f), new Vector3(0.15f, 1.6f, 2.4f), Vector3.zero, M("CookieDough"));
        Box("BoxWallS", new Vector3(12.7f, 0.8f, 3.35f), new Vector3(2.55f, 1.6f, 0.15f), Vector3.zero, M("CookieDough"));
        Box("BoxLid", new Vector3(12.7f, 1.62f, 4.7f), new Vector3(2.9f, 0.1f, 2.6f), new Vector3(-8, 0, 0), M("CandyStripe"));
        Food("cookie", "cookieStack", new Vector3(12.7f, 0, 5.2f), 15, 1.2f, 0.06f, Vector3.zero);
    }

    // ---------------------------------------------------------------- decoys

    static void BuildDecoys()
    {
        System.Action<Vector3, float, int, Color, string> decoy = (pos, rotY, pose, ca, nm) =>
        {
            var g = new GameObject(nm);
            g.transform.SetParent(_env, false);
            g.transform.position = pos;
            g.transform.rotation = Quaternion.Euler(0, rotY, 0);
            var d = g.AddComponent<DecoyStatue>();
            d.pose = (Pose)pose;
            d.paint = DecoyStatue.Paint.Stone;
            d.colorA = ca;
        };
        var ginger = new Color(0.72f, 0.45f, 0.22f);
        decoy(new Vector3(2.8f, 0.06f, 8.8f), 140, 4, ginger, "DecoyStatue");   // scarecrow in the gingerbread crowd
        decoy(new Vector3(4.9f, 0.06f, 9.3f), 320, 2, ginger, "DecoyStatue");   // statue in the crowd
        decoy(new Vector3(16.5f, 0.02f, 13.5f), 60, 6, new Color(0.4f, 0.8f, 0.4f), "DecoyStatue");   // green Ball among gumdrops
        decoy(new Vector3(11f, 0.02f, -12.5f), 220, 7, new Color(0.98f, 0.75f, 0.8f), "DecoyStatue"); // melted pink Dead by the scoops
        decoy(new Vector3(-12.5f, 0.02f, 13f), 30, 8, new Color(0.85f, 0.64f, 0.35f), "DecoyStatue"); // Bend picking crumbs in pastry
        decoy(new Vector3(-19.8f, 1.0f, 16.4f), 170, 5, new Color(0.9f, 0.75f, 0.5f), "DecoyStatue"); // Chair on the pancake stack
    }

    static void RegisterScene()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in scenes) if (s.path == ScenePath) return;
        scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
