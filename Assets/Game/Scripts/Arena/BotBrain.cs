using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives one bot through the match.
/// As a hider: run to a random spot during the hide phase, auto-camouflage to the
/// nearby surface colour, strike a pose and freeze.
/// As a hunter: patrol, build per-target suspicion from line of sight — faster for
/// targets that move, stand upright, or whose paint mismatches the surface behind
/// them — and fire the shotgun once sure. Converted hiders start hunting too.
/// </summary>
public class BotBrain : MonoBehaviour
{
    public Character self;
    public MatchManager match;
    public ArenaMap map;
    public LobbyRoom lobby; // where we mill about before the match (and hunters wait)
    public bool lobbyVolunteer; // wants to hunt → stands on the red pad
    public Shotgun gun; // set when on the hunter team

    [Header("Hunting")]
    public float detectRange = 11f;
    public float fireRange = 9f;
    public float viewAngle = 75f;
    public float fireThreshold = 1.6f;

    static readonly RaycastHit[] HitBuf = new RaycastHit[24];

    Vector3 _target;
    bool _hasTarget;
    bool _settled;
    float _stuckTimer;
    Vector3 _lastPos;
    float _scanAt;
    float _nextPatrolAt;
    readonly Dictionary<Character, float> _suspicion = new Dictionary<Character, float>();
    readonly List<Character> _keys = new List<Character>();

    void Update()
    {
        if (match == null || self == null) return;

        switch (match.Phase)
        {
            case MatchPhase.Lobby:
                LobbyBehavior();
                break;
            case MatchPhase.Hide:
                if (self.team == Team.Hider) HideBehavior();
                else LobbyBehavior(); // the hunter waits upstairs in the lobby
                break;
            case MatchPhase.Seek:
                if (self.team == Team.Hunter) HuntBehavior();
                else { Idle(); MaybeTaunt(); } // frozen in camo — but bravado pays
                break;
            default:
                Idle();
                break;
        }
    }

    void Idle()
    {
        self.motor.desiredMove = Vector3.zero;
    }

    // ---------------- lobby ----------------

    float _lobbyNextAt;
    float _lobbyRethinkAt;
    bool _lobbyIdling;
    Vector3 _lobbyTarget;

    /// <summary>Mill about the lobby: short walks, pauses, the odd pose or hop.
    /// Volunteers head for the hunter pad and stand on it (and sometimes chicken out).</summary>
    void LobbyBehavior()
    {
        if (lobby == null) { Idle(); return; }

        // change of heart, only while volunteering still matters
        if (match.Phase == MatchPhase.Lobby && Time.time >= _lobbyRethinkAt)
        {
            _lobbyRethinkAt = Time.time + Random.Range(5f, 9f);
            if (Random.value < 0.18f)
            {
                lobbyVolunteer = !lobbyVolunteer;
                _lobbyNextAt = 0f; // re-decide destination now
            }
        }

        if (lobbyVolunteer && match.Phase == MatchPhase.Lobby)
        {
            if (lobby.OnPlatform(transform.position)) { Idle(); return; }
            if (Time.time >= _lobbyNextAt)
            {
                _lobbyNextAt = Time.time + Random.Range(1.2f, 2.5f);
                _lobbyTarget = lobby.PlatformSpot();
            }
            Vector3 toPad = _lobbyTarget - transform.position;
            toPad.y = 0f;
            if (toPad.magnitude < 0.3f) { Idle(); return; }
            Steer(_lobbyTarget);
            return;
        }

        if (Time.time >= _lobbyNextAt)
        {
            _lobbyNextAt = Time.time + Random.Range(1.6f, 4f);
            _lobbyIdling = Random.value < 0.35f;
            if (_lobbyIdling)
            {
                if (Random.value < 0.5f) self.motor.SetPose((Pose)Random.Range(0, 6));
            }
            else
            {
                _lobbyTarget = lobby.SpawnPoint();
            }
        }

        if (_lobbyIdling) { Idle(); return; }

        Vector3 flat = _lobbyTarget - transform.position;
        flat.y = 0f;
        if (flat.magnitude < 0.4f) { Idle(); return; }
        Steer(_lobbyTarget);
        if (Random.value < 0.002f) self.motor.wantJumpPressed = true;
    }

    // ---------------- hider ----------------

