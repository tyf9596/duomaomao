using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "travelling to the map" overlay, styled after the game's core fantasy:
/// a paint roller drags a rainbow stroke across the screen as the progress bar,
/// paint drips fall off its underside, splats pop around it, and gameplay tips
/// rotate underneath. Fully opaque so teleports happen invisibly behind it;
/// ends with a white flash and a fade so the map is revealed under the lifting
/// paint. Hunter-styled version runs in reds ("THE HUNT BEGINS").
/// Built 100% in code; destroys itself when done.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Show(string mapName, string subtitle, float seconds, bool hunterStyle)
    {
        var go = new GameObject("LoadingScreen");
        var ls = go.AddComponent<LoadingScreen>();
        ls._duration = Mathf.Max(1f, seconds);
        ls._hunter = hunterStyle;
        ls.Build(mapName, subtitle);
        return ls;
    }

    const float BarWidth = 840f;

    class Splat { public RectTransform rt; public float born; }

    float _duration;
    bool _hunter;
    float _t;
    int _stage; // 0 = loading, 1 = flash & fade out

    CanvasGroup _group;
    Image _fill;
    RectTransform _head;
    Text _percent, _tip;
    Image _flash;
    Texture2D _grad;
    RectTransform _splatRoot;
    RectTransform[] _stripes;
    Image[] _drips;
    float[] _dripX, _dripTargetH;
    string[] _tips;
    int _tipIndex = -1;
    float _nextSplatAt, _fadeT;
    readonly List<Splat> _splats = new List<Splat>();

    void Build(string mapName, string subtitle)
    {
        var canvas = UiKit.MakeCanvas("LoadingCanvas", 90, transform);
        _group = canvas.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f; // quick fade-in on first frames
        Transform root = canvas.transform;

        // opaque backdrop (also swallows touches aimed at the controls beneath)
        var bg = new GameObject("Bg", typeof(Image));
        bg.transform.SetParent(root, false);
        bg.GetComponent<Image>().color = _hunter ? UiKit.Hex("1D0F0C") : UiKit.Hex("15151C");
        Stretch((RectTransform)bg.transform);

        // slow-drifting diagonal stripes give the flat backdrop some life
        _stripes = new RectTransform[6];
        for (int i = 0; i < 6; i++)
        {
            var s = new GameObject("Stripe", typeof(Image));
            s.transform.SetParent(root, false);
            var img = s.GetComponent<Image>();
            img.color = _hunter ? new Color(1f, 0.35f, 0.26f, 0.055f) : new Color(1f, 1f, 1f, 0.028f);
            img.raycastTarget = false;
            var rt = (RectTransform)s.transform;
            Center(rt, new Vector2(0f, -900f + i * 360f), new Vector2(2600f, 80f));
            rt.localRotation = Quaternion.Euler(0f, 0f, 18f);
            _stripes[i] = rt;
        }

        // ambient paint dots (design 03) / warning stripe edges (design 04)
        if (_hunter)
        {
            foreach (float yEdge in new[] { 1f, 0f })
            {
                var edge = UiKit.MakeImage(root, UiKit.Shape("stripe-warn-tile"), new Color(UiKit.HunterRed.r, UiKit.HunterRed.g, UiKit.HunterRed.b, 0.85f), "WarnEdge");
                edge.type = Image.Type.Tiled;
                UiKit.SetRect(edge.rectTransform, new Vector2(0f, yEdge), new Vector2(1f, yEdge), new Vector2(0.5f, yEdge), Vector2.zero, new Vector2(0f, 26f));
            }
        }
        else
        {
            Color[] dotC = { UiKit.Gold, UiKit.Hex("2AB8A8"), UiKit.Hex("E85CA0") };
            float[] dx = { -420f, 390f, -370f };
            float[] dy = { 790f, 640f, -440f };
            float[] ds = { 120f, 76f, 90f };
            for (int i = 0; i < 3; i++)
            {
                var d = UiKit.MakeImage(root, UiKit.CircleSprite, new Color(dotC[i].r, dotC[i].g, dotC[i].b, 0.2f), "Dot");
                Center(d.rectTransform, new Vector2(dx[i], dy[i]), new Vector2(ds[i], ds[i]));
            }
        }

        var kicker = UiKit.MakeText(root, _hunter ? "DEPLOYING TO" : "NOW ENTERING", 40, TextAnchor.MiddleCenter);
        kicker.color = _hunter ? new Color(1f, 0.71f, 0.65f, 0.75f) : new Color(1f, 1f, 1f, 0.55f);
        Center(kicker.rectTransform, new Vector2(0f, 430f), new Vector2(900f, 60f));

        var title = UiKit.MakeText(root, mapName, 100, TextAnchor.MiddleCenter);
        Center(title.rectTransform, new Vector2(0f, 330f), new Vector2(1040f, 130f));

        // subtitle pill (tinted border chip, design 03/04)
        Color subTint = _hunter ? UiKit.Hex("FF6B54") : UiKit.Hex("8CCFFF");
        var subPill = new GameObject("SubPill", typeof(RectTransform));
        subPill.transform.SetParent(root, false);
        Center((RectTransform)subPill.transform, new Vector2(0f, 224f), new Vector2(660f, 66f));
        var subBorder = UiKit.MakePill(subPill.transform, new Color(subTint.r, subTint.g, subTint.b, 0.45f), "Border");
        UiKit.SetRect(subBorder.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var subInner = UiKit.MakePill(subPill.transform, _hunter ? UiKit.Hex("1D0F0C") : UiKit.Hex("15151C"), "Inner");
        UiKit.SetRect(subInner.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-6f, -6f));
        var subFillTint = UiKit.MakePill(subPill.transform, new Color(subTint.r, subTint.g, subTint.b, 0.12f), "Tint");
        UiKit.SetRect(subFillTint.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-6f, -6f));
        var sub = UiKit.MakeText(subPill.transform, subtitle, 34, TextAnchor.MiddleCenter, false);
        sub.color = subTint;
        UiKit.SetRect(sub.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        // paint-stroke progress bar (rounded trough)
        var barBg = new GameObject("BarBg", typeof(Image));
        barBg.transform.SetParent(root, false);
        var barBgImg = barBg.GetComponent<Image>();
        barBgImg.sprite = UiKit.Shape("chip-pill");
        barBgImg.type = Image.Type.Sliced;
        barBgImg.color = _hunter ? UiKit.Hex("2A1512") : UiKit.Hex("26262E");
        var barRt = (RectTransform)barBg.transform;
        Center(barRt, Vector2.zero, new Vector2(BarWidth + 20f, 66f));

        _grad = Gradient(_hunter);
        var fillGo = new GameObject("Fill", typeof(Image));
        fillGo.transform.SetParent(barRt, false);
        _fill = fillGo.GetComponent<Image>();
        _fill.sprite = Sprite.Create(_grad, new Rect(0, 0, _grad.width, 1), new Vector2(0.5f, 0.5f));
        _fill.type = Image.Type.Filled;
        _fill.fillMethod = Image.FillMethod.Horizontal;
        _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _fill.fillAmount = 0f;
        _fill.raycastTarget = false;
        Center((RectTransform)fillGo.transform, Vector2.zero, new Vector2(BarWidth, 50f));

        // drips hang off the underside once the stroke passes them
        _dripX = new float[] { -310f, -150f, 30f, 210f, 355f };
        _drips = new Image[_dripX.Length];
        _dripTargetH = new float[_dripX.Length];
        for (int i = 0; i < _dripX.Length; i++)
        {
            var dgo = new GameObject("Drip", typeof(Image));
            dgo.transform.SetParent(barRt, false);
            _drips[i] = dgo.GetComponent<Image>();
            _drips[i].color = _grad.GetPixelBilinear((_dripX[i] + BarWidth * 0.5f) / BarWidth, 0.5f);
            _drips[i].raycastTarget = false;
            var rt = (RectTransform)dgo.transform;
            UiKit.SetRect(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f),
                new Vector2(_dripX[i], -24f), new Vector2(14f, 0f));
            _dripTargetH[i] = Random.Range(30f, 80f);
        }

        // roller head riding the leading edge (+ its little handle, design 03)
        var headGo = new GameObject("Head", typeof(Image));
        headGo.transform.SetParent(barRt, false);
        var headImg = headGo.GetComponent<Image>();
        headImg.sprite = UiKit.CircleSprite;
        headImg.color = Color.white;
        headImg.raycastTarget = false;
        _head = (RectTransform)headGo.transform;
        Center(_head, new Vector2(-BarWidth * 0.5f, 0f), new Vector2(100f, 100f));
        var handle = UiKit.MakeImage(_head, UiKit.Shape("chip-pill"), UiKit.Hex("B9B6AD"), "Handle");
        UiKit.SetRect(handle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f),
            new Vector2(30f, 34f), new Vector2(14f, 80f));
        handle.rectTransform.localEulerAngles = new Vector3(0f, 0f, -28f);
        handle.transform.SetAsFirstSibling();

        _percent = UiKit.MakeText(root, "0%", 52, TextAnchor.MiddleCenter);
        Center(_percent.rectTransform, new Vector2(0f, -110f), new Vector2(300f, 64f));

        // TIP row: gold chip + text, in a particle keep-out band (fix #13)
        var tipBadge = UiKit.MakePill(root, _hunter ? UiKit.HunterRed : UiKit.Gold, "TipBadge");
        Center(tipBadge.rectTransform, new Vector2(-420f, -560f), new Vector2(110f, 50f));
        var tipBadgeTxt = UiKit.MakeText(tipBadge.transform, "TIP", 26, TextAnchor.MiddleCenter, false);
        tipBadgeTxt.color = _hunter ? Color.white : UiKit.GoldText;
        UiKit.SetRect(tipBadgeTxt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _tip = UiKit.MakeText(root, "", 34, TextAnchor.MiddleLeft, false, false);
        _tip.color = _hunter ? UiKit.Hex("FFD9D1") : new Color(1f, 1f, 1f, 0.77f);
        UiKit.SetRect(_tip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(-340f, -560f), new Vector2(860f, 90f));
        _tips = _hunter
            ? new[]
            {
                "EVERY HIDER YOU TAG JOINS YOUR TEAM",
                "CHECK PATTERNED WALLS TWICE",
                "A SHAPE THAT IS TOO PERFECT IS A HIDER",
                "WALK CLOSE - PAINT CANNOT HIDE A BODY",
            }
            : new[]
            {
                "PAINT YOURSELF TO MATCH THE WALLS",
                "HOLD JUMP AGAINST A WALL TO CLIMB",
                "POSING MAKES YOU PART OF THE SCENERY",
                "PATTERNS HIDE YOU BETTER THAN FLAT COLOR",
                "SHOT HIDERS JOIN THE HUNT - STAY HIDDEN",
            };

        // splat layer + end flash
        var splatGo = new GameObject("Splats", typeof(RectTransform));
        splatGo.transform.SetParent(root, false);
        _splatRoot = (RectTransform)splatGo.transform;
        Stretch(_splatRoot);

        var flashGo = new GameObject("Flash", typeof(Image));
        flashGo.transform.SetParent(root, false);
        _flash = flashGo.GetComponent<Image>();
        _flash.color = new Color(1f, 1f, 1f, 0f);
        _flash.raycastTarget = false;
        Stretch((RectTransform)flashGo.transform);

        _nextSplatAt = 0.4f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _t += dt;

        // drifting backdrop stripes
        for (int i = 0; i < _stripes.Length; i++)
        {
            var p = _stripes[i].anchoredPosition;
            p.x = -150f + Mathf.PingPong(_t * 26f + i * 55f, 300f);
            _stripes[i].anchoredPosition = p;
        }

        if (_stage == 0)
        {
            _group.alpha = Mathf.Min(1f, _t / 0.18f);

            float raw = Mathf.Clamp01(_t / _duration);
            float p = 1f - Mathf.Pow(1f - raw, 2.1f); // fast start, eases near the end
            _fill.fillAmount = p;
            float edgeX = -BarWidth * 0.5f + BarWidth * p;
            // spec 5: the roller head bobs +-3px as it paints
            _head.anchoredPosition = new Vector2(edgeX, Mathf.Sin(_t * Mathf.PI * 2f / 0.3f) * 3f);
            _percent.text = Mathf.RoundToInt(p * 100f) + "%";

            for (int i = 0; i < _drips.Length; i++)
            {
                if (edgeX < _dripX[i]) continue;
                var rt = (RectTransform)_drips[i].transform;
                var sz = rt.sizeDelta;
                sz.y = Mathf.MoveTowards(sz.y, _dripTargetH[i], 130f * dt);
                rt.sizeDelta = sz;
            }

            int tipIdx = Mathf.FloorToInt(_t / 1.35f) % _tips.Length;
            if (tipIdx != _tipIndex) { _tipIndex = tipIdx; _tip.text = _tips[tipIdx]; }

            if (_t >= _nextSplatAt)
            {
                _nextSplatAt = _t + Random.Range(0.28f, 0.5f);
                SpawnSplat();
            }

            if (raw >= 1f)
            {
                _stage = 1;
                _flash.color = new Color(1f, 1f, 1f, 0.75f);
            }
        }
        else
        {
            _fadeT += dt;
            _flash.color = new Color(1f, 1f, 1f, Mathf.Max(0f, 0.75f - _fadeT * 3f));
            _group.alpha = 1f - Mathf.Clamp01((_fadeT - 0.12f) / 0.38f);
            if (_fadeT > 0.55f) Destroy(gameObject);
        }

        // splat pop-in (slight overshoot)
        for (int i = 0; i < _splats.Count; i++)
        {
            float k = Mathf.Clamp01((_t - _splats[i].born) / 0.3f);
            float e = 1f - Mathf.Pow(1f - k, 3f);
            _splats[i].rt.localScale = Vector3.one * (e * (1f + 0.35f * Mathf.Sin(e * Mathf.PI)));
        }
    }

    void SpawnSplat()
    {
        var go = new GameObject("Splat", typeof(Image));
        go.transform.SetParent(_splatRoot, false);
        var img = go.GetComponent<Image>();
        img.sprite = UiKit.CircleSprite;
        float hue = _hunter
            ? (Random.value < 0.5f ? Random.Range(0f, 0.06f) : Random.Range(0.93f, 1f))
            : Random.value;
        var c = Color.HSVToRGB(hue, Random.Range(0.65f, 0.9f), Random.Range(0.85f, 1f));
        c.a = 0.7f;
        img.color = c;
        img.raycastTarget = false;

        // keep clear of the bar/text band, the title block AND the TIP row (fix #13)
        float y;
        do { y = Random.Range(170f, 800f) * (Random.value < 0.5f ? -1f : 1f); }
        while ((y > -700f && y < -420f) || (y > 150f && y < 500f));
        float size = Random.Range(24f, 110f);
        var rt = (RectTransform)go.transform;
        Center(rt, new Vector2(Random.Range(-440f, 440f), y), new Vector2(size, size));
        rt.localScale = Vector3.zero;
        _splats.Add(new Splat { rt = rt, born = _t });
    }

    static Texture2D Gradient(bool hunter)
    {
        var tex = new Texture2D(256, 1, TextureFormat.RGBA32, false);
        for (int x = 0; x < 256; x++)
        {
            float u = x / 255f;
            Color c = hunter
                ? Color.Lerp(new Color(1f, 0.45f, 0.25f), new Color(0.72f, 0.08f, 0.08f), u)
                : Color.HSVToRGB(u * 0.85f, 0.72f, 0.97f);
            tex.SetPixel(x, 0, c);
        }
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply(false);
        return tex;
    }

    static void Stretch(RectTransform rt)
    {
        UiKit.SetRect(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    static void Center(RectTransform rt, Vector2 pos, Vector2 size)
    {
        UiKit.SetRect(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
    }
}
