using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The network half of a character. Lives on the NetCharacter prefab root next to
/// CharacterController/CharacterMotor/Character; the visual blocky body is attached
/// locally on every peer in OnNetworkSpawn (from the synced variant letter), so only
/// the root transform travels over the wire — never mesh or texture data.
///
/// Sync model:
///   movement   — OwnerNetworkTransform (owner-authoritative, interpolated)
///   pose       — owner-written NetworkVariable, applied to the proxy motor
///   team/lock  — server-written NetworkVariables (match rules are host-authoritative)
///   paint      — stroke RPCs: owner stamps locally, server rebroadcasts, the
///                originator skips its own echo (soft brushes aren't idempotent)
///   bot camo   — server-invoked fills; FillCamo carries a seed so the random
///                blotches come out pixel-identical on every peer
/// </summary>
public class CharacterNetSync : NetworkBehaviour
{
    const ulong NoHuman = ulong.MaxValue;

    public readonly NetworkVariable<FixedString32Bytes> netVariant = new NetworkVariable<FixedString32Bytes>();
    public readonly NetworkVariable<FixedString32Bytes> netName = new NetworkVariable<FixedString32Bytes>();
    public readonly NetworkVariable<ulong> netHumanClientId = new NetworkVariable<ulong>(NoHuman);
    public readonly NetworkVariable<bool> netIsDecoy = new NetworkVariable<bool>();
    public readonly NetworkVariable<byte> netTeam = new NetworkVariable<byte>();
    public readonly NetworkVariable<bool> netLocked = new NetworkVariable<bool>();
    public readonly NetworkVariable<byte> netPose = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    Character _ch;
    CharacterMotor _motor;
    Vector3 _lastPos;
    float _proxySpeed;
    float _lastFireAt = -10f;

    public override void OnNetworkSpawn()
    {
        _ch = GetComponent<Character>();
        _motor = GetComponent<CharacterMotor>();
        _ch.netSync = this;
        _ch.variant = netVariant.Value.ToString();
        _ch.displayName = netName.Value.ToString();
        _ch.isDecoy = netIsDecoy.Value;
        _ch.isPlayer = netHumanClientId.Value != NoHuman
                       && netHumanClientId.Value == NetworkManager.LocalClientId;
        gameObject.name = _ch.displayName;

        Character.AttachVisual(_ch, _ch.variant);

        if (!IsOwner) _motor.proxyMode = true;
        _lastPos = transform.position;

        netTeam.OnValueChanged += (o, n) => ApplyTeam((Team)n);
        netLocked.OnValueChanged += (o, n) => _motor.movementLocked = n;
        netPose.OnValueChanged += (o, n) => { if (!IsOwner) _motor.SetPose((Pose)n); };
        ApplyTeam((Team)netTeam.Value);
        _motor.movementLocked = netLocked.Value;
        if (!IsOwner && netPose.Value != 0) _motor.SetPose((Pose)netPose.Value);

        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.Register(_ch);
            if (_ch.isPlayer) MatchManager.Instance.AdoptLocalPlayer(_ch);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (MatchManager.Instance != null) MatchManager.Instance.Unregister(_ch);
    }

    void Update()
    {
        if (!IsSpawned) return;
        if (IsOwner) return;
        // proxies get no CharacterController.Move calls, so their animator speed
        // comes from how fast the interpolated transform is actually travelling
        Vector3 d = transform.position - _lastPos;
        _lastPos = transform.position;
        d.y = 0f;
        float v = Time.deltaTime > 0.0001f ? d.magnitude / Time.deltaTime : 0f;
        _proxySpeed = Mathf.Lerp(_proxySpeed, v, 12f * Time.deltaTime);
        _motor.proxySpeed = _proxySpeed;
    }

