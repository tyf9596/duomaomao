using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
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

    public static MatchManager Instance { get; private set; }

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
    float _nextVolCheckAt;

    // style scoring (original-game rule: points for time spent inside the hunter's
    // line of sight — the closer the richer; taunts add risk-bonus on top)
    readonly Dictionary<Character, float> _score = new Dictionary<Character, float>();
    float _nextLosTickAt;
    float _lastGainAt;
    static readonly RaycastHit[] LosBuf = new RaycastHit[24];

    // HUD (INK & PAINT redesign 2026-07-21; layout values from design canvas 1a)
    Text _title, _timer, _info, _banner, _scoreText;
    GameObject _titleCard;
    Text _titleBig;                     // gold countdown number line (lobby)
    GameObject _titleRainbow;           // rainbow underline (hider mood)
    Image _titleWarnStrip;              // red warning underline (hunter mood)
    GameObject _hudRow;                 // timer pill + info chip row
    Image _timerPill;
    Text _timerLabel;
    GameObject _infoChip;
    GameObject _scoreBadge;
    Image _scoreGlow;
    GameObject _rosterPanel;
    RectTransform _rosterRows;
    Text _rosterCount;
    GameObject _revealRoot;             // hunter reveal plate (dim + stripes)
    Text _revealName, _revealFoot;
    GameObject _warnRoot;               // HUNTER INCOMING! plate
    GameObject _resultPanel;
    Text _resultText;                   // poster headline
    Image _resultBadge;
    Text _resultBadgeText;
    Text _resultScoreValue;
    RectTransform _resultRows;
    RectTransform _resultTitleCard, _resultScoreCard, _resultAgain;
    readonly List<RectTransform> _resultConfetti = new List<RectTransform>();
    Image _hudFlash;                    // roulette freeze-frame white flash
    Image _countVignette;               // last-3-seconds ink vignette
    Image _bannerPill;                  // backdrop behind FlashBanner text
    int _rosterLastCount;

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

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
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
            SetTitle("WAITING FOR PLAYERS  " + Characters.Count + "/" + totalCharacters, null);
        }

        SetTitle("ROOM FULL!", null);
        yield return new WaitForSeconds(0.8f);

        float end = Time.time + lobbyCountdownSeconds;
        int last = -1;
        while (Time.time < end)
        {
            int s = Mathf.CeilToInt(end - Time.time);
            if (s != last) { last = s; SetTitle("MATCH STARTS IN", s.ToString()); }
            yield return null;
        }
        SetTitle("", null);

        // hunter roulette — volunteers first, otherwise anyone
        Character hunter = PickHunter();
        SetAllLocked(true); // everyone freezes for the reveal
        _rosterPanel.SetActive(false); // clear the stage for the reveal plate
        yield return HunterRoulette(hunter);
        MakeHunter(hunter);
        yield return new WaitForSeconds(1.5f);
        yield return UiKit.Fade(UiKit.EnsureGroup(_revealRoot), 0f, 0.2f, deactivateAtZero: true);
        UiKit.EnsureGroup(_revealRoot).alpha = 1f; // reset for the next match

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

    /// <summary>Design 02: names flip inside the full-width INK plate, then the pick
    /// freezes big and red with the GO GET THEM! beat.</summary>
    IEnumerator HunterRoulette(Character chosen)
    {
        _revealRoot.SetActive(true);
        _revealName.color = new Color(1f, 1f, 1f, 0.85f);
        _revealName.transform.localScale = Vector3.one;
        _revealFoot.text = "";
        float step = 0.055f;
        while (step < 0.45f)
        {
            var any = Characters[Random.Range(0, Characters.Count)];
            _revealName.text = any.isPlayer ? "YOU" : any.displayName;
            yield return new WaitForSeconds(step);
            step *= 1.22f;
        }
        _revealName.color = UiKit.HunterRedBright;
        _revealName.text = chosen.isPlayer ? "YOU" : chosen.displayName;
        _revealFoot.text = chosen.isPlayer ? "GO GET THEM!" : "STAY OUT OF SIGHT!";
        // freeze-frame beat (spec 5): scale 1.35 -> 1 + white flash 120ms + plate shake
        if (_hudFlash != null) StartCoroutine(HudFlash(0.5f, 0.12f));
        var plate = _revealName.transform.parent as RectTransform;
        Vector2 plateBase = plate != null ? plate.anchoredPosition : Vector2.zero;
        float t = 0f;
        while (t < 0.26f)
        {
            t += Time.deltaTime;
            float s = Mathf.Lerp(1.35f, 1f, Mathf.SmoothStep(0f, 1f, t / 0.26f));
            _revealName.transform.localScale = new Vector3(s, s, 1f);
            if (plate != null)
                plate.anchoredPosition = plateBase + new Vector2(
                    Mathf.Sin(t * 90f) * 4f * (1f - t / 0.26f), 0f);
            yield return null;
        }
        _revealName.transform.localScale = Vector3.one;
        if (plate != null) plate.anchoredPosition = plateBase;
        yield return new WaitForSeconds(0.34f);
    }

    IEnumerator HudFlash(float peak, float dur)
    {
        _hudFlash.gameObject.SetActive(true);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            _hudFlash.color = new Color(1f, 1f, 1f, peak * (1f - t / dur));
            yield return null;
        }
        _hudFlash.gameObject.SetActive(false);
    }

    IEnumerator TravelHiders()
    {
        SetPhase(MatchPhase.Travel);
        bool playerGoes = _player.team == Team.Hider;
        if (playerGoes)
            LoadingScreen.Show(MapDisplayName(), HidersLeft() + " HIDERS DEPLOYING", travelSeconds, false);
        else
            SetTitle("HIDERS ARE DEPLOYING...", null);

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
            _warnRoot.SetActive(true); // HUNTER INCOMING! plate (design 10)
            var warnPlate = _warnRoot.transform.Find("Plate") as RectTransform;
            if (warnPlate != null) StartCoroutine(UiKit.PopIn(warnPlate, 0.92f, 0.24f));
        }

        yield return new WaitForSeconds(0.8f);
        foreach (var ch in Characters)
            if (ch != null && ch.team == Team.Hunter) Teleport(ch, _map.RandomPointOnFloor());
        yield return new WaitForSeconds(Mathf.Max(0.1f, travelSeconds - 0.8f));
        _warnRoot.SetActive(false);
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
            case "Arena06": return "THE MANSION";
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
        ch.ApplySize(Character.HiderScale); // everyone starts hider-sized; MakeHunter grows them
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
        ch.ApplySize(Character.HiderScale);
        Characters.Add(ch);

        var brain = ch.gameObject.AddComponent<BotBrain>();
        brain.self = ch;
        brain.match = this;
        brain.map = _map;
        brain.lobby = _lobby;
        brain.lobbyVolunteer = Random.value < botVolunteerChance; // walks onto the pad

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

    // ---------------- volunteers (Roblox-style: stand on the red pad) ----------------

    public bool IsVolunteer(Character ch) { return _volunteers.Contains(ch); }

    void RefreshVolunteersFromPlatform()
    {
        if (_lobby == null) return;
        bool changed = false;
        foreach (var ch in Characters)
        {
            if (ch == null) continue;
            bool on = _lobby.OnPlatform(ch.transform.position);
            if (on != _volunteers.Contains(ch))
            {
                if (on) _volunteers.Add(ch);
                else _volunteers.Remove(ch);
                changed = true;
            }
        }
        if (changed) UpdateRoster();
    }

    // ---------------- teams & win logic ----------------

    void MakeHunter(Character ch)
    {
        ch.team = Team.Hunter;
        ch.skin.Fill(HunterColor);
        ch.motor.SetPose(Pose.Stand);
        ch.ApplySize(Character.HunterScale); // infected hiders GROW into hunters
        ch.motor.SetAiming(true); // holding-both stance

        var gun = ch.GetComponent<Shotgun>();
        if (gun == null) gun = ch.gameObject.AddComponent<Shotgun>();
        AddGunVisual(ch);

        var brain = ch.GetComponent<BotBrain>();
        if (brain != null) brain.gun = gun;
        if (ch.isPlayer && _rig != null) _rig.SetTeam(Team.Hunter);
    }

    public static void AddGunVisual(Character ch)
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

    /// <summary>A shot hider joins the hunters (the infection rule). Decoys just pop.</summary>
    public void Convert(Character victim)
    {
        if (victim == null || victim.team == Team.Hunter) return;

        if (victim.isDecoy)
        {
            Characters.Remove(victim);
            StartCoroutine(DecoyPop(victim));
            if (_player.team == Team.Hunter) StartCoroutine(FlashBanner("A DECOY!", 1.2f));
            return;
        }

        MakeHunter(victim);
        UpdateInfo();
        // a converted player switches sides mid-round: flip the banner mood too
        if (victim.isPlayer && Phase == MatchPhase.Seek)
        {
            SetTitle("FIND THEM ALL!", null, hunterMood: true);
            if (_scoreBadge != null) _scoreBadge.SetActive(false);
        }

        if (HidersLeft() == 0)
        {
            EndMatch(_player.team == Team.Hunter ? "ALL HIDERS\nFOUND!" : "YOU WERE\nTHE LAST ONE!", true);
        }
    }

    IEnumerator DecoyPop(Character decoy)
    {
        if (decoy != null) decoy.motor.SetPose(Pose.Dead); // crumples like a dropped prop
        yield return new WaitForSeconds(1.6f);
        if (decoy != null) Destroy(decoy.gameObject);
    }

    IEnumerator FlashBanner(string text, float seconds)
    {
        _banner.color = Color.white;
        _banner.text = text;
        if (_bannerPill != null)
        {
            _bannerPill.rectTransform.sizeDelta = new Vector2(_banner.preferredWidth + 110f, 140f);
            _bannerPill.gameObject.SetActive(true);
            StartCoroutine(UiKit.PopIn(_bannerPill.rectTransform, 0.9f, 0.18f));
        }
        yield return new WaitForSeconds(seconds);
        if (_banner.text == text)
        {
            _banner.text = "";
            if (_bannerPill != null) _bannerPill.gameObject.SetActive(false);
        }
    }

    /// <summary>One-use hider ability: leave a painted, posed copy of yourself behind.</summary>
    public Character SpawnDecoy(Character owner)
    {
        if (owner == null || owner.team != Team.Hider) return null;
        Vector3 pos = owner.transform.position - owner.transform.forward * 0.6f;
        var d = Character.Create(owner.displayName + " decoy", pos, false, owner.variant);
        d.isDecoy = true;
        d.ApplySize(owner.bodyScale); // clone matches the owner's size
        d.transform.rotation = owner.transform.rotation;
        d.skin.CopySkinFrom(owner.skin);
        // a standing decoy would sway — freeze it in a scenery pose instead
        d.motor.SetPose(owner.motor.CurrentPose == Pose.Stand ? Pose.Statue : owner.motor.CurrentPose);
        d.motor.movementLocked = true;
        Characters.Add(d); // bots scan the list, so decoys draw real suspicion
        return d;
    }

    int HidersLeft()
    {
        int n = 0;
        foreach (var ch in Characters)
            if (ch != null && ch.team == Team.Hider && !ch.isDecoy) n++;
        return n;
    }

    // ---------------- phase machine ----------------

    void Update()
    {
        float remaining = _phaseEndsAt - Time.time;

        switch (Phase)
        {
            case MatchPhase.Lobby:
                if (Time.time >= _nextVolCheckAt)
                {
                    _nextVolCheckAt = Time.time + 0.25f;
                    RefreshVolunteersFromPlatform();
                }
                break;

            case MatchPhase.Hide:
                SetTimer(_player.team == Team.Hunter ? "SEEK IN" : "HIDE", remaining, remaining <= 10f);
                if (remaining <= 0f && !_hunterEntryStarted)
                {
                    _hunterEntryStarted = true;
                    StartCoroutine(HunterEntry());
                }
                break;

            case MatchPhase.Seek:
                SetTimer("SEEK", remaining, true); // seek always runs on the red pill
                if (Time.time >= _nextLosTickAt)
                {
                    _nextLosTickAt = Time.time + 0.5f;
                    LosScoreTick();
                }
                if (remaining <= 0f)
                    EndMatch(_player.team == Team.Hider ? "TIME'S UP -\nYOU SURVIVED!" : "TIME'S UP -\nHIDERS WIN!", false);
                break;
        }

        // STYLE badge feedback: value pops + gold glow decays after each gain
        if (_scoreText != null)
        {
            float gainT = Time.time - _lastGainAt;
            float pop = gainT < 0.24f ? 1f + 0.18f * Mathf.Sin(gainT / 0.24f * Mathf.PI) : 1f;
            _scoreText.transform.localScale = new Vector3(pop, pop, 1f);
            if (_scoreGlow != null)
            {
                var g = UiKit.Gold;
                _scoreGlow.color = new Color(g.r, g.g, g.b, Mathf.Clamp01(1f - gainT / 0.45f) * 0.55f);
            }
        }
    }

    static string FmtTime(float remaining)
    {
        int s = Mathf.Max(0, Mathf.CeilToInt(remaining));
        return (s / 60) + ":" + (s % 60).ToString("00");
    }

    /// <summary>Timer pill (design: INK pill; SEEK and the last 10s run HUNTER-RED
    /// with a heartbeat pulse on each second; final 3s add a soft INK vignette).</summary>
    void SetTimer(string label, float remaining, bool red)
    {
        if (_timer == null) return;
        if (_hudRow != null) _hudRow.SetActive(true);
        _timerLabel.text = label;
        _timer.text = FmtTime(remaining);
        if (_timerPill != null)
            _timerPill.color = red ? UiKit.HunterRed : new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.80f);
        _timerLabel.color = red ? UiKit.Hex("FFD9D1") : UiKit.TextDim;
        // heartbeat in the final stretch: 1 -> 1.18 -> 1 on each second boundary
        float frac = remaining - Mathf.Floor(remaining);
        float pulse = red && remaining <= 10.5f ? 1f + 0.18f * Mathf.Clamp01(frac) * Mathf.Clamp01((1f - frac) * 4f) : 1f;
        _timerPill.transform.localScale = new Vector3(pulse, pulse, 1f);
        // spec 5: the last 3 seconds squeeze the screen with an 8% ink vignette
        if (_countVignette != null)
        {
            bool on = red && remaining <= 3.5f;
            var c = _countVignette.color;
            float a = Mathf.MoveTowards(c.a, on ? 0.08f : 0f, Time.deltaTime * 0.5f);
            _countVignette.color = new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, a);
            _countVignette.gameObject.SetActive(a > 0.001f);
        }
    }

    public void SetPhase(MatchPhase phase)
    {
        Phase = phase;

        bool playerHunts = _player != null && _player.team == Team.Hunter;
        switch (phase)
        {
            case MatchPhase.Lobby:
                SetTitle("WAITING FOR PLAYERS...", null);
                HideTimer();
                _info.text = "";
                if (_infoChip != null) _infoChip.SetActive(false);
                SetAllLocked(false);
                _resultPanel.SetActive(false);
                _rosterPanel.SetActive(true);
                if (_rig != null) _rig.SetControlsVisible(true);
                break;

            case MatchPhase.Travel:
                HideTimer();
                SetAllLocked(true);
                break;

            case MatchPhase.Hide:
                _phaseEndsAt = Time.time + hideSeconds;
                SetTitle(playerHunts ? "THE HIDERS ARE HIDING" : "PAINT AND HIDE!", null, hunterMood: playerHunts);
                SetAllLocked(false); // the hunter roams the lobby, hiders roam the map
                _rosterPanel.SetActive(false);
                UpdateInfo();
                break;

            case MatchPhase.Seek:
                _phaseEndsAt = Time.time + seekSeconds;
                SetTitle(playerHunts ? "FIND THEM ALL!" : "DON'T GET FOUND!", null, hunterMood: playerHunts);
                SetAllLocked(false);
                break;

            case MatchPhase.Result:
                SetAllLocked(true);
                _rosterPanel.SetActive(false);
                _resultPanel.SetActive(true);
                SetTitle("", null);
                HideTimer();
                if (_infoChip != null) _infoChip.SetActive(false);
                // fix #11: the poster owns the screen — no controls underneath
                if (_rig != null) _rig.SetControlsVisible(false);
                break;
        }

        if (_scoreBadge != null)
            _scoreBadge.SetActive(
                (phase == MatchPhase.Hide || phase == MatchPhase.Seek)
                && _player != null && _player.team == Team.Hider);

        if (_rig != null) _rig.RefreshContextButton();
    }

    void EndMatch(string headline, bool huntersWin)
    {
        SetPhase(MatchPhase.Result);
        _info.text = "";

        float pv;
        _score.TryGetValue(_player, out pv);

        var top = new List<KeyValuePair<Character, float>>();
        foreach (var kv in _score)
            if (kv.Key != null && !kv.Key.isDecoy && kv.Value >= 1f) top.Add(kv);
        top.Sort((a, b) => b.Value.CompareTo(a.Value));

        FillResultPoster(headline, huntersWin, Mathf.FloorToInt(pv), top);
    }

    /// <summary>resultB (design 1b): full-screen cream poster — badge + 2-line headline
    /// on a white INK-bordered card, gold score card, medal leaderboard, PLAY AGAIN.</summary>
    void FillResultPoster(string headline, bool huntersWin, int playerScore, List<KeyValuePair<Character, float>> top)
    {
        if (_resultBadgeText != null)
        {
            _resultBadgeText.text = huntersWin ? "HUNTERS WIN" : "HIDERS WIN";
            _resultBadge.color = huntersWin ? UiKit.HunterRed : UiKit.Green;
        }
        if (_resultText != null) _resultText.text = headline;
        if (_resultScoreValue != null) _resultScoreValue.text = playerScore.ToString();

        if (_resultRows == null) return;
        foreach (Transform c in _resultRows) Destroy(c.gameObject);
        Color[] medal = { UiKit.Gold, UiKit.Hex("C9CDD4"), UiKit.Hex("D8935C") };
        int rows = top == null ? 0 : Mathf.Min(3, top.Count);
        for (int i = 0; i < rows; i++)
        {
            var row = MakeInkCard(_resultRows, Color.white, 6f, "Row" + i);
            UiKit.SetRect((RectTransform)row.transform.parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -64f - i * 104f), new Vector2(820f, 90f));
            var med = UiKit.MakeImage(row.transform, UiKit.Shape("btn-circle-base"), medal[i], "Medal");
            UiKit.SetRect(med.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(56f, 0f), new Vector2(56f, 56f));
            var medN = UiKit.MakeText(med.transform, (i + 1).ToString(), 30, TextAnchor.MiddleCenter, false);
            medN.color = UiKit.Ink;
            UiKit.SetRect(medN.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), Vector2.zero);
            var name = UiKit.MakeText(row.transform, top[i].Key.isPlayer ? "YOU" : top[i].Key.displayName, i == 0 ? 40 : 36, TextAnchor.MiddleLeft, false);
            name.color = UiKit.Ink;
            UiKit.SetRect(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(104f, 0f), new Vector2(-160f, 0f));
            var score = UiKit.MakeText(row.transform, Mathf.FloorToInt(top[i].Value).ToString(), i == 0 ? 44 : 38, TextAnchor.MiddleRight, false);
            score.color = i == 0 ? UiKit.GoldEdge : UiKit.Hex("55524A");
            UiKit.SetRect(score.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-34f, 0f), new Vector2(-68f, 0f));
        }

        if (_resultPanel != null && _resultPanel.activeSelf)
            StartCoroutine(ResultEntrance(huntersWin));
    }

    // ---------------- style scoring ----------------

    void AddScore(Character ch, float pts)
    {
        if (ch == null || ch.isDecoy) return;
        float v;
        _score.TryGetValue(ch, out v);
        _score[ch] = v + pts;
        if (ch.isPlayer)
        {
            int before = Mathf.FloorToInt(v);
            int after = Mathf.FloorToInt(v + pts);
            _lastGainAt = Time.time;
            UpdateScoreHud();
            if (after > before && _scoreBadge != null && _scoreBadge.activeInHierarchy)
                StartCoroutine(ScoreFloat("+" + (after - before)));
        }
    }

    /// <summary>Design 08: "+N" floats up from the STYLE badge and fades (650ms).</summary>
    IEnumerator ScoreFloat(string text)
    {
        var t = UiKit.MakeText(_scoreBadge.transform, text, 48, TextAnchor.MiddleCenter);
        t.color = UiKit.Gold;
        UiKit.SetRect(t.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-10f, 10f), new Vector2(160f, 60f));
        float e = 0f;
        while (e < 0.65f && t != null)
        {
            e += Time.deltaTime;
            float k = e / 0.65f;
            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            t.rectTransform.anchoredPosition = new Vector2(-10f, 10f + 90f * ease);
            t.color = new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 1f - k);
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }

    void UpdateScoreHud()
    {
        if (_scoreText == null) return;
        float v;
        _score.TryGetValue(_player, out v);
        _scoreText.text = Mathf.FloorToInt(v).ToString();
    }

    /// <summary>The original's signature rule: surviving inside the hunter's view pays,
    /// and the closer you dare to sit, the faster it pays.</summary>
    void LosScoreTick()
    {
        foreach (var hider in Characters)
        {
            if (hider == null || hider.team != Team.Hider || hider.isDecoy) continue;
            float best = 0f;
            foreach (var h in Characters)
            {
                if (h == null || h.team != Team.Hunter) continue;
                Vector3 to = hider.EyePos - h.EyePos;
                float dist = to.magnitude;
                if (dist > 14f) continue;
                if (Vector3.Angle(h.transform.forward, to) > 70f) continue;
                if (!HasLine(h, hider)) continue;
                best = Mathf.Max(best, Mathf.Lerp(2.4f, 0.4f, dist / 14f));
            }
            if (best > 0f) AddScore(hider, best * 0.5f); // per half-second tick
        }
    }

    bool HasLine(Character from, Character to)
    {
        Vector3 a = from.EyePos, b = to.EyePos;
        int n = Physics.RaycastNonAlloc(new Ray(a, (b - a).normalized), LosBuf, Vector3.Distance(a, b) + 0.5f);
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (Character.FromCollider(LosBuf[i].collider) == from) continue;
            if (best < 0 || LosBuf[i].distance < LosBuf[best].distance) best = i;
        }
        if (best < 0) return false;
        return Character.FromCollider(LosBuf[best].collider) == to;
    }

    /// <summary>Taunt: emote + noise. Points scale with how close the hunter is; every
    /// bot hunter in earshot gets suspicious of you (the original's risk/reward whistle).</summary>
    public void DoTaunt(Character ch)
    {
        if (ch == null || ch.team != Team.Hider || ch.isDecoy) return;
        if (Phase != MatchPhase.Seek && Phase != MatchPhase.Hide) return;
        if (!ch.motor.Taunt()) return; // on cooldown

        StartCoroutine(TauntMarker(ch));
        if (Phase != MatchPhase.Seek) return; // no hunter around yet — just the emote

        float nearest = float.MaxValue;
        foreach (var h in Characters)
            if (h != null && h.team == Team.Hunter)
                nearest = Mathf.Min(nearest, Vector3.Distance(h.transform.position, ch.transform.position));

        AddScore(ch, nearest < 16f ? 5f + Mathf.Max(0f, 16f - nearest) : 2f);

        foreach (var h in Characters)
        {
            if (h == null || h.team != Team.Hunter) continue;
            var brain = h.GetComponent<BotBrain>();
            if (brain == null) continue;
            float d = Vector3.Distance(h.transform.position, ch.transform.position);
            if (d < 18f) brain.HearTaunt(ch, Mathf.Lerp(0.9f, 0.25f, d / 18f));
        }
    }

    IEnumerator TauntMarker(Character ch)
    {
        // a bold "!" pops over the taunter's head, floats up and fades
        var go = new GameObject("TauntMark");
        var tm = go.AddComponent<TextMesh>();
        tm.text = "!";
        tm.font = UiKit.DefaultFont;
        tm.fontSize = 120;
        tm.characterSize = 0.02f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = new Color(1f, 0.85f, 0.25f);
        if (UiKit.DefaultFont != null)
            go.GetComponent<MeshRenderer>().sharedMaterial = UiKit.DefaultFont.material;

        float t = 0f;
        while (t < 1.2f && ch != null)
        {
            t += Time.deltaTime;
            go.transform.position = ch.transform.position + Vector3.up * (1.7f + t * 0.5f);
            if (Camera.main != null)
                go.transform.rotation = Quaternion.LookRotation(go.transform.position - Camera.main.transform.position);
            tm.color = new Color(1f, 0.85f, 0.25f, Mathf.Clamp01(1.4f - t));
            yield return null;
        }
        Destroy(go);
    }

    void SetAllLocked(bool locked)
    {
        foreach (var ch in Characters)
            if (ch != null) ch.motor.movementLocked = locked;
    }

    void UpdateInfo()
    {
        if (_info == null) return;
        _info.text = "HIDERS LEFT  " + HidersLeft();
        if (_infoChip != null) _infoChip.SetActive(true);
        if (_hudRow != null) _hudRow.SetActive(true);
    }

    // stable per-name avatar dot colors (design 01: colored dots instead of text bullets)
    static readonly Color[] DotColors =
    {
        UiKit.Hex("2E8FE0"), UiKit.Hex("57A84C"), UiKit.Hex("D65B48"), UiKit.Hex("8A5CD6"),
        UiKit.Hex("F5822A"), UiKit.Hex("2AB8A8"), UiKit.Hex("E85CA0"), UiKit.Hex("8C6239"),
        UiKit.Hex("2E6BD6"), UiKit.Hex("D9A93C"),
    };

    /// <summary>Design 01: CREAM roster card — dot avatar + name rows, gold YOU row,
    /// red HUNT badges for pad volunteers, legend at the bottom (fix #7).</summary>
    void UpdateRoster()
    {
        if (_rosterRows == null) return;
        if (_rosterCount != null) _rosterCount.text = Characters.Count + "/" + totalCharacters;
        foreach (Transform c in _rosterRows) Destroy(c.gameObject);
        bool joined = Characters.Count > _rosterLastCount;
        _rosterLastCount = Characters.Count;

        RectTransform lastRow = null;
        float y = 0f;
        foreach (var ch in Characters)
        {
            if (ch == null) continue;
            bool you = ch.isPlayer;
            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(_rosterRows, false);
            UiKit.SetRect((RectTransform)rowGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y), new Vector2(0f, 52f));
            if (you)
            {
                var hl = UiKit.MakeImage(rowGo.transform, UiKit.Shape("tile-round-12"), new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.30f), "Hl");
                UiKit.SetRect(hl.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            }
            var dot = UiKit.MakeImage(rowGo.transform, UiKit.Shape("btn-circle-base"),
                you ? UiKit.Blue : DotColors[Mathf.Abs(ch.displayName.GetHashCode()) % DotColors.Length], "Dot");
            UiKit.SetRect(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(28f, 0f), new Vector2(30f, 30f));
            var name = UiKit.MakeText(rowGo.transform, you ? "YOU" : ch.displayName, 30, TextAnchor.MiddleLeft, false);
            name.color = UiKit.Hex("2A2622");
            UiKit.SetRect(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(56f, 0f), new Vector2(-60f, 0f));
            if (_volunteers.Contains(ch))
            {
                var badge = UiKit.MakePill(rowGo.transform, UiKit.HunterRed, "HuntBadge");
                UiKit.SetRect(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(118f, 36f));
                var ic = UiKit.MakeIconImage(badge.transform, "hunt-target", Color.white, 22f);
                ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                ic.rectTransform.anchoredPosition = new Vector2(22f, 0f);
                var bt = UiKit.MakeText(badge.transform, "HUNT", 20, TextAnchor.MiddleCenter, false);
                UiKit.SetRect(bt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 0f), Vector2.zero);
            }
            else if (you)
            {
                var badge = UiKit.MakePill(rowGo.transform, UiKit.Ink, "YouBadge");
                UiKit.SetRect(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(84f, 34f));
                var bt = UiKit.MakeText(badge.transform, "YOU", 20, TextAnchor.MiddleCenter, false);
                bt.color = UiKit.Gold;
                UiKit.SetRect(bt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            }
            lastRow = (RectTransform)rowGo.transform;
            y -= 54f;
        }

        // matchmaking feel (design 01): the newcomer's row slides in from the right
        if (joined && lastRow != null && _rosterPanel.activeInHierarchy)
            StartCoroutine(UiKit.SlideIn(lastRow, new Vector2(70f, 0f), 0.22f));
    }

    // ---------------- HUD (INK & PAINT, design canvas 1a + resultB) ----------------

    /// <summary>Phase banner: INK card, tilted -1.2deg, rainbow underline; hunter mood
    /// swaps to red text + warning-stripe underline. big != null adds the gold
    /// countdown number line (lobby). Card slides in from above / fades out on hide
    /// (spec 5: enter y -140 -> 0 easeOutBack 320ms, exit 200ms fade up).</summary>
    void SetTitle(string main, string big, bool hunterMood = false)
    {
        if (_titleCard == null) return;
        bool show = !string.IsNullOrEmpty(main);
        bool wasShown = _titleCard.activeSelf;
        if (!show)
        {
            if (wasShown) StartCoroutine(TitleOut());
            return;
        }
        _titleCard.SetActive(true);
        _title.text = main;
        _title.color = hunterMood ? UiKit.HunterRedBright : Color.white;
        _title.fontSize = big != null ? 30 : 54;
        if (_titleBig != null)
        {
            _titleBig.gameObject.SetActive(big != null);
            if (big != null) _titleBig.text = big;
        }
        if (_titleRainbow != null) _titleRainbow.SetActive(!hunterMood);
        if (_titleWarnStrip != null) _titleWarnStrip.gameObject.SetActive(hunterMood);
        if (!wasShown) StartCoroutine(TitleIn());
    }

    IEnumerator TitleIn()
    {
        var rt = (RectTransform)_titleCard.transform;
        var g = UiKit.EnsureGroup(_titleCard);
        float t = 0f;
        while (t < 0.32f && _titleCard.activeSelf)
        {
            t += Time.deltaTime;
            float k = UiKit.EaseOutBack(t / 0.32f);
            rt.anchoredPosition = new Vector2(0f, -44f + Mathf.LerpUnclamped(140f, 0f, k));
            g.alpha = Mathf.Clamp01(t / 0.18f);
            yield return null;
        }
        rt.anchoredPosition = new Vector2(0f, -44f);
        g.alpha = 1f;
    }

    IEnumerator TitleOut()
    {
        var rt = (RectTransform)_titleCard.transform;
        var g = UiKit.EnsureGroup(_titleCard);
        float t = 0f;
        while (t < 0.2f && _titleCard.activeSelf)
        {
            t += Time.deltaTime;
            float k = t / 0.2f;
            rt.anchoredPosition = new Vector2(0f, -44f + 100f * k * k);
            g.alpha = 1f - k;
            yield return null;
        }
        rt.anchoredPosition = new Vector2(0f, -44f);
        g.alpha = 1f;
        _titleCard.SetActive(false);
    }

    void HideTimer()
    {
        if (_hudRow != null) _hudRow.SetActive(false);
    }

    /// <summary>Ink-bordered light card (resultB language). Returns the inner image;
    /// position the row via inner.transform.parent.</summary>
    Image MakeInkCard(Transform parent, Color fill, float border, string name)
    {
        var outerGo = new GameObject(name, typeof(RectTransform));
        outerGo.transform.SetParent(parent, false);
        var outerImg = UiKit.MakeCard(outerGo.transform, UiKit.Ink, "Border");
        UiKit.SetRect(outerImg.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var inner = UiKit.MakeCard(outerGo.transform, fill, "Fill");
        UiKit.SetRect(inner.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-border * 2f, -border * 2f));
        return inner;
    }

    void BuildHUD()
    {
        var canvas = UiKit.MakeCanvas("MatchHUD", 60, transform);
        Transform root = canvas.transform;

        // --- phase banner card ---
        _titleCard = new GameObject("TitleCard", typeof(RectTransform));
        _titleCard.transform.SetParent(root, false);
        var titleRt = (RectTransform)_titleCard.transform;
        UiKit.SetRect(titleRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -44f), Vector2.zero);
        titleRt.localEulerAngles = new Vector3(0f, 0f, -1.2f);
        // backdrop images live INSIDE the layout group -> must opt out of layout
        var titleShadow = UiKit.MakePanel(_titleCard.transform, new Color(0f, 0f, 0f, 0.19f), "Drop");
        UiKit.SetRect(titleShadow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), Vector2.zero);
        titleShadow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var titleBg = UiKit.MakePanel(_titleCard.transform, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.91f), "Bg");
        UiKit.SetRect(titleBg.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        titleBg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var lay = _titleCard.AddComponent<VerticalLayoutGroup>();
        lay.padding = new RectOffset(44, 44, 14, 16);
        lay.spacing = 10;
        lay.childAlignment = TextAnchor.MiddleCenter;
        lay.childControlWidth = true;
        lay.childControlHeight = true;
        lay.childForceExpandWidth = true;
        lay.childForceExpandHeight = false;
        var fit = _titleCard.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _title = UiKit.MakeText(_titleCard.transform, "", 54, TextAnchor.MiddleCenter, false);
        _titleBig = UiKit.MakeText(_titleCard.transform, "", 88, TextAnchor.MiddleCenter, false);
        _titleBig.color = UiKit.Gold;
        _titleBig.gameObject.SetActive(false);
        var rainbow = UiKit.MakeRainbowStrip(_titleCard.transform, 7f);
        rainbow.gameObject.AddComponent<LayoutElement>().preferredHeight = 7f;
        _titleRainbow = rainbow.gameObject;
        _titleWarnStrip = UiKit.MakeImage(_titleCard.transform, UiKit.Shape("stripe-warn-tile"), UiKit.HunterRed, "WarnStrip");
        _titleWarnStrip.type = Image.Type.Tiled;
        _titleWarnStrip.gameObject.AddComponent<LayoutElement>().preferredHeight = 7f;
        _titleWarnStrip.gameObject.SetActive(false);

        // --- timer pill + info chip row ---
        _hudRow = new GameObject("HudRow", typeof(RectTransform));
        _hudRow.transform.SetParent(root, false);
        UiKit.SetRect((RectTransform)_hudRow.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -196f), Vector2.zero);
        var rowLay = _hudRow.AddComponent<HorizontalLayoutGroup>();
        rowLay.spacing = 14;
        rowLay.childAlignment = TextAnchor.MiddleCenter;
        rowLay.childControlWidth = true;
        rowLay.childControlHeight = true;
        rowLay.childForceExpandWidth = false;
        rowLay.childForceExpandHeight = false;
        var rowFit = _hudRow.AddComponent<ContentSizeFitter>();
        rowFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        rowFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _timerPill = UiKit.MakePill(_hudRow.transform, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.80f), "TimerPill");
        _timerPill.raycastTarget = false;
        var tpLay = _timerPill.gameObject.AddComponent<HorizontalLayoutGroup>();
        tpLay.padding = new RectOffset(30, 30, 4, 8);
        tpLay.spacing = 12;
        tpLay.childAlignment = TextAnchor.MiddleCenter;
        tpLay.childControlWidth = true;
        tpLay.childControlHeight = true;
        tpLay.childForceExpandWidth = false;
        _timerLabel = UiKit.MakeText(_timerPill.transform, "", 28, TextAnchor.MiddleCenter, false);
        _timerLabel.color = UiKit.TextDim;
        _timer = UiKit.MakeText(_timerPill.transform, "", 52, TextAnchor.MiddleCenter, false);

        _infoChip = UiKit.MakePill(_hudRow.transform, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.80f), "InfoChip").gameObject;
        var icLay = _infoChip.AddComponent<HorizontalLayoutGroup>();
        icLay.padding = new RectOffset(24, 24, 10, 12);
        icLay.childAlignment = TextAnchor.MiddleCenter;
        icLay.childControlWidth = true;
        icLay.childControlHeight = true;
        icLay.childForceExpandWidth = false;
        _info = UiKit.MakeText(_infoChip.transform, "", 26, TextAnchor.MiddleCenter, false);
        _info.color = UiKit.TextDim;
        _hudRow.SetActive(false);

        // --- STYLE badge (top-left; INK + gold outline, glow + value pop on gain) ---
        _scoreBadge = new GameObject("StyleBadge", typeof(RectTransform));
        _scoreBadge.transform.SetParent(root, false);
        UiKit.SetRect((RectTransform)_scoreBadge.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -36f), new Vector2(206f, 150f));
        _scoreGlow = UiKit.MakePanel(_scoreBadge.transform, new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0f), "Glow");
        UiKit.SetRect(_scoreGlow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
        var badgeBorder = UiKit.MakePanel(_scoreBadge.transform, UiKit.Gold, "BorderGold");
        UiKit.SetRect(badgeBorder.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var badgeInner = UiKit.MakePanel(_scoreBadge.transform, UiKit.Ink, "Inner");
        UiKit.SetRect(badgeInner.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-12f, -12f));
        var styleLbl = UiKit.MakeText(_scoreBadge.transform, "STYLE", 26, TextAnchor.MiddleCenter, false);
        styleLbl.color = UiKit.Gold;
        UiKit.SetRect(styleLbl.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(0f, 32f));
        _scoreText = UiKit.MakeText(_scoreBadge.transform, "0", 64, TextAnchor.MiddleCenter, false);
        UiKit.SetRect(_scoreText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), new Vector2(0f, -40f));

        // centre banner (decoy pops, net banners) on an auto-sized INK pill
        _bannerPill = UiKit.MakePill(root, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.88f), "BannerPill");
        UiKit.SetRect(_bannerPill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 220), new Vector2(600, 140));
        _bannerPill.gameObject.SetActive(false);
        _banner = UiKit.MakeText(root, "", 84, TextAnchor.MiddleCenter);
        UiKit.SetRect(_banner.rectTransform, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 220), new Vector2(-40, 160));

        BuildRoster(root);
        BuildRevealPlate(root);
        BuildWarnPlate(root);
        BuildResultPoster(root);

        // last-3-seconds squeeze + roulette flash live above everything in this canvas
        _countVignette = UiKit.MakeImage(root, UiKit.Shape("vignette"), new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0f), "CountVignette");
        UiKit.SetRect(_countVignette.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _countVignette.gameObject.SetActive(false);
        _hudFlash = UiKit.MakeImage(root, null, new Color(1f, 1f, 1f, 0f), "Flash");
        UiKit.SetRect(_hudFlash.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _hudFlash.gameObject.SetActive(false);
    }

    void BuildRoster(Transform root)
    {
        _rosterPanel = new GameObject("Roster", typeof(RectTransform));
        _rosterPanel.transform.SetParent(root, false);
        float h = 170f + totalCharacters * 54f;
        UiKit.SetRect((RectTransform)_rosterPanel.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-32, -330), new Vector2(430, h));
        var cardShadow = UiKit.MakeCard(_rosterPanel.transform, new Color(0f, 0f, 0f, 0.15f), "Drop");
        UiKit.SetRect(cardShadow.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), Vector2.zero);
        var card = UiKit.MakeCard(_rosterPanel.transform, UiKit.Cream, "Card");
        UiKit.SetRect(card.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        var head = UiKit.MakeText(_rosterPanel.transform, "PLAYERS", 32, TextAnchor.MiddleLeft, false);
        head.color = UiKit.Hex("2A2622");
        UiKit.SetRect(head.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(26f, -16f), new Vector2(-52f, 44f));
        var countPill = UiKit.MakePill(_rosterPanel.transform, UiKit.Blue, "Count");
        UiKit.SetRect(countPill.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -18f), new Vector2(124f, 42f));
        _rosterCount = UiKit.MakeText(countPill.transform, "", 28, TextAnchor.MiddleCenter, false);
        UiKit.SetRect(_rosterCount.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        var rowsGo = new GameObject("Rows", typeof(RectTransform));
        rowsGo.transform.SetParent(_rosterPanel.transform, false);
        _rosterRows = (RectTransform)rowsGo.transform;
        UiKit.SetRect(_rosterRows, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -74f), new Vector2(-36f, -140f));

        // legend (fix #7): what the red badge means
        var legend = new GameObject("Legend", typeof(RectTransform));
        legend.transform.SetParent(_rosterPanel.transform, false);
        UiKit.SetRect((RectTransform)legend.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(-44f, 44f));
        var lgBadge = UiKit.MakePill(legend.transform, UiKit.HunterRed, "Pill");
        UiKit.SetRect(lgBadge.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(86f, 32f));
        var lgTxt = UiKit.MakeText(lgBadge.transform, "HUNT", 18, TextAnchor.MiddleCenter, false);
        UiKit.SetRect(lgTxt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var lgLabel = UiKit.MakeText(legend.transform, "= standing on the hunter pad", 22, TextAnchor.MiddleLeft, false, false);
        lgLabel.color = UiKit.Hex("7A756A");
        UiKit.SetRect(lgLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(102f, 0f), new Vector2(-102f, 0f));

        _rosterPanel.SetActive(false);
    }

    /// <summary>Design 02: dim + full-width INK plate with red warning-stripe edges;
    /// THE HUNTER IS / name / GO GET THEM!.</summary>
    void BuildRevealPlate(Transform root)
    {
        _revealRoot = new GameObject("Reveal", typeof(RectTransform));
        _revealRoot.transform.SetParent(root, false);
        UiKit.SetRect((RectTransform)_revealRoot.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var dim = UiKit.MakeImage(_revealRoot.transform, null, new Color(0f, 0f, 0f, 0.55f), "Dim");
        UiKit.SetRect(dim.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        var plate = UiKit.MakeImage(_revealRoot.transform, null, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.95f), "Plate");
        UiKit.SetRect(plate.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 130f), new Vector2(0f, 430f));
        MakeWarnEdges(plate.transform);

        var kicker = UiKit.MakeText(plate.transform, "THE HUNTER IS", 34, TextAnchor.MiddleCenter, false);
        kicker.color = new Color(1f, 1f, 1f, 0.6f);
        UiKit.SetRect(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(0f, 44f));
        _revealName = UiKit.MakeText(plate.transform, "", 150, TextAnchor.MiddleCenter, false);
        UiKit.SetRect(_revealName.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(0f, 170f));
        var nameShadow = _revealName.gameObject.AddComponent<Shadow>();
        nameShadow.effectColor = UiKit.Hex("6B1408");
        nameShadow.effectDistance = new Vector2(0f, -8f);
        _revealFoot = UiKit.MakeText(plate.transform, "", 56, TextAnchor.MiddleCenter, false);
        UiKit.SetRect(_revealFoot.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(0f, 64f));

        _revealRoot.SetActive(false);
    }

    /// <summary>Design 10: HUNTER INCOMING! plate + GET READY gold pill + red vignette.</summary>
    void BuildWarnPlate(Transform root)
    {
        _warnRoot = new GameObject("HunterWarn", typeof(RectTransform));
        _warnRoot.transform.SetParent(root, false);
        UiKit.SetRect((RectTransform)_warnRoot.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var vig = UiKit.MakeImage(_warnRoot.transform, UiKit.Shape("vignette"), new Color(UiKit.HunterRedEdge.r, UiKit.HunterRedEdge.g, UiKit.HunterRedEdge.b, 0.5f), "Vignette");
        UiKit.SetRect(vig.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        var plate = UiKit.MakeImage(_warnRoot.transform, null, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.94f), "Plate");
        UiKit.SetRect(plate.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(0f, 330f));
        MakeWarnEdges(plate.transform);

        var big = UiKit.MakeText(plate.transform, "HUNTER INCOMING!", 92, TextAnchor.MiddleCenter, false);
        big.color = UiKit.HunterRedBright;
        UiKit.SetRect(big.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(0f, 110f));
        var bigShadow = big.gameObject.AddComponent<Shadow>();
        bigShadow.effectColor = UiKit.Hex("6B1408");
        bigShadow.effectDistance = new Vector2(0f, -6f);

        var ready = UiKit.MakePill(plate.transform, UiKit.Gold, "GetReady");
        UiKit.SetRect(ready.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(380f, 78f));
        var readyEdge = UiKit.MakePill(ready.transform, new Color(UiKit.GoldEdge.r, UiKit.GoldEdge.g, UiKit.GoldEdge.b, 1f), "Under");
        UiKit.SetRect(readyEdge.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), Vector2.zero);
        readyEdge.transform.SetAsFirstSibling();
        var readyTxt = UiKit.MakeText(ready.transform, "GET READY", 44, TextAnchor.MiddleCenter, false);
        readyTxt.color = UiKit.Ink;
        UiKit.SetRect(readyTxt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), Vector2.zero);

        _warnRoot.SetActive(false);
    }

    void MakeWarnEdges(Transform plate)
    {
        var top = UiKit.MakeImage(plate, UiKit.Shape("stripe-warn-tile"), UiKit.HunterRed, "EdgeTop");
        top.type = Image.Type.Tiled;
        UiKit.SetRect(top.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 24f));
        var bottom = UiKit.MakeImage(plate, UiKit.Shape("stripe-warn-tile"), UiKit.HunterRed, "EdgeBottom");
        bottom.type = Image.Type.Tiled;
        UiKit.SetRect(bottom.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 24f));
    }

    /// <summary>resultB: opaque cream poster with candy stripes + confetti; content
    /// filled by FillResultPoster.</summary>
    void BuildResultPoster(Transform root)
    {
        _resultPanel = new GameObject("Result", typeof(RectTransform));
        _resultPanel.transform.SetParent(root, false);
        UiKit.SetRect((RectTransform)_resultPanel.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        var bg = UiKit.MakeImage(_resultPanel.transform, null, UiKit.CreamBg, "Bg");
        UiKit.SetRect(bg.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        bg.raycastTarget = true; // opaque poster owns all input (fix #11)
        for (int i = 0; i < 12; i++)
        {
            var band = UiKit.MakeImage(_resultPanel.transform, null, UiKit.CreamBg2, "Band" + i);
            UiKit.SetRect(band.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, i * 180f - 60f), new Vector2(2600f, 90f));
            band.rectTransform.localEulerAngles = new Vector3(0f, 0f, -24f);
        }
        // confetti sprinkles around the headline
        float[] cx = { 110f, -420f, 330f, -80f, 470f, -300f };
        float[] cy = { -140f, -100f, -210f, -330f, -300f, -420f };
        float[] cr = { 24f, -32f, 40f, 58f, -52f, 18f };
        for (int i = 0; i < 6; i++)
        {
            var conf = UiKit.MakeImage(_resultPanel.transform, UiKit.Shape("tile-round-12"), UiKit.Rainbow[(i * 2 + 1) % 7], "Confetti" + i);
            UiKit.SetRect(conf.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(cx[i], cy[i]), new Vector2(34f, 56f));
            conf.rectTransform.localEulerAngles = new Vector3(0f, 0f, cr[i]);
            _resultConfetti.Add(conf.rectTransform);
        }

        // headline card (white, ink border, tilted)
        var titleCardGo = new GameObject("TitleCard", typeof(RectTransform));
        titleCardGo.transform.SetParent(_resultPanel.transform, false);
        UiKit.SetRect((RectTransform)titleCardGo.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -210f), new Vector2(900f, 370f));
        ((RectTransform)titleCardGo.transform).localEulerAngles = new Vector3(0f, 0f, -1.6f);
        _resultTitleCard = (RectTransform)titleCardGo.transform;
        var titleDrop = UiKit.MakeCard(titleCardGo.transform, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.19f), "Drop");
        UiKit.SetRect(titleDrop.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -18f), Vector2.zero);
        var titleBorder = UiKit.MakeCard(titleCardGo.transform, UiKit.Ink, "Border");
        UiKit.SetRect(titleBorder.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var titleFill = UiKit.MakeCard(titleCardGo.transform, Color.white, "Fill");
        UiKit.SetRect(titleFill.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-16f, -16f));

        // border pill first; the colored fill sits INSIDE it (children render on top)
        var badgeBorder = UiKit.MakePill(titleCardGo.transform, UiKit.Ink, "BadgeBorder");
        UiKit.SetRect(badgeBorder.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(352f, 70f));
        _resultBadge = UiKit.MakePill(badgeBorder.transform, UiKit.Green, "Badge");
        UiKit.SetRect(_resultBadge.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-10f, -10f));
        _resultBadgeText = UiKit.MakeText(badgeBorder.transform, "HIDERS WIN", 34, TextAnchor.MiddleCenter, false);
        UiKit.SetRect(_resultBadgeText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        _resultText = UiKit.MakeText(titleCardGo.transform, "", 86, TextAnchor.MiddleCenter, false);
        _resultText.color = UiKit.Ink;
        _resultText.lineSpacing = 0.78f; // Baloo 2 has a tall line box
        UiKit.SetRect(_resultText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -44f), new Vector2(-60f, -120f));

        // score card (gold, ink border)
        var scoreGo = new GameObject("ScoreCard", typeof(RectTransform));
        scoreGo.transform.SetParent(_resultPanel.transform, false);
        UiKit.SetRect((RectTransform)scoreGo.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -700f), new Vector2(836f, 250f));
        _resultScoreCard = (RectTransform)scoreGo.transform;
        var scoreDrop = UiKit.MakeCard(scoreGo.transform, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.19f), "Drop");
        UiKit.SetRect(scoreDrop.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -14f), Vector2.zero);
        var scoreBorder = UiKit.MakeCard(scoreGo.transform, UiKit.Ink, "Border");
        UiKit.SetRect(scoreBorder.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var scoreFill = UiKit.MakeCard(scoreGo.transform, UiKit.Gold, "Fill");
        UiKit.SetRect(scoreFill.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-16f, -16f));
        var scoreLbl = UiKit.MakeText(scoreGo.transform, "YOUR STYLE SCORE", 30, TextAnchor.MiddleCenter, false);
        scoreLbl.color = UiKit.Hex("6B4A08");
        UiKit.SetRect(scoreLbl.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(0f, 40f));
        _resultScoreValue = UiKit.MakeText(scoreGo.transform, "0", 130, TextAnchor.MiddleCenter, false);
        _resultScoreValue.color = UiKit.Ink;
        UiKit.SetRect(_resultScoreValue.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(0f, -70f));

        // leaderboard
        var lbTitle = UiKit.MakeText(_resultPanel.transform, "BOLDEST HIDERS", 34, TextAnchor.MiddleCenter, false);
        lbTitle.color = UiKit.Ink;
        UiKit.SetRect(lbTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -1010f), new Vector2(0f, 46f));
        var rowsGo = new GameObject("Rows", typeof(RectTransform));
        rowsGo.transform.SetParent(_resultPanel.transform, false);
        _resultRows = (RectTransform)rowsGo.transform;
        UiKit.SetRect(_resultRows, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -1010f), new Vector2(820f, 380f));

        // PLAY AGAIN (blue pill, ink border, replay icon)
        var againGo = new GameObject("PlayAgain", typeof(RectTransform));
        againGo.transform.SetParent(_resultPanel.transform, false);
        UiKit.SetRect((RectTransform)againGo.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(656f, 166f));
        _resultAgain = (RectTransform)againGo.transform;
        var againDrop = UiKit.MakePill(againGo.transform, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.19f), "Drop");
        UiKit.SetRect(againDrop.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -12f), Vector2.zero);
        var againBorder = UiKit.MakePill(againGo.transform, UiKit.Ink, "Border");
        UiKit.SetRect(againBorder.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var againGoInner = new GameObject("Body", typeof(Image), typeof(Button));
        againGoInner.transform.SetParent(againGo.transform, false);
        var againImg = againGoInner.GetComponent<Image>();
        againImg.sprite = UiKit.Shape("chip-pill");
        againImg.type = Image.Type.Sliced;
        againImg.color = UiKit.Blue;
        UiKit.SetRect((RectTransform)againGoInner.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-16f, -16f));
        var againBtn = againGoInner.GetComponent<Button>();
        againBtn.transition = Selectable.Transition.None;
        againGoInner.AddComponent<PressFx>().target = againImg;
        var againIcon = UiKit.MakeIconImage(againGoInner.transform, "replay", Color.white, 52f);
        againIcon.rectTransform.anchoredPosition = new Vector2(-170f, 0f);
        var againTxt = UiKit.MakeText(againGoInner.transform, "PLAY AGAIN", 52, TextAnchor.MiddleCenter, false);
        UiKit.SetRect(againTxt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(30f, 0f), Vector2.zero);
        againBtn.onClick.AddListener(() => StartCoroutine(ReloadWithFade()));

        _resultPanel.SetActive(false);
    }

    /// <summary>PLAY AGAIN: quick ink fade instead of a hard scene cut.</summary>
    IEnumerator ReloadWithFade()
    {
        var cover = UiKit.MakeImage(_resultPanel.transform, null, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0f), "ReloadCover");
        UiKit.SetRect(cover.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        cover.raycastTarget = true; // block double-taps during the fade
        float t = 0f;
        while (t < 0.2f)
        {
            t += Time.deltaTime;
            cover.color = new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, t / 0.2f);
            yield return null;
        }
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>resultB entrance (spec 5): mask fade, title card pop, score card pop,
    /// leaderboard rows sliding in staggered, PLAY AGAIN last; confetti rains down
    /// when the hiders won.</summary>
    IEnumerator ResultEntrance(bool huntersWin)
    {
        var g = UiKit.EnsureGroup(_resultPanel);
        g.alpha = 0f;
        // stash confetti at their resting spots; hiders-win drops them from above
        var confettiBase = new List<Vector2>();
        foreach (var c in _resultConfetti) confettiBase.Add(c.anchoredPosition);
        if (!huntersWin)
            for (int i = 0; i < _resultConfetti.Count; i++)
                _resultConfetti[i].anchoredPosition = confettiBase[i] + new Vector2(0f, 500f + i * 60f);

        yield return UiKit.Fade(g, 1f, 0.18f);
        if (_resultTitleCard != null) yield return UiKit.PopIn(_resultTitleCard, 0.8f, 0.32f);
        if (_resultScoreCard != null) StartCoroutine(UiKit.PopIn(_resultScoreCard, 0.85f, 0.24f));
        yield return new WaitForSeconds(0.12f);
        if (_resultRows != null)
        {
            foreach (Transform row in _resultRows)
            {
                StartCoroutine(UiKit.SlideIn((RectTransform)row, new Vector2(70f, 0f), 0.24f));
                yield return new WaitForSeconds(0.09f);
            }
        }
        if (_resultAgain != null) StartCoroutine(UiKit.PopIn(_resultAgain, 0.9f, 0.2f));
        // confetti fall (1.2s, eased) — celebration only when the hiders survive
        if (!huntersWin)
        {
            float t = 0f;
            while (t < 1.2f)
            {
                t += Time.deltaTime;
                for (int i = 0; i < _resultConfetti.Count; i++)
                {
                    float k = UiKit.EaseOutCubic(Mathf.Clamp01((t - i * 0.06f) / 0.9f));
                    _resultConfetti[i].anchoredPosition = Vector2.LerpUnclamped(
                        confettiBase[i] + new Vector2(0f, 500f + i * 60f), confettiBase[i], k);
                    _resultConfetti[i].localEulerAngles = new Vector3(0f, 0f,
                        _resultConfetti[i].localEulerAngles.z + Time.deltaTime * 60f * (i % 2 == 0 ? 1f : -1f));
                }
                yield return null;
            }
            for (int i = 0; i < _resultConfetti.Count; i++)
                _resultConfetti[i].anchoredPosition = confettiBase[i];
        }
    }

    // ---------------- net bridge (offline-safe) ----------------
    // The netcode layer (Arena/Net/) drives these; offline every Request* falls
    // through to the local fast path, so bot matches behave exactly as before.

    MatchNet _net;

    public void AttachNet(MatchNet net) { _net = net; }

    /// <summary>Net-spawned characters announce themselves (host and clients).</summary>
    public void Register(Character ch)
    {
        if (ch == null || Characters.Contains(ch)) return;
        Characters.Add(ch);
        UpdateRoster();
    }

    public void Unregister(Character ch)
    {
        if (ch == null) return;
        Characters.Remove(ch);
        _volunteers.Remove(ch);
        _score.Remove(ch);
        UpdateRoster();
    }

    /// <summary>Bind the character this peer controls (rig/camera wiring is NetGame's job).</summary>
    public void AdoptLocalPlayer(Character ch) { _player = ch; }

    public void RequestTaunt(Character ch)
    {
        if (ch == null) return;
        if (ch.NetActive && !ch.netSync.IsServer) { ch.netSync.TauntServerRpc(); return; }
        DoTaunt(ch);
    }

    public bool RequestDecoy(Character ch)
    {
        if (ch == null) return false;
        if (ch.NetActive && !ch.netSync.IsServer) { ch.netSync.DecoyServerRpc(); return true; }
        return SpawnDecoy(ch) != null;
    }

    public void RequestHit(Character shooter, Character victim)
    {
        if (shooter != null && shooter.NetActive && !shooter.netSync.IsServer)
        {
            bool hasVictim = victim != null && victim.netSync != null;
            var vRef = hasVictim ? new NetworkObjectReference(victim.netSync.NetworkObject) : default(NetworkObjectReference);
            shooter.netSync.HunterFireServerRpc(vRef, hasVictim, NetworkManager.Singleton.LocalClientId);
            return;
        }
        if (shooter != null && shooter.NetActive) shooter.netSync.ServerShootFx();
        if (victim != null) Convert(victim);
    }

    /// <summary>Replicated taunt FX: the floating "!" above the taunter, on every peer.</summary>
    public void SpawnTauntMarker(Character ch) { StartCoroutine(TauntMarker(ch)); }

    /// <summary>Server told this client its style-score total changed.</summary>
    public void OnLocalScore(float total)
    {
        if (_player == null) return;
        float old;
        _score.TryGetValue(_player, out old);
        if (total > old) _lastGainAt = Time.time; // keep the gold pulse
        _score[_player] = total;
        UpdateScoreHud();
    }

    public void OnNetBanner(string text, bool red, bool huntersOnly, float seconds)
    {
        if (huntersOnly && (_player == null || _player.team != Team.Hunter)) return;
        StartCoroutine(FlashBanner(text, seconds));
        if (red) _banner.color = new Color(1f, 0.35f, 0.3f); // after: FlashBanner resets to white
    }

    public void OnNetHunterReveal(Character hunter)
    {
        string who = hunter == null ? "SOMEONE IS" : (hunter.isPlayer ? "YOU ARE" : hunter.displayName + " IS");
        StartCoroutine(FlashBanner(who + " THE HUNTER!", 1.5f));
        _banner.color = new Color(1f, 0.35f, 0.3f);
    }

    public void OnNetResult(bool huntersWin, string topScores)
    {
        // Minimal client-side poster; the structured scoreboard sync is owned by the
        // netcode workstream (topScores is ignored until it lands).
        SetPhase(MatchPhase.Result);
        float pv;
        _score.TryGetValue(_player, out pv);
        FillResultPoster(huntersWin ? "ALL HIDERS\nFOUND!" : "TIME'S UP -\nHIDERS WIN!", huntersWin, Mathf.FloorToInt(pv), null);
    }
}
