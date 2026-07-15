using UnityEngine;

public enum Team { Hider, Hunter }

/// <summary>
/// Identity of one match participant (human or bot) and the factory that builds the
/// whole rig in code: CharacterController + motor on the root, and a Kenney blocky
/// character (six rigid paintable parts, 27 animation clips) as the visual body with
/// MeshColliders for UV painting + shot detection. Falls back to the white capsule
/// when the model or its AnimatorController can't be loaded. Scenes contain no
/// character objects at edit time.
/// </summary>
public class Character : MonoBehaviour
{
    public Team team = Team.Hider;
    public bool isPlayer;
    public string displayName = "Bot";

    [HideInInspector] public CharacterMotor motor;
    [HideInInspector] public PaintableBody skin;
    [HideInInspector] public Collider bodyCollider;

    public Vector3 EyePos => transform.position + Vector3.up * 1.15f;

    public static Character Create(string name, Vector3 pos, bool isPlayer)
    {
        var root = new GameObject(name);
        root.transform.position = pos;

        var cc = root.AddComponent<CharacterController>();
        cc.radius = 0.24f;
        cc.height = 1.35f;
        cc.center = new Vector3(0f, 0.675f, 0f);
        cc.slopeLimit = 50f;
        cc.stepOffset = 0.32f;

        // Visual body: an animated blocky character (or capsule fallback). Its part
        // meshes get MeshColliders for painting UVs and pellet hits; the CC capsule
        // handles locomotion.
        GameObject body = null;
        Animator anim = null;
        string variant = ((char)('a' + Random.Range(0, 18))).ToString();
        var prefab = Resources.Load<GameObject>("Characters/character-" + variant);
        if (prefab != null)
        {
            body = Instantiate(prefab, root.transform);
            body.name = "Body";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one * 0.5f; // 2.7m rig -> 1.35m
            foreach (var mf in body.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.GetComponent<Collider>() == null)
                {
                    var partCol = mf.gameObject.AddComponent<MeshCollider>();
                    partCol.sharedMesh = mf.sharedMesh;
                }
            }
            var ctrl = Resources.Load<RuntimeAnimatorController>("CharacterAnimator");
            if (ctrl != null)
            {
                anim = body.GetComponent<Animator>();
                if (anim == null) anim = body.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
            }
        }
        else
        {
            body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.675f, 0f);
            body.transform.localScale = new Vector3(0.48f, 0.675f, 0.48f);
            var mc = body.AddComponent<MeshCollider>();
            mc.sharedMesh = body.GetComponent<MeshFilter>().sharedMesh;
            MakeEye(body.transform, new Vector3(0.18f, 0.78f, 0.38f));
            MakeEye(body.transform, new Vector3(-0.18f, 0.78f, 0.38f));
        }

        var skin = body.AddComponent<PaintableBody>();

        var motor = root.AddComponent<CharacterMotor>();
        motor.body = body.transform;
        motor.anim = anim;

        var ch = root.AddComponent<Character>();
        ch.isPlayer = isPlayer;
        ch.displayName = name;
        ch.motor = motor;
        ch.skin = skin;
        ch.bodyCollider = body.GetComponentInChildren<Collider>();
        return ch;
    }

    static void MakeEye(Transform body, Vector3 localPos)
    {
        var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        eye.name = "Eye";
        Destroy(eye.GetComponent<Collider>());
        eye.transform.SetParent(body, false);
        eye.transform.localPosition = localPos;
        eye.transform.localScale = new Vector3(0.22f, 0.16f, 0.16f);
        var r = eye.GetComponent<Renderer>();
        r.material.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.1f));
    }

    /// <summary>Find the Character a collider belongs to (pellets hit body parts or CC).</summary>
    public static Character FromCollider(Collider col)
    {
        return col != null ? col.GetComponentInParent<Character>() : null;
    }
}
