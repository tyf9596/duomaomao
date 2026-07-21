using Unity.Netcode.Components;

/// <summary>
/// Owner-authoritative NetworkTransform (the stock one is server-authoritative).
/// Characters are driven by whoever owns them — each human moves their own body,
/// the host moves the bots — and everyone else sees an interpolated replica.
/// Party-game trust model: client-side movement buys zero perceived input latency;
/// the server still owns every match rule (conversion, scoring, phases).
/// </summary>
public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
