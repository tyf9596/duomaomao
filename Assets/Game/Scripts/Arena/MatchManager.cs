using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum MatchPhase { Lobby, Travel, Hide, Seek, Result }

/// <summary>
/// One offline match against bots — the AI-first version of the PvP loop:
///   LOBBY  — everyone gathers in the floating lobby room; bots trickle in like a
///            real matchmaking queue, players volunteer to hunt (HUNT? button),
///            then a roulette picks the hunter from the volunteers.
///   TRAVEL — the hiders are teleported down into the map behind a paint-roller
///            loading screen; the hunter STAYS in the lobby.
///   HIDE   — hiders run/dash/climb/pose and paint themselves while the hunter
///            waits upstairs; when time is up the hunter travels in too.
///   SEEK   — the hunter stalks and shoots; every hider hit JOINS the hunter team.
///   RESULT — hunters win if no hider survives the clock, otherwise hiders win.
/// Bootstraps itself into any scene with an ArenaMap; characters, lobby and UI are
/// all built in code, so arena scenes only need geometry with colliders.
/// </summary>
public class MatchManager : MonoBehaviour
{
    [Header("Match rules")]
    public int totalCharacters = 7; // 1 human + bots
    public float hideSeconds = 45f;
    public float seekSeconds = 150f;

    [Header("Lobby pacing")]
    public float joinIntervalMin = 0.4f;
    public float joinIntervalMax = 1.3f;
    public float lobbyCountdownSeconds = 8f;
    public float travelSeconds = 3.2f;
    [Range(0f, 1f)] public float botVolunteerChance = 0.3f;

    public MatchPhase Phase { get; private set; }
    public readonly List<Character> Characters = new List<Character>();

    static readonly Color HunterColor = new Color(0.78f, 0.22f, 0.18f);
    static readonly string[] BotNames =
        { "MOSS", "BRICK", "PIXEL", "SOCKS", "MANGO", "OTTO", "FERN", "LUMEN", "TAIGA", "PLANK", "CINDER", "DOT" };

    Character _player;
    PlayerRig _rig;
    ThirdPersonCamera _cam;
    ArenaMap _map;
    LobbyRoom _lobby;
    readonly HashSet<Character> _volunteers = new HashSet<Character>();
    float _phaseEndsAt;
    bool _hunterEntryStarted;

