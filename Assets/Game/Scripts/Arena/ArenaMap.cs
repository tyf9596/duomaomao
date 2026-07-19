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
    [Tooltip("Reject floor points above this height — keeps spawns off unreachable rooftops on multi-storey maps.")]
    public float maxSpawnY = 100f;

    [Header("Match overrides (0 = MatchManager default)")]
    public int characterCountOverride;
    public float hideSecondsOverride;
    public float seekSecondsOverride;

    // 32: one column through the Mansion can cross roof + 2 floors + furniture + basement
    static readonly RaycastHit[] SpawnBuf = new RaycastHit[32];

    /// <summary>
    /// Random position on a floor inside the play area. The ray pierces ALL geometry in
    /// the column and picks a random valid storey (reservoir sample), so basements,
    /// ground floors, upper floors and balconies all receive spawns. A point is valid
    /// when it faces up, sits below maxSpawnY (rooftops), and has standing headroom.
    /// </summary>
    public Vector3 RandomPointOnFloor()
    {
        for (int i = 0; i < 24; i++)
        {
            var p = new Vector3(
                areaCenter.x + (Random.value - 0.5f) * areaSize.x,
                areaCenter.y + 24f,
                areaCenter.z + (Random.value - 0.5f) * areaSize.y);
            int n = Physics.RaycastNonAlloc(new Ray(p, Vector3.down), SpawnBuf, 40f);
            int picked = -1, seen = 0;
            for (int h = 0; h < n; h++)
            {
                var hit = SpawnBuf[h];
                if (hit.normal.y <= floorNormalMinY) continue;
                if (hit.point.y > maxSpawnY) continue;
                // a character must fit standing here (rejects wall tops under ceilings,
                // closed voids, and points inside furniture)
                if (Physics.CheckCapsule(hit.point + Vector3.up * 0.45f, hit.point + Vector3.up * 1.1f, 0.2f)) continue;
                seen++;
                if (Random.Range(0, seen) == 0) picked = h;
            }
            if (picked >= 0) return SpawnBuf[picked].point;
        }
        return areaCenter; // pathological fallback
    }
}