    void HideBehavior()
    {
        if (_settled) { Idle(); return; }

        if (!_hasTarget)
        {
            _target = map.RandomPointOnFloor();
            _hasTarget = true;
            _lastPos = transform.position;
            _stuckTimer = 0f;
        }

        Vector3 flat = _target - transform.position;
        flat.y = 0f;
        if (flat.magnitude < 0.45f) { Settle(); return; }

        Steer(_target);
        if (Random.value < 0.003f) self.motor.wantDash = true; // a little hustle
        CheckStuck(() => _hasTarget = false);
    }

    void Settle()
    {
        Idle();
        // sample two nearby points — a colour difference means a patterned surface
        // (tiles, zebra crossing, graffiti), and stripes blend better than a flat coat
        Color c1, c2;
        bool s1 = SampleSurfaceColor(transform.position + Vector3.up * 0.5f, Vector3.down, 3f, out c1);
        bool s2 = SampleSurfaceColor(transform.position + Vector3.up * 0.5f + transform.right * 0.4f, Vector3.down, 3f, out c2);
        if (s1 && s2 && ColorDiff(c1, c2) > 0.25f)
            self.SkinFillStripes(c1, c2, Random.Range(16, 28));
        else if (s1)
            self.SkinFillCamo(c1);
        else if (SampleSurfaceColor(self.EyePos, transform.forward, 2.5f, out c1))
            self.SkinFillCamo(c1);

        self.motor.SetPose((Pose)Random.Range(0, 9)); // any pose incl. Ball/Dead/Bend
        _settled = true;
    }

    static float ColorDiff(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
    }

    // Bot bravado: taunt occasionally once settled, when the hunter is at a spicy
    // middle distance — earns style points (and shows off the system).
    float _nextTauntThinkAt;

    void MaybeTaunt()
    {
        if (!_settled || Time.time < _nextTauntThinkAt) return;
        _nextTauntThinkAt = Time.time + Random.Range(5f, 10f);
        float nearest = float.MaxValue;
        foreach (var ch in match.Characters)
            if (ch != null && ch.team == Team.Hunter)
                nearest = Mathf.Min(nearest, Vector3.Distance(ch.transform.position, transform.position));
        if (nearest > 8f && nearest < 18f && Random.value < 0.25f)
            match.DoTaunt(self);
    }

    /// <summary>Taunts are audible: a hunter that hears one gets suspicious of the noise-maker.</summary>
    public void HearTaunt(Character who, float strength)
    {
        if (self.team != Team.Hunter || who == null || who == self) return;
        float v;
        _suspicion.TryGetValue(who, out v);
        _suspicion[who] = v + strength;
    }

    // ---------------- hunter ----------------

    void HuntBehavior()
    {
        // poses persist through movement now — a hunter must never stalk in a lobby pose
        if (self.motor.CurrentPose != Pose.Stand) self.motor.SetPose(Pose.Stand);

        if (Time.time >= _scanAt)
        {
            _scanAt = Time.time + 0.2f;
            Scan();
        }

        Character suspect = null;
        float top = 0f;
        foreach (var kv in _suspicion)
        {
            if (kv.Key == null || kv.Key.team != Team.Hider) continue;
            if (kv.Value > top) { top = kv.Value; suspect = kv.Key; }
        }

        if (suspect != null && top > 0.6f)
        {
            // Walk right up to a suspect to inspect — proximity resolves doubt fast.
            Vector3 to = suspect.transform.position - transform.position;
            float dist = to.magnitude;
            if (dist > 2.2f)
            {
                Steer(suspect.transform.position);
            }
            else
            {
                Idle();
                FaceToward(to);
            }

            if (top >= fireThreshold && dist <= fireRange && gun != null && gun.CanFire && HasLineOfSight(suspect))
            {
                self.motor.TriggerShoot();
                self.NetShootFx(); // remote peers play the same swing
                var victim = gun.Fire(self.EyePos, (suspect.EyePos - self.EyePos).normalized, self);
                if (victim != null) match.Convert(victim);
                _suspicion[suspect] = 0.3f; // re-evaluate after the shot
            }
        }
        else
        {
            Patrol();
        }
    }