    // HUD
    Text _title, _timer, _info, _banner;
    GameObject _rosterPanel;
    Text _rosterText;
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
        _lobby = LobbyRoom.Build(_map);
        SpawnPlayer();
        BuildHUD();
        UpdateRoster();
        StartCoroutine(LobbyFlow());
    }

    // ---------------- lobby & travel sequence ----------------

    IEnumerator LobbyFlow()
    {
        SetPhase(MatchPhase.Lobby);

        // matchmaking feel: bots join the room one by one
        while (Characters.Count < totalCharacters)
        {
            yield return new WaitForSeconds(Random.Range(joinIntervalMin, joinIntervalMax));
            SpawnBot();
            _title.text = "WAITING FOR PLAYERS  " + Characters.Count + "/" + totalCharacters;
        }

        _title.text = "ROOM FULL!";
        yield return new WaitForSeconds(0.8f);

        float end = Time.time + lobbyCountdownSeconds;
        int last = -1;
        while (Time.time < end)
        {
            int s = Mathf.CeilToInt(end - Time.time);
            if (s != last) { last = s; _title.text = "MATCH STARTS IN " + s; }
            yield return null;
        }
        _title.text = "";

        // hunter roulette — volunteers first, otherwise anyone
        Character hunter = PickHunter();
        SetAllLocked(true); // everyone freezes for the reveal
        _rosterPanel.SetActive(false); // clear the stage for the banner
        yield return HunterRoulette(hunter);
        MakeHunter(hunter);
        _banner.text = hunter.isPlayer ? "YOU ARE THE HUNTER!" : hunter.displayName + " IS THE HUNTER!";
        yield return new WaitForSeconds(1.5f);
        _banner.text = "";
        _banner.color = Color.white;

        yield return TravelHiders();
    }

    Character PickHunter()
    {
        var pool = new List<Character>();
        foreach (var ch in Characters)
            if (ch != null && _volunteers.Contains(ch)) pool.Add(ch);
        if (pool.Count == 0)
            foreach (var ch in Characters)
                if (ch != null) pool.Add(ch);
        return pool[Random.Range(0, pool.Count)];
    }

    IEnumerator HunterRoulette(Character chosen)
    {
        _banner.color = Color.white;
        float step = 0.055f;
        while (step < 0.45f)
        {
            var any = Characters[Random.Range(0, Characters.Count)];
            _banner.text = any.isPlayer ? "YOU" : any.displayName;
            yield return new WaitForSeconds(step);
            step *= 1.22f;
        }
        _banner.color = new Color(1f, 0.35f, 0.3f);
        _banner.text = chosen.isPlayer ? "YOU" : chosen.displayName;
        yield return new WaitForSeconds(0.6f);
    }

    IEnumerator TravelHiders()
    {
        SetPhase(MatchPhase.Travel);
        bool playerGoes = _player.team == Team.Hider;
        if (playerGoes)
            LoadingScreen.Show(MapDisplayName(), HidersLeft() + " HIDERS DEPLOYING", travelSeconds, false);
        else
            _title.text = "HIDERS ARE DEPLOYING...";

        yield return new WaitForSeconds(0.8f); // overlay is opaque by now
        var used = new List<Vector3>();
        foreach (var ch in Characters)
        {
            if (ch == null || ch.team != Team.Hider) continue;
            Vector3 p = PickSpawn(used);
            used.Add(p);
            Teleport(ch, p);
        }
        yield return new WaitForSeconds(Mathf.Max(0.1f, travelSeconds - 0.8f));
        SetPhase(MatchPhase.Hide);
    }

    IEnumerator HunterEntry()
    {
        SetPhase(MatchPhase.Travel);
        bool playerGoes = _player.team == Team.Hunter;
        if (playerGoes)
        {
            LoadingScreen.Show(MapDisplayName(), "THE HUNT BEGINS", travelSeconds, true);
        }
        else
        {
            _title.text = "HUNTER INCOMING!";
            _banner.color = new Color(1f, 0.35f, 0.3f);
            _banner.text = "GET READY";
        }

        yield return new WaitForSeconds(0.8f);
        foreach (var ch in Characters)
            if (ch != null && ch.team == Team.Hunter) Teleport(ch, _map.RandomPointOnFloor());
        yield return new WaitForSeconds(Mathf.Max(0.1f, travelSeconds - 0.8f));
        _banner.text = "";
        _banner.color = Color.white;
        SetPhase(MatchPhase.Seek);
    }

    static void Teleport(Character ch, Vector3 pos)
    {
        var cc = ch.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        ch.transform.position = pos + Vector3.up * 0.05f;
        if (cc != null) cc.enabled = true;
    }

    string MapDisplayName()
    {
        switch (SceneManager.GetActiveScene().name)
        {
            case "Arena05": return "THE NEIGHBORHOOD";
            case "Arena04": return "TWIN HOUSES";
            case "Arena03": return "THE WAREHOUSE";
            case "Diorama02": return "THE DIORAMA";
            default: return SceneManager.GetActiveScene().name.ToUpper();
        }
    }

    // ---------------- spawning ----------------

    void SpawnPlayer()
    {
        var ch = Character.Create("You", _lobby.SpawnPoint(), true);
        Characters.Add(ch);
        _player = ch;
        _cam.target = ch.transform;
        _rig = ch.gameObject.AddComponent<PlayerRig>();
        _rig.Setup(ch, _cam, this);
    }

    void SpawnBot()
    {
        string name = BotNames[(Characters.Count - 1) % BotNames.Length];
        // drop in from slightly above the floor — a tiny "joined" moment
        var ch = Character.Create(name, _lobby.SpawnPoint() + Vector3.up * 1.4f, false);
        Characters.Add(ch);

        var brain = ch.gameObject.AddComponent<BotBrain>();
        brain.self = ch;
        brain.match = this;
        brain.map = _map;
        brain.lobby = _lobby;

        if (Random.value < botVolunteerChance) _volunteers.Add(ch);
        UpdateRoster();
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

    // ---------------- volunteers ----------------

    public bool IsVolunteer(Character ch) { return _volunteers.Contains(ch); }

    public void ToggleVolunteer(Character ch)
    {
        if (Phase != MatchPhase.Lobby) return;
        if (!_volunteers.Add(ch)) _volunteers.Remove(ch);
        UpdateRoster();
    }

    // ---------------- teams & win logic ----------------

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

    // ---------------- phase machine ----------------

    void Update()
    {
        float remaining = _phaseEndsAt - Time.time;

        switch (Phase)
        {
            case MatchPhase.Hide:
                _timer.text = (_player.team == Team.Hunter ? "SEEK IN  " : "HIDE  ") + Mathf.Max(0, Mathf.CeilToInt(remaining));
                if (remaining <= 0f && !_hunterEntryStarted)
                {
                    _hunterEntryStarted = true;
                    StartCoroutine(HunterEntry());
                }
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
            case MatchPhase.Lobby:
                _title.text = "WAITING FOR PLAYERS...";
                _timer.text = "";
                _info.text = "";
                SetAllLocked(false);
                _resultPanel.SetActive(false);
                _rosterPanel.SetActive(true);
                break;

            case MatchPhase.Travel:
                _timer.text = "";
                SetAllLocked(true);
                break;

            case MatchPhase.Hide:
                _phaseEndsAt = Time.time + hideSeconds;
                _title.text = _player.team == Team.Hunter ? "THE HIDERS ARE HIDING" : "PAINT AND HIDE!";
                SetAllLocked(false); // the hunter roams the lobby, hiders roam the map
                _rosterPanel.SetActive(false);
                UpdateInfo();
                break;

            case MatchPhase.Seek:
                _phaseEndsAt = Time.time + seekSeconds;
                _title.text = _player.team == Team.Hunter ? "FIND THEM ALL!" : "DON'T GET FOUND!";
                SetAllLocked(false);
                break;

            case MatchPhase.Result:
                SetAllLocked(true);
                _rosterPanel.SetActive(false);
                _resultPanel.SetActive(true);
                _title.text = "";
                _timer.text = "";
                break;
        }

        if (_rig != null) _rig.RefreshContextButton();
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

    void UpdateRoster()
    {
        if (_rosterText == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append("PLAYERS  ").Append(Characters.Count).Append("/").Append(totalCharacters).Append("\n");
        foreach (var ch in Characters)
        {
            if (ch == null) continue;
            sb.Append("\n").Append(ch.isPlayer ? "> YOU" : "  " + ch.displayName);
            if (_volunteers.Contains(ch)) sb.Append("   [H]");
        }
        sb.Append("\n\n[H] = wants to hunt");
        _rosterText.text = sb.ToString();
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

        // big centre banner for the hunter roulette / reveal moments
        _banner = UiKit.MakeText(root, "", 84, TextAnchor.MiddleCenter);
        UiKit.SetRect(_banner.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 220), new Vector2(-40, 160));

        // lobby roster (top-right)
        _rosterPanel = new GameObject("Roster", typeof(Image));
        _rosterPanel.transform.SetParent(root, false);
        _rosterPanel.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.55f);
        UiKit.SetRect((RectTransform)_rosterPanel.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-24, -250), new Vector2(400, 160 + totalCharacters * 44));
        _rosterText = UiKit.MakeText(_rosterPanel.transform, "", 34, TextAnchor.UpperLeft);
        UiKit.SetRect(_rosterText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(6, -12), new Vector2(-48, -48));
        _rosterPanel.SetActive(false);

        // result overlay
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
