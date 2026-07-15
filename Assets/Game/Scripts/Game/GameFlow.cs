using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public enum GamePhase { Hide, Handoff, Seek, Result }

/// <summary>
/// The pass-and-play round loop that turns the paint demo into a game:
///   HIDE    — player 1 paints the chameleon and MOVEs it somewhere sneaky, then taps READY.
///   HANDOFF — full-screen curtain: hand the phone to player 2 (camera resets so no peeking).
///   SEEK    — player 2 orbits and taps to find the chameleon: limited time and tries.
///   RESULT  — reveal + score, PLAY AGAIN resets the skin and starts a new round.
/// Bootstraps itself into any scene that has a ChameleonPainter, so dioramas need no wiring.
/// </summary>
public class GameFlow : MonoBehaviour
{
    [Header("Seek rules")]
    public float seekSeconds = 45f;
    public int maxGuesses = 5;
    public float tapMoveThreshold = 14f; // px; a bigger swipe is an orbit drag, not a guess

    GamePhase _phase;
    ChameleonPainter _painter;
    PaintUI _paintUI;
    OrbitCamera _orbit;

    float _timeLeft;
    int _guessesLeft;
    int _misses;
    float _seekStarted;

    // seek tap tracking
    Vector2 _downPos;
    bool _down, _moved, _downOverUI;

