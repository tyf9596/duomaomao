using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum MatchPhase { Intro, Hide, Seek, Result }

/// <summary>
/// One offline match against bots — the AI-first version of the PvP loop:
///   INTRO  — roles are dealt (one random hunter), a banner tells you yours.
///   HIDE   — hiders run/dash/climb/pose and paint themselves; the hunter is blindfolded
///            (bots idle, a human hunter gets a curtain with a countdown).
///   SEEK   — the hunter stalks and shoots; every hider hit JOINS the hunter team.
///   RESULT — hunters win if no hider survives the clock, otherwise hiders win.
/// Bootstraps itself into any scene with an ArenaMap; characters are built in code,
/// so arena scenes only need geometry with colliders.
/// </summary>
public class MatchManager : MonoBehaviour
{
    [Header("Match rules")]
    public int totalCharacters = 7; // 1 human + bots
    public float introSeconds = 2.5f;
    public float hideSeconds = 45f;
    public float seekSeconds = 150f;

    public MatchPhase Phase { get; private set; }
    public readonly List<Character> Characters = new List<Character>();

    static readonly Color HunterColor = new Color(0.78f, 0.22f, 0.18f);

    Character _player;
    PlayerRig _rig;
    ThirdPersonCamera _cam;
    ArenaMap _map;
    float _phaseEndsAt;

    // HUD
    Text _title, _timer, _info;
    GameObject _curtain;
    Text _curtainText;
    GameObject _resultPanel;
    Text _resultText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        TrySpawn();
        SceneManager.sceneLoaded -= OnSceneLoaded; // guard double-hook across reloads
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { TrySpawn(); }

    static void TrySpawn()
    {
        if (FindFirstObjectByType<ArenaMap>() == null) return;
        if (FindFirstObjectByType<MatchManager>() != null) return;
        new GameObject("MatchManager").AddComponent<MatchManager>();
    }

    void Start()
    {
        _map = FindFirstObjectByType<ArenaMap>();
        if (_map != null)
        {
            if (_map.characterCountOverride > 0) totalCharacters = _map.characterCountOverride;
            if (_map.hideSecondsOverride > 0f) hideSeconds = _map.hideSecondsOverride;
            if (_map.seekSecondsOverride > 0f) seekSeconds = _map.seekSecondsOverride;
        }

        var mainCam = Camera.main;
        var orbit = mainCam != null ? mainCam.GetComponent<OrbitCamera>() : null;
        if (orbit != null) orbit.enabled = false;
        _cam = mainCam.gameObject.AddComponent<ThirdPersonCamera>();

        PaintUI.EnsureEventSystem();
        SpawnCharacters();
        BuildHUD();
        SetPhase(MatchPhase.Intro);
    }

    void SpawnCharacters()
    {
        int hunterIndex = Random.Range(0, totalCharacters);
        var used = new List<Vector3>();

        for (int i = 0; i < totalCharacters; i++)
        {
            Vector3 pos = PickSpawn(used);
            used.Add(pos);
            bool isPlayer = i == 0;
            var ch = Character.Create(isPlayer ? "You" : "Bot " + i, pos, isPlayer);
            Characters.Add(ch);

            if (isPlayer)
            {
                _player = ch;
                _cam.target = ch.transform;
                _rig = ch.gameObject.AddComponent<PlayerRig>();
                _rig.Setup(ch, _cam, this);
            }
            else
            {
                var brain = ch.gameObject.AddComponent<BotBrain>();
                brain.self = ch;
                brain.match = this;
                brain.map = _map;
            }

            if (i == hunterIndex) MakeHunter(ch);
        }
    }

