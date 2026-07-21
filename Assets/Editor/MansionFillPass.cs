using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch scene surgery for Arena06 (and one Arena05 fix), driven via menu items because
/// execute_code is dead (netcode assemblies overflow CodeDom's command line).
///   1 — settle floating props onto whatever is below them, fix known bad spots, report overlaps
///   2 — the mega fill: furniture/prop density, protruding wall art, graffiti objects
///   3 — Arena05: monument figures keep their tuned size after DecoyStatue's 0.5 -> 0.4 change
/// </summary>
public static class MansionFillPass
{
    const string Arena06Path = "Assets/Game/Scenes/Arena06.unity";
    const string Arena05Path = "Assets/Game/Scenes/Arena05.unity";
    const string P = "Assets/Game/Art/Patterns/";

    // ---------------------------------------------------------------- shared helpers

    static Transform Env() { return GameObject.Find("Env").transform; }

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

    static Transform Box(string name, Vector3 c, Vector3 s, Vector3 e, Material mat)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        var mc = g.AddComponent<MeshCollider>();
        mc.sharedMesh = g.GetComponent<MeshFilter>().sharedMesh;
        g.transform.SetParent(Env(), false);
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
        g.transform.SetParent(Env(), false);
        g.transform.localPosition = c;
        g.transform.localEulerAngles = e;
        g.transform.localScale = new Vector3(r * 2f, h / 2f, r * 2f);
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g.transform;
    }

    static Transform Spawn(string path, string nm, Vector3 pos, float rotY, float scale)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { Debug.LogWarning("[MansionFill] missing " + path); return null; }
        var g = Object.Instantiate(prefab, Env());
        g.name = nm;
        g.transform.position = pos;
        g.transform.rotation = Quaternion.Euler(0, rotY, 0);
        g.transform.localScale = Vector3.one * scale;
        foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            if (mf.GetComponent<Collider>() == null)
            {
                var mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
        return g.transform;
    }

    static Bounds RenderBounds(Transform t)
    {
        var rends = t.GetComponentsInChildren<Renderer>();
        var b = new Bounds(t.position, Vector3.zero);
        bool first = true;
        foreach (var r in rends)
        {
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    static void SaveActive()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void EnsureArena06()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != Arena06Path)
            EditorSceneManager.OpenScene(Arena06Path);
    }

    // names of props eligible for the settle pass (both naming schemes used across passes)
    static readonly HashSet<string> Settleable = new HashSet<string> {
        "crate","cardboardBoxClosed","cardboardBoxOpen","cb","bear","fruit","bottle","pot","pan",
        "plate","books","pottedPlant","pp","plantSmall1","plantSmall2","ps","dryer","dr","chair",
        "chairDesk","ch","desk","tbl","wb","loungeSofa","sofa","lch","bedDouble","bedSingle","bed",
        "cabinetBed","cab","bathroomCabinet","bcab","bookcaseOpen","bookcaseClosedWide","bo","bc",
        "sh","shelf","kitchenCabinet","kitchenCabinetDrawer","kitchenStove","kitchenSink",
        "kitchenBar","buf","k","isl","kitchenFridgeLarge","fr","bathtub","tub","bathroomSink",
        "sink","coatRackStanding","cr","sideTable","st","radio","pc","spk","speaker","toy",
        "ToyBlock","barrel","benchCushion","bench","fl","tree","ftree","bush"
    };

    // ---------------------------------------------------------------- 1: floaters + overlaps

    [MenuItem("Tools/Mansion/1 Fix Floaters And Overlaps")]
    public static void FixFloatersAndOverlaps()
    {
        EnsureArena06();
        var env = Env();
        Physics.SyncTransforms();
        int settled = 0;

        for (int i = 0; i < env.childCount; i++)
        {
            var c = env.GetChild(i);
            if (!Settleable.Contains(c.name)) continue;
            var b = RenderBounds(c);
            if (b.size == Vector3.zero) continue;

            // cast down from just above the prop's base, ignoring its own colliders
            var own = new HashSet<Collider>(c.GetComponentsInChildren<Collider>());
            Vector3 origin = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
            var hits = Physics.RaycastAll(new Ray(origin, Vector3.down), 8f);
            float bestDist = float.MaxValue;
            foreach (var h in hits)
            {
                if (own.Contains(h.collider)) continue;
                if (h.distance < bestDist) bestDist = h.distance;
            }
            if (bestDist == float.MaxValue) continue;
            float gap = bestDist - 0.05f; // distance from prop base to the surface below
            if (gap > 0.03f)
            {
                c.position += Vector3.down * gap;
                settled++;
            }
        }

        // the mezzanine sitter decoy was placed for the old y2.2 mezz — balcony is 3.45 now
        foreach (var d in Object.FindObjectsByType<DecoyStatue>(FindObjectsSortMode.None))
        {
            var p = d.transform.position;
            if (p.y > 1.5f && p.y < 3.4f)
            {
                d.transform.position = new Vector3(p.x, 3.45f, p.z);
                Debug.Log("[MansionFill] raised mezz decoy to balcony level");
            }
        }

        // overlap report between settleable props (log only; big offenders get nudged)
        var props = new List<Transform>();
        for (int i = 0; i < env.childCount; i++)
            if (Settleable.Contains(env.GetChild(i).name)) props.Add(env.GetChild(i));
        int nudged = 0;
        for (int a = 0; a < props.Count; a++)
            for (int b2 = a + 1; b2 < props.Count; b2++)
            {
                var ba = RenderBounds(props[a]);
                var bb = RenderBounds(props[b2]);
                if (!ba.Intersects(bb)) continue;
                var inter = new Vector3(
                    Mathf.Min(ba.max.x, bb.max.x) - Mathf.Max(ba.min.x, bb.min.x),
                    Mathf.Min(ba.max.y, bb.max.y) - Mathf.Max(ba.min.y, bb.min.y),
                    Mathf.Min(ba.max.z, bb.max.z) - Mathf.Max(ba.min.z, bb.min.z));
                float vol = inter.x * inter.y * inter.z;
                float minVol = Mathf.Min(ba.size.x * ba.size.y * ba.size.z, bb.size.x * bb.size.y * bb.size.z);
                if (minVol <= 0f || vol / minVol < 0.30f) continue;
                // stacked on purpose? (one sits on top of the other)
                if (Mathf.Abs(ba.min.y - bb.max.y) < 0.06f || Mathf.Abs(bb.min.y - ba.max.y) < 0.06f) continue;
                // items resting ON furniture (small on big) are fine
                if (minVol < 0.02f) continue;
                var delta = props[b2].position - props[a].position;
                delta.y = 0f;
                if (delta.sqrMagnitude < 0.001f) delta = Vector3.right;
                props[b2].position += delta.normalized * 0.25f;
                nudged++;
                Debug.Log("[MansionFill] overlap: " + props[a].name + " vs " + props[b2].name + " at " + props[a].position + " -> nudged");
            }

        SaveActive();
        Debug.Log("[MansionFill] PASS1 done: settled=" + settled + " nudged=" + nudged);
    }

    // ---------------------------------------------------------------- 2: mega fill

    [MenuItem("Tools/Mansion/2 Mega Fill")]
    public static void MegaFill()
    {
        EnsureArena06();
        string F = "Assets/Game/Art/Kits/FurnitureKit/";
        string FD = "Assets/Game/Art/Kits/FoodKit/";
        string CAR = "Assets/Game/Art/Kits/CarKit/";
        var wood = M("WoodBrown");
        var white = Plain("PureWhite", new Color(0.96f, 0.96f, 0.95f), 0.2f);
        Material[] bright = { M("BalRed"), M("BalYellow"), M("BalBlue"), M("BalGreen"), M("BalPink") };
        float y2 = 3.49f;

        // ---- BALLROOM: banquet row, theater chairs, rug, plants ----
        for (int i = 0; i < 3; i++) Spawn(F + "kitchenBar.fbx", "banq", new Vector3(-7.3f, 0.1f, 5.6f + i * 1.15f), 90, 0.25f);
        for (int i = 0; i < 3; i++) Spawn(FD + "plate.fbx", "plate", new Vector3(-7.3f, 1.16f, 5.6f + i * 1.15f), i * 50, 0.35f);
        Spawn(FD + "pot.fbx", "pot", new Vector3(-7.3f, 1.16f, 7.9f), 100, 0.35f);
        for (int r = 0; r < 2; r++)
            for (int i = 0; i < 4; i++)
                Spawn(F + "chair.fbx", "ch", new Vector3(-2.1f + i * 1.0f, 0.1f, 8.5f + r * 0.9f), 0, 0.25f);
        Spawn(F + "rugRectangle.fbx", "rug", new Vector3(0f, 0.11f, 5.5f), 0, 0.25f);
        Spawn(F + "pottedPlant.fbx", "pp", new Vector3(-7.3f, 0.1f, 13.4f), 0, 0.25f);
        Spawn(F + "pottedPlant.fbx", "pp", new Vector3(7.3f, 0.1f, 13.4f), 0, 0.25f);
        Spawn(F + "cardboardBoxClosed.fbx", "cb", new Vector3(7.2f, 0.1f, 1.0f), 25, 0.35f);
        Spawn(F + "coatRackStanding.fbx", "cr", new Vector3(-3.2f, 0.1f, 0.6f), 0, 0.25f);

        // ---- LIBRARY: wall bookcases, reading nook, globe, books ----
        float[] lz = { 5.3f, 6.6f, 11.4f, 12.7f };
        foreach (float z in lz) Spawn(F + "bookcaseClosedWide.fbx", "bc", new Vector3(-19.4f, 0.1f, z), 90, 0.25f);
        Spawn(F + "loungeChair.fbx", "lch", new Vector3(-10.6f, 0.1f, 12.3f), 220, 0.25f);
        Spawn(F + "sideTable.fbx", "st", new Vector3(-11.5f, 0.1f, 12.9f), 0, 0.25f);
        Spawn(F + "books.fbx", "books", new Vector3(-11.5f, 0.55f, 12.9f), 40, 0.25f);
        Spawn(F + "books.fbx", "books", new Vector3(-17.8f, 0.1f, 5.2f), 80, 0.25f);
        Spawn(F + "books.fbx", "books", new Vector3(-10.2f, 0.1f, 9.8f), 150, 0.25f);
        Cyl("GlobePole", new Vector3(-9.4f, 0.45f, 7f), 0.03f, 0.7f, Vector3.zero, wood);
        var globe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        globe.name = "Globe";
        globe.transform.SetParent(Env(), false);
        globe.transform.localPosition = new Vector3(-9.4f, 0.95f, 7f);
        globe.transform.localScale = Vector3.one * 0.36f;
        globe.GetComponent<Renderer>().sharedMaterial = M("BalBlue");

        // ---- KITCHEN: upper cabinets, south counter run, cookware ----
        for (int i = 0; i < 4; i++) Spawn(F + "kitchenCabinetUpper.fbx", "kup", new Vector3(-19.75f, 1.85f, -6.3f + i * 1.2f), 90, 0.25f);
        string[] run2 = { "kitchenCabinet", "kitchenStoveElectric", "kitchenCabinetDrawer" };
        for (int i = 0; i < run2.Length; i++) Spawn(F + run2[i] + ".fbx", "k", new Vector3(-17.5f + i * 1.2f, 0.1f, -7.5f), 180, 0.25f);
        Spawn(F + "kitchenMicrowave.fbx", "kmw", new Vector3(-17.5f, 1.22f, -7.55f), 180, 0.25f);
        Spawn(F + "kitchenBlender.fbx", "kbl", new Vector3(-15.1f, 1.22f, -7.55f), 160, 0.25f);
        Spawn(FD + "pan.fbx", "pan", new Vector3(-19.3f, 1.25f, -2.9f), 80, 0.35f);
        for (int i = 0; i < 3; i++) Spawn(FD + "plate.fbx", "plate", new Vector3(-19.3f, 1.25f + i * 0.045f, -1.8f), i * 20, 0.35f);

        // ---- PANTRY: shelves, sacks, barrels, boxes ----
        Spawn(F + "bookcaseOpenLow.fbx", "sh", new Vector3(-19.5f, 0.1f, -9.2f), 90, 0.25f);
        Spawn(F + "bookcaseOpenLow.fbx", "sh", new Vector3(-19.5f, 0.1f, -10.6f), 90, 0.25f);
        Spawn(FD + "bag.fbx", "sack", new Vector3(-16.8f, 0.1f, -12.6f), 30, 0.4f);
        Spawn(FD + "bag.fbx", "sack", new Vector3(-16.1f, 0.1f, -13.1f), 140, 0.4f);
        Spawn(FD + "barrel.fbx", "barrel", new Vector3(-9.6f, 0.1f, -13.3f), 20, 0.8f);
        Spawn(F + "cardboardBoxClosed.fbx", "cb", new Vector3(-13f, 0.1f, -9.4f), 55, 0.35f);
        Spawn(F + "cardboardBoxOpen.fbx", "cb", new Vector3(-12.2f, 0.1f, -10f), 210, 0.35f);

        // ---- DINING: candles, buffet dressing, doormat ----
        Cyl("Candle", new Vector3(-2.4f, 1.18f, -9f), 0.03f, 0.24f, Vector3.zero, white);
        Cyl("Candle", new Vector3(-1.6f, 1.18f, -9f), 0.03f, 0.24f, Vector3.zero, white);
        Spawn(FD + "plate.fbx", "plate", new Vector3(-7.45f, 1.25f, -6.3f), 15, 0.35f);
        var pieT = FindFood("pie");
        if (pieT != null) Spawn(pieT, "pie", new Vector3(-7.45f, 1.25f, -7.5f), 70, 0.35f);
        Spawn(F + "rugDoormat.fbx", "mat", new Vector3(-1f, 0.11f, -13.3f), 0, 0.25f);

        // ---- HALLWAYS: consoles, plants, doormats ----
        Spawn(F + "sideTable.fbx", "st", new Vector3(-16.5f, 0.1f, 0.6f), 0, 0.25f);
        Spawn(F + "books.fbx", "books", new Vector3(-16.5f, 0.55f, 0.6f), 30, 0.25f);
        Spawn(F + "plantSmall3.fbx", "ps", new Vector3(-12.5f, 0.1f, 0.5f), 0, 0.25f);
        Spawn(F + "sideTable.fbx", "st", new Vector3(2.5f, 0.1f, -3.5f), 180, 0.25f);
        Spawn(F + "rugDoormat.fbx", "mat", new Vector3(-1f, 0.11f, -0.5f), 0, 0.25f);

        // ---- BEDROOM: dresser, lamps, pillows, extras ----
        Spawn(F + "cabinetTelevisionDoors.fbx", "dresser", new Vector3(11.5f, 0.1f, 6.6f), 180, 0.25f);
        Spawn(F + "lampRoundFloor.fbx", "lamp", new Vector3(19.3f, 0.1f, 13.2f), 0, 0.25f);
        Spawn(F + "pillow.fbx", "pillow", new Vector3(16f, 0.75f, 12.1f), 10, 0.25f);
        Spawn(F + "pillowBlue.fbx", "pillow", new Vector3(17f, 0.75f, 12.1f), 350, 0.25f);
        Spawn(F + "pillowBlue.fbx", "pillow", new Vector3(10.6f, 0.75f, 8.6f), 95, 0.25f);
        Spawn(F + "rugRound.fbx", "rug", new Vector3(13.5f, 0.11f, 9.5f), 0, 0.25f);
        Spawn(F + "bear.fbx", "bear", new Vector3(12.6f, 0.1f, 13.3f), 240, 0.25f);

        // ---- BATHROOM: towels, cabinet, mat, mirror ----
        Box("TowelB", new Vector3(11f, 1.18f, 0.75f), new Vector3(0.5f, 0.06f, 0.3f), Vector3.zero, M("BalBlue"));
        Box("TowelB", new Vector3(15f, 1.18f, 0.75f), new Vector3(0.5f, 0.06f, 0.3f), Vector3.zero, M("BalPink"));
        Box("TowelB", new Vector3(10.7f, 1.32f, 5.35f), new Vector3(0.4f, 0.05f, 0.25f), Vector3.zero, M("BalGreen"));
        Spawn(F + "bathroomCabinetDrawer.fbx", "bcab", new Vector3(18f, 0.1f, 5.2f), 180, 0.25f);
        Spawn(F + "rugSquare.fbx", "mat", new Vector3(13.5f, 0.11f, 3f), 0, 0.25f);
        Box("MirrorFrame", new Vector3(10.75f, 2.0f, 5.86f), new Vector3(1.5f, 0.9f, 0.06f), Vector3.zero, wood);
        Box("Mirror", new Vector3(10.75f, 2.0f, 5.81f), new Vector3(1.3f, 0.7f, 0.02f), Vector3.zero, Plain("MirrorSilver", new Color(0.75f, 0.8f, 0.85f), 0.95f));

        // ---- STORAGE: shelving units w/ goods, pallets, more crates ----
        for (int u = 0; u < 2; u++)
        {
            float ux = 9f + u * 4f;
            Box("ShelfUpr", new Vector3(ux - 1.1f, 1.1f, -4f), new Vector3(0.1f, 2.0f, 0.9f), Vector3.zero, wood);
            Box("ShelfUpr", new Vector3(ux + 1.1f, 1.1f, -4f), new Vector3(0.1f, 2.0f, 0.9f), Vector3.zero, wood);
            Box("ShelfPlank", new Vector3(ux, 0.75f, -4f), new Vector3(2.3f, 0.06f, 0.9f), Vector3.zero, wood);
            Box("ShelfPlank", new Vector3(ux, 1.55f, -4f), new Vector3(2.3f, 0.06f, 0.9f), Vector3.zero, wood);
            Spawn(F + "cardboardBoxClosed.fbx", "cb", new Vector3(ux - 0.5f, 0.78f, -4f), 15, 0.3f);
            Spawn(F + "cardboardBoxOpen.fbx", "cb", new Vector3(ux + 0.5f, 0.78f, -4f), 200, 0.3f);
            Spawn(F + "cardboardBoxClosed.fbx", "cb", new Vector3(ux, 1.58f, -4f), 80, 0.3f);
        }
        for (int i = 0; i < 3; i++)
            Box("Pallet", new Vector3(6f, 0.15f + i * 0.13f, -6.5f), new Vector3(1.2f, 0.1f, 1.0f), new Vector3(0, i * 14f, 0), wood);
        Spawn(CAR + "box.fbx", "crate", new Vector3(16.5f, 0.1f, -1.8f), 30, 1.5f);
        Spawn(CAR + "box.fbx", "crate", new Vector3(18.2f, 0.1f, -6.2f), -18, 1.8f);

        // ---- BALCONY: plants + seats ----
        Spawn(F + "pottedPlant.fbx", "pp", new Vector3(-6.8f, y2 - 0.04f, 4.5f), 0, 0.25f);
        Spawn(F + "chair.fbx", "ch", new Vector3(-6.8f, y2 - 0.04f, 10.2f), 90, 0.25f);
        Spawn(F + "sideTable.fbx", "st", new Vector3(-6.8f, y2 - 0.04f, 11f), 0, 0.25f);
        Spawn(F + "pottedPlant.fbx", "pp", new Vector3(6.8f, y2 - 0.04f, 4.5f), 0, 0.25f);

        // ---- STUDY: more cubicles, filing, papers ----
        Spawn(F + "desk.fbx", "desk", new Vector3(-17f, y2, 12.4f), 180, 0.25f);
        Spawn(F + "desk.fbx", "desk", new Vector3(-14f, y2, 12.4f), 180, 0.25f);
        Spawn(F + "chairDesk.fbx", "ch", new Vector3(-17f, y2, 13.2f), 0, 0.25f);
        Spawn(F + "chairDesk.fbx", "ch", new Vector3(-14f, y2, 13.2f), 0, 0.25f);
        Spawn(F + "kitchenCabinetDrawer.fbx", "file", new Vector3(-9.2f, y2, 7f), 270, 0.25f);
        Spawn(F + "kitchenCabinetDrawer.fbx", "file", new Vector3(-9.2f, y2, 8.2f), 270, 0.25f);
        for (int i = 0; i < 4; i++)
            Box("Paper", new Vector3(-16.9f + i * 1.9f, y2 + 0.88f, 9.6f), new Vector3(0.22f, 0.015f, 0.3f), new Vector3(0, i * 25f, 0), white);
        Spawn(F + "plantSmall1.fbx", "ps", new Vector3(-11f, y2 + 0.86f, 9.7f), 0, 0.25f);

        // ---- KIDS: play table, more toys ----
        Spawn(F + "sideTable.fbx", "kidtable", new Vector3(-16.5f, y2, 3.8f), 0, 0.25f);
        Spawn(F + "chair.fbx", "ch", new Vector3(-17.2f, y2, 3.2f), 140, 0.25f);
        Spawn(F + "chair.fbx", "ch", new Vector3(-15.8f, y2, 4.3f), 320, 0.25f);
        var rng = new System.Random(17);
        for (int i = 0; i < 6; i++)
        {
            float s = 0.2f + (float)rng.NextDouble() * 0.15f;
            Box("ToyBlock", new Vector3(-11.5f + (float)rng.NextDouble() * 2.5f, y2 + s / 2f, -0.8f + (float)rng.NextDouble() * 2f),
                Vector3.one * s, new Vector3(0, rng.Next(0, 90), 0), bright[rng.Next(bright.Length)]);
        }
        Spawn(F + "bear.fbx", "bear", new Vector3(-10.2f, y2, 2.6f), 200, 0.3f);

        // ---- ATTIC: junk mountain ----
        Spawn(F + "chair.fbx", "junk", new Vector3(-13.2f, y2, -12.4f), 45, 0.25f);
        Spawn(F + "chair.fbx", "junk", new Vector3(-12.6f, y2 + 0.05f, -12.7f), 165, 0.25f);
        Cyl("RolledRug", new Vector3(-15.8f, y2 + 0.16f, -11.8f), 0.16f, 1.5f, new Vector3(0, 20, 90), M("CarpetRose"));
        Cyl("RolledRug", new Vector3(-15.5f, y2 + 0.16f, -11.3f), 0.14f, 1.3f, new Vector3(0, -15, 90), M("WallpaperStripe"));
        Spawn(CAR + "box.fbx", "crate", new Vector3(-12f, y2, -9.5f), 60, 1.3f);
        Spawn(F + "cardboardBoxClosed.fbx", "cb", new Vector3(-14.2f, y2, -6.8f), 30, 0.35f);
        Spawn(F + "cardboardBoxOpen.fbx", "cb", new Vector3(-13.3f, y2, -7.3f), 260, 0.35f);
        Spawn(F + "books.fbx", "books", new Vector3(-16.6f, y2, -7.9f), 110, 0.25f);
        Spawn(F + "lampSquareFloor.fbx", "lamp", new Vector3(-9.4f, y2, -8.6f), 0, 0.25f);
        var fallenRack = Spawn(F + "coatRackStanding.fbx", "junk", new Vector3(-17.5f, y2 + 0.3f, -4.5f), 0, 0.25f);
        if (fallenRack != null) fallenRack.localEulerAngles = new Vector3(0, 30, 78);

        // ---- MASTER: bench, lamps, books ----
        Spawn(F + "benchCushion.fbx", "bench", new Vector3(15.5f, y2, 9.6f), 0, 0.25f);
        Spawn(F + "lampRoundTable.fbx", "lamp", new Vector3(13.8f, y2 + 0.55f, 13.2f), 0, 0.25f);
        Spawn(F + "books.fbx", "books", new Vector3(17.2f, y2 + 0.55f, 13.2f), 60, 0.25f);
        Spawn(F + "rugDoormat.fbx", "mat", new Vector3(11f, y2 + 0.01f, 11f), 90, 0.25f);

        // ---- LOUNGE: bar corner + dartboard ----
        Spawn(F + "kitchenBar.fbx", "bar", new Vector3(10.6f, y2, 0.9f), 0, 0.25f);
        Spawn(F + "kitchenBar.fbx", "bar", new Vector3(11.7f, y2, 0.9f), 0, 0.25f);
        var bt = FindFood("bottle");
        if (bt != null)
        {
            Spawn(bt, "bottle", new Vector3(10.5f, y2 + 1.06f, 0.9f), 20, 0.35f);
            Spawn(bt, "bottle", new Vector3(11.1f, y2 + 1.06f, 0.95f), 200, 0.35f);
        }
        Cyl("Dartboard", new Vector3(19.82f, y2 + 1.7f, 4f), 0.28f, 0.05f, new Vector3(0, 0, 90), M("BalRed"));
        Cyl("DartboardEye", new Vector3(19.79f, y2 + 1.7f, 4f), 0.09f, 0.05f, new Vector3(0, 0, 90), white);

        // ---- BASEMENT: more barrels, pipes, coal, bottles ----
        Spawn(FD + "barrel.fbx", "barrel", new Vector3(9f, -3.0f, 4.6f), 45, 0.9f);
        Spawn(FD + "barrel.fbx", "barrel", new Vector3(-1.5f, -3.0f, 1.5f), 210, 0.9f);
        Spawn(FD + "barrel.fbx", "barrel", new Vector3(-12.8f, -3.0f, 3.2f), 80, 0.9f);
        Box("B_Pipe2", new Vector3(0f, -0.55f, 5.9f), new Vector3(27f, 0.22f, 0.22f), Vector3.zero, M("ConcreteGray"));
        for (int i = 0; i < 5; i++)
        {
            var coal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coal.name = "Coal";
            coal.transform.SetParent(Env(), false);
            coal.transform.localPosition = new Vector3(12.6f + (i % 3) * 0.35f, -2.82f, -11f + (i / 3) * 0.35f);
            coal.transform.localScale = Vector3.one * (0.3f + (i % 2) * 0.12f);
            coal.GetComponent<Renderer>().sharedMaterial = M("PianoBlack");
        }
        if (bt != null)
        {
            Spawn(bt, "bottle", new Vector3(-13.6f, -1.95f, -0.9f), 30, 0.4f);
            Spawn(bt, "bottle", new Vector3(-13.6f, -1.95f, 0.4f), 130, 0.4f);
        }

        // ---- COURTYARD: mural benches, planters, picnic, birdbath ----
        Spawn(F + "benchCushion.fbx", "bench", new Vector3(-8f, 0f, -16.2f), 180, 0.25f);
        Spawn(F + "benchCushion.fbx", "bench", new Vector3(8f, 0f, -16.2f), 180, 0.25f);
        for (int i = 0; i < 3; i++)
        {
            Box("Planter", new Vector3(-19f + i * 2.2f, 0.28f, -21.8f), new Vector3(1.4f, 0.5f, 0.7f), Vector3.zero, wood);
            Spawn("Assets/Game/Art/Kits/NatureKit/flower_redA.fbx", "fl", new Vector3(-19.2f + i * 2.2f, 0.5f, -21.8f), i * 70, 1.6f);
        }
        Box("PicnicBlanket", new Vector3(9f, 0.02f, -20.3f), new Vector3(1.7f, 0.03f, 1.4f), new Vector3(0, 15, 0), M("CarpetRose"));
        Spawn(FD + "plate.fbx", "plate", new Vector3(8.7f, 0.06f, -20.2f), 10, 0.35f);
        var burger = FindFood("burger");
        if (burger != null) Spawn(burger, "food", new Vector3(9.4f, 0.06f, -20.5f), 80, 0.35f);
        Cyl("BirdbathPole", new Vector3(-10f, 0.35f, -20.5f), 0.14f, 0.7f, Vector3.zero, M("StoneGray"));
        Cyl("BirdbathDish", new Vector3(-10f, 0.74f, -20.5f), 0.45f, 0.1f, Vector3.zero, M("StoneGray"));

        // ---- PROTRUDING WALL ART ----
        DeepRelief(new Vector3(-6.2f, 2.1f, 13.84f), 0, M("SplatMural"));      // ballroom north (west of stage)
        DeepRelief(new Vector3(-7.83f, 1.9f, -7.2f), 90, M("RainbowDrip"));    // dining west
        DeepRelief(new Vector3(14f, 5.1f, 13.84f), 0, M("DoodleArcs"));        // master north
        BustShelf(new Vector3(-6.9f, 2.3f, 13.82f), M("StoneGray"));           // ballroom trophy bust L
        BustShelf(new Vector3(6.9f, 2.3f, 13.82f), M("StoneGray"));            // ballroom trophy bust R
        WallShelf(new Vector3(-16f, 1.9f, -7.82f), 0, bt);                     // kitchen shelf w/ bottle
        WallShelf(new Vector3(16f, y2 + 1.6f, 0.24f), 180, bt);                // lounge shelf
        // hallway clock
        Cyl("ClockFace", new Vector3(-2f, 2.3f, -3.82f), 0.4f, 0.07f, new Vector3(90, 0, 0), white);
        Box("ClockHand", new Vector3(-2f, 2.42f, -3.86f), new Vector3(0.04f, 0.26f, 0.02f), Vector3.zero, M("PianoBlack"));
        Box("ClockHand", new Vector3(-2.1f, 2.3f, -3.86f), new Vector3(0.18f, 0.04f, 0.02f), Vector3.zero, M("PianoBlack"));

        // ---- GRAFFITI OBJECTS ----
        LeanBoard(new Vector3(6.5f, 0f, -0.6f), 180, M("SplatMural"));         // storage vs north wall
        LeanBoard(new Vector3(19.4f, 0f, -9.5f), 270, M("RainbowDrip"));       // storage east wall
        LeanBoard(new Vector3(-14.5f, 0f, -14.6f), 0, M("DoodleArcs"));        // courtyard south face
        LeanBoard(new Vector3(14.5f, 0f, -14.6f), 0, M("SplatMural"));         // courtyard south face
        LeanBoard(new Vector3(-13.9f, -3.1f, -6f), 90, M("RainbowDrip"));      // wine cellar
        LeanBoard(new Vector3(-19.4f, y2 - 0.1f, -10f), 90, M("SplatMural"));  // attic
        FloorSplat(new Vector3(-6.5f, 0.115f, 6.5f), 0.8f);                    // ballroom under-balcony
        FloorSplat(new Vector3(12f, 0.115f, -3f), 0.95f);                      // storage
        FloorSplat(new Vector3(0f, 0.045f, -16.3f), 0.9f);                     // courtyard path
        FloorSplat(new Vector3(1.5f, -2.985f, -4f), 0.7f);                     // basement corridor
        // graffiti-bombed crates
        var gc1 = Spawn(CAR + "box.fbx", "crate", new Vector3(5.2f, 0.1f, -3f), 15, 1.5f);
        if (gc1 != null) foreach (var r in gc1.GetComponentsInChildren<Renderer>()) r.sharedMaterial = M("SplatMural");
        var gc2 = Spawn(CAR + "box.fbx", "crate", new Vector3(17f, 0f, -16.5f), -25, 1.4f);
        if (gc2 != null) foreach (var r in gc2.GetComponentsInChildren<Renderer>()) r.sharedMaterial = M("RainbowDrip");
        // corner wrap in storage SE
        Box("G_Corner", new Vector3(18.2f, 1.3f, -13.72f), new Vector3(3.2f, 2.4f, 0.08f), Vector3.zero, M("DoodleArcs"));
        Box("G_Corner", new Vector3(19.72f, 1.3f, -12.4f), new Vector3(0.08f, 2.4f, 2.6f), Vector3.zero, M("DoodleArcs"));

        SaveActive();
        Debug.Log("[MansionFill] PASS2 mega fill done, children=" + Env().childCount);
    }

    static string FindFood(string keyword)
    {
        var guids = AssetDatabase.FindAssets(keyword + " t:Model", new[] { "Assets/Game/Art/Kits/FoodKit" });
        return guids.Length > 0 ? AssetDatabase.GUIDToAssetPath(guids[0]) : null;
    }

    /// <summary>Painting with a fat protruding frame + ledge (user: 突起的画作).</summary>
    static void DeepRelief(Vector3 c, float rotY, Material canvas)
    {
        var root = new GameObject("ReliefArt");
        root.transform.SetParent(Env(), false);
        root.transform.localPosition = c;
        root.transform.localRotation = Quaternion.Euler(0, rotY, 0);
        System.Action<string, Vector3, Vector3, Material> part = (n, lp, ls, m) => {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = n;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            var mc = g.AddComponent<MeshCollider>();
            mc.sharedMesh = g.GetComponent<MeshFilter>().sharedMesh;
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = lp;
            g.transform.localScale = ls;
            g.GetComponent<Renderer>().sharedMaterial = m;
        };
        var wood = M("WoodBrown");
        part("FrameT", new Vector3(0, 0.75f, -0.1f), new Vector3(1.9f, 0.14f, 0.22f), wood);
        part("FrameB", new Vector3(0, -0.75f, -0.1f), new Vector3(1.9f, 0.14f, 0.22f), wood);
        part("FrameL", new Vector3(-0.88f, 0, -0.1f), new Vector3(0.14f, 1.36f, 0.22f), wood);
        part("FrameR", new Vector3(0.88f, 0, -0.1f), new Vector3(0.14f, 1.36f, 0.22f), wood);
        part("Canvas", new Vector3(0, 0, -0.04f), new Vector3(1.62f, 1.36f, 0.03f), canvas);
        part("Ledge", new Vector3(0, -0.9f, -0.14f), new Vector3(1.5f, 0.06f, 0.3f), wood);
    }

    /// <summary>Blocky "trophy bust" head sticking out of the wall on a plinth.</summary>
    static void BustShelf(Vector3 c, Material stone)
    {
        Box("BustPlinth", c + new Vector3(0, -0.25f, -0.15f), new Vector3(0.4f, 0.1f, 0.35f), Vector3.zero, M("WoodBrown"));
        Box("BustHead", c + new Vector3(0, 0f, -0.16f), new Vector3(0.3f, 0.32f, 0.3f), new Vector3(0, 15, 0), stone);
        Box("BustShoulders", c + new Vector3(0, -0.19f, -0.16f), new Vector3(0.42f, 0.09f, 0.32f), Vector3.zero, stone);
    }

    /// <summary>Small wall shelf with a bottle on it.</summary>
    static void WallShelf(Vector3 c, float rotY, string bottlePath)
    {
        var shelf = Box("WallShelf", c, new Vector3(0.9f, 0.05f, 0.26f), new Vector3(0, rotY, 0), M("WoodBrown"));
        Box("ShelfBracket", c + Quaternion.Euler(0, rotY, 0) * new Vector3(-0.3f, -0.09f, 0.06f), new Vector3(0.05f, 0.14f, 0.12f), new Vector3(0, rotY, 0), M("WoodBrown"));
        Box("ShelfBracket", c + Quaternion.Euler(0, rotY, 0) * new Vector3(0.3f, -0.09f, 0.06f), new Vector3(0.05f, 0.14f, 0.12f), new Vector3(0, rotY, 0), M("WoodBrown"));
        if (bottlePath != null) Spawn(bottlePath, "bottle", c + new Vector3(0, 0.03f, 0), rotY + 40f, 0.3f);
    }

    /// <summary>Graffiti plank leaning against a wall (rotY faces the wall's normal).</summary>
    static void LeanBoard(Vector3 basePos, float rotY, Material art)
    {
        var t = Box("GraffitiBoard", basePos + new Vector3(0, 1.05f, 0), new Vector3(1.7f, 2.15f, 0.07f), new Vector3(-13f, rotY, 0), art);
        // shift so the top leans onto the wall behind it
        t.position += Quaternion.Euler(0, rotY, 0) * new Vector3(0, 0, 0.12f);
    }

    static void FloorSplat(Vector3 c, float r)
    {
        Cyl("FloorSplat", c, r, 0.012f, new Vector3(0, Random.Range(0, 360f), 0), M("SplatMural"));
    }

    // ---------------------------------------------------------------- 3: Arena05 monument

    [MenuItem("Tools/Mansion/3 Rescale Monument Markers (Arena05)")]
    public static void RescaleMonument()
    {
        EditorSceneManager.OpenScene(Arena05Path);
        var env = Env();
        var monument = env.Find("Monument");
        int n = 0;
        if (monument != null)
        {
            for (int i = 0; i < monument.childCount; i++)
            {
                var c = monument.GetChild(i);
                if (c.name != "MonumentFigure") continue;
                c.localScale *= 1.25f; // DecoyStatue body scale went 0.5 -> 0.4; keep world size
                n++;
            }
        }
        SaveActive();
        Debug.Log("[MansionFill] monument markers rescaled: " + n);
        EditorSceneManager.OpenScene(Arena06Path);
    }
}
