using MotionCharacterController;
using UnityEngine;

/// <summary>
/// Unity 原生 CharacterController 对比用控制器。
/// 操作与 PlayerController 一致：WASD 移动、空格跳跃、同一套第三人称相机。
/// 用法：角色上挂 CharacterController + 本脚本，不要同时挂 MotionCC / PlayerController。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class NormalCc : MonoBehaviour
{
    [Header("相机")]
    [SerializeField] private ExampleMCharacterCamera characterCamera;

    [Header("移动（默认值对齐 MccConfig 示例）")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float groundSharpness = 15f;
    [SerializeField] private float airAcceleration = 15f;
    [SerializeField] private float jumpSpeed = 8f;
    [SerializeField] private float gravity = -25f;
    [SerializeField] private float airDrag = 0.1f;
    [SerializeField, Range(0f, 1f)] private float inputDeadZone = 0.1f;

    private CharacterController controller;
    private Vector3 moveInput;
    private Vector3 velocity;

    private void Reset()
    {
        CharacterController cc = GetComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.5f;
        cc.center = new Vector3(0f, 1f, 0f);
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (characterCamera == null)
            characterCamera = FindFirstObjectByType<ExampleMCharacterCamera>();

        if (characterCamera != null)
            characterCamera.SetFollowTarget(transform);
    }

    private void Update()
    {
        ReadInput();
        UpdateVelocity(Time.deltaTime);
        controller.Move(velocity * Time.deltaTime);
        UpdateRotation(Time.deltaTime);
    }

    private void ReadInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 rawInput = new Vector3(h, 0f, v);

        if (characterCamera != null)
            moveInput = characterCamera.ConvertInputToWorld(rawInput, Vector3.up);
        else
            moveInput = rawInput.sqrMagnitude > 1f ? rawInput.normalized : rawInput;
    }

    private void UpdateVelocity(float deltaTime)
    {
        Vector3 up = Vector3.up;
        Vector3 input = Vector3.ProjectOnPlane(moveInput, up);
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        bool grounded = controller.isGrounded && velocity.y <= 0f;

        if (grounded)
        {
            Vector3 targetVelocity = HasMoveInput(input) ? input * moveSpeed : Vector3.zero;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, up);
            planarVelocity = Vector3.Lerp(
                planarVelocity,
                targetVelocity,
                1f - Mathf.Exp(-groundSharpness * deltaTime));

            velocity = planarVelocity;
            velocity.y = -2f;

            if (Input.GetKeyDown(KeyCode.Space))
                velocity.y = jumpSpeed;

            return;
        }

        if (HasMoveInput(input))
        {
            Vector3 addedVelocity = input * airAcceleration * deltaTime;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, up);
            planarVelocity = Vector3.ClampMagnitude(planarVelocity + addedVelocity, moveSpeed);
            velocity = planarVelocity + Vector3.Project(velocity, up);
        }

        velocity += up * gravity * deltaTime;
        velocity *= 1f / (1f + airDrag * deltaTime);
    }

    private void UpdateRotation(float deltaTime)
    {
        if (moveInput.sqrMagnitude < 0.0001f)
            return;

        Quaternion target = Quaternion.LookRotation(moveInput, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, deltaTime * rotationSpeed);
    }

    private bool HasMoveInput(Vector3 input)
    {
        return input.sqrMagnitude > inputDeadZone * inputDeadZone;
    }
}