    Vector3 PickSpawn(List<Vector3> used)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            Vector3 p = _map.RandomPointOnFloor();
            bool clear = true;
            foreach (var u in used)
                if (Vector3.Distance(u, p) < 1.2f) { clear = false; break; }
            if (clear) return p;
        }
        return _map.RandomPointOnFloor();
    }

    void MakeHunter(Character ch)
    {
        ch.team = Team.Hunter;
        ch.skin.Fill(HunterColor);
        ch.motor.SetPose(Pose.Stand);
        ch.motor.SetAiming(true); // holding-both stance

        var gun = ch.GetComponent<Shotgun>();
        if (gun == null) gun = ch.gameObject.AddComponent<Shotgun>();
        AddGunVisual(ch);

        var brain = ch.GetComponent<BotBrain>();
        if (brain != null) brain.gun = gun;
        if (ch.isPlayer && _rig != null) _rig.SetTeam(Team.Hunter);
    }

    static void AddGunVisual(Character ch)
    {
        if (ch.motor.body == null) return;
        foreach (var t in ch.motor.body.GetComponentsInChildren<Transform>())
            if (t.name == "Gun") return;

        // stick the barrel to the right arm when the blocky rig is present
        Transform hand = null;
        foreach (var t in ch.motor.body.GetComponentsInChildren<Transform>())
            if (t.name == "arm-right") { hand = t; break; }

        // simple shotgun: barrel + stock built along the arm's own axis, so when the
        // holding-both animation raises the arm the gun points where the arm points
        var gun = new GameObject("Gun");
        var dark = new Color(0.15f, 0.13f, 0.12f);
        var wood = new Color(0.42f, 0.28f, 0.16f);
        System.Action<PrimitiveType, Vector3, Vector3, Color> part = (type, lp, ls, col) =>
        {
            var p = GameObject.CreatePrimitive(type);
            p.name = "GunPart";
            Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(gun.transform, false);
            p.transform.localPosition = lp;
            p.transform.localScale = ls;
            p.GetComponent<Renderer>().material.SetColor("_BaseColor", col);
        };
        if (hand != null)
        {
            gun.transform.SetParent(hand, false);
            gun.transform.localPosition = Vector3.zero;
            gun.transform.localRotation = Quaternion.identity;
            part(PrimitiveType.Cylinder, new Vector3(0f, -1.15f, 0.05f), new Vector3(0.15f, 0.55f, 0.15f), dark);
            part(PrimitiveType.Cube, new Vector3(0f, -0.55f, 0.05f), new Vector3(0.18f, 0.5f, 0.22f), wood);
        }
        else
        {
            gun.transform.SetParent(ch.motor.body, false);
            gun.transform.localPosition = new Vector3(0.5f, 0.25f, 0.45f);
            gun.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.14f, 0.45f, 0.14f), dark);
        }
    }

    /// <summary>A shot hider joins the hunters (the infection rule).</summary>
    public void Convert(Character victim)
    {
        if (victim == null || victim.team == Team.Hunter) return;
        MakeHunter(victim);
        UpdateInfo();

        if (HidersLeft() == 0)
        {
            EndMatch(_player.team == Team.Hunter ? "ALL HIDERS FOUND — HUNTERS WIN!" : "YOU WERE THE LAST ONE — HUNTERS WIN!");
        }
    }

    int HidersLeft()
    {
        int n = 0;
        foreach (var ch in Characters)
            if (ch != null && ch.team == Team.Hider) n++;
        return n;
    }

    void Update()
    {
        float remaining = _phaseEndsAt - Time.time;

        switch (Phase)
        {
            case MatchPhase.Intro:
                if (remaining <= 0f) SetPhase(MatchPhase.Hide);
                break;

            case MatchPhase.Hide:
                _timer.text = "HIDE  " + Mathf.Max(0, Mathf.CeilToInt(remaining));
                if (_curtain.activeSelf)
                    _curtainText.text = "HIDERS ARE HIDING...\n\n" + Mathf.Max(0, Mathf.CeilToInt(remaining));
                if (remaining <= 0f) SetPhase(MatchPhase.Seek);
                break;

            case MatchPhase.Seek:
                _timer.text = "SEEK  " + Mathf.Max(0, Mathf.CeilToInt(remaining));
                if (remaining <= 0f)
                    EndMatch(_player.team == Team.Hider ? "TIME'S UP — YOU SURVIVED!" : "TIME'S UP — HIDERS WIN!");
                break;
        }
    }

    public void SetPhase(MatchPhase phase)
    {
        Phase = phase;

        switch (phase)
        {
            case MatchPhase.Intro:
                _phaseEndsAt = Time.time + introSeconds;
                _title.text = _player.team == Team.Hunter ? "YOU ARE THE HUNTER!" : "YOU ARE A HIDER!";
                _timer.text = "";
                SetAllLocked(true);
                _curtain.SetActive(false);
                _resultPanel.SetActive(false);
                UpdateInfo();
                break;

            case MatchPhase.Hide:
                _phaseEndsAt = Time.time + hideSeconds;
                _title.text = _player.team == Team.Hunter ? "WAIT FOR THE HIDERS" : "PAINT & HIDE!";
                foreach (var ch in Characters)
                    if (ch != null) ch.motor.movementLocked = ch.team == Team.Hunter;
                _curtain.SetActive(_player.team == Team.Hunter);
                break;

            case MatchPhase.Seek:
                _phaseEndsAt = Time.time + seekSeconds;
                _title.text = _player.team == Team.Hunter ? "FIND THEM ALL!" : "DON'T GET FOUND!";
                SetAllLocked(false);
                _curtain.SetActive(false);
                break;

            case MatchPhase.Result:
                SetAllLocked(true);
                _curtain.SetActive(false);
                _resultPanel.SetActive(true);
                _title.text = "";
                _timer.text = "";
                break;
        }
    }

    void EndMatch(string message)
    {
        SetPhase(MatchPhase.Result);
        _resultText.text = message;
    }

    void SetAllLocked(bool locked)
    {
        foreach (var ch in Characters)
            if (ch != null) ch.motor.movementLocked = locked;
    }

    void UpdateInfo()
    {
        if (_info != null) _info.text = "HIDERS LEFT  " + HidersLeft();
    }

    // ---------------- HUD ----------------

    void BuildHUD()
    {
        var canvas = UiKit.MakeCanvas("MatchHUD", 60, transform);
        Transform root = canvas.transform;

        _title = UiKit.MakeText(root, "", 56, TextAnchor.MiddleCenter);
        UiKit.SetRect(_title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(-40, 90));

        _timer = UiKit.MakeText(root, "", 48, TextAnchor.MiddleCenter);
        UiKit.SetRect(_timer.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -130), new Vector2(-40, 70));

        _info = UiKit.MakeText(root, "", 40, TextAnchor.MiddleCenter);
        UiKit.SetRect(_info.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -195), new Vector2(-40, 60));

        // Curtain shown to a human hunter while hiders hide
        _curtain = new GameObject("Curtain", typeof(Image));
        _curtain.transform.SetParent(root, false);
        _curtain.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.97f);
        UiKit.SetRect((RectTransform)_curtain.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _curtainText = UiKit.MakeText(_curtain.transform, "", 64, TextAnchor.MiddleCenter);
        UiKit.SetRect(_curtainText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _curtain.SetActive(false);

        // Result overlay
        _resultPanel = new GameObject("Result", typeof(RectTransform));
        _resultPanel.transform.SetParent(root, false);
        UiKit.SetRect((RectTransform)_resultPanel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _resultText = UiKit.MakeText(_resultPanel.transform, "", 62, TextAnchor.MiddleCenter);
        UiKit.SetRect(_resultText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -260), new Vector2(-60, 180));
        var again = UiKit.MakeButton(_resultPanel.transform, "PLAY AGAIN", new Color(0.20f, 0.40f, 0.75f), Color.white, 50);
        UiKit.SetRect((RectTransform)again.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 110), new Vector2(520, 140));
        again.onClick.AddListener(() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex));
        _resultPanel.SetActive(false);
    }
}
