namespace KinematicCharacterController
{
    using Sirenix.OdinInspector;
    using UnityEngine;
    using System.Collections.Generic;
    /// <summary>
    /// 私有变量
    /// </summary>
    public partial class KinematicCharacterMotor
    {
        // 私有变量
        // 角色内部射线检测结果缓存数组，用于存储移动/碰撞检测的射线信息
        private RaycastHit[] _internalCharacterHits = new RaycastHit[MaxHitsBudget];

        // 内部碰撞体探测缓存数组，用于存储场景中检测到的碰撞体
        private Collider[] _internalProbedColliders = new Collider[MaxCollisionBudget];

        // 本次移动过程中被角色推动的刚体列表，用于后续物理逻辑处理
        private List<Rigidbody> _rigidbodiesPushedThisMoveList = new(16);

        // 内部刚体投影检测结果数组，处理角色与刚体的碰撞投影逻辑
        private RigidbodyProjectionHit[] _internalRigidbodyProjectionHits = new RigidbodyProjectionHit[MaxRigidbodyOverlapsCount];

        // 上一帧附着的刚体（移动平台/可碰撞物体）
        private Rigidbody _lastAttachedRigidbody;

        // 开关：是否启用移动碰撞解算逻辑
        private bool _solveMovementCollisions = true;

        // 开关：是否启用地面检测与接地解算逻辑
        private bool _solveGrounding = true;

        // 标记：角色目标位置是否需要更新
        private bool _movePositionDirty = false;

        // 角色本次移动的目标位置
        private Vector3 _movePositionTarget = Vector3.zero;

        // 标记：角色目标旋转是否需要更新
        private bool _moveRotationDirty = false;

        // 角色本次移动的目标旋转
        private Quaternion _moveRotationTarget = Quaternion.identity;

        // 本次检测到的刚体投影碰撞数量
        private int _rigidbodyProjectionHitCount = 0;

        // 标记：当前角色是否由附着的刚体（平台）带动移动
        private bool _isMovingFromAttachedRigidbody = false;

        // 标记：是否强制角色脱离接地状态
        private bool _mustUnground = false;
        
        // 强制脱离接地状态的计时计数器
        private float _mustUngroundTimeCounter = 0f;

        // 缓存全局方向向量
        private Vector3 _cachedWorldUp = Vector3.up;
        private Vector3 _cachedWorldForward = Vector3.forward;
        private Vector3 _cachedWorldRight = Vector3.right;
        private Vector3 _cachedZeroVector = Vector3.zero;
    }
}