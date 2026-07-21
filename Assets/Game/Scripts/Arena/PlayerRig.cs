using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// The human player's driver: reads touch (left half = virtual joystick, right half =
/// camera look, multitouch aware) with a WASD/mouse fallback for the Editor, drives the
/// CharacterMotor, and owns the on-screen buttons (DASH, JUMP/CLIMB, POSE, and the
/// context action — PAINT as a hider, SHOOT as a hunter).
/// </summary>
public class PlayerRig : MonoBehaviour
{
    public Character self;
    public ThirdPersonCamera cam;
    public MatchManager match;

    // touch bookkeeping
    int _moveTouchId = -1;
    int _lookTouchId = -1;
    Vector2 _moveOrigin;
    Vector2 _stick;
    bool _mouseLook;

    // UI
    GameObject _controlsRoot;
    RectTransform _stickKnob;
    HoldButton _jumpHold;
    Text _actionLabel;
    Image _actionBg;
    Image _actionIcon;
    Image _actionGlow;
    PressFx _actionFx;
    Button _actionBtn;
    GameObject _actionRoot;
    RectTransform[] _crossTicks;
    Image _camFade;
    GameObject _crosshair;
    Image _crossRing;
    Text _reloadLabel;
    float _lastShotAt = -10f;
    Image _jumpRing;
    float _jumpCharge;
    GameObject _posePanel;
    GameObject _poseDim;
    Image[] _poseTileBgs;
    Image[] _poseTileIcons;
    Text[] _poseTileLabels;
    GameObject _tauntBtn;
    GameObject _decoyBtn;
    GameObject _eyeBtn;
    Text _eyeLabel;
    Image _eyeBg;
    PressFx _eyeFx;
    Image _vignette;
    SelfPaintMode _paint;
    bool _decoyUsed;
    bool _spectating;

    const float ShotCooldown = 1.1f; // mirrors Shotgun; drives the crosshair ring

    void TogglePosePanel()
    {
        if (_posePanel == null) return;
        bool open = !_posePanel.activeSelf;
        if (open)
        {
            _posePanel.SetActive(true);
            if (_poseDim != null)
            {
                _poseDim.SetActive(true);
                var dg = UiKit.EnsureGroup(_poseDim);
                dg.alpha = 0f;
                StartCoroutine(UiKit.Fade(dg, 1f, 0.15f));
            }
            RefreshPoseTiles();
            StartCoroutine(UiKit.PopIn((RectTransform)_posePanel.transform, 0.92f, 0.18f));
        }
        else
        {
            if (_poseDim != null) StartCoroutine(UiKit.Fade(UiKit.EnsureGroup(_poseDim), 0f, 0.12f, deactivateAtZero: true));
            StartCoroutine(UiKit.Fade(UiKit.EnsureGroup(_posePanel), 0f, 0.12f, deactivateAtZero: true));
        }
    }

