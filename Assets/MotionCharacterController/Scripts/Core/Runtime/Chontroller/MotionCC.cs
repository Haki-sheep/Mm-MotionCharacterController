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
            MccSystem.SyncAutoSimulationFromCharacters();
        }

        private void Awake()
        {
            ValidateData();
            InitializeSolvers();
            SyncTransientFromTransform();
            controller = GetComponent<IMcc>();
        }

        private void OnEnable()
        {
            SyncTransientFromTransform();
            MccSystem.RegisterCharacter(this);
        }

        private void OnDisable()
        {
            MccSystem.UnregisterCharacter(this);
        }

        /// <summary>
        /// 用场景 Transform 初始化瞬时位姿 避免首帧 PreSimulation 用默认值把人拽走
        /// </summary>
        private void SyncTransientFromTransform()
        {
            context.TransientPosition = transform.position;
            context.TransientRotation = transform.rotation;
            context.InitialTickPosition = context.TransientPosition;
            context.InitialTickRotation = context.TransientRotation;
        }

        private void Update()
        {
            // 更新输入方向和跳跃请求
            controller?.InputVectorUpdate(ref inputDirection, ref jumpRequested);
        }

        /// <summary>
        /// 预模拟帧
        /// </summary>
        /// <param name="deltaTime">时间差</param>
        internal void PreSimulationTick(float deltaTime)
        {
            context.InitialTickPosition = context.TransientPosition;
            context.InitialTickRotation = context.TransientRotation;
            context.Transform.SetPositionAndRotation(context.TransientPosition, context.TransientRotation);
        }

        /// <summary>
        /// 更新阶段1
        /// 此阶段主要更新
        /// </summary>
        /// <param name="deltaTime">时间差</param>
        internal void UpdatePhase1(float deltaTime)
        {
            float phaseStart = Time.realtimeSinceStartup;
            context.BeginSimulation();
            context.SanitizeVelocity();
            rigidbodySolver.Clear();

            controller?.BeforeCharacterUpdate(deltaTime);

            // 如果移动位置标记为脏
            if (context.MovePositionDirty)
            {
                var moveDistance = context.MovePositionTarget - context.TransientPosition;
                var moveVelocity = GetVelocityFromMovement(moveDistance, deltaTime);
                // 如果需要解决移动碰撞
                if (context.SolveMovementCollisions)
                    collisionSolver.Move(ref moveVelocity, deltaTime);
                else
                    context.TransientPosition = context.MovePositionTarget;
                
                context.MovePositionDirty = false;
            }
    
            // 如果需要解决移动碰撞
            if (context.SolveMovementCollisions)
                collisionSolver.ResolveInitialOverlaps();

            // 更新接地状态
            groundSolver.UpdateGrounding(deltaTime);
            // 后接地更新
            controller?.PostGroundingUpdate(deltaTime);
            // 更新平台附件
            platformSolver.UpdateAttachment(deltaTime);
            context.DebugPhase1Seconds = Time.realtimeSinceStartup - phaseStart;
        }

        /// <summary>
        /// 更新阶段2
        /// </summary>
        /// <param name="deltaTime">时间差</param>
        internal void UpdatePhase2(float deltaTime)
        {
            float phaseStart = Time.realtimeSinceStartup;
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
            context.DebugPhase2Seconds = Time.realtimeSinceStartup - phaseStart;
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


        /// <summary>
        /// 校验数据
        /// </summary>
        private void ValidateData()
        {
            if (context.Transform is null)
                context.Transform = transform;

            // 注意这里 从Unity获取胶囊体组件 后续给到Context 相当于程序的入口
            context.Capsule = GetComponent<CapsuleCollider>();
            context.Config = config;
            context.Owner = this;
            context.Transform.localScale = Vector3.one;

            // 设置Context中的胶囊体尺寸
            if (context.Capsule is not null)
            {
                SetCapsuleDimensions(config.capsuleRadius, config.capsuleHeight, config.capsuleYOffset);
                context.Capsule.direction = 1;
                context.Capsule.sharedMaterial = config.capsulePhysicsMaterial;
            }

            // 刷新Context中的可碰撞层和角色朝向
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
            rigidbodySolver = new RigidbodySolver(context);
            collisionSolver = new CollisionSolver(context, stepSolver, rigidbodySolver);
            groundSolver = new GroundSolver(context, collisionSolver, stepSolver);
            platformSolver = new PlatformSolver(context, collisionSolver);
        }
    }
}