    // UI
    Font _font;
    Text _title;
    Text _timer;
    GameObject _readyButton;
    GameObject _handoffOverlay;
    GameObject _resultPanel;
    Text _resultText;
    Material _missMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<ChameleonPainter>() == null) return;
        if (FindFirstObjectByType<GameFlow>() != null) return;
        new GameObject("GameFlow").AddComponent<GameFlow>();
    }

    void Start()
    {
        _painter = FindFirstObjectByType<ChameleonPainter>();
        _paintUI = FindFirstObjectByType<PaintUI>();
        _orbit = FindFirstObjectByType<OrbitCamera>();
        try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { _font = null; }
        PaintUI.EnsureEventSystem();
        if (_paintUI != null) _paintUI.EnsureBuilt();
        BuildUI();
        SetPhase(GamePhase.Hide);
    }

    void OnDestroy()
    {
        if (_missMat != null) Destroy(_missMat);
    }

    void Update()
    {
        if (_phase != GamePhase.Seek) return;

        _timeLeft -= Time.deltaTime;
        if (_timer != null)
            _timer.text = $"TIME {Mathf.Max(0, Mathf.CeilToInt(_timeLeft))}   TRIES {_guessesLeft}/{maxGuesses}";
        if (_timeLeft <= 0f)
        {
            EndSeek("TIME'S UP — CHAMELEON WINS!");
            return;
        }

        HandleSeekTap();
    }

    public void SetPhase(GamePhase phase)
    {
        _phase = phase;
        bool hide = phase == GamePhase.Hide;

        if (_painter != null)
        {
            _painter.interactionEnabled = hide;
            if (!hide) _painter.tool = BrushTool.Paint;
        }
        if (_paintUI != null && _paintUI.Root != null) _paintUI.Root.SetActive(hide);
        // The handoff curtain blocks touches anyway, but a disabled camera can't leak the
        // hiding spot through a stray drag on the frame the curtain appears.
        if (_orbit != null) _orbit.enabled = phase != GamePhase.Handoff;

        _readyButton.SetActive(hide);
        _handoffOverlay.SetActive(phase == GamePhase.Handoff);
        _resultPanel.SetActive(phase == GamePhase.Result);
        _timer.gameObject.SetActive(phase == GamePhase.Seek);

        switch (phase)
        {
            case GamePhase.Hide:
                _title.text = "PAINT & HIDE";
                break;
            case GamePhase.Handoff:
                _title.text = "";
                if (_orbit != null) _orbit.ResetView(); // don't hand the seeker a zoomed-in view
                break;
            case GamePhase.Seek:
                _title.text = "FIND THE CHAMELEON!";
                _timeLeft = seekSeconds;
                _guessesLeft = maxGuesses;
                _misses = 0;
                _seekStarted = Time.time;
                _down = false;
                break;
            case GamePhase.Result:
                _title.text = "";
                break;
        }
    }

    void HandleSeekTap()
    {
        Vector2 pos; bool pressed, held, released;
        ReadPointer(out pos, out pressed, out held, out released);

        if (pressed)
        {
            _down = true;
            _moved = false;
            _downPos = pos;
            _downOverUI = UiGuard.IsPointerOverUI();
        }
        else if (_down)
        {
            if ((pos - _downPos).magnitude > tapMoveThreshold) _moved = true;
            if (released)
            {
                if (!_moved && !_downOverUI) Guess(pos);
                _down = false;
            }
            else if (!held)
            {
                _down = false; // touch cancelled
            }
        }
    }

    void Guess(Vector2 screenPos)
    {
        Camera cam = _painter != null && _painter.cam != null ? _painter.cam : Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos);
        RaycastHit hit;
        bool hitSomething = Physics.Raycast(ray, out hit, 100f);

        if (hitSomething && _painter != null && hit.collider == _painter.targetCollider)
        {
            float took = Time.time - _seekStarted;
            EndSeek($"FOUND IN {took:0.0}s · {_misses} {(_misses == 1 ? "MISS" : "MISSES")}");
            return;
        }

        _misses++;
        _guessesLeft--;
        if (hitSomething) StartCoroutine(MissMarker(hit.point));
        if (_guessesLeft <= 0) EndSeek("OUT OF TRIES — CHAMELEON WINS!");
    }

    void EndSeek(string message)
    {
        SetPhase(GamePhase.Result);
        if (_resultText != null) _resultText.text = message;
        if (_painter != null) StartCoroutine(RevealPulse(_painter.Body)); // show where it was hiding
    }

    IEnumerator MissMarker(Vector3 point)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "MissMarker";
        go.transform.position = point;
        go.transform.localScale = Vector3.one * 0.12f;
        var r = go.GetComponent<Renderer>();
        if (_missMat == null)
        {
            _missMat = new Material(r.sharedMaterial);
            if (_missMat.HasProperty("_BaseColor")) _missMat.SetColor("_BaseColor", new Color(0.9f, 0.15f, 0.1f));
            else _missMat.color = new Color(0.9f, 0.15f, 0.1f);
        }
        r.sharedMaterial = _missMat;
        yield return new WaitForSeconds(0.8f);
        if (go != null) Destroy(go);
    }

    IEnumerator RevealPulse(Transform body)
    {
        Vector3 baseScale = body.localScale;
        const float duration = 1.5f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float s = 1f + 0.25f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * (1f - t / duration);
            body.localScale = baseScale * s;
            yield return null;
        }
        body.localScale = baseScale;
    }

    void ReadPointer(out Vector2 pos, out bool pressed, out bool held, out bool released)
    {
        pos = Vector2.zero; pressed = false; held = false; released = false;

        var ts = Touchscreen.current;
        if (ts != null)
        {
            var pt = ts.primaryTouch;
            if (pt.press.isPressed || pt.press.wasPressedThisFrame || pt.press.wasReleasedThisFrame)
            {
                pos = pt.position.ReadValue();
                pressed = pt.press.wasPressedThisFrame;
                held = pt.press.isPressed;
                released = pt.press.wasReleasedThisFrame;
                return;
            }
        }

        var m = Mouse.current;
        if (m != null)
        {
            pos = m.position.ReadValue();
            pressed = m.leftButton.wasPressedThisFrame;
            held = m.leftButton.isPressed;
            released = m.leftButton.wasReleasedThisFrame;
        }
    }

    // ---------------- UI construction ----------------

    void BuildUI()
    {
        var canGo = new GameObject("GameFlowCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canGo.transform.SetParent(transform, false);
        var canvas = canGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60; // above the paint bar
        var scaler = canGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        Transform root = canGo.transform;

        // Top HUD: phase title + seek timer
        _title = MakeText(root, "", 58, TextAnchor.MiddleCenter);
        SetRect(_title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(-40, 90));
        _timer = MakeText(root, "", 48, TextAnchor.MiddleCenter);
        SetRect(_timer.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -135), new Vector2(-40, 70));

        // READY button (hide phase, top-right)
        var ready = MakeButton(root, "READY!", new Color(0.20f, 0.55f, 0.25f), Color.white, 52);
        SetRect((RectTransform)ready.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -240), new Vector2(300, 120));
        ready.onClick.AddListener(() => SetPhase(GamePhase.Handoff));
        _readyButton = ready.gameObject;

        // Handoff curtain: covers everything, tap to begin seeking
        var curtain = new GameObject("Handoff", typeof(Image), typeof(Button));
        curtain.transform.SetParent(root, false);
        curtain.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.97f);
        SetRect((RectTransform)curtain.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        curtain.GetComponent<Button>().onClick.AddListener(() => SetPhase(GamePhase.Seek));
        var curtainTitle = MakeText(curtain.transform, "PASS THE PHONE\nTO THE SEEKER", 72, TextAnchor.MiddleCenter);
        SetRect(curtainTitle.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(-80, 300));
        var curtainHint = MakeText(curtain.transform, "TAP TO START SEEKING", 44, TextAnchor.MiddleCenter);
        curtainHint.color = new Color(1f, 1f, 1f, 0.55f);
        SetRect(curtainHint.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -220), new Vector2(-80, 80));
        _handoffOverlay = curtain;

        // Result: banner + play again (no dim — let the winner admire the reveal)
        var result = new GameObject("Result", typeof(RectTransform));
        result.transform.SetParent(root, false);
        SetRect((RectTransform)result.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _resultText = MakeText(result.transform, "", 64, TextAnchor.MiddleCenter);
        SetRect(_resultText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -220), new Vector2(-60, 180));
        var again = MakeButton(result.transform, "PLAY AGAIN", new Color(0.20f, 0.40f, 0.75f), Color.white, 52);
        SetRect((RectTransform)again.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(520, 140));
        again.onClick.AddListener(() =>
        {
            if (_painter != null) _painter.ClearCanvas();
            SetPhase(GamePhase.Hide);
        });
        _resultPanel = result;
    }

    Text MakeText(Transform parent, string text, int size, TextAnchor align)
    {
        var go = new GameObject("Text", typeof(Text), typeof(Shadow));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = _font;
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var sh = go.GetComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.7f);
        sh.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    Button MakeButton(Transform parent, string label, Color bg, Color fg, int fontSize)
    {
        var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bg;
        var t = MakeText(go.transform, label, fontSize, TextAnchor.MiddleCenter);
        SetRect(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        return go.GetComponent<Button>();
    }

    static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
    }
}
