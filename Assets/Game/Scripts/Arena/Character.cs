using UnityEngine;

public enum Team { Hider, Hunter }

/// <summary>
/// Identity of one match participant (human or bot) and the factory that builds the
/// whole rig in code: CharacterController + motor on the root, and a Kenney blocky
/// character (six rigid paintable parts, 27 animation clips) as the visual body with
/// MeshColliders for UV painting + shot detection. Falls back to the white capsule
/// when the model or its AnimatorController can't be loaded. Scenes contain no
/// character objects at edit time.
///
/// Two spawn paths share AttachVisual():
///   Create()    — offline/legacy: everything built on a fresh GameObject
///   CreateNet() — server-only: instantiates the registered NetCharacter prefab and
///                 spawns it; every peer attaches the visual in OnNetworkSpawn from
///                 the synced variant letter, so no mesh data crosses the wire.
/// The Skin* wrappers route paint operations through the net layer when one exists
/// (and straight to the local texture when offline) — callers never care which.
/// </summary>
public class Character : MonoBehaviour
{
    // Asymmetric sizes (user call 2026-07-21): hiders are small and easy to tuck away,
    // the hunter keeps the old full size and looms. Values are BODY scales on the 2.7m rig.
    public const float HiderScale = 0.4f;   // 1.08m
    public const float HunterScale = 0.5f;  // 1.35m

    public Team team = Team.Hider;
    public bool isPlayer;
    public bool isDecoy;          // shootable fake hider — pops without converting anyone
    public string displayName = "Bot";
    public string variant;        // blocky model letter a–r (decoys clone their owner's)
    public float bodyScale = HunterScale;   // current rig scale (ApplySize)

    [HideInInspector] public CharacterMotor motor;
    [HideInInspector] public PaintableBody skin;
    [HideInInspector] public Collider bodyCollider;
    [HideInInspector] public CharacterNetSync netSync; // null when offline

    public bool NetActive => netSync != null && netSync.IsSpawned;

    public Vector3 EyePos => transform.position + Vector3.up * (2.3f * bodyScale);

    /// <summary>Resize the whole rig (visual body + CharacterController). Safe to call
    /// mid-match — conversion grows a shot hider up to hunter size.</summary>
    public void ApplySize(float scale)
    {
        bodyScale = scale;
        var cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.height = 2.7f * scale;
            cc.radius = 0.48f * scale;
            cc.center = new Vector3(0f, 1.35f * scale, 0f);
        }
        if (motor != null && motor.body != null)
        {
            motor.body.localScale = Vector3.one * scale;
            motor.RecacheBodyBase();
        }
    }

    public static Character Create(string name, Vector3 pos, bool isPlayer)
    {
        return Create(name, pos, isPlayer, null);
    }

    public static Character Create(string name, Vector3 pos, bool isPlayer, string forceVariant)
    {
        var root = new GameObject(name);
        root.transform.position = pos;

        var cc = root.AddComponent<CharacterController>();
        cc.radius = 0.24f;
        cc.height = 1.35f;
        cc.center = new Vector3(0f, 0.675f, 0f);
        cc.slopeLimit = 50f;
        cc.stepOffset = 0.32f;

        root.AddComponent<CharacterMotor>();
        var ch = root.AddComponent<Character>();
        ch.isPlayer = isPlayer;
        ch.displayName = name;
        ch.variant = string.IsNullOrEmpty(forceVariant) ? RandomVariant() : forceVariant;
        AttachVisual(ch, ch.variant);
        return ch;
    }

    /// <summary>Server-only: spawn a replicated character. humanClientId = the owning
    /// player's connection (null for bots/decoys, which the host owns and drives).</summary>
    public static Character CreateNet(string name, Vector3 pos, string forceVariant, ulong? humanClientId, bool isDecoy)
    {
        var prefab = NetGame.CharacterPrefab;
        var go = Instantiate(prefab, pos, Quaternion.identity);
        var sync = go.GetComponent<CharacterNetSync>();
        sync.netVariant.Value = string.IsNullOrEmpty(forceVariant) ? RandomVariant() : forceVariant;
        sync.netName.Value = name;
        sync.netHumanClientId.Value = humanClientId.HasValue ? humanClientId.Value : ulong.MaxValue;
        sync.netIsDecoy.Value = isDecoy;
        var no = go.GetComponent<Unity.Netcode.NetworkObject>();
        if (humanClientId.HasValue) no.SpawnAsPlayerObject(humanClientId.Value, true);
        else no.Spawn(true);
        return go.GetComponent<Character>(); // visual already attached via OnNetworkSpawn
    }

    static string RandomVariant()
    {
        return ((char)('a' + Random.Range(0, 18))).ToString();
    }

    /// <summary>Build the visual body under an existing root (blocky model or capsule
    /// fallback) and wire motor/skin. Runs once per peer, never over the network.</summary>
    public static void AttachVisual(Character ch, string variant)
    {
        if (ch.motor == null) ch.motor = ch.GetComponent<CharacterMotor>();
        if (ch.motor != null && ch.motor.body != null) return; // already built

        var root = ch.gameObject;
        GameObject body = null;
        Animator anim = null;
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

        ch.skin = body.AddComponent<PaintableBody>();
        ch.motor.body = body.transform;
        ch.motor.anim = anim;
        ch.bodyCollider = body.GetComponentInChildren<Collider>();
    }

    // ---------------- paint routing (offline = direct, online = replicated) ----------------

    /// <summary>Owner-side brush stamp (self-paint mode).</summary>
    public void SkinPaintAt(Vector2 uv, Color color, float radiusUV, float hardness)
    {
        if (NetActive) netSync.SubmitPaintAt(uv, color, radiusUV, hardness);
        else skin.PaintAt(uv, color, radiusUV, hardness);
    }

    /// <summary>Owner-side reset to the white base skin.</summary>
    public void SkinClear()
    {
        if (NetActive) netSync.SubmitClear();
        else skin.Clear();
    }

    /// <summary>Server-side flat coat (hunter red).</summary>
    public void SkinFill(Color c)
    {
        if (NetActive) netSync.NetFill(c);
        else skin.Fill(c);
    }

    /// <summary>Server-side bot camo against patterned ground.</summary>
    public void SkinFillStripes(Color a, Color b, int stripes)
    {
        if (NetActive) netSync.NetFillStripes(a, b, stripes);
        else skin.FillStripes(a, b, stripes);
    }

    /// <summary>Server-side bot camo (seeded, so blotches match on every peer).</summary>
    public void SkinFillCamo(Color c)
    {
        if (NetActive) netSync.NetFillCamo(c);
        else skin.FillCamo(c);
    }

    /// <summary>Server-side: let other peers see a bot/host hunter's shoot animation.</summary>
    public void NetShootFx()
    {
        if (NetActive) netSync.ServerShootFx();
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
