using MotionCharacterController;
using UnityEngine;

public class PlayerController : MonoBehaviour, IMcc
{
    [SerializeField] private ExampleMCharacterCamera characterCamera;
    [SerializeField, Range(0f, 1f)] private float inputDeadZone = 0.1f;

    private MotionCC motion;
    private Vector3 moveInput;
    private bool jumpRequested;

    private void Awake()
    {
        motion = GetComponent<MotionCC>();

        if (characterCamera == null)
            characterCamera = FindFirstObjectByType<ExampleMCharacterCamera>();

        if (characterCamera != null)
            characterCamera.SetFollowTarget(transform);
    }

    public void InputVectorUpdate(ref Vector3 inputDirection, ref bool jumpRequested)
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 rawInput = new Vector3(h, 0f, v);

        if (characterCamera != null)
            moveInput = characterCamera.ConvertInputToWorld(rawInput, Vector3.up);
        else
            moveInput = rawInput.sqrMagnitude > 1f ? rawInput.normalized : rawInput;

        inputDirection = moveInput;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            this.jumpRequested = true;
            jumpRequested = true;
        }
    }

    public void BeforeCharacterUpdate(float deltaTime)
    {
    }

    public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
    {
        if (motion == null)
        {
            return;
        }

        MccConfig config = motion.Config;
        Vector3 up = motion.CharacterUp;
        Vector3 input = Vector3.ProjectOnPlane(moveInput, up);
        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        if (motion.GroundingStatus.IsStableOnGround)
        {
            currentVelocity = motion.GetDirectionTangentToSurface(currentVelocity, motion.GroundingStatus.GroundNormal) * currentVelocity.magnitude;

            Vector3 targetVelocity = Vector3.zero;
            if (HasMoveInput(input))
            {
                Vector3 inputRight = Vector3.Cross(input, up);
                Vector3 reorientedInput = Vector3.Cross(motion.GroundingStatus.GroundNormal, inputRight).normalized * input.magnitude;
                targetVelocity = reorientedInput * config.moveSpeed;
            }

            currentVelocity = Vector3.Lerp(
                currentVelocity,
                targetVelocity,
                1f - Mathf.Exp(-config.stableMovementSharpness * deltaTime));

            if (jumpRequested)
            {
                // 对齐 KCC 先清竖直分量再施加跳跃 并强制离地
                motion.ForceUnground();
                currentVelocity += (up * config.jumpSpeed) - Vector3.Project(currentVelocity, up);
                jumpRequested = false;
                motion.ConsumeJumpRequest();
            }

            return;
        }

        if (HasMoveInput(input))
        {
            Vector3 addedVelocity = input * config.AirAcceleration * deltaTime;
            Vector3 currentPlanarVelocity = Vector3.ProjectOnPlane(currentVelocity, up);
            float airSpeedLimit = config.maxAirMoveSpeed;
            if (currentPlanarVelocity.magnitude < airSpeedLimit)
            {
                Vector3 newTotal = Vector3.ClampMagnitude(currentPlanarVelocity + addedVelocity, airSpeedLimit);
                addedVelocity = newTotal - currentPlanarVelocity;
            }
            else if (Vector3.Dot(currentPlanarVelocity, addedVelocity) > 0f)
            {
                addedVelocity = Vector3.ProjectOnPlane(addedVelocity, currentPlanarVelocity.normalized);
            }

            currentVelocity += addedVelocity;
        }

        currentVelocity += up * config.gravity * deltaTime;
        currentVelocity *= 1f / (1f + config.airDrag * deltaTime);
        jumpRequested = false;
    }

    private bool HasMoveInput(Vector3 input)
    {
        return input.sqrMagnitude > inputDeadZone * inputDeadZone;
    }

    public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
    {
        if (moveInput.sqrMagnitude < 0.0001f)
            return;

        Quaternion target = Quaternion.LookRotation(moveInput, Vector3.up);
        float rotationSpeed = motion != null ? motion.Config.rotationSpeed : 10f;
        currentRotation = Quaternion.Slerp(currentRotation, target, deltaTime * rotationSpeed);
    }

    public void PostGroundingUpdate(float deltaTime)
    {
    }

    public void AfterCharacterUpdate(float deltaTime)
    {
    }

    public bool IsColliderValidForCollisions(Collider coll)
    {
        return true;
    }

    public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
    {
    }

    public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
    {
    }

    public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport)
    {
    }

    public void OnDiscreteCollisionDetected(Collider hitCollider)
    {
    }

}
