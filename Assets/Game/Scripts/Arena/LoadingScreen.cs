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
        bg.GetComponent<Image>().color = _hunter ? new Color(0.10f, 0.06f, 0.06f) : new Color(0.085f, 0.085f, 0.11f);
        Stretch((RectTransform)bg.transform);

        // slow-drifting diagonal stripes give the flat backdrop some life
        _stripes = new RectTransform[6];
        for (int i = 0; i < 6; i++)
        {
            var s = new GameObject("Stripe", typeof(Image));
            s.transform.SetParent(root, false);
            var img = s.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.028f);
            img.raycastTarget = false;
            var rt = (RectTransform)s.transform;
            Center(rt, new Vector2(0f, -900f + i * 360f), new Vector2(2600f, 80f));
            rt.localRotation = Quaternion.Euler(0f, 0f, 18f);
            _stripes[i] = rt;
        }

        var kicker = UiKit.MakeText(root, _hunter ? "DEPLOYING TO" : "NOW ENTERING", 40, TextAnchor.MiddleCenter);
        kicker.color = new Color(1f, 1f, 1f, 0.55f);
        Center(kicker.rectTransform, new Vector2(0f, 430f), new Vector2(900f, 60f));

        var title = UiKit.MakeText(root, mapName, 86, TextAnchor.MiddleCenter);
        Center(title.rectTransform, new Vector2(0f, 340f), new Vector2(1040f, 120f));

        var sub = UiKit.MakeText(root, subtitle, 40, TextAnchor.MiddleCenter);
        sub.color = _hunter ? new Color(1f, 0.42f, 0.35f) : new Color(0.55f, 0.8f, 1f);
        Center(sub.rectTransform, new Vector2(0f, 250f), new Vector2(900f, 60f));

        // paint-stroke progress bar
        var barBg = new GameObject("BarBg", typeof(Image));
        barBg.transform.SetParent(root, false);
        barBg.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.20f);
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

        // roller head riding the leading edge
        var headGo = new GameObject("Head", typeof(Image));
        headGo.transform.SetParent(barRt, false);
        var headImg = headGo.GetComponent<Image>();
        headImg.sprite = UiKit.CircleSprite;
        headImg.color = Color.white;
        headImg.raycastTarget = false;
        _head = (RectTransform)headGo.transform;
        Center(_head, new Vector2(-BarWidth * 0.5f, 0f), new Vector2(76f, 76f));

        _percent = UiKit.MakeText(root, "0%", 46, TextAnchor.MiddleCenter);
        Center(_percent.rectTransform, new Vector2(0f, -110f), new Vector2(300f, 60f));

        _tip = UiKit.MakeText(root, "", 36, TextAnchor.MiddleCenter);
        _tip.color = new Color(1f, 1f, 1f, 0.75f);
        Center(_tip.rectTransform, new Vector2(0f, -300f), new Vector2(1000f, 100f));
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
            _head.anchoredPosition = new Vector2(edgeX, 0f);
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
            if (tipIdx != _tipIndex) { _tipIndex = tipIdx; _tip.text = "TIP - " + _tips[tipIdx]; }

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

        // keep clear of the bar and text band in the middle
        float y = Random.Range(170f, 800f) * (Random.value < 0.5f ? -1f : 1f);
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
