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
    GameObject _actionRoot;
    GameObject _crosshair;
    GameObject _posePanel;
    GameObject _tauntBtn;
    GameObject _decoyBtn;
    GameObject _eyeBtn;
    Text _eyeLabel;
    Image _eyeBg;
    SelfPaintMode _paint;
    bool _decoyUsed;
    bool _spectating;

    void TogglePosePanel()
    {
        if (_posePanel != null) _posePanel.SetActive(!_posePanel.activeSelf);
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
            _actionBg.color = wait ? new Color(0.35f, 0.35f, 0.4f, 0.85f) : new Color(0.75f, 0.22f, 0.18f, 0.85f);
        }
        else
        {
            _actionLabel.text = "PAINT";
            _actionBg.color = new Color(0.24f, 0.5f, 0.75f, 0.85f);
        }

        if (_tauntBtn != null) _tauntBtn.SetActive(hider && match != null && match.Phase == MatchPhase.Seek);
        if (_decoyBtn != null) _decoyBtn.SetActive(hider && inRound && !_decoyUsed);
        if (_eyeBtn != null) _eyeBtn.SetActive(hider && inRound);
        if (_spectating && !(hider && inRound)) StopSpectate();

        UpdateViewMode(); // phase changes can flip hunter FPS on/off
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
        match.DoTaunt(self);
    }

    void OnDecoy()
    {
        if (_decoyUsed || self.team != Team.Hider || match == null) return;
        if (match.Phase != MatchPhase.Hide && match.Phase != MatchPhase.Seek) return;
        if (match.SpawnDecoy(self) == null) return;
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
        cam.spectate = hunter;
        self.motor.desiredMove = Vector3.zero;
        if (_eyeLabel != null) _eyeLabel.text = "BACK";
        if (_eyeBg != null) _eyeBg.color = new Color(0.75f, 0.22f, 0.18f, 0.9f);
    }

    void StopSpectate()
    {
        _spectating = false;
        if (cam != null) cam.spectate = null;
        if (_eyeLabel != null) _eyeLabel.text = "EYE";
        if (_eyeBg != null) _eyeBg.color = new Color(0.25f, 0.25f, 0.3f, 0.85f);
    }

    void OnAction()
    {
        if (self.team == Team.Hider)
        {
            if (match != null && match.Phase != MatchPhase.Hide && match.Phase != MatchPhase.Seek) return;
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
            // fire from the camera so the crosshair is exactly the impact point (FPS style)
            var victim = gun.Fire(cam.transform.position, cam.transform.forward, self);
            if (victim != null) match.Convert(victim);
        }
    }

    void BuildControls()
    {
        PaintUI.EnsureEventSystem();
        var canvas = UiKit.MakeCanvas("Controls", 40, transform);
        _controlsRoot = canvas.gameObject;
        Transform root = canvas.transform;

        // Joystick visual (fixed bottom-left; input origin is wherever the touch lands)
        var stickBg = new GameObject("Stick", typeof(Image));
        stickBg.transform.SetParent(root, false);
        var bgImg = stickBg.GetComponent<Image>();
        bgImg.sprite = UiKit.CircleSprite;
        bgImg.color = new Color(1f, 1f, 1f, 0.14f);
        bgImg.raycastTarget = false;
        UiKit.SetRect((RectTransform)stickBg.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(250, 320), new Vector2(320, 320));

        var knob = new GameObject("Knob", typeof(Image));
        knob.transform.SetParent(stickBg.transform, false);
        var knobImg = knob.GetComponent<Image>();
        knobImg.sprite = UiKit.CircleSprite;
        knobImg.color = new Color(1f, 1f, 1f, 0.35f);
        knobImg.raycastTarget = false;
        UiKit.SetRect((RectTransform)knob.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130, 130));
        _stickKnob = (RectTransform)knob.transform;

        // Buttons bottom-right
        var jump = UiKit.MakeButton(root, "JUMP", new Color(0.25f, 0.25f, 0.3f, 0.85f), Color.white, 40, round: true);
        UiKit.SetRect((RectTransform)jump.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-160, 250), new Vector2(210, 210));
        _jumpHold = jump.gameObject.AddComponent<HoldButton>();
        _jumpHold.onDown = () => { self.motor.wantJumpPressed = true; };

        var dash = UiKit.MakeButton(root, "DASH", new Color(0.25f, 0.25f, 0.3f, 0.85f), Color.white, 40, round: true);
        UiKit.SetRect((RectTransform)dash.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-390, 320), new Vector2(180, 180));
        dash.onClick.AddListener(() => { self.motor.wantDash = true; });

        var pose = UiKit.MakeButton(root, "POSE", new Color(0.25f, 0.25f, 0.3f, 0.85f), Color.white, 40, round: true);
        UiKit.SetRect((RectTransform)pose.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-160, 500), new Vector2(180, 180));
        pose.onClick.AddListener(TogglePosePanel);

        // pose picker: a 2-column grid above the POSE button (9 poses)
        _posePanel = new GameObject("PosePanel", typeof(RectTransform));
        _posePanel.transform.SetParent(root, false);
        UiKit.SetRect((RectTransform)_posePanel.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0f), new Vector2(-290, 610), new Vector2(500, 440));
        string[] poseNames = { "STAND", "CROUCH", "STATUE", "LIE", "SCARECROW", "CHAIR", "BALL", "DEAD", "BEND" };
        for (int i = 0; i < poseNames.Length; i++)
        {
            Pose p = (Pose)i;
            var pb = UiKit.MakeButton(_posePanel.transform, poseNames[i], new Color(0.18f, 0.18f, 0.22f, 0.92f), Color.white, 32);
            UiKit.SetRect((RectTransform)pb.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(i % 2 == 0 ? -125f : 125f, (i / 2) * 84f), new Vector2(240, 76));
            pb.onClick.AddListener(() =>
            {
                self.motor.SetPose(p);
                _posePanel.SetActive(false);
            });
        }
        _posePanel.SetActive(false);

        var action = UiKit.MakeButton(root, "PAINT", new Color(0.24f, 0.5f, 0.75f, 0.85f), Color.white, 46, round: true);
        UiKit.SetRect((RectTransform)action.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-400, 560), new Vector2(230, 230));
        action.onClick.AddListener(OnAction);
        _actionRoot = action.gameObject;
        _actionBg = action.GetComponent<Image>();
        _actionLabel = action.GetComponentInChildren<Text>();

        // hider abilities: taunt (style points, makes noise), one-use decoy, hunter-cam
        var tauntB = UiKit.MakeButton(root, "TAUNT", new Color(0.62f, 0.45f, 0.15f, 0.85f), Color.white, 32, round: true);
        UiKit.SetRect((RectTransform)tauntB.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-620, 330), new Vector2(150, 150));
        tauntB.onClick.AddListener(OnTaunt);
        _tauntBtn = tauntB.gameObject;
        _tauntBtn.SetActive(false);

        var decoyB = UiKit.MakeButton(root, "DECOY", new Color(0.4f, 0.3f, 0.55f, 0.85f), Color.white, 30, round: true);
        UiKit.SetRect((RectTransform)decoyB.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-620, 510), new Vector2(150, 150));
        decoyB.onClick.AddListener(OnDecoy);
        _decoyBtn = decoyB.gameObject;
        _decoyBtn.SetActive(false);

        var eyeB = UiKit.MakeButton(root, "EYE", new Color(0.25f, 0.25f, 0.3f, 0.85f), Color.white, 32, round: true);
        UiKit.SetRect((RectTransform)eyeB.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-100, -320), new Vector2(150, 150));
        eyeB.onClick.AddListener(ToggleSpectate);
        _eyeBtn = eyeB.gameObject;
        _eyeBg = eyeB.GetComponent<Image>();
        _eyeLabel = eyeB.GetComponentInChildren<Text>();
        _eyeBtn.SetActive(false);

        // Hunter crosshair
        var cross = UiKit.MakeText(root, "+", 72, TextAnchor.MiddleCenter);
        UiKit.SetRect(cross.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100, 100));
        _crosshair = cross.gameObject;
        _crosshair.SetActive(false);
    }
}