    void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        byte p = (byte)_motor.CurrentPose;
        if (netPose.Value != p) netPose.Value = p;
    }

    /// <summary>Everything a team change means visually, on every peer: red skin comes
    /// separately via FillClientRpc; here it's stance, gun prop, gun logic and rig UI.</summary>
    void ApplyTeam(Team team)
    {
        _ch.team = team;
        if (team != Team.Hunter) return;

        _motor.SetAiming(true);
        MatchManager.AddGunVisual(_ch);
        if ((IsServer || _ch.isPlayer) && GetComponent<Shotgun>() == null)
            gameObject.AddComponent<Shotgun>();

        var rig = GetComponent<PlayerRig>();
        if (rig != null) rig.SetTeam(team);
    }

    // ---------------- teleport (authority must move owner-authoritative transforms) ----------------

    public void RequestTeleport(Vector3 pos)
    {
        if (!IsServer) return;
        if (IsOwner) TeleportLocal(pos);
        else TeleportOwnerRpc(pos);
    }

    [Rpc(SendTo.Owner)]
    void TeleportOwnerRpc(Vector3 pos) { TeleportLocal(pos); }

    void TeleportLocal(Vector3 pos)
    {
        Vector3 target = pos + Vector3.up * 0.05f;
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.position = target;
        if (cc != null) cc.enabled = true;
        var nt = GetComponent<OwnerNetworkTransform>();
        if (nt != null) nt.Teleport(target, transform.rotation, transform.localScale);
    }

    // ---------------- paint: owner strokes ----------------

    /// <summary>Owner-side entry: stamp locally for zero latency, then relay.</summary>
    public void SubmitPaintAt(Vector2 uv, Color color, float radiusUV, float hardness)
    {
        _ch.skin.PaintAt(uv, color, radiusUV, hardness);
        PaintAtServerRpc(uv, color, radiusUV, hardness, NetworkManager.LocalClientId);
    }

    public void SubmitClear()
    {
        _ch.skin.Clear();
        ClearServerRpc(NetworkManager.LocalClientId);
    }

    [Rpc(SendTo.Server, RequireOwnership = true)]
    void PaintAtServerRpc(Vector2 uv, Color32 color, float radiusUV, float hardness, ulong sender)
    {
        PaintAtClientRpc(uv, color, radiusUV, hardness, sender);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void PaintAtClientRpc(Vector2 uv, Color32 color, float radiusUV, float hardness, ulong sender)
    {
        if (NetworkManager.LocalClientId == sender) return; // originator already stamped
        _ch.skin.PaintAt(uv, color, radiusUV, hardness);
    }

    [Rpc(SendTo.Server, RequireOwnership = true)]
    void ClearServerRpc(ulong sender) { ClearClientRpc(sender); }

    [Rpc(SendTo.ClientsAndHost)]
    void ClearClientRpc(ulong sender)
    {
        if (NetworkManager.LocalClientId == sender) return;
        _ch.skin.Clear();
    }

    // ---------------- paint: server-side fills (hunter red, bot camo) ----------------
    // Server never applies locally by hand — the host receives its own ClientsAndHost
    // broadcast exactly once. (A future dedicated server needs a local apply here so
    // the hunter AI's AverageColor still works.)

    public void NetFill(Color color)
    {
        if (IsServer) FillClientRpc(color);
    }

    public void NetFillStripes(Color a, Color b, int stripes)
    {
        if (IsServer) FillStripesClientRpc(a, b, stripes);
    }

    public void NetFillCamo(Color baseColor)
    {
        if (IsServer) FillCamoClientRpc(baseColor, Random.Range(0, 100000));
    }

    [Rpc(SendTo.ClientsAndHost)]
    void FillClientRpc(Color32 color) { _ch.skin.Fill(color); }

    [Rpc(SendTo.ClientsAndHost)]
    void FillStripesClientRpc(Color32 a, Color32 b, int stripes) { _ch.skin.FillStripes(a, b, stripes); }

    [Rpc(SendTo.ClientsAndHost)]
    void FillCamoClientRpc(Color32 baseColor, int seed) { _ch.skin.FillCamo(baseColor, seed); }

    // ---------------- gameplay requests (owner → server) ----------------

    [Rpc(SendTo.Server, RequireOwnership = true)]
    public void TauntServerRpc()
    {
        if (MatchManager.Instance != null) MatchManager.Instance.DoTaunt(_ch);
    }

    [Rpc(SendTo.Server, RequireOwnership = true)]
    public void DecoyServerRpc()
    {
        if (MatchManager.Instance != null) MatchManager.Instance.SpawnDecoy(_ch);
    }

    /// <summary>Client hunter reports its local pellet result; the server sanity-checks
    /// range/teams before converting (good enough for a friendly test build).</summary>
    [Rpc(SendTo.Server, RequireOwnership = true)]
    public void HunterFireServerRpc(NetworkObjectReference victimRef, bool hasVictim, ulong sender)
    {
        if (MatchManager.Instance == null) return;
        if (_ch.team != Team.Hunter) return;
        if (Time.time < _lastFireAt + 0.8f) return; // server-side rate limit
        _lastFireAt = Time.time;

        ShootFxClientRpc(sender);

        if (!hasVictim) return;
        NetworkObject vo;
        if (!victimRef.TryGet(out vo)) return;
        var victim = vo.GetComponent<Character>();
        if (victim == null || victim.team != Team.Hider) return;
        if (Vector3.Distance(victim.transform.position, transform.position) > 20f) return;
        MatchManager.Instance.Convert(victim);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ShootFxClientRpc(ulong sender)
    {
        if (NetworkManager.LocalClientId == sender) return; // shooter already played it
        _motor.TriggerShoot();
    }

    /// <summary>Server-side fire (bots, host human): replicate the shoot anim to others.</summary>
    public void ServerShootFx()
    {
        if (IsServer) ShootFxClientRpc(NetworkManager.LocalClientId);
    }

    /// <summary>Server → everyone: taunt emote + the floating "!" marker.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void TauntFxClientRpc()
    {
        _motor.PlayTauntAnim();
        if (MatchManager.Instance != null) MatchManager.Instance.SpawnTauntMarker(_ch);
    }

    /// <summary>Server → the owning client: your style score total changed.</summary>
    [Rpc(SendTo.Owner)]
    public void ScoreClientRpc(float total)
    {
        if (MatchManager.Instance != null) MatchManager.Instance.OnLocalScore(total);
    }
}
