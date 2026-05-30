using Sirenix.OdinInspector;
using UnityEngine;

namespace MotionCharacterController
{
    [RequireComponent(typeof(CapsuleCollider))]
    public partial class MotionCC : MonoBehaviour
    {
        [BoxGroup("基础数据")]
        [SerializeField]
        private MccConfig config = new MccConfig();

        [BoxGroup("调试")]
        [ShowInInspector, ReadOnly]
        public bool isGrounded => GroundingStatus.IsStableOnGround;

        [SerializeField]
        private bool drawGroundGizmos = true;

        #region 变量
        private readonly MccMotorContext context = new MccMotorContext();
        private CollisionSolver collisionSolver;
        private GroundSolver groundSolver;
        private StepSolver stepSolver;
        private LedgeSolver ledgeSolver;
        private PlatformSolver platformSolver;
        private RigidbodySolver rigidbodySolver;
        private IMcc controller;
        private Vector3 inputDirection;
        private bool jumpRequested;
        #endregion

        #region 属性
        public MccConfig Config => config;
        public MccMotorContext Context => context;
        public IMcc Controller => controller;
        public CharacterGroundingReport GroundingStatus => context.GroundingStatus;
        public CharacterTransientGroundingReport LastGroundingStatus => context.LastGroundingStatus;
        public Vector3 InputDirection => inputDirection;
        public bool JumpRequested => jumpRequested;
        public Vector3 TransientPosition => context.TransientPosition;
        public Quaternion TransientRotation => context.TransientRotation;
        public Vector3 CharacterUp => context.CharacterUp;
        public Vector3 CharacterForward => context.CharacterForward;
        public Vector3 CharacterRight => context.CharacterRight;
        public Vector3 Velocity => context.BaseVelocity + context.AttachedRigidbodyVelocity;
        public Rigidbody AttachedRigidbody => context.AttachedRigidbody;
        public Vector3 AttachedRigidbodyVelocity => context.AttachedRigidbodyVelocity;
        public CapsuleCollider Capsule => context.Capsule;
        public LayerMask CollidableLayers => context.CollidableLayers;
        public int OverlapsCount => context.OverlapsCount;
        public OverlapResult[] Overlaps => context.Overlaps;

        public Vector3 BaseVelocity
        {
            get => context.BaseVelocity;
            set => context.BaseVelocity = value;
        }
        #endregion

        /// <summary>
        /// 在Inspector中修改时触发 ValidateData 校验数据
        /// </summary>
        private void OnValidate()
        {
            ValidateData();
        }

        private void Awake()
        {
            ValidateData();
            InitializeSolvers();
            FindController();
        }

        private void OnEnable()
        {
            MccSystem.RegisterCharacter(this);
        }

        private void OnDisable()
        {
            MccSystem.UnregisterCharacter(this);
        }

        private void Update()
        {
            FindController();
            controller?.InputVectorUpdate(ref inputDirection, ref jumpRequested);
        }

        internal void PreSimulationTick(float deltaTime)
        {
            context.InitialTickPosition = context.TransientPosition;
            context.InitialTickRotation = context.TransientRotation;
            context.Transform.SetPositionAndRotation(context.TransientPosition, context.TransientRotation);
        }

        internal void UpdatePhase1(float deltaTime)
        {
            FindController();
            ValidateData();
            context.BeginSimulation();
            context.SanitizeVelocity();
            rigidbodySolver.Clear();

            controller?.BeforeCharacterUpdate(deltaTime);

            if (context.MovePositionDirty)
            {
                Vector3 moveVelocity = GetVelocityFromMovement(context.MovePositionTarget - context.TransientPosition, deltaTime);
                if (context.SolveMovementCollisions)
                {
                    collisionSolver.Move(ref moveVelocity, deltaTime);
                }
                else
                {
                    context.TransientPosition = context.MovePositionTarget;
                }
                context.MovePositionDirty = false;
            }

            if (context.SolveMovementCollisions)
            {
                collisionSolver.ResolveInitialOverlaps();
            }

            groundSolver.UpdateGrounding(deltaTime);
            controller?.PostGroundingUpdate(deltaTime);
            platformSolver.UpdateAttachment(deltaTime);
        }

