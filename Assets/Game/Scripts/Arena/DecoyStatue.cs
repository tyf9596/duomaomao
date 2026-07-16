using UnityEngine;

/// <summary>
/// Scene marker that spawns a painted, posed mannequin at runtime — scenery that is
/// deliberately shaped like a player. The reference game's maps are packed with
/// character-shaped props (statues, scarecrows, plushes), so a posed hider reads as
/// "just another prop"; these decoys plant the same doubt in hunters here. The
/// marker only holds tuning fields; model, paint and pose are built in code.
/// </summary>
public class DecoyStatue : MonoBehaviour
{
    public enum Paint { Stone, MatchGround, Stripes }

    [Tooltip("Blocky character variant a..r; empty = random")]
    public string variant = "";
    public Pose pose = Pose.Statue;
    public Paint paint = Paint.Stone;
    public Color colorA = new Color(0.62f, 0.63f, 0.66f); // stone grey / stripe A
    public Color colorB = Color.white;                     // stripe B
    public int stripeCount = 20;

    static readonly RaycastHit[] HitBuf = new RaycastHit[16];

    void Awake()
    {
        // Sample the ground first, while this object still has no colliders of its own.
        Color g1, g2;
        bool s1 = SampleDown(transform.position + Vector3.up * 0.5f, out g1);
        bool s2 = SampleDown(transform.position + Vector3.up * 0.5f + transform.right * 0.4f, out g2);

        string v = string.IsNullOrEmpty(variant) ? ((char)('a' + Random.Range(0, 18))).ToString() : variant;
        var prefab = Resources.Load<GameObject>("Characters/character-" + v);
        if (prefab == null) return;

        var body = Instantiate(prefab, transform);
        body.name = "Body";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one * 0.5f; // same 1.35m silhouette as players
        foreach (var mf in body.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.GetComponent<Collider>() == null)
            {
                var col = mf.gameObject.AddComponent<MeshCollider>();
                col.sharedMesh = mf.sharedMesh;
            }
        }

        var ctrl = Resources.Load<RuntimeAnimatorController>("CharacterAnimator");
        if (ctrl != null)
        {
            var anim = body.GetComponent<Animator>();
            if (anim == null) anim = body.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.SetFloat("Speed", 0f);
            anim.SetInteger("Pose", (int)pose);
        }
        if (pose == Pose.Lie)
        {
            // same procedural plank as CharacterMotor.SetPose
            body.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            body.transform.localPosition = new Vector3(0f, 0.37f, 0.25f) * 0.5f;
        }

        var skin = body.AddComponent<PaintableBody>();
        switch (paint)
        {
            case Paint.Stone:
                skin.FillCamo(colorA);
                break;
            case Paint.MatchGround:
                if (s1 && s2 && ColorDiff(g1, g2) > 0.25f) skin.FillStripes(g1, g2, Random.Range(16, 28));
                else if (s1) skin.FillCamo(g1);
                else skin.FillCamo(colorA);
                break;
            case Paint.Stripes:
                skin.FillStripes(colorA, colorB, stripeCount);
                break;
        }
    }

    static bool SampleDown(Vector3 origin, out Color color)
    {
        color = Color.gray;
        int n = Physics.RaycastNonAlloc(new Ray(origin, Vector3.down), HitBuf, 3f);
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            if (Character.FromCollider(HitBuf[i].collider) != null) continue;
            if (best < 0 || HitBuf[i].distance < HitBuf[best].distance) best = i;
        }
        if (best < 0) return false;
        return PaintableBody.SampleSurfaceColor(HitBuf[best], out color);
    }

    static float ColorDiff(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.7f, 0.5f, 0.9f, 0.8f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.675f, new Vector3(0.5f, 1.35f, 0.5f));
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.6f);
    }
}
