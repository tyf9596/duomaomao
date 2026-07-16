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
    float _nextVolCheckAt;

    // style scoring (original-game rule: points for time spent inside the hunter's
    // line of sight — the closer the richer; taunts add risk-bonus on top)
    readonly Dictionary<Character, float> _score = new Dictionary<Character, float>();
    float _nextLosTickAt;
    float _lastGainAt;
    static readonly RaycastHit[] LosBuf = new RaycastHit[24];

    // HUD
    Text _title, _timer, _info, _banner, _scoreText;
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

        if (HidersLeft() == 0)
        {
            EndMatch(_player.team == Team.Hunter ? "ALL HIDERS FOUND — HUNTERS WIN!" : "YOU WERE THE LAST ONE — HUNTERS WIN!");
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
        yield return new WaitForSeconds(seconds);
        if (_banner.text == text) _banner.text = "";
    }

    /// <summary>One-use hider ability: leave a painted, posed copy of yourself behind.</summary>
    public Character SpawnDecoy(Character owner)
    {
        if (owner == null || owner.team != Team.Hider) return null;
        Vector3 pos = owner.transform.position - owner.transform.forward * 0.6f;
        var d = Character.Create(owner.displayName + " decoy", pos, false, owner.variant);
        d.isDecoy = true;
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
                _timer.text = (_player.team == Team.Hunter ? "SEEK IN  " : "HIDE  ") + Mathf.Max(0, Mathf.CeilToInt(remaining));
                if (remaining <= 0f && !_hunterEntryStarted)
                {
                    _hunterEntryStarted = true;
                    StartCoroutine(HunterEntry());
                }
                break;

            case MatchPhase.Seek:
                _timer.text = "SEEK  " + Mathf.Max(0, Mathf.CeilToInt(remaining));
                if (Time.time >= _nextLosTickAt)
                {
                    _nextLosTickAt = Time.time + 0.5f;
                    LosScoreTick();
                }
                if (remaining <= 0f)
                    EndMatch(_player.team == Team.Hider ? "TIME'S UP — YOU SURVIVED!" : "TIME'S UP — HIDERS WIN!");
                break;
        }

        // score readout glows gold for a beat whenever points come in
        if (_scoreText != null)
            _scoreText.color = Time.time - _lastGainAt < 0.45f ? new Color(1f, 0.85f, 0.3f) : Color.white;
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

        if (_scoreText != null)
            _scoreText.gameObject.SetActive(
                (phase == MatchPhase.Hide || phase == MatchPhase.Seek)
                && _player != null && _player.team == Team.Hider);

        if (_rig != null) _rig.RefreshContextButton();
    }

    void EndMatch(string message)
    {
        SetPhase(MatchPhase.Result);
        _info.text = "";
        var sb = new System.Text.StringBuilder(message);
        float pv;
        _score.TryGetValue(_player, out pv);
        sb.Append("\n\nYOUR STYLE SCORE  ").Append(Mathf.FloorToInt(pv));

        var top = new List<KeyValuePair<Character, float>>();
        foreach (var kv in _score)
            if (kv.Key != null && !kv.Key.isDecoy && kv.Value >= 1f) top.Add(kv);
        top.Sort((a, b) => b.Value.CompareTo(a.Value));
        if (top.Count > 0)
        {
            sb.Append("\nBOLDEST HIDERS");
            for (int i = 0; i < Mathf.Min(3, top.Count); i++)
                sb.Append("\n").Append(i + 1).Append(".  ")
                  .Append(top[i].Key.isPlayer ? "YOU" : top[i].Key.displayName)
                  .Append("   ").Append(Mathf.FloorToInt(top[i].Value));
        }
        _resultText.text = sb.ToString();
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
            _lastGainAt = Time.time;
            UpdateScoreHud();
        }
    }

    void UpdateScoreHud()
    {
        if (_scoreText == null) return;
        float v;
        _score.TryGetValue(_player, out v);
        _scoreText.text = "STYLE  " + Mathf.FloorToInt(v);
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
        sb.Append("\n\n[H] = on the hunter pad");
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

        // style score (top-left, hider-relevant)
        _scoreText = UiKit.MakeText(root, "STYLE  0", 42, TextAnchor.UpperLeft);
        UiKit.SetRect(_scoreText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -36), new Vector2(400, 60));

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
