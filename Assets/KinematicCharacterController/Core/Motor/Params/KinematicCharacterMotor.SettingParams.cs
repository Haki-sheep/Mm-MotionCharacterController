
namespace KinematicCharacterController
{
    using Sirenix.OdinInspector;
    using UnityEngine;

    /// <summary>
    /// 面板设置变量 
    /// </summary>
    public partial class KinematicCharacterMotor : MonoBehaviour
    {
        #region Unity组件 核心依赖
        /// Component that manages character collisions and movement solving
        /// 负责管理角色碰撞及移动解决的组件
        /// 
        [Header("Components/ 组件")] 
        [ReadOnly]
        public CapsuleCollider Capsule;

        [Header("Capsule Settings/ 胶囊体设置")]

        [SerializeField, Tooltip("胶囊体半径")]
        private float CapsuleRadius = 0.5f;
        [SerializeField, Tooltip("胶囊体高度")]
        private float CapsuleHeight = 2f;

        [SerializeField, Tooltip("胶囊体Y偏移")]
        private float CapsuleYOffset = 1f;

        [SerializeField, Tooltip("角色胶囊的物理材质（不会影响角色的运动,只会影响与之碰撞的物体）)")]
        private PhysicsMaterial CapsulePhysicsMaterial;

        #endregion

        #region 地面检测设置
        [Header("地面检测设置")]

        [Tooltip("增加地面检测的额外距离，用于高速移动时也能稳定吸附到地面")]
        public float GroundDetectionExtraDistance = 0f;

        [Range(0f, 89f)]
        [Tooltip("角色可以稳定站立的最大斜坡角度")]
        public float MaxStableSlopeAngle = 60f;

        [Tooltip("判定为可稳定站立的地面层")]
        public LayerMask StableGroundLayers = -1;

        [Tooltip("检测到离散碰撞时，通知角色控制器")]
        public bool DiscreteCollisionEvents = false;

        #endregion

        #region 台阶设置
        [Header("台阶设置")]
        [Tooltip("正确处理台阶上的接地检测，但会产生一定性能消耗")]
        public StepHandlingMethod StepHandling = StepHandlingMethod.Standard;

        [Tooltip("角色可攀爬的最大台阶高度")]
        public float MaxStepHeight = 0.5f;

        [Tooltip("角色在未稳定接地时，是否仍可以攀爬台阶")]
        public bool AllowSteppingWithoutStableGrounding = false;

        [Tooltip("角色可踩踏的最小台阶深度（用于进阶台阶处理），可用于角色攀爬小于自身半径的台阶")]
        public float MinRequiredStepDepth = 0.1f;

        #endregion

        #region 边缘设置
        [Header("边缘设置")]
        [Tooltip("精准检测边缘位置与接地状态，开启会产生一定性能消耗")]
        public bool LedgeAndDenivelationHandling = true;

        [Tooltip("角色能稳定站立在边缘时，与胶囊体中心轴的最大距离")]
        public float MaxStableDistanceFromLedge = 0.5f;

        [Tooltip("当移动速度超过该值时，将禁止在边缘处吸附到地面")]
        public float MaxVelocityForLedgeSnap = 0f;

        [Tooltip("角色能保持地面吸附状态时，可承受的最大向下坡度变化角度")]
        [Range(1f, 180f)]
        public float MaxStableDenivelationAngle = 180f;
        #endregion

        #region 刚体交互设置
        [Header("刚体交互设置")]
        [Tooltip("处理角色被物理移动物体/动态刚体推动、站立，以及角色推动动态刚体的逻辑")]
        public bool InteractiveRigidbodyHandling = true;

        [Tooltip("角色与非运动学刚体的交互方式：运动学模式以无限力推动刚体；\n模拟动态模式使用模拟质量值推动刚体")]
        public RigidbodyInteractionType RigidbodyInteractionType;

        [Tooltip("推动刚体时使用的模拟质量")]
        public float SimulatedCharacterMass = 1f;

        [Tooltip("角色离开移动平台时，是否保留平台赋予的速度惯性")]
        public bool PreserveAttachedRigidbodyMomentum = true;
        #endregion

        #region 约束设置
        [Header("约束设置")]
        [Tooltip("是否为角色移动启用平面约束")]
        public bool HasPlanarConstraint = false;

        [Tooltip("当启用平面约束时，定义角色移动的约束平面轴向")]
        public Vector3 PlanarConstraintAxis = Vector3.forward;
        #endregion

        #region 其他设置
        [Header("其他设置")]
        [Tooltip("每次更新中最多可执行多少次移动扫描检测")]
        public int MaxMovementIterations = 5;

        [Tooltip("每次更新中最多可执行多少次脱离碰撞检测")]
        public int MaxDecollisionIterations = 1;

        [Tooltip("在移动检测前检查重叠，确保即使已与几何体相交也能检测到所有碰撞（有性能消耗，但可防止穿透碰撞体）")]
        public bool CheckMovementInitialOverlaps = true;

        [Tooltip("超出最大移动迭代次数时将速度归零")]
        public bool KillVelocityWhenExceedMaxMovementIterations = true;

        [Tooltip("超出最大移动迭代次数时将剩余移动距离归零")]
        public bool KillRemainingMovementWhenExceedMaxMovementIterations = true;
        #endregion
    }

}