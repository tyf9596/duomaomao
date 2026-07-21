using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Replicated match state — the one NetworkObject the host spawns per match.
/// The host's MatchManager stays the single authority and writes these variables;
/// every peer's MatchManager renders its HUD from them (the host keeps its local
/// fast path and ignores the callbacks). Per-viewer phrasing ("YOU ARE THE HUNTER")
/// is resolved client-side from structured RPCs, never baked into synced strings.
/// </summary>
public class MatchNet : NetworkBehaviour
{
    public static MatchNet Instance { get; private set; }

    public readonly NetworkVariable<byte> phase = new NetworkVariable<byte>();
    public readonly NetworkVariable<byte> travelKind = new NetworkVariable<byte>(); // 0 = hiders deploy, 1 = hunter entry
    public readonly NetworkVariable<double> phaseEndsAt = new NetworkVariable<double>(); // ServerTime seconds
    public readonly NetworkVariable<int> totalSlots = new NetworkVariable<int>();
    public readonly NetworkVariable<int> hidersLeft = new NetworkVariable<int>();
    public readonly NetworkVariable<FixedString128Bytes> lobbyTitle = new NetworkVariable<FixedString128Bytes>();
    public readonly NetworkVariable<FixedString512Bytes> roster = new NetworkVariable<FixedString512Bytes>();

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (MatchManager.Instance != null) MatchManager.Instance.AttachNet(this);
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    [Rpc(SendTo.NotServer)]
    public void BannerRpc(FixedString128Bytes text, bool red, bool huntersOnly, float seconds)
    {
        if (MatchManager.Instance == null) return;
        MatchManager.Instance.OnNetBanner(text.ToString(), red, huntersOnly, seconds);
    }

    /// <summary>The roulette result: each viewer decides whether that's THEM.</summary>
    [Rpc(SendTo.NotServer)]
    public void HunterRevealRpc(NetworkObjectReference hunterRef)
    {
        if (MatchManager.Instance == null) return;
        NetworkObject no;
        Character hunter = null;
        if (hunterRef.TryGet(out no)) hunter = no.GetComponent<Character>();
        MatchManager.Instance.OnNetHunterReveal(hunter);
    }

    [Rpc(SendTo.NotServer)]
    public void ResultRpc(bool huntersWin, FixedString512Bytes topScores)
    {
        if (MatchManager.Instance == null) return;
        MatchManager.Instance.OnNetResult(huntersWin, topScores.ToString());
    }
}
