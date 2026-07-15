using UnityEngine;

/// <summary>
/// Marker + playable-area definition for a PvP arena scene. MatchManager bootstraps
/// into any scene that contains one of these. Spawn/hide points are sampled from the
/// area at runtime so the scene needs no further wiring. Per-map match settings
/// (0 = use MatchManager defaults) let bigger maps run longer rounds with more bots.
/// </summary>
public class ArenaMap : MonoBehaviour
{
    public Vector3 areaCenter = Vector3.zero;
    public Vector2 areaSize = new Vector2(5.5f, 5.5f); // x/z extents of the walkable field

    [Header("Spawn sampling")]
    [Tooltip("Minimum surface normal Y for a valid floor point — raise to exclude sloped roofs.")]
    public float floorNormalMinY = 0.7f;

    [Header("Match overrides (0 = MatchManager default)")]
    public int characterCountOverride;
    public float hideSecondsOverride;
    public float seekSecondsOverride;

    /// <summary>Random position on the floor inside the play area (raycast down onto geometry).</summary>
    public Vector3 RandomPointOnFloor()
    {
        for (int i = 0; i < 20; i++)
        {
            var p = new Vector3(
                areaCenter.x + (Random.value - 0.5f) * areaSize.x,
                areaCenter.y + 8f,
                areaCenter.z + (Random.value - 0.5f) * areaSize.y);
            RaycastHit hit;
            if (Physics.Raycast(p, Vector3.down, out hit, 16f) && hit.normal.y > floorNormalMinY)
                return hit.point;
        }
        return areaCenter; // pathological fallback
    }
}
