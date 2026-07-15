using System.Collections;
using UnityEngine;

/// <summary>
/// The hunter's tool: a cone of pellet raycasts with a cooldown. Returns the first
/// hider any pellet touched (hunters are immune — no friendly fire). Pellet impacts
/// flash briefly so both sides can read where the shot went.
/// </summary>
public class Shotgun : MonoBehaviour
{
    public float cooldown = 1.1f;
    public float range = 14f;
    public int pellets = 7;
    public float spreadDegrees = 5f;

    static Material _impactMat;
    static readonly RaycastHit[] HitBuf = new RaycastHit[24];

    float _readyAt;

    public bool CanFire => Time.time >= _readyAt;

    /// <summary>Fire from origin along dir; returns the hider hit, or null.</summary>
    public Character Fire(Vector3 origin, Vector3 dir, Character owner)
    {
        if (!CanFire) return null;
        _readyAt = Time.time + cooldown;

        Character victim = null;
        for (int i = 0; i < pellets; i++)
        {
            Vector2 s = Random.insideUnitCircle * spreadDegrees;
            Vector3 d = Quaternion.LookRotation(dir) * Quaternion.Euler(s.y, s.x, 0f) * Vector3.forward;

            int n = Physics.RaycastNonAlloc(new Ray(origin, d), HitBuf, range);
            int best = -1;
            for (int h = 0; h < n; h++)
            {
                var ch = Character.FromCollider(HitBuf[h].collider);
                if (ch == owner) continue; // skip the shooter's own capsule
                if (best < 0 || HitBuf[h].distance < HitBuf[best].distance) best = h;
            }
            if (best < 0) continue;

            StartCoroutine(ImpactFlash(HitBuf[best].point));
            var hitChar = Character.FromCollider(HitBuf[best].collider);
            if (victim == null && hitChar != null && hitChar.team == Team.Hider)
                victim = hitChar;
        }
        return victim;
    }

    IEnumerator ImpactFlash(Vector3 point)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "PelletImpact";
        go.transform.position = point;
        go.transform.localScale = Vector3.one * 0.07f;
        var r = go.GetComponent<Renderer>();
        if (_impactMat == null)
        {
            _impactMat = new Material(r.sharedMaterial);
            if (_impactMat.HasProperty("_BaseColor")) _impactMat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.25f));
            else _impactMat.color = new Color(1f, 0.85f, 0.25f);
        }
        r.sharedMaterial = _impactMat;
        yield return new WaitForSeconds(0.25f);
        if (go != null) Destroy(go);
    }
}