    void RefreshPoseTiles()
    {
        if (_poseTileBgs == null || self == null) return;
        int cur = (int)self.motor.CurrentPose;
        for (int i = 0; i < _poseTileBgs.Length; i++)
        {
            bool sel = i == cur;
            _poseTileBgs[i].color = sel ? new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.20f) : UiKit.Ink2;
            _poseTileIcons[i].color = sel ? UiKit.Gold : UiKit.Hex("EDEDF2");
            _poseTileLabels[i].color = sel ? UiKit.Gold : UiKit.Hex("EDEDF2");
        }
    }

    public void Setup(Character character, ThirdPersonCamera camera, MatchManager matchManager)
    {
        self = character;
        cam = camera;
        match = matchManager;
        BuildControls();
        SetTeam(self.team);
    }

    public void SetTeam(Team team)
    {
        if (_paint != null && _paint.Active) _paint.Exit();
        RefreshContextButton();
        UpdateViewMode();
    }

    /// <summary>
    /// Hunters aim like a normal FPS, but only once the hunt is actually on — in the
    /// lobby (including the wait while hiders hide) they stay third-person so they
    /// can see themselves turn red and shoulder the gun.
    /// </summary>
    void UpdateViewMode()
    {
        bool fps = self != null && self.team == Team.Hunter && match != null
            && (match.Phase == MatchPhase.Seek || match.Phase == MatchPhase.Result);
        if (_crosshair != null) _crosshair.SetActive(fps);
        if (_vignette != null) _vignette.gameObject.SetActive(fps);
        SetFirstPerson(fps);
    }

    /// <summary>
    /// Phase-aware button refresh: PAINT/SHOOT/WAIT on the big context button (hidden in
    /// the lobby — you volunteer by standing on the red pad), and visibility of the hider
    /// ability buttons (TAUNT/DECOY/EYE). MatchManager calls this on phase changes.
    /// </summary>
    public void RefreshContextButton()
    {
        if (_actionLabel == null || _actionBg == null) return;
        bool lobby = match != null && match.Phase == MatchPhase.Lobby;
        bool hider = self.team == Team.Hider;
        bool inRound = match != null && (match.Phase == MatchPhase.Hide || match.Phase == MatchPhase.Seek);

        if (_actionRoot != null) _actionRoot.SetActive(!lobby);

        if (self.team == Team.Hunter)
        {
            bool wait = match != null && match.Phase != MatchPhase.Seek && match.Phase != MatchPhase.Result;
            _actionLabel.text = wait ? "WAIT" : "SHOOT";
            SetActionSkin(wait ? UiKit.Hex("3A3A44") : UiKit.HunterRed, "crosshair", wait ? 0.6f : 1f, glow: !wait);
            if (_actionBtn != null) _actionBtn.interactable = !wait; // WAIT is a state, not a button
        }
        else
        {
            _actionLabel.text = "PAINT";
            SetActionSkin(UiKit.Blue, "paint-roller", 1f, glow: false);
            if (_actionBtn != null) _actionBtn.interactable = true;
        }

        if (_tauntBtn != null) _tauntBtn.SetActive(hider && match != null && match.Phase == MatchPhase.Seek);
        if (_decoyBtn != null) _decoyBtn.SetActive(hider && inRound && !_decoyUsed);
        if (_eyeBtn != null) _eyeBtn.SetActive(hider && inRound);
        if (_spectating && !(hider && inRound)) StopSpectate();
        // the pose panel must not survive a team/phase change (e.g. mid-seek conversion)
        if (_posePanel != null && _posePanel.activeSelf && !(hider && inRound)) TogglePosePanel();

        UpdateViewMode(); // phase changes can flip hunter FPS on/off
    }

    /// <summary>The context button is one sticker with different outfits (spec section 2).</summary>
    void SetActionSkin(Color fill, string icon, float contentAlpha, bool glow)
    {
        _actionBg.color = fill;
        if (_actionFx != null) _actionFx.RebaseColor(fill);
        if (_actionIcon != null)
        {
            _actionIcon.sprite = UiKit.Icon(icon);
            _actionIcon.color = new Color(1f, 1f, 1f, contentAlpha);
        }
        if (_actionLabel != null) _actionLabel.color = new Color(1f, 1f, 1f, contentAlpha);
        if (_actionGlow != null) _actionGlow.gameObject.SetActive(glow);
    }

    void SetFirstPerson(bool on)
    {
        if (cam != null) cam.firstPerson = on;
        if (self != null)
        {
            self.motor.faceLocked = on;
            // hide our own body so it doesn't block the first-person view
            foreach (var r in self.GetComponentsInChildren<Renderer>(true))
                r.enabled = !on;
        }
        _firstPerson = on;
    }
    bool _firstPerson;

    public void SetControlsVisible(bool visible)
    {
        if (_controlsRoot != null) _controlsRoot.SetActive(visible);
    }

    void Update()
    {
        if (self == null || cam == null) return;

        bool painting = _paint != null && _paint.Active;
        if (!painting && !_spectating)
        {
            ReadTouch();
            ReadEditorFallback();
            DriveMotor();
            // FPS: the body always faces the camera so shots and model agree
            if (_firstPerson)
                self.transform.rotation = Quaternion.Euler(0f, cam.yaw, 0f);
        }
        else if (_spectating)
        {
            self.motor.desiredMove = Vector3.zero; // frozen while watching the hunter
        }

        // editor hotkeys that work in any mode
        var kb = Keyboard.current;
        if (kb != null && kb.fKey.wasPressedThisFrame) OnAction();
        if (kb != null && kb.tKey.wasPressedThisFrame) OnTaunt();
        if (kb != null && kb.qKey.wasPressedThisFrame) OnDecoy();
        if (kb != null && kb.vKey.wasPressedThisFrame) ToggleSpectate();

        DriveUiFeedback();
    }

    /// <summary>JUMP charge ring (250ms fill while held) + crosshair cooldown ring.</summary>
    void DriveUiFeedback()
    {
        if (_jumpRing != null)
        {
            bool held = _jumpHold != null && _jumpHold.Held;
            _jumpCharge = held ? Mathf.MoveTowards(_jumpCharge, 1f, Time.deltaTime / 0.25f) : 0f;
            bool climbing = held && _jumpCharge >= 1f;
            _jumpRing.fillAmount = held ? _jumpCharge : Mathf.MoveTowards(_jumpRing.fillAmount, 0f, Time.deltaTime / 0.12f);
            // full ring breathes gently while climbing
            float breathe = climbing ? 1f + 0.06f * Mathf.Sin(Time.time * Mathf.PI * 2f / 0.6f) : 1f;
            _jumpRing.transform.localScale = new Vector3(breathe, breathe, 1f);
        }
        if (_crosshair != null && _crosshair.activeSelf && _crossRing != null)
        {
            float t = Mathf.Clamp01((Time.time - _lastShotAt) / ShotCooldown);
            _crossRing.fillAmount = t;
            bool ready = t >= 1f;
            // ready = white flash beat, then settle to red
            _crossRing.color = ready && Time.time - _lastShotAt < ShotCooldown + 0.12f ? Color.white : UiKit.HunterRed;
            if (_reloadLabel != null) _reloadLabel.gameObject.SetActive(!ready);
        }
    }

    void ReadTouch()
    {
        var ts = Touchscreen.current;
        if (ts == null) return;

        var touches = ts.touches;
        for (int i = 0; i < touches.Count; i++)
        {
            var t = touches[i];
            int id = t.touchId.ReadValue();
            Vector2 pos = t.position.ReadValue();

            if (t.press.wasPressedThisFrame)
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                bool overUI = es != null && es.IsPointerOverGameObject(id);
                if (!overUI)
                {
                    if (pos.x < Screen.width * 0.45f && _moveTouchId < 0)
                    {
                        _moveTouchId = id;
                        _moveOrigin = pos;
                    }
                    else if (_lookTouchId < 0)
                    {
                        _lookTouchId = id;
                    }
                }
            }

            if (t.press.isPressed)
            {
                if (id == _moveTouchId)
                {
                    float radius = Screen.width * 0.11f;
                    _stick = Vector2.ClampMagnitude((pos - _moveOrigin) / radius, 1f);
                }
                else if (id == _lookTouchId)
                {
                    cam.AddLook(t.delta.ReadValue());
                }
            }

            if (t.press.wasReleasedThisFrame)
            {
                if (id == _moveTouchId) { _moveTouchId = -1; _stick = Vector2.zero; }
                if (id == _lookTouchId) _lookTouchId = -1;
            }
        }
    }

    void ReadEditorFallback()
    {
        // Keyboard move (only when no joystick touch active)
        var kb = Keyboard.current;
        if (kb != null && _moveTouchId < 0)
        {
            Vector2 k = Vector2.zero;
            if (kb.wKey.isPressed) k.y += 1f;
            if (kb.sKey.isPressed) k.y -= 1f;
            if (kb.dKey.isPressed) k.x += 1f;
            if (kb.aKey.isPressed) k.x -= 1f;
            if (k != Vector2.zero) _stick = k.normalized;
            else if (_moveTouchId < 0) _stick = Vector2.Lerp(_stick, Vector2.zero, 20f * Time.deltaTime);

            if (kb.leftShiftKey.wasPressedThisFrame) self.motor.wantDash = true;
            if (kb.spaceKey.wasPressedThisFrame) self.motor.wantJumpPressed = true;
            if (kb.pKey.wasPressedThisFrame) self.motor.CyclePose();
        }

        // Mouse look: drag with LMB held (gesture must start off-UI)
        var mouse = Mouse.current;
        if (mouse != null && _lookTouchId < 0)
        {
            if (mouse.leftButton.wasPressedThisFrame) _mouseLook = !UiGuard.IsPointerOverUI();
            if (!mouse.leftButton.isPressed) _mouseLook = false;
            if (_mouseLook) cam.AddLook(mouse.delta.ReadValue());
        }
    }

    void DriveMotor()
    {
        var motor = self.motor;
        Vector3 f = cam.FlatForward();
        Vector3 r = new Vector3(f.z, 0f, -f.x);
        motor.desiredMove = f * _stick.y + r * _stick.x;
        bool jumpHeld = _jumpHold != null && _jumpHold.Held;
        var kb = Keyboard.current;
        if (kb != null && kb.spaceKey.isPressed) jumpHeld = true;
        motor.wantJumpHold = jumpHeld;

        // joystick knob visual
        if (_stickKnob != null) _stickKnob.anchoredPosition = _stick * 95f;
    }

    void OnTaunt()
    {
        if (self.team != Team.Hider || match == null) return;
        match.RequestTaunt(self);
    }

    void OnDecoy()
    {
        if (_decoyUsed || self.team != Team.Hider || match == null) return;
        if (match.Phase != MatchPhase.Hide && match.Phase != MatchPhase.Seek) return;
        if (!match.RequestDecoy(self)) return;
        _decoyUsed = true;
        RefreshContextButton();
    }

    void ToggleSpectate()
    {
        if (_spectating) { StopSpectate(); return; }
        if (self.team != Team.Hider || match == null) return;
        if (_paint != null && _paint.Active) return; // not while painting
        Character hunter = null;
        foreach (var c in match.Characters)
            if (c != null && c.team == Team.Hunter) { hunter = c; break; }
        if (hunter == null) return;
        _spectating = true;
        StartCoroutine(CamBlink(() =>
        {
            cam.spectate = hunter;
            self.motor.desiredMove = Vector3.zero;
        }));
        if (_eyeLabel != null) _eyeLabel.text = "BACK";
        if (_eyeBg != null)
        {
            _eyeBg.color = UiKit.HunterRed;
            if (_eyeFx != null) _eyeFx.RebaseColor(UiKit.HunterRed);
        }
    }

    /// <summary>Quick ink blink to smooth hard camera cuts (spectate in/out).</summary>
    IEnumerator CamBlink(System.Action midway)
    {
        if (_camFade != null)
        {
            _camFade.gameObject.SetActive(true);
            for (float t = 0f; t < 0.1f; t += Time.deltaTime)
            {
                _camFade.color = new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, t / 0.1f);
                yield return null;
            }
        }
        if (midway != null) midway();
        if (_camFade != null)
        {
            for (float t = 0f; t < 0.14f; t += Time.deltaTime)
            {
                _camFade.color = new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 1f - t / 0.14f);
                yield return null;
            }
            _camFade.gameObject.SetActive(false);
        }
    }

    void StopSpectate()
    {
        _spectating = false;
        if (cam != null && gameObject.activeInHierarchy) StartCoroutine(CamBlink(() => { cam.spectate = null; }));
        else if (cam != null) cam.spectate = null;
        if (_eyeLabel != null) _eyeLabel.text = "EYE";
        if (_eyeBg != null)
        {
            var c = new Color(UiKit.Ink2.r, UiKit.Ink2.g, UiKit.Ink2.b, 0.88f);
            _eyeBg.color = c;
            if (_eyeFx != null) _eyeFx.RebaseColor(c);
        }
    }

    void OnAction()
    {
        if (self.team == Team.Hider)
        {
            if (match != null && match.Phase != MatchPhase.Hide && match.Phase != MatchPhase.Seek) return;
            // the pose panel and paint mode never overlap
            if (_posePanel != null && _posePanel.activeSelf) TogglePosePanel();
            if (_paint == null)
            {
                _paint = gameObject.AddComponent<SelfPaintMode>();
                _paint.Init(self, cam, this);
            }
            _paint.Toggle();
        }
        else
        {
            if (match == null || match.Phase != MatchPhase.Seek) return;
            var gun = GetComponent<Shotgun>();
            if (gun == null || !gun.CanFire) return;
            self.motor.TriggerShoot();
            // fire from the camera so the crosshair is exactly the impact point (FPS style);
            // the local raycast decides the hit, the server validates and converts
            var victim = gun.Fire(cam.transform.position, cam.transform.forward, self);
            _lastShotAt = Time.time; // drives the crosshair cooldown ring
            StartCoroutine(CrosshairRecoil());
            match.RequestHit(self, victim);
        }
    }

    /// <summary>Spec 5: the crosshair ticks kick 8px outward and settle over 160ms.</summary>
    IEnumerator CrosshairRecoil()
    {
        if (_crossTicks == null) yield break;
        var basePos = new Vector2[4];
        for (int i = 0; i < 4; i++) basePos[i] = _crossTicks[i].anchoredPosition;
        float t = 0f;
        while (t < 0.16f)
        {
            t += Time.deltaTime;
            float k = 1f - UiKit.EaseOutCubic(t / 0.16f); // out fast, settle back
            for (int i = 0; i < 4; i++)
                _crossTicks[i].anchoredPosition = basePos[i] + basePos[i].normalized * (8f * k);
            yield return null;
        }
        for (int i = 0; i < 4; i++) _crossTicks[i].anchoredPosition = basePos[i];
    }

    void BuildControls()
    {
        PaintUI.EnsureEventSystem();
        var canvas = UiKit.MakeCanvas("Controls", 40, transform);
        _controlsRoot = canvas.gameObject;
        Transform root = canvas.transform;

        // hunter-view red vignette (design 09: constant hunter language)
        _vignette = UiKit.MakeImage(root, UiKit.Shape("vignette"),
            new Color(UiKit.HunterRedEdge.r, UiKit.HunterRedEdge.g, UiKit.HunterRedEdge.b, 0.38f), "HunterVignette");
        UiKit.SetRect(_vignette.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _vignette.gameObject.SetActive(false);

        // Joystick visual (fixed bottom-left; input origin is wherever the touch lands)
        var stickBg = new GameObject("Stick", typeof(Image));
        stickBg.transform.SetParent(root, false);
        var bgImg = stickBg.GetComponent<Image>();
        bgImg.sprite = UiKit.CircleSprite;
        bgImg.color = new Color(1f, 1f, 1f, 0.14f);
        bgImg.raycastTarget = false;
        UiKit.SetRect((RectTransform)stickBg.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(250, 320), new Vector2(320, 320));

        var stickRing = UiKit.MakeImage(stickBg.transform, UiKit.Shape("ring-thin"), new Color(1f, 1f, 1f, 0.18f), "Ring");
        UiKit.SetRect(stickRing.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        // four direction ticks (design: white 30% triangles just inside the rim)
        for (int i = 0; i < 4; i++)
        {
            var tick = UiKit.MakeImage(stickBg.transform, UiKit.Shape("triangle"), new Color(1f, 1f, 1f, 0.30f), "Tick" + i);
            UiKit.SetRect(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Quaternion.Euler(0, 0, 90 * i) * new Vector2(0f, 118f), new Vector2(32f, 22f));
            tick.rectTransform.localEulerAngles = new Vector3(0, 0, 90 * i);
        }

        var knob = new GameObject("Knob", typeof(Image));
        knob.transform.SetParent(stickBg.transform, false);
        var knobImg = knob.GetComponent<Image>();
        knobImg.sprite = UiKit.CircleSprite;
        knobImg.color = new Color(1f, 1f, 1f, 0.9f);
        knobImg.raycastTarget = false;
        UiKit.SetRect((RectTransform)knob.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130, 130));
        var knobShadow = UiKit.MakeImage(knob.transform, UiKit.Shape("btn-circle-base"), new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.30f), "Shadow");
        UiKit.SetRect(knobShadow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), Vector2.zero);
        knobShadow.transform.SetAsFirstSibling();
        _stickKnob = (RectTransform)knob.transform;

        // ---- sticker action buttons (design 05; same anchors as before) ----
        var jump = UiKit.MakeStickerButton(root, "JUMP", "jump", new Color(UiKit.Ink2.r, UiKit.Ink2.g, UiKit.Ink2.b, 0.88f), Color.white, 210, holdButton: true);
        UiKit.SetRect(jump.root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-160, 250), new Vector2(210, 210));
        _jumpHold = jump.body.GetComponent<HoldButton>();
        _jumpHold.onDown = () => { self.motor.wantJumpPressed = true; };
        var jumpHint = UiKit.MakeText(jump.body.transform, "HOLD = CLIMB", 17, TextAnchor.MiddleCenter, false);
        jumpHint.color = UiKit.GreenHint;
        UiKit.SetRect(jumpHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -78f), new Vector2(210, 26));
        // charge ring: fills over 250ms while held (visible hold feedback, pain point #4)
        _jumpRing = UiKit.MakeImage(jump.root, UiKit.Shape("ring"), UiKit.Green, "ChargeRing");
        UiKit.SetRect(_jumpRing.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 28f));
        _jumpRing.type = Image.Type.Filled;
        _jumpRing.fillMethod = Image.FillMethod.Radial360;
        _jumpRing.fillOrigin = (int)Image.Origin360.Top;
        _jumpRing.fillClockwise = true;
        _jumpRing.fillAmount = 0f;

        var dash = UiKit.MakeStickerButton(root, "DASH", "dash", new Color(UiKit.Ink2.r, UiKit.Ink2.g, UiKit.Ink2.b, 0.88f), Color.white, 180);
        UiKit.SetRect(dash.root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-390, 320), new Vector2(180, 180));
        dash.button.onClick.AddListener(() => { self.motor.wantDash = true; });

        var pose = UiKit.MakeStickerButton(root, "POSE", "pose", new Color(UiKit.Ink2.r, UiKit.Ink2.g, UiKit.Ink2.b, 0.88f), Color.white, 180);
        UiKit.SetRect(pose.root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-160, 500), new Vector2(180, 180));
        pose.button.onClick.AddListener(TogglePosePanel);

        // context action (PAINT / SHOOT / WAIT) — one button, different outfits
        var action = UiKit.MakeStickerButton(root, "PAINT", "paint-roller", UiKit.Blue, Color.white, 230);
        UiKit.SetRect(action.root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-400, 560), new Vector2(230, 230));
        action.button.onClick.AddListener(OnAction);
        _actionRoot = action.root.gameObject;
        _actionBtn = action.button;
        _actionBg = action.body;
        _actionIcon = action.icon;
        _actionLabel = action.label;
        _actionFx = action.fx;
        _actionGlow = UiKit.MakeImage(action.root, UiKit.Shape("btn-circle-base"), new Color(UiKit.HunterRed.r, UiKit.HunterRed.g, UiKit.HunterRed.b, 0.30f), "Glow");
        UiKit.SetRect(_actionGlow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
        _actionGlow.transform.SetAsFirstSibling();
        _actionGlow.gameObject.SetActive(false);

        // hider abilities: taunt (style points, makes noise), one-use decoy, hunter-cam
        var taunt = UiKit.MakeStickerButton(root, "TAUNT", "taunt-horn", UiKit.Gold, UiKit.GoldText, 150);
        UiKit.SetRect(taunt.root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-620, 330), new Vector2(150, 150));
        taunt.button.onClick.AddListener(OnTaunt);
        _tauntBtn = taunt.root.gameObject;
        _tauntBtn.SetActive(false);

        var decoy = UiKit.MakeStickerButton(root, "DECOY", "decoy", UiKit.Purple, Color.white, 150);
        UiKit.SetRect(decoy.root, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-620, 510), new Vector2(150, 150));
        decoy.button.onClick.AddListener(OnDecoy);
        _decoyBtn = decoy.root.gameObject;
        var decoyBadge = UiKit.MakePill(decoy.root, UiKit.Gold, "x1Badge");
        UiKit.SetRect(decoyBadge.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-14f, -2f), new Vector2(62f, 40f));
        var badgeText = UiKit.MakeText(decoyBadge.transform, "x1", 24, TextAnchor.MiddleCenter, false);
        badgeText.color = UiKit.GoldText;
        UiKit.SetRect(badgeText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), Vector2.zero);
        _decoyBtn.SetActive(false);

        var eye = UiKit.MakeStickerButton(root, "EYE", "eye", new Color(UiKit.Ink2.r, UiKit.Ink2.g, UiKit.Ink2.b, 0.88f), Color.white, 150);
        UiKit.SetRect(eye.root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-100, -320), new Vector2(150, 150));
        eye.button.onClick.AddListener(ToggleSpectate);
        _eyeBtn = eye.root.gameObject;
        _eyeBg = eye.body;
        _eyeLabel = eye.label;
        _eyeFx = eye.fx;
        _eyeBtn.SetActive(false);

        BuildPosePanel(root);
        BuildCrosshair(root);

        // camera-cut blink overlay (topmost in this canvas)
        _camFade = UiKit.MakeImage(root, null, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0f), "CamFade");
        UiKit.SetRect(_camFade.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _camFade.gameObject.SetActive(false);
    }

    /// <summary>Design 06: INK card, PICK A POSE header, 3x3 silhouette tiles, gold
    /// current pose, dim overlay behind, pointer at the POSE button; the panel's bottom
    /// clears the PAINT button by 45px (pain point #10).</summary>
    void BuildPosePanel(Transform root)
    {
        // dim overlay: swallows stray touches and closes the panel
        var dimGo = new GameObject("PoseDim", typeof(Image), typeof(Button));
        dimGo.transform.SetParent(root, false);
        var dimImg = dimGo.GetComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.4f);
        UiKit.SetRect((RectTransform)dimGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        dimGo.GetComponent<Button>().onClick.AddListener(TogglePosePanel);
        dimGo.GetComponent<Button>().transition = Selectable.Transition.None;
        _poseDim = dimGo;
        _poseDim.SetActive(false);

        var panelGo = new GameObject("PosePanel", typeof(RectTransform));
        panelGo.transform.SetParent(root, false);
        UiKit.SetRect((RectTransform)panelGo.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1f, 0f), new Vector2(-40, 720), new Vector2(660, 700));
        var panelBg = UiKit.MakePanel(panelGo.transform, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.95f), "Bg");
        panelBg.raycastTarget = true; // block touches from leaking through the card
        UiKit.SetRect(panelBg.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        // pointer down at the POSE button side
        var pointer = UiKit.MakeImage(panelGo.transform, UiKit.Shape("triangle"), new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.95f), "Pointer");
        UiKit.SetRect(pointer.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-140f, -12f), new Vector2(44f, 30f));
        pointer.rectTransform.localEulerAngles = new Vector3(0, 0, 180f);

        var title = UiKit.MakeText(panelGo.transform, "PICK A POSE", 36, TextAnchor.MiddleLeft, false);
        UiKit.SetRect(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1f), new Vector2(30f, -18f), new Vector2(-60f, 56f));

        var close = UiKit.MakeStickerButton(panelGo.transform, "", "close-x", UiKit.Hex("2A2A36"), Color.white, 56);
        UiKit.SetRect(close.root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-58f, -46f), new Vector2(56f, 56f));
        close.shadow.gameObject.SetActive(false);
        close.edge.gameObject.SetActive(false);
        close.button.onClick.AddListener(TogglePosePanel);

        // NORMAL = back to regular walk/run anims; every other pose PERSISTS while moving
        string[] poseNames = { "NORMAL", "CROUCH", "STATUE", "LIE", "SCARECROW", "CHAIR", "BALL", "DEAD", "BEND" };
        string[] poseIcons = { "pose-stand", "pose-crouch", "pose-statue", "pose-lie", "pose-scarecrow", "pose-chair", "pose-ball", "pose-dead", "pose-bend" };
        _poseTileBgs = new Image[9];
        _poseTileIcons = new Image[9];
        _poseTileLabels = new Text[9];
        const float tileW = 192f, tileH = 172f, gap = 14f;
        for (int i = 0; i < 9; i++)
        {
            Pose p = (Pose)i;
            int cx = i % 3, cy = i / 3;
            var tileGo = new GameObject("Pose_" + poseNames[i], typeof(Image), typeof(Button));
            tileGo.transform.SetParent(panelGo.transform, false);
            var tileImg = tileGo.GetComponent<Image>();
            tileImg.sprite = UiKit.Shape("tile-round-12");
            tileImg.type = Image.Type.Sliced;
            tileImg.color = UiKit.Ink2;
            UiKit.SetRect((RectTransform)tileGo.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2((cx - 1) * (tileW + gap), -86f - cy * (tileH + gap)), new Vector2(tileW, tileH));
            var tileBtn = tileGo.GetComponent<Button>();
            tileBtn.transition = Selectable.Transition.None;
            tileGo.AddComponent<PressFx>().target = tileImg;
            var icon = UiKit.MakeIconImage(tileGo.transform, poseIcons[i], UiKit.Hex("EDEDF2"), 76f);
            icon.rectTransform.anchoredPosition = new Vector2(0f, 18f);
            var lbl = UiKit.MakeText(tileGo.transform, poseNames[i], 24, TextAnchor.MiddleCenter, false);
            lbl.color = UiKit.Hex("EDEDF2");
            UiKit.SetRect(lbl.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(tileW, 30f));
            _poseTileBgs[i] = tileImg;
            _poseTileIcons[i] = icon;
            _poseTileLabels[i] = lbl;
            tileBtn.onClick.AddListener(() =>
            {
                self.motor.SetPose(p);
                TogglePosePanel();
            });
        }
        _posePanel = panelGo;
        _posePanel.SetActive(false);
    }

    /// <summary>Design 09: real FPS crosshair — center dot + four ticks with INK
    /// outlines + a red cooldown ring that refills over the 1.1s shot cooldown.</summary>
    void BuildCrosshair(Transform root)
    {
        var crossGo = new GameObject("Crosshair", typeof(RectTransform));
        crossGo.transform.SetParent(root, false);
        UiKit.SetRect((RectTransform)crossGo.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200, 200));

        _crossRing = UiKit.MakeImage(crossGo.transform, UiKit.Shape("ring"), UiKit.HunterRed, "CooldownRing");
        UiKit.SetRect(_crossRing.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _crossRing.type = Image.Type.Filled;
        _crossRing.fillMethod = Image.FillMethod.Radial360;
        _crossRing.fillOrigin = (int)Image.Origin360.Top;
        _crossRing.fillClockwise = true;

        var dot = UiKit.MakeImage(crossGo.transform, UiKit.Shape("btn-circle-base"), Color.white, "Dot");
        UiKit.SetRect(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 14));
        AddInkOutline(dot.gameObject);
        _crossTicks = new RectTransform[4];
        for (int i = 0; i < 4; i++)
        {
            var tick = UiKit.MakeImage(crossGo.transform, null, Color.white, "Tick" + i);
            bool vertical = i % 2 == 0;
            Vector2 off = i == 0 ? new Vector2(0, 64) : i == 1 ? new Vector2(64, 0) : i == 2 ? new Vector2(0, -64) : new Vector2(-64, 0);
            UiKit.SetRect(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                off, vertical ? new Vector2(12, 36) : new Vector2(36, 12));
            AddInkOutline(tick.gameObject);
            _crossTicks[i] = tick.rectTransform;
        }
        var reload = UiKit.MakeText(crossGo.transform, "RELOADING", 24, TextAnchor.MiddleCenter, true, false);
        reload.color = new Color(1f, 1f, 1f, 0.55f);
        UiKit.SetRect(reload.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(300, 32));
        _reloadLabel = reload;

        _crosshair = crossGo;
        _crosshair.SetActive(false);
    }

    static void AddInkOutline(GameObject go)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.5f);
        o.effectDistance = new Vector2(3f, -3f);
    }
}
