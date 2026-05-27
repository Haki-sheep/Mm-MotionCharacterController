namespace KinematicCharacterController
{
    using System;
    using Sirenix.OdinInspector;
    using UnityEngine;

    /// <summary>
    /// 属性变量
    /// </summary>
    public partial class KinematicCharacterMotor
    {
        // 当前接地状态信息
        [System.NonSerialized]
        public CharacterGroundingReport GroundingStatus = new();
        // 上一帧的接地状态信息
        [System.NonSerialized]
        public CharacterTransientGroundingReport LastGroundingStatus = new();

        // 角色移动算法可检测碰撞的层遮罩，默认使用刚体层的碰撞矩阵
        [System.NonSerialized]
        public LayerMask CollidableLayers = -1;

        // 角色电机的Transform组件
        public Transform Transform { get { return _transform; } }
        private Transform _transform;

        // 上一时 的最终计算位置
        public Vector3 TransientPosition { get { return _transientPosition; } }
        private Vector3 _transientPosition;

        // 角色朝上方向，更新阶段始终保持最新
        public Vector3 CharacterUp { get { return _characterUp; } }
        private Vector3 _characterUp;

        // 角色朝前方向，更新阶段始终保持最新
        public Vector3 CharacterForward { get { return _characterForward; } }
        private Vector3 _characterForward;

        // 角色朝右方向，更新阶段始终保持最新
        public Vector3 CharacterRight { get { return _characterRight; } }
        private Vector3 _characterRight;

        // 移动计算开始前的初始位置
        public Vector3 InitialSimulationPosition { get { return _initialSimulationPosition; } }
        private Vector3 _initialSimulationPosition;

        // 移动计算开始前的初始旋转
        public Quaternion InitialSimulationRotation { get { return _initialSimulationRotation; } }
        private Quaternion _initialSimulationRotation;

        // 当前附着的刚体（如移动平台）
        public Rigidbody AttachedRigidbody { get { return _attachedRigidbody; } }
        private Rigidbody _attachedRigidbody;

        // 角色Transform到胶囊体中心的偏移向量 
        public Vector3 CharacterTransformToCapsuleCenter { get { return _characterTransformToCapsuleCenter; } }
        private Vector3 _characterTransformToCapsuleCenter;

        // 角色Transform到胶囊体底部的偏移向量 
        public Vector3 CharacterTransformToCapsuleBottom { get { return _characterTransformToCapsuleBottom; } }
        private Vector3 _characterTransformToCapsuleBottom;

        // 角色Transform到胶囊体顶部的偏移向量
        public Vector3 CharacterTransformToCapsuleTop { get { return _characterTransformToCapsuleTop; } }
        private Vector3 _characterTransformToCapsuleTop;

        // 角色Transform到胶囊体底部半球中心的偏移向量
        public Vector3 CharacterTransformToCapsuleBottomHemi { get { return _characterTransformToCapsuleBottomHemi; } }
        private Vector3 _characterTransformToCapsuleBottomHemi;

        // 角色Transform到胶囊体顶部半球中心的偏移向量
        public Vector3 CharacterTransformToCapsuleTopHemi { get { return _characterTransformToCapsuleTopHemi; } }
        private Vector3 _characterTransformToCapsuleTopHemi;

        // 站在刚体/物理移动物体上获得的速度
        public Vector3 AttachedRigidbodyVelocity { get { return _attachedRigidbodyVelocity; } }
        private Vector3 _attachedRigidbodyVelocity;

        // 角色更新期间检测到的重叠数量，每次更新开始时重置
        public int OverlapsCount { get { return _overlapsCount; } }
        private int _overlapsCount;

        // 角色更新期间检测到的重叠结果数组
        public OverlapResult[] Overlaps { get { return _overlaps; } }
        private OverlapResult[] _overlaps = new OverlapResult[MaxRigidbodyOverlapsCount];

        // 电机绑定的角色控制器
        [NonSerialized]
        public ICharacterController CharacterController;

        // 上一次扫描碰撞检测是否检测到地面
        [NonSerialized]
        public bool LastMovementIterationFoundAnyGround;

        // 该电机在角色物理系统数组中的索引
        [NonSerialized]
        public int IndexInCharacterSystem;

        // 本时的初始位置
        [NonSerialized]
        public Vector3 InitialTickPosition;

        // 本时的初始旋转
        [NonSerialized]
        public Quaternion InitialTickRotation;

        // 强制指定角色附着的刚体
        [NonSerialized]
        public Rigidbody AttachedRigidbodyOverride;

        // 角色由直接移动产生的基础速度
        [NonSerialized]
        public Vector3 BaseVelocity;

        // 上一时的最终旋转
        private Quaternion _transientRotation;
        /// <summary>
        /// The character's goal rotation in its movement calculations (always up-to-date during the character update phase)
        /// 该角色在移动计算中的目标旋转（在角色更新阶段始终保持最新状态）
        /// </summary>
        public Quaternion TransientRotation
        {
            get
            {
                return _transientRotation;
            }
            private set
            {
                // 为什么要限定三轴呢?
                // 因为玩家可能在非世界空间下旋转 比如重力反转
                _transientRotation = value;
                _characterUp = _transientRotation * _cachedWorldUp;
                _characterForward = _transientRotation * _cachedWorldForward;
                _characterRight = _transientRotation * _cachedWorldRight;
            }
        }

        /// <summary>
        /// The character's total velocity, including velocity from standing on rigidbodies or PhysicsMover
        /// 角色总速度，包括站立在刚体或PhysicsMover上获得的速度
        /// </summary>
        public Vector3 Velocity
        {
            get
            {
                return BaseVelocity + _attachedRigidbodyVelocity;
            }
        }

    }
}