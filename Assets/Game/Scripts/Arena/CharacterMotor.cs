using UnityEngine;

// Stand=idle · Crouch=sit-on-ground clip · Statue=mannequin stance · Lie=plank (procedural)
// Scarecrow=arms-forward holding stance · Chair=seated driving stance
public enum Pose { Stand, Crouch, Statue, Lie, Scarecrow, Chair }

/// <summary>
/// Input-agnostic character movement: walk, dash, jump, wall-climb and posing.
/// A driver (PlayerRig or BotBrain) writes the desired* fields every frame;
/// the motor consumes them here. Movement input breaks the current pose.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CharacterMotor : MonoBehaviour
{
    [Header("Speeds")]
    public float walkSpeed = 3.2f;
    public float dashSpeed = 7f;
    public float dashDuration = 0.8f;
    public float dashCooldown = 3f;
    public float turnSpeed = 14f;      // slerp factor toward move direction
    public float gravity = -22f;
    public float jumpSpeed = 6.5f;
    public float climbSpeed = 2.2f;
    public float climbCheckDistance = 0.55f;

    [Header("Wiring (set by factory)")]
    public Transform body;             // visual child; poses transform it (capsule fallback)
    public Animator anim;              // optional; when present, poses/locomotion are animation states

    // --- driver inputs (world space, consumed every Update) ---
    [HideInInspector] public Vector3 desiredMove;   // magnitude 0..1
    [HideInInspector] public bool wantDash;         // edge-triggered
    [HideInInspector] public bool wantJumpHold;     // held: climb on walls, jump on press
    [HideInInspector] public bool wantJumpPressed;  // edge-triggered
    [HideInInspector] public bool movementLocked;   // paint mode / hunter waiting
    [HideInInspector] public bool faceLocked;       // first-person: the driver owns facing

    public Pose CurrentPose { get; private set; } = Pose.Stand;
    public bool IsDashing => Time.time < _dashUntil;
    public bool IsClimbing { get; private set; }
    public Vector3 Velocity => _velocity;

    CharacterController _cc;
    Vector3 _velocity;
    float _dashUntil = -10f;
    float _dashReadyAt;
    Vector3 _dashDir;
    Vector3 _bodyBasePos;
    Quaternion _bodyBaseRot;
    Vector3 _bodyBaseScale;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (body != null)
        {
            _bodyBasePos = body.localPosition;
            _bodyBaseRot = body.localRotation;
            _bodyBaseScale = body.localScale;
        }
    }

    void Update()
    {
        if (movementLocked)
        {
            // stay grounded but ignore all input
            _velocity.x = 0f; _velocity.z = 0f;
            _velocity.y += gravity * Time.deltaTime;
            _cc.Move(_velocity * Time.deltaTime);
            if (_cc.isGrounded) _velocity.y = -1f;
            ConsumeEdges();
            return;
        }

        Vector3 move = desiredMove;
        if (move.sqrMagnitude > 1f) move.Normalize();

        // Any real movement input breaks a pose.
        if (move.sqrMagnitude > 0.1f && CurrentPose != Pose.Stand) SetPose(Pose.Stand);

        // Dash
        if (wantDash && Time.time >= _dashReadyAt && move.sqrMagnitude > 0.01f)
        {
            _dashUntil = Time.time + dashDuration;
            _dashReadyAt = Time.time + dashCooldown;
            _dashDir = move.normalized;
        }

        // Climb: holding jump while pushing against a wall slides you up it.
        IsClimbing = false;
        Vector3 wallNormal = Vector3.zero;
        if (wantJumpHold && move.sqrMagnitude > 0.05f && CheckWall(move, out wallNormal))
        {
            IsClimbing = true;
            _velocity.y = climbSpeed;
            // press into the wall so we stay attached
            Vector3 into = -wallNormal * 0.6f;
            _cc.Move((into + Vector3.up * climbSpeed) * Time.deltaTime);
            FaceToward(-wallNormal);
            ConsumeEdges();
            return;
        }

        // Horizontal velocity
        Vector3 horizontal = IsDashing ? _dashDir * dashSpeed : move * walkSpeed;

        // Jump
        if (wantJumpPressed && _cc.isGrounded) _velocity.y = jumpSpeed;

        // Gravity
        _velocity.y += gravity * Time.deltaTime;
        _cc.Move((horizontal + Vector3.up * _velocity.y) * Time.deltaTime);
        if (_cc.isGrounded && _velocity.y < 0f) _velocity.y = -1f;

        if (horizontal.sqrMagnitude > 0.01f) FaceToward(horizontal);

        ConsumeEdges();
    }

    void LateUpdate()
    {
        if (anim == null) return;
        Vector3 v = _cc.velocity;
        v.y = 0f;
        anim.SetFloat("Speed", v.magnitude);
        anim.SetInteger("Pose", (int)CurrentPose);
    }

    public void SetAiming(bool on)
    {
        if (anim != null) anim.SetBool("Aiming", on);
    }

    public void TriggerShoot()
    {
        if (anim != null) anim.SetTrigger("Shoot");
    }

    void ConsumeEdges()
    {
        wantDash = false;
        wantJumpPressed = false;
    }

    void FaceToward(Vector3 dir)
    {
        if (faceLocked) return;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        var target = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    bool CheckWall(Vector3 moveDir, out Vector3 normal)
    {
        normal = Vector3.zero;
        Vector3 origin = transform.position + Vector3.up * (_cc.height * 0.5f);
        RaycastHit hit;
        if (Physics.Raycast(origin, moveDir.normalized, out hit, climbCheckDistance + _cc.radius))
        {
            if (hit.normal.y < 0.3f && hit.collider.GetComponentInParent<Character>() == null)
            {
                normal = hit.normal;
                return true;
            }
        }
        return false;
    }

    /// <summary>Cycle through all poses (editor P key; the touch UI picks directly).</summary>
    public void CyclePose()
    {
        SetPose((Pose)(((int)CurrentPose + 1) % 6));
    }

    public void SetPose(Pose pose)
    {
        CurrentPose = pose;
        if (body == null) return;

        if (anim != null)
        {
            // Sit/Statue come from animation states (Pose int in LateUpdate); the plank
            // is procedural: freeze in the statue stance and tip the whole body over.
            if (pose == Pose.Lie)
            {
                body.localRotation = _bodyBaseRot * Quaternion.Euler(-90f, 0f, 0f);
                // offset scales with the rig so the plank hugs the ground at any body size
                body.localPosition = _bodyBasePos + new Vector3(0f, 0.37f, 0.25f) * _bodyBaseScale.y;
            }
            else
            {
                body.localRotation = _bodyBaseRot;
                body.localPosition = _bodyBasePos;
            }
            return;
        }

        switch (pose)
        {
            case Pose.Stand:
            case Pose.Statue:
                body.localPosition = _bodyBasePos;
                body.localRotation = _bodyBaseRot;
                body.localScale = _bodyBaseScale;
                break;
            case Pose.Crouch:
                body.localScale = new Vector3(_bodyBaseScale.x * 1.15f, _bodyBaseScale.y * 0.55f, _bodyBaseScale.z * 1.15f);
                body.localPosition = new Vector3(_bodyBasePos.x, _bodyBasePos.y * 0.55f, _bodyBasePos.z);
                body.localRotation = _bodyBaseRot;
                break;
            case Pose.Lie:
                body.localScale = _bodyBaseScale;
                body.localRotation = _bodyBaseRot * Quaternion.Euler(90f, 0f, 0f);
                body.localPosition = new Vector3(_bodyBasePos.x, 0.3f, _bodyBasePos.z);
                break;
        }
    }
}