    void Scan()
    {
        // decay old suspicion
        _keys.Clear();
        foreach (var k in _suspicion.Keys) _keys.Add(k);
        foreach (var k in _keys) _suspicion[k] = Mathf.Max(0f, _suspicion[k] - 0.06f);

        foreach (var ch in match.Characters)
        {
            if (ch == null || ch == self || ch.team != Team.Hider) continue;
            Vector3 to = ch.EyePos - self.EyePos;
            float dist = to.magnitude;
            if (dist > detectRange) continue;
            if (Vector3.Angle(transform.forward, to) > viewAngle) continue;
            if (!HasLineOfSight(ch)) continue;

            bool moving = ch.motor.desiredMove.sqrMagnitude > 0.01f;
            float camo = CamoMatch(ch);
            float rate = 0.5f * (1f - camo)
                       + (moving ? 1.6f : 0f)
                       + (ch.motor.CurrentPose == Pose.Stand ? 0.3f : 0f);
            rate *= Mathf.Lerp(1.4f, 0.5f, dist / detectRange);
            // even perfect paint can't hide a body shape you can almost touch,
            // and a visible silhouette always registers a little
            rate += Mathf.Max(0f, 2.5f - dist) * 0.8f;
            rate = Mathf.Max(rate, 0.15f);

            float v;
            _suspicion.TryGetValue(ch, out v);
            _suspicion[ch] = v + rate * 0.2f;
        }
    }

    /// <summary>1 = perfectly blended into whatever is behind them, 0 = sticks out.</summary>
    float CamoMatch(Character ch)
    {
        Vector3 dir = (ch.EyePos - self.EyePos).normalized;
        Color bg;
        if (!SampleSurfaceColor(ch.transform.position + Vector3.up * 0.8f + dir * 0.7f, dir, 4f, out bg))
            return 0f; // silhouetted against open space = obvious
        Color skin = ch.skin.AverageColor();
        float d = Mathf.Abs(skin.r - bg.r) + Mathf.Abs(skin.g - bg.g) + Mathf.Abs(skin.b - bg.b);
        return 1f - Mathf.Clamp01(d / 1.2f);
    }

    bool HasLineOfSight(Character ch)
    {
        Vector3 from = self.EyePos;
        Vector3 to = ch.EyePos;
        int n = Physics.RaycastNonAlloc(new Ray(from, (to - from).normalized), HitBuf, Vector3.Distance(from, to) + 0.5f);
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (Character.FromCollider(HitBuf[i].collider) == self) continue;
            if (best < 0 || HitBuf[i].distance < HitBuf[best].distance) best = i;
        }
        if (best < 0) return false;
        return Character.FromCollider(HitBuf[best].collider) == ch;
    }

    // ---------------- shared movement ----------------

    void Patrol()
    {
        Vector3 flat = _target - transform.position;
        flat.y = 0f;
        if (!_hasTarget || flat.magnitude < 0.5f || Time.time >= _nextPatrolAt)
        {
            _target = map.RandomPointOnFloor();
            _hasTarget = true;
            _nextPatrolAt = Time.time + 7f;
        }
        Steer(_target);
        CheckStuck(() => _hasTarget = false);
    }

    void Steer(Vector3 pos)
    {
        Vector3 d = pos - transform.position;
        d.y = 0f;
        self.motor.desiredMove = d.sqrMagnitude > 1f ? d.normalized : d;
    }

    void FaceToward(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        var target = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, 10f * Time.deltaTime);
    }

    void CheckStuck(System.Action onStuck)
    {
        if (Vector3.Distance(transform.position, _lastPos) < 0.05f)
        {
            _stuckTimer += Time.deltaTime;
        }
        else
        {
            _stuckTimer = 0f;
            _lastPos = transform.position;
        }
        if (_stuckTimer > 1.5f)
        {
            _stuckTimer = 0f;
            onStuck();
        }
    }

    static bool SampleSurfaceColor(Vector3 origin, Vector3 dir, float maxDist, out Color color)
    {
        color = Color.gray;
        int n = Physics.RaycastNonAlloc(new Ray(origin, dir.normalized), HitBuf, maxDist);
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (Character.FromCollider(HitBuf[i].collider) != null) continue; // environment only
            if (best < 0 || HitBuf[i].distance < HitBuf[best].distance) best = i;
        }
        if (best < 0) return false;
        return PaintableBody.SampleSurfaceColor(HitBuf[best], out color);
    }
}