        internal void UpdatePhase2(float deltaTime)
        {
            Quaternion rotation = context.TransientRotation;
            controller?.UpdateRotation(ref rotation, deltaTime);
            context.TransientRotation = rotation.normalized;

            if (context.MoveRotationDirty)
            {
                context.TransientRotation = context.MoveRotationTarget.normalized;
                context.MoveRotationDirty = false;
            }

            if (context.SolveMovementCollisions)
            {
                collisionSolver.ResolveInitialOverlaps();
            }

            controller?.UpdateVelocity(ref context.BaseVelocity, deltaTime);

            if (context.BaseVelocity.magnitude < MccConfig.MIN_VELOCITY_MAGNITUDE)
            {
                context.BaseVelocity = Vector3.zero;
            }

            if (context.BaseVelocity.sqrMagnitude > 0f)
            {
                if (context.SolveMovementCollisions)
                {
                    collisionSolver.Move(ref context.BaseVelocity, deltaTime);
                }
                else
                {
                    context.TransientPosition += context.BaseVelocity * deltaTime;
                }
            }

            rigidbodySolver.ProcessVelocityForHits(ref context.BaseVelocity, deltaTime);
            collisionSolver.ProcessDiscreteCollisionEvents();
            controller?.AfterCharacterUpdate(deltaTime);
        }

        /// <summary>
        /// 提交模拟
        /// </summary>
        /// <param name="interpolate">是否插值</param>
        internal void CommitSimulation(bool interpolate)
        {
            if (interpolate)
            {
                context.Transform.SetPositionAndRotation(context.InitialTickPosition, context.InitialTickRotation);
            }
            else
            {
                context.Transform.SetPositionAndRotation(context.TransientPosition, context.TransientRotation);
            }
        }

        /// <summary>
        /// 插值更新
        /// </summary>
        /// <param name="factor">插值系数</param>
        internal void InterpolationUpdate(float factor)
        {
            // 插值位置和旋转
            context.Transform.SetPositionAndRotation(
                Vector3.Lerp(context.InitialTickPosition, context.TransientPosition, factor),
                Quaternion.Slerp(context.InitialTickRotation, context.TransientRotation, factor));
        }

        private void ValidateData()
        {
            if (context.Transform == null)
            {
                context.Transform = transform;
            }

            context.Capsule = GetComponent<CapsuleCollider>();
            context.Config = config;
            context.Owner = this;
            context.Transform.localScale = Vector3.one;

            if (context.Capsule != null)
            {
                SetCapsuleDimensions(config.capsuleRadius, config.capsuleHeight, config.capsuleYOffset);
                context.Capsule.direction = 1;
                context.Capsule.sharedMaterial = config.capsulePhysicsMaterial;
            }

            context.RefreshCollidableLayers();
            context.RefreshCharacterAxes();
        }

        /// <summary>
        /// 初始化求解器
        /// </summary>
        private void InitializeSolvers()
        {
            if (collisionSolver is not null)
                return;

            stepSolver = new StepSolver(context);
            ledgeSolver = new LedgeSolver(context);
            rigidbodySolver = new RigidbodySolver(context);
            collisionSolver = new CollisionSolver(context, stepSolver, rigidbodySolver);
            groundSolver = new GroundSolver(context, collisionSolver, stepSolver, ledgeSolver);
            
            stepSolver.Bind(collisionSolver, groundSolver);
            ledgeSolver.Bind(collisionSolver);

            platformSolver = new PlatformSolver(context, collisionSolver);
        }

        private void FindController()
        {
            if (controller == null)
            {
                controller = GetComponent<IMcc>();
            }
        }
    }
}
