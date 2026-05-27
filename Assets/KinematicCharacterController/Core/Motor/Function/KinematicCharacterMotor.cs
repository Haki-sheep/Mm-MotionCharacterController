using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace KinematicCharacterController
{
    /// <summary>
    /// 核心运动逻辑
    /// </summary>
    public partial class KinematicCharacterMotor : MonoBehaviour
    {
        private void OnEnable()
        {
            KinematicCharacterSystem.EnsureCreation();
            KinematicCharacterSystem.RegisterCharacterMotor(this);
        }

        private void OnDisable()
        {
            KinematicCharacterSystem.UnregisterCharacterMotor(this);
        }

        private void Reset()
        {
            ValidateData();
        }

        private void OnValidate()
        {
            ValidateData();
        }

        [ContextMenu("Remove Component")]
        private void HandleRemoveComponent()
        {
            CapsuleCollider tmpCapsule = gameObject.GetComponent<CapsuleCollider>();
            DestroyImmediate(this);
            DestroyImmediate(tmpCapsule);
        }


        private void Awake()
        {
            _transform = this.transform;
            ValidateData();

            _transientPosition = _transform.position;
            TransientRotation = _transform.rotation;

            // Build CollidableLayers mask - 构建可碰撞层掩码
            CollidableLayers = 0;
            for (int i = 0; i < 32; i++)
            {
                if (!Physics.GetIgnoreLayerCollision(this.gameObject.layer, i))
                {
                    CollidableLayers |= (1 << i);
                }
            }

            // 设置胶囊体尺寸
            SetCapsuleDimensions(CapsuleRadius, CapsuleHeight, CapsuleYOffset);
        }

        /// <summary>
        /// Update phase 1 is meant to be called after physics movers have calculated their velocities, but
        /// before they have simulated their goal positions/rotations. It is responsible for:
        /// - Initializing all values for update
        /// - Handling MovePosition calls
        /// - Solving initial collision overlaps
        /// - Ground probing
        /// - Handle detecting potential interactable rigidbodies
        /// 
        /// 第一阶段更新：需在物理移动组件计算出速度后、模拟目标位置/旋转前调用 
        /// 核心职责：
        /// - 初始化更新所需的所有数值 
        /// - 处理移动位置调用  
        /// - 解决初始的碰撞重叠问题 
        /// - 地面探测检测 
        /// - 检测潜在可交互的刚体
        /// </summary>
        public void UpdatePhase1(float deltaTime)
        {
            // NaN表示Not a Number，是一种特殊的浮点数，表示无法表示的数值 比如0/0,Infinity,-Infinity,开平方负数等
            // 如果速度是NaN，则设置为 0
            if (float.IsNaN(BaseVelocity.x) || float.IsNaN(BaseVelocity.y) || float.IsNaN(BaseVelocity.z))
            {
                BaseVelocity = Vector3.zero;
            }
            // 如果附着刚体的速度是NaN，则设置为0
            if (float.IsNaN(_attachedRigidbodyVelocity.x) || float.IsNaN(_attachedRigidbodyVelocity.y) || float.IsNaN(_attachedRigidbodyVelocity.z))
            {
                _attachedRigidbodyVelocity = Vector3.zero;
            }

#if UNITY_EDITOR

            // 在Editor下检查控制器缩放是否为1
            // Approximately 近似的,用于比较两个浮点数是否相等 式子为return Abs(a - b) < 9.99999944E-11f;
            if (!Mathf.Approximately(_transform.lossyScale.x, 1f)
                || !Mathf.Approximately(_transform.lossyScale.y, 1f)
                || !Mathf.Approximately(_transform.lossyScale.z, 1f))
            {
                Debug.LogError("Character's lossy scale is not (1,1,1). This is not allowed. Make sure the character's transform and all of its parents have a (1,1,1) scale.", this.gameObject);
            }
#endif

            // 清空本次移动推挤的刚体列表 准备进入1阶段更新
            _rigidbodiesPushedThisMoveList.Clear();

            // 用户接口 Before update 在更新前调用
            CharacterController.BeforeCharacterUpdate(deltaTime);

            // 记录0时的初始位置/旋转
            _transientPosition = _transform.position;
            TransientRotation = _transform.rotation;
            _initialSimulationPosition = _transientPosition;
            _initialSimulationRotation = _transientRotation;

            // 碰撞与重叠检测
            _rigidbodyProjectionHitCount = 0;
            _overlapsCount = 0;

            #region 外部处理移动位置 一般用于强制移动角色位置 做细微处理 比如处决,攀爬等脚下的位置匹配

            if (_movePositionDirty)
            {
                // 开启碰撞解算
                if (_solveMovementCollisions)
                {
                    // 计算速度 这个目标由外部KinematicCharacterMotor.Extensions.cs -> MoveCharacter()设置
                    Vector3 tmpVelocity = GetVelocityFromMovement(_movePositionTarget - _transientPosition, deltaTime);
                    // 将平均速度进行Kcc核心解算
                    if (InternalCharacterMove(ref tmpVelocity, deltaTime))
                    {
                        if (InteractiveRigidbodyHandling)
                        {
                            ProcessVelocityForRigidbodyHits(ref tmpVelocity, deltaTime);
                        }
                    }
                }
                else
                {
                    _transientPosition = _movePositionTarget;
                }

                _movePositionDirty = false;
            }
            #endregion

            #region 迭代处理碰撞重叠 - 解决初始重叠部分 (Resolve initial overlap)

            // 记录上一帧的接地状态
            LastGroundingStatus.CopyFrom(GroundingStatus);
            // 重置当前帧的接地状态
            GroundingStatus = new CharacterGroundingReport();
            // 设置当前帧的接地状态的法线为角色朝上方向
            GroundingStatus.GroundNormal = _characterUp;

            // 如果开启了移动解算就进行
            if (_solveMovementCollisions)
            {
                Vector3 resolutionDirection = _cachedWorldUp;
                float resolutionDistance = 0f;
                // 重要: 为什么处理的是本帧的事情 还需要迭代?什么东西这么大面子?
                // 因为角色可能卡在多个碰撞体之间 推出来一次可能又和别的碰撞体重叠了,而且一帧默认50次处理并非只执行一次

                int iterationsMade = 0; // 已经进行迭代的次数
                bool overlapSolved = false; // 是否已经解决所有重叠

                while (iterationsMade < MaxDecollisionIterations && !overlapSolved)
                {
                    // 拿到当前所有重叠的碰撞体的个数 传入的碰撞体数组用于内部填充计算 
                    int nbOverlaps = CharacterCollisionsOverlap(_transientPosition,
                                                                _transientRotation,
                                                                _internalProbedColliders);
                    // 如果需要处理的重叠碰撞体数量大于0 
                    if (nbOverlaps > 0)
                    {
                        // Solve overlaps that aren't against dynamic rigidbodies or physics movers
                        // 解决不是对动态刚体或物理移动物体的重叠
                        for (int i = 0; i < nbOverlaps; i++)
                        {
                            // 判断是不是可交互刚体 如果不是当做普通静态障碍物解重叠
                            if (GetInteractiveRigidbody(_internalProbedColliders[i]) == null)
                            {
                                Transform overlappedTransform = _internalProbedColliders[i].GetComponent<Transform>();

                                // 如果有穿透或重叠情况 
                                if (Physics.ComputePenetration(
                                        Capsule,
                                        _transientPosition,
                                        _transientRotation,
                                        _internalProbedColliders[i],
                                        overlappedTransform.position,
                                        overlappedTransform.rotation,
                                        out resolutionDirection,// 分离方向作用于A物体 垂直于B表面 就是将角色从重叠处推出去的方向
                                        out resolutionDistance  // 分离距离(就是穿透深度)
                                        ))
                                {
                                    // Resolve along obstruction direction  - 沿障碍物方向推开分离

                                    // 创建碰撞报告 记录是不是稳定点 
                                    HitStabilityReport mockReport = new HitStabilityReport();
                                    mockReport.IsStable = IsStableOnNormal(resolutionDirection);
                                    resolutionDirection = GetObstructionNormal(resolutionDirection, mockReport.IsStable);

                                    // Solve overlap - 解决重叠
                                    // 将纯净的分离向量和穿透距离相乘 得到角色应该脱离重叠的值 赋给_transientPosition
                                    Vector3 resolutionMovement = resolutionDirection
                                                                * (resolutionDistance + CollisionOffset);
                                    _transientPosition += resolutionMovement;

                                    // Remember overlaps - 记住重叠部分 
                                    // 用于后续InternalCharacterMove 本质是不想让角色刚被退出来又想挤进去造成抖动
                                    if (_overlapsCount < _overlaps.Length)
                                    {
                                        _overlaps[_overlapsCount] = new OverlapResult(resolutionDirection,
                                                                                      _internalProbedColliders[i]);
                                        _overlapsCount++;
                                    }

                                    break;
                                }
                            }
                        }
                    }
                    else // 没有任何碰撞体需要解重叠 跳出循环
                    {
                        overlapSolved = true;
                    }

                    // 迭代次数+1
                    iterationsMade++;
                }
            }
            #endregion

            #region 地面探测与贴地吸附 (Ground Probing and Snapping)
            // Handle ungrounding - 处理离地状态
            // 默认开启地面吸附 在一些特殊状态比如游泳,攀爬梯子的时候不需要开启
            if (_solveGrounding)
            {
                // 如果需要强制离地,将角色上推
                if (MustUnground())
                {
                    _transientPosition += _characterUp * (MinimumGroundProbingDistance * 1.5f);
                }
                else
                {
                    // Choose the appropriate ground probing distance 
                    // 动态调整角色向下检测地面的长度，保证贴地、上台阶、落地都能精准检测
                    float selectedGroundProbingDistance = MinimumGroundProbingDistance;

                    // 如果没有阻止地面吸附 且 稳定站立 或 是上一帧扫描到了地面(意味着角色虽然不一定稳定站住了 但是肯定和地面很近了)
                    if (!LastGroundingStatus.SnappingPrevented
                            && (LastGroundingStatus.IsStableOnGround
                            || LastMovementIterationFoundAnyGround))
                    {
                        // 判断台阶情况 动态调整检测距离
                        if (StepHandling != StepHandlingMethod.None)
                        {
                            // 判断角色底部正常接地范围和可跨越台阶高度里面更大的那个
                            selectedGroundProbingDistance = Mathf.Max(CapsuleRadius, MaxStepHeight);
                        }
                        else
                        {
                            selectedGroundProbingDistance = CapsuleRadius;
                        }

                        selectedGroundProbingDistance += GroundDetectionExtraDistance;
                    }

                    // 地面检测
                    ProbeGround(ref _transientPosition,
                                _transientRotation,
                                selectedGroundProbingDistance,
                                ref GroundingStatus // 传递地面稳定报告
                                );

                    // 如果上一帧没站稳 且 这一帧站稳了 说明落地 
                    if (!LastGroundingStatus.IsStableOnGround && GroundingStatus.IsStableOnGround)
                    {
                        // Handle stable landing - 处理稳定落地
                        // 先移除速度与向上向量平行的分量 再让水平速度贴到当前地面的切线方向上 
                        // 这样做的意义是不让角色砸进到地里面去
                        BaseVelocity = Vector3.ProjectOnPlane(BaseVelocity, CharacterUp);
                        BaseVelocity = GetDirectionTangentToSurface(BaseVelocity,
                                                                    GroundingStatus.GroundNormal) * BaseVelocity.magnitude;
                    }
                }
            }

            // 把本帧用过的临时接地/离地状态清理掉，为下一帧准备
            LastMovementIterationFoundAnyGround = false;
            if (_mustUngroundTimeCounter > 0f)
                _mustUngroundTimeCounter -= deltaTime;
            _mustUnground = false;

            #endregion

            #region 外部开发者调用接口
            // 地面检测完了 给下层开发者开放此阶段完成后的接口使用
            if (_solveGrounding)
                CharacterController.PostGroundingUpdate(deltaTime);
            #endregion

            #region Interactive Rigidbody Handling - 可交互刚体处理
            // 这部分是在处理角色站在可交互刚体上时，怎么跟随它的移动/旋转，并在离开时保留合理惯性
            if (InteractiveRigidbodyHandling)
            {
                // 记录上一帧附着的刚体
                _lastAttachedRigidbody = _attachedRigidbody;
                if (AttachedRigidbodyOverride)
                {
                    // 外部强制指定当前附着刚体
                    _attachedRigidbody = AttachedRigidbodyOverride;
                }
                else
                {
                    // Detect interactive rigidbodies from grounding
                    // 从接地结果里识别脚下是否站在可交互刚体上
                    if (GroundingStatus.IsStableOnGround && GroundingStatus.GroundCollider.attachedRigidbody)
                    {
                        Rigidbody interactiveRigidbody = GetInteractiveRigidbody(GroundingStatus.GroundCollider);
                        // 从当前地面碰撞体里拿到真正可交互的刚体
                        if (interactiveRigidbody)
                        {
                            // 记录当前附着刚体 一般是移动平台之类
                            _attachedRigidbody = interactiveRigidbody;
                        }
                    }
                    else
                    {
                        // 没站在可交互刚体上就清空附着刚体
                        _attachedRigidbody = null;
                    }
                }

                // 当前附着刚体给角色带来的线速度
                Vector3 tmpVelocityFromCurrentAttachedRigidbody = Vector3.zero;
                // 当前附着刚体给角色带来的角速度
                Vector3 tmpAngularVelocityFromCurrentAttachedRigidbody = Vector3.zero;
                if (_attachedRigidbody)
                {
                    // 计算附着刚体在角色当前位置处产生的线速度和角速度
                    GetVelocityFromRigidbodyMovement(_attachedRigidbody, _transientPosition, deltaTime, out tmpVelocityFromCurrentAttachedRigidbody, out tmpAngularVelocityFromCurrentAttachedRigidbody);
                }

                // Conserve momentum when de-stabilized from an attached rigidbody
                // 当角色离开旧平台或切换到新平台时 保留合理惯性 避免速度突变
                if (PreserveAttachedRigidbodyMomentum && _lastAttachedRigidbody
                                != null && _attachedRigidbody != _lastAttachedRigidbody)
                {
                    // 先加回旧平台上一次提供的附加速度
                    BaseVelocity += _attachedRigidbodyVelocity;
                    // 再减掉新平台当前提供的速度 防止速度重复叠加
                    BaseVelocity -= tmpVelocityFromCurrentAttachedRigidbody;
                }

                // Process additionnal Velocity from attached rigidbody
                // 处理当前附着刚体给角色带来的额外速度
                _attachedRigidbodyVelocity = _cachedZeroVector;
                if (_attachedRigidbody)
                {
                    // 保存当前平台给角色带来的附加线速度
                    _attachedRigidbodyVelocity = tmpVelocityFromCurrentAttachedRigidbody;

                    // Rotation from attached rigidbody - 处理平台旋转对角色朝向的影响
                    Vector3 newForward = Vector3.ProjectOnPlane(Quaternion.Euler(Mathf.Rad2Deg * tmpAngularVelocityFromCurrentAttachedRigidbody * deltaTime) * _characterForward,
                                                                _characterUp).normalized;
                    // 让角色朝向跟着平台旋转一起更新
                    TransientRotation = Quaternion.LookRotation(newForward, _characterUp);
                }

                // Cancel out horizontal velocity upon landing on an attached rigidbody
                // 在落在附着的刚体上时消除水平速度
                if (GroundingStatus.GroundCollider &&
                    GroundingStatus.GroundCollider.attachedRigidbody &&
                    GroundingStatus.GroundCollider.attachedRigidbody == _attachedRigidbody &&
                    _attachedRigidbody != null &&
                    _lastAttachedRigidbody == null)
                {
                    BaseVelocity -= Vector3.ProjectOnPlane(_attachedRigidbodyVelocity, _characterUp);
                }

                // Movement from Attached Rigidbody - 让角色实际跟着平台一起移动
                if (_attachedRigidbodyVelocity.sqrMagnitude > 0f)
                {
                    // 标记这次位移来源于附着刚体
                    _isMovingFromAttachedRigidbody = true;

                    if (_solveMovementCollisions)
                    {
                        // Perform the move from rgdbdy velocity
                        // 用平台速度驱动角色 再走一次完整碰撞解算
                        InternalCharacterMove(ref _attachedRigidbodyVelocity, deltaTime);
                    }
                    else
                    {
                        // 不做碰撞解算时 直接按平台速度平移角色
                        _transientPosition += _attachedRigidbodyVelocity * deltaTime;
                    }

                    // 平台带动的位移处理结束
                    _isMovingFromAttachedRigidbody = false;
                }
                #endregion
            }
        }

        /// <summary>
        /// Update phase 2 is meant to be called after physics movers have simulated their goal positions/rotations. 
        /// At the end of this, the TransientPosition/Rotation values will be up-to-date with where the motor should be at the end of its move. 
        /// It is responsible for:
        /// - Solving Rotation
        /// - Handle MoveRotation calls
        /// - Solving potential attached rigidbody overlaps
        /// - Solving Velocity
        /// - Applying planar constraint
        /// 
        /// 第二阶段更新：应当在物理运动器完成目标位置/旋转的模拟之后调用
        /// 此阶段执行完毕后，瞬时位置（TransientPosition）和瞬时旋转（TransientRotation）会被更新为运动控制器最终应处的正确状态。
        /// 该方法负责处理：
        /// - 处理角色旋转逻辑
        /// - 处理移动旋转调用
        /// - 解决与附着刚体可能产生的重叠
        /// - 处理速度逻辑
        /// - 应用平面约束
        /// </summary>
        public void UpdatePhase2(float deltaTime)
        {
            // Handle rotation
            CharacterController.UpdateRotation(ref _transientRotation, deltaTime);
            TransientRotation = _transientRotation;

            // Handle move rotation
            if (_moveRotationDirty)
            {
                TransientRotation = _moveRotationTarget;
                _moveRotationDirty = false;
            }

            if (_solveMovementCollisions && InteractiveRigidbodyHandling)
            {
                if (InteractiveRigidbodyHandling)
                {
                    #region Solve potential attached rigidbody overlap
                    if (_attachedRigidbody)
                    {
                        float upwardsOffset = Capsule.radius;

                        RaycastHit closestHit;
                        if (CharacterGroundSweep(
                            _transientPosition + (_characterUp * upwardsOffset),
                            _transientRotation,
                            -_characterUp,
                            upwardsOffset,
                            out closestHit))
                        {
                            if (closestHit.collider.attachedRigidbody == _attachedRigidbody && IsStableOnNormal(closestHit.normal))
                            {
                                float distanceMovedUp = (upwardsOffset - closestHit.distance);
                                _transientPosition = _transientPosition + (_characterUp * distanceMovedUp) + (_characterUp * CollisionOffset);
                            }
                        }
                    }
                    #endregion
                }

                if (InteractiveRigidbodyHandling)
                {
                    #region Resolve overlaps that could've been caused by rotation or physics movers simulation pushing the character
                    Vector3 resolutionDirection = _cachedWorldUp;
                    float resolutionDistance = 0f;
                    int iterationsMade = 0;
                    bool overlapSolved = false;
                    while (iterationsMade < MaxDecollisionIterations && !overlapSolved)
                    {
                        int nbOverlaps = CharacterCollisionsOverlap(_transientPosition, _transientRotation, _internalProbedColliders);
                        if (nbOverlaps > 0)
                        {
                            for (int i = 0; i < nbOverlaps; i++)
                            {
                                // Process overlap
                                Transform overlappedTransform = _internalProbedColliders[i].GetComponent<Transform>();
                                if (Physics.ComputePenetration(
                                        Capsule,
                                        _transientPosition,
                                        _transientRotation,
                                        _internalProbedColliders[i],
                                        overlappedTransform.position,
                                        overlappedTransform.rotation,
                                        out resolutionDirection,
                                        out resolutionDistance))
                                {
                                    // Resolve along obstruction direction
                                    HitStabilityReport mockReport = new HitStabilityReport();
                                    mockReport.IsStable = IsStableOnNormal(resolutionDirection);
                                    resolutionDirection = GetObstructionNormal(resolutionDirection, mockReport.IsStable);

                                    // Solve overlap
                                    Vector3 resolutionMovement = resolutionDirection * (resolutionDistance + CollisionOffset);
                                    _transientPosition += resolutionMovement;

                                    // If interactiveRigidbody, register as rigidbody hit for velocity
                                    if (InteractiveRigidbodyHandling)
                                    {
                                        Rigidbody probedRigidbody = GetInteractiveRigidbody(_internalProbedColliders[i]);
                                        if (probedRigidbody != null)
                                        {
                                            HitStabilityReport tmpReport = new HitStabilityReport();
                                            tmpReport.IsStable = IsStableOnNormal(resolutionDirection);
                                            if (tmpReport.IsStable)
                                            {
                                                LastMovementIterationFoundAnyGround = tmpReport.IsStable;
                                            }
                                            if (probedRigidbody != _attachedRigidbody)
                                            {
                                                Vector3 characterCenter = _transientPosition + (_transientRotation * _characterTransformToCapsuleCenter);
                                                Vector3 estimatedCollisionPoint = _transientPosition;


                                                StoreRigidbodyHit(
                                                    probedRigidbody,
                                                    Velocity,
                                                    estimatedCollisionPoint,
                                                    resolutionDirection,
                                                    tmpReport);
                                            }
                                        }
                                    }

                                    // Remember overlaps
                                    if (_overlapsCount < _overlaps.Length)
                                    {
                                        _overlaps[_overlapsCount] = new OverlapResult(resolutionDirection, _internalProbedColliders[i]);
                                        _overlapsCount++;
                                    }

                                    break;
                                }
                            }
                        }
                        else
                        {
                            overlapSolved = true;
                        }

                        iterationsMade++;
                    }
                    #endregion
                }
            }

            // Handle velocity
            CharacterController.UpdateVelocity(ref BaseVelocity, deltaTime);

            //this.CharacterController.UpdateVelocity(ref BaseVelocity, deltaTime);
            if (BaseVelocity.magnitude < MinVelocityMagnitude)
            {
                BaseVelocity = Vector3.zero;
            }

            #region Calculate Character movement from base velocity   
            // Perform the move from base velocity
            if (BaseVelocity.sqrMagnitude > 0f)
            {
                if (_solveMovementCollisions)
                {
                    InternalCharacterMove(ref BaseVelocity, deltaTime);
                }
                else
                {
                    _transientPosition += BaseVelocity * deltaTime;
                }
            }

            // Process rigidbody hits/overlaps to affect velocity
            if (InteractiveRigidbodyHandling)
            {
                ProcessVelocityForRigidbodyHits(ref BaseVelocity, deltaTime);
            }
            #endregion

            // Handle planar constraint
            if (HasPlanarConstraint)
            {
                _transientPosition = _initialSimulationPosition + Vector3.ProjectOnPlane(_transientPosition - _initialSimulationPosition, PlanarConstraintAxis.normalized);
            }

            // Discrete collision detection
            if (DiscreteCollisionEvents)
            {
                int nbOverlaps = CharacterCollisionsOverlap(_transientPosition, _transientRotation, _internalProbedColliders, CollisionOffset * 2f);
                for (int i = 0; i < nbOverlaps; i++)
                {
                    CharacterController.OnDiscreteCollisionDetected(_internalProbedColliders[i]);
                }
            }

            CharacterController.AfterCharacterUpdate(deltaTime);
        }


        /// <summary>
        /// Moves the character's position by given movement while taking into account all physics simulation, 
        /// step-handling and velocity projection rules that affect the character motor
        /// 根据指定的移动量执行角色内部移动
        /// 会完整遵循物理模拟、台阶处理、速度投影规则，是角色电机的核心移动逻辑
        /// </summary>
        /// <param name="transientVelocity">临时速度（引用传递，会被物理逻辑修改）</param>
        /// <param name="deltaTime">物理时间增量</param>
        /// <returns> Returns false if movement could not be solved until the end
        ///           -> 如果移动完整执行完毕返回true；因碰撞/迭代限制未完成返回false </returns>
        private bool InternalCharacterMove(ref Vector3 transientVelocity, float deltaTime)
        {
            // 如果物理时间增量小于等于0，则返回false
            if (deltaTime <= 0f)
                return false;

            // Planar constraint 平台约束 
            // 主要是指将速度投影到指定平面,防止角色在斜坡上移动时出现速度异常
            if (HasPlanarConstraint)
            {
                // 这里取的是forward的法向量 实际上仍然是约束了XZ 平面（地面）	
                transientVelocity = Vector3.ProjectOnPlane(transientVelocity, PlanarConstraintAxis.normalized);
            }

            bool wasCompleted = true;
            Vector3 remainingMovementDirection = transientVelocity.normalized;
            float remainingMovementMagnitude = transientVelocity.magnitude * deltaTime;
            Vector3 originalVelocityDirection = remainingMovementDirection;
            int sweepsMade = 0;
            bool hitSomethingThisSweepIteration = true;
            Vector3 tmpMovedPosition = _transientPosition;
            bool previousHitIsStable = false;
            Vector3 previousVelocity = _cachedZeroVector;
            Vector3 previousObstructionNormal = _cachedZeroVector;
            MovementSweepState sweepState = MovementSweepState.Initial;

            // Project movement against current overlaps before doing the sweeps
            // 在进行检测工作之前，先对当前存在的问题进行梳理和处理
            for (int i = 0; i < _overlapsCount; i++)
            {
                Vector3 overlapNormal = _overlaps[i].Normal;
                if (Vector3.Dot(remainingMovementDirection, overlapNormal) < 0f)
                {
                    bool stableOnHit = IsStableOnNormal(overlapNormal) && !MustUnground();
                    Vector3 velocityBeforeProjection = transientVelocity;
                    Vector3 obstructionNormal = GetObstructionNormal(overlapNormal, stableOnHit);

                    InternalHandleVelocityProjection(
                        stableOnHit,
                        overlapNormal,
                        obstructionNormal,
                        originalVelocityDirection,
                        ref sweepState,
                        previousHitIsStable,
                        previousVelocity,
                        previousObstructionNormal,
                        ref transientVelocity,
                        ref remainingMovementMagnitude,
                        ref remainingMovementDirection);

                    previousHitIsStable = stableOnHit;
                    previousVelocity = velocityBeforeProjection;
                    previousObstructionNormal = obstructionNormal;
                }
            }

            // Sweep the desired movement to detect collisions
            while (remainingMovementMagnitude > 0f &&
                (sweepsMade <= MaxMovementIterations) &&
                hitSomethingThisSweepIteration)
            {
                bool foundClosestHit = false;
                Vector3 closestSweepHitPoint = default;
                Vector3 closestSweepHitNormal = default;
                float closestSweepHitDistance = 0f;
                Collider closestSweepHitCollider = null;

                if (CheckMovementInitialOverlaps)
                {
                    int numOverlaps = CharacterCollisionsOverlap(
                                        tmpMovedPosition,
                                        _transientRotation,
                                        _internalProbedColliders,
                                        0f,
                                        false);
                    if (numOverlaps > 0)
                    {
                        closestSweepHitDistance = 0f;

                        float mostObstructingOverlapNormalDotProduct = 2f;

                        for (int i = 0; i < numOverlaps; i++)
                        {
                            Collider tmpCollider = _internalProbedColliders[i];

                            if (Physics.ComputePenetration(
                                Capsule,
                                tmpMovedPosition,
                                _transientRotation,
                                tmpCollider,
                                tmpCollider.transform.position,
                                tmpCollider.transform.rotation,
                                out Vector3 resolutionDirection,
                                out float resolutionDistance))
                            {
                                float dotProduct = Vector3.Dot(remainingMovementDirection, resolutionDirection);
                                if (dotProduct < 0f && dotProduct < mostObstructingOverlapNormalDotProduct)
                                {
                                    mostObstructingOverlapNormalDotProduct = dotProduct;

                                    closestSweepHitNormal = resolutionDirection;
                                    closestSweepHitCollider = tmpCollider;
                                    closestSweepHitPoint = tmpMovedPosition + (_transientRotation * CharacterTransformToCapsuleCenter) + (resolutionDirection * resolutionDistance);

                                    foundClosestHit = true;
                                }
                            }
                        }
                    }
                }

                if (!foundClosestHit && CharacterCollisionsSweep(
                        tmpMovedPosition, // position
                        _transientRotation, // rotation
                        remainingMovementDirection, // direction
                        remainingMovementMagnitude + CollisionOffset, // distance
                        out RaycastHit closestSweepHit, // closest hit
                        _internalCharacterHits) // all hits
                    > 0)
                {
                    closestSweepHitNormal = closestSweepHit.normal;
                    closestSweepHitDistance = closestSweepHit.distance;
                    closestSweepHitCollider = closestSweepHit.collider;
                    closestSweepHitPoint = closestSweepHit.point;

                    foundClosestHit = true;
                }

                if (foundClosestHit)
                {
                    // Calculate movement from this iteration
                    Vector3 sweepMovement = (remainingMovementDirection * (Mathf.Max(0f, closestSweepHitDistance - CollisionOffset)));
                    tmpMovedPosition += sweepMovement;
                    remainingMovementMagnitude -= sweepMovement.magnitude;

                    // Evaluate if hit is stable
                    HitStabilityReport moveHitStabilityReport = new HitStabilityReport();
                    EvaluateHitStability(closestSweepHitCollider, closestSweepHitNormal, closestSweepHitPoint, tmpMovedPosition, _transientRotation, transientVelocity, ref moveHitStabilityReport);

                    // Handle stepping up steps points higher than bottom capsule radius
                    bool foundValidStepHit = false;
                    if (_solveGrounding && StepHandling != StepHandlingMethod.None && moveHitStabilityReport.ValidStepDetected)
                    {
                        float obstructionCorrelation = Mathf.Abs(Vector3.Dot(closestSweepHitNormal, _characterUp));
                        if (obstructionCorrelation <= CorrelationForVerticalObstruction)
                        {
                            Vector3 stepForwardDirection = Vector3.ProjectOnPlane(-closestSweepHitNormal, _characterUp).normalized;
                            Vector3 stepCastStartPoint = (tmpMovedPosition + (stepForwardDirection * SteppingForwardDistance)) +
                                (_characterUp * MaxStepHeight);

                            // Cast downward from the top of the stepping height
                            int nbStepHits = CharacterCollisionsSweep(
                                                stepCastStartPoint, // position
                                                _transientRotation, // rotation
                                                -_characterUp, // direction
                                                MaxStepHeight, // distance
                                                out RaycastHit closestStepHit, // closest hit
                                                _internalCharacterHits,
                                                0f,
                                                true); // all hits 

                            // Check for hit corresponding to stepped collider
                            for (int i = 0; i < nbStepHits; i++)
                            {
                                if (_internalCharacterHits[i].collider == moveHitStabilityReport.SteppedCollider)
                                {
                                    Vector3 endStepPosition = stepCastStartPoint + (-_characterUp * (_internalCharacterHits[i].distance - CollisionOffset));
                                    tmpMovedPosition = endStepPosition;
                                    foundValidStepHit = true;

                                    // Project velocity on ground normal at step
                                    transientVelocity = Vector3.ProjectOnPlane(transientVelocity, CharacterUp);
                                    remainingMovementDirection = transientVelocity.normalized;

                                    break;
                                }
                            }
                        }
                    }

                    // Handle movement solving
                    if (!foundValidStepHit)
                    {
                        Vector3 obstructionNormal = GetObstructionNormal(closestSweepHitNormal, moveHitStabilityReport.IsStable);

                        // Movement hit callback
                        CharacterController.OnMovementHit(closestSweepHitCollider, closestSweepHitNormal, closestSweepHitPoint, ref moveHitStabilityReport);

                        // Handle remembering rigidbody hits
                        if (InteractiveRigidbodyHandling && closestSweepHitCollider.attachedRigidbody)
                        {
                            StoreRigidbodyHit(
                                closestSweepHitCollider.attachedRigidbody,
                                transientVelocity,
                                closestSweepHitPoint,
                                obstructionNormal,
                                moveHitStabilityReport);
                        }

                        bool stableOnHit = moveHitStabilityReport.IsStable && !MustUnground();
                        Vector3 velocityBeforeProj = transientVelocity;

                        // Project velocity for next iteration
                        InternalHandleVelocityProjection(
                            stableOnHit,
                            closestSweepHitNormal,
                            obstructionNormal,
                            originalVelocityDirection,
                            ref sweepState,
                            previousHitIsStable,
                            previousVelocity,
                            previousObstructionNormal,
                            ref transientVelocity,
                            ref remainingMovementMagnitude,
                            ref remainingMovementDirection);

                        previousHitIsStable = stableOnHit;
                        previousVelocity = velocityBeforeProj;
                        previousObstructionNormal = obstructionNormal;
                    }
                }
                // If we hit nothing...
                else
                {
                    hitSomethingThisSweepIteration = false;
                }

                // Safety for exceeding max sweeps allowed
                sweepsMade++;
                if (sweepsMade > MaxMovementIterations)
                {
                    if (KillRemainingMovementWhenExceedMaxMovementIterations)
                    {
                        remainingMovementMagnitude = 0f;
                    }

                    if (KillVelocityWhenExceedMaxMovementIterations)
                    {
                        transientVelocity = Vector3.zero;
                    }
                    wasCompleted = false;
                }
            }

            // Move position for the remainder of the movement
            tmpMovedPosition += (remainingMovementDirection * remainingMovementMagnitude);
            _transientPosition = tmpMovedPosition;

            return wasCompleted;
        }

        /// <summary>
        /// 记住一个刚体碰撞信息，用于后续处理
        /// </summary>
        /// <param name="hitRigidbody">碰撞刚体</param>
        /// <param name="hitVelocity">碰撞速度</param>
        /// <param name="hitPoint">碰撞点</param>
        /// <param name="obstructionNormal">阻碍法线</param>
        /// <param name="hitStabilityReport">碰撞稳定性报告</param>
        private void StoreRigidbodyHit_OldUnused(
                        Rigidbody hitRigidbody,
                        Vector3 hitVelocity,
                        Vector3 hitPoint,
                        Vector3 obstructionNormal,
                        HitStabilityReport hitStabilityReport)
        {
            if (_rigidbodyProjectionHitCount < _internalRigidbodyProjectionHits.Length)
            {
                if (!hitRigidbody.GetComponent<KinematicCharacterMotor>())
                {
                    RigidbodyProjectionHit rph = new RigidbodyProjectionHit();
                    rph.Rigidbody = hitRigidbody;
                    rph.HitPoint = hitPoint;
                    rph.EffectiveHitNormal = obstructionNormal;
                    rph.HitVelocity = hitVelocity;
                    rph.StableOnHit = hitStabilityReport.IsStable;

                    _internalRigidbodyProjectionHits[_rigidbodyProjectionHitCount] = rph;
                    _rigidbodyProjectionHitCount++;
                }
            }
        }

        /// <summary>
        /// 当检测到碰撞时处理运动投影
        /// Processes movement projection upon detecting a hit
        /// <param name="stableOnHit">是否稳定站立</param>
        /// <param name="hitNormal">碰撞法线</param>
        /// <param name="obstructionNormal">阻碍法线</param>
        /// <param name="originalDirection">原始方向</param>
        /// <param name="sweepState">扫掠状态</param>
        /// <param name="previousHitIsStable">前一个碰撞是否稳定</param>
        /// <param name="previousVelocity">前一个速度</param>
        /// <param name="previousObstructionNormal">前一个阻碍法线</param>
        /// </summary>
        private void InternalHandleVelocityProjection_OldUnused(bool stableOnHit, Vector3 hitNormal, Vector3 obstructionNormal, Vector3 originalDirection,
            ref MovementSweepState sweepState, bool previousHitIsStable, Vector3 previousVelocity, Vector3 previousObstructionNormal,
            ref Vector3 transientVelocity, ref float remainingMovementMagnitude, ref Vector3 remainingMovementDirection)
        {
            if (transientVelocity.sqrMagnitude <= 0f)
            {
                return;
            }

            Vector3 velocityBeforeProjection = transientVelocity;

            if (stableOnHit)
            {
                LastMovementIterationFoundAnyGround = true;
                HandleVelocityProjection(ref transientVelocity, obstructionNormal, stableOnHit);
            }
            else
            {
                // Handle projection
                if (sweepState == MovementSweepState.Initial)
                {
                    HandleVelocityProjection(ref transientVelocity, obstructionNormal, stableOnHit);
                    sweepState = MovementSweepState.AfterFirstHit;
                }
                // Blocking crease handling
                else if (sweepState == MovementSweepState.AfterFirstHit)
                {
                    EvaluateCrease(
                        transientVelocity,
                        previousVelocity,
                        obstructionNormal,
                        previousObstructionNormal,
                        stableOnHit,
                        previousHitIsStable,
                        GroundingStatus.IsStableOnGround && !MustUnground(),
                        out bool foundCrease,
                        out Vector3 creaseDirection);

                    if (foundCrease)
                    {
                        if (GroundingStatus.IsStableOnGround && !MustUnground())
                        {
                            transientVelocity = Vector3.zero;
                            sweepState = MovementSweepState.FoundBlockingCorner;
                        }
                        else
                        {
                            transientVelocity = Vector3.Project(transientVelocity, creaseDirection);
                            sweepState = MovementSweepState.FoundBlockingCrease;
                        }
                    }
                    else
                    {
                        HandleVelocityProjection(ref transientVelocity, obstructionNormal, stableOnHit);
                    }
                }
                // Blocking corner handling
                else if (sweepState == MovementSweepState.FoundBlockingCrease)
                {
                    transientVelocity = Vector3.zero;
                    sweepState = MovementSweepState.FoundBlockingCorner;
                }
            }

            if (HasPlanarConstraint)
            {
                transientVelocity = Vector3.ProjectOnPlane(transientVelocity, PlanarConstraintAxis.normalized);
            }

            float newVelocityFactor = transientVelocity.magnitude / velocityBeforeProjection.magnitude;
            remainingMovementMagnitude *= newVelocityFactor;
            remainingMovementDirection = transientVelocity.normalized;
        }

        private void EvaluateCrease_OldUnused(
            Vector3 currentCharacterVelocity,
            Vector3 previousCharacterVelocity,
            Vector3 currentHitNormal,
            Vector3 previousHitNormal,
            bool currentHitIsStable,
            bool previousHitIsStable,
            bool characterIsStable,
            out bool isValidCrease,
            out Vector3 creaseDirection)
        {
            isValidCrease = false;
            creaseDirection = default;

            if (!characterIsStable || !currentHitIsStable || !previousHitIsStable)
            {
                Vector3 tmpBlockingCreaseDirection = Vector3.Cross(currentHitNormal, previousHitNormal).normalized;
                float dotPlanes = Vector3.Dot(currentHitNormal, previousHitNormal);
                bool isVelocityConstrainedByCrease = false;

                // Avoid calculations if the two planes are the same
                if (dotPlanes < 0.999f)
                {
                    // TODO: can this whole part be made simpler? (with 2d projections, etc)
                    Vector3 normalAOnCreasePlane = Vector3.ProjectOnPlane(currentHitNormal, tmpBlockingCreaseDirection).normalized;
                    Vector3 normalBOnCreasePlane = Vector3.ProjectOnPlane(previousHitNormal, tmpBlockingCreaseDirection).normalized;
                    float dotPlanesOnCreasePlane = Vector3.Dot(normalAOnCreasePlane, normalBOnCreasePlane);

                    Vector3 enteringVelocityDirectionOnCreasePlane = Vector3.ProjectOnPlane(previousCharacterVelocity, tmpBlockingCreaseDirection).normalized;

                    if (dotPlanesOnCreasePlane <= (Vector3.Dot(-enteringVelocityDirectionOnCreasePlane, normalAOnCreasePlane) + 0.001f) &&
                        dotPlanesOnCreasePlane <= (Vector3.Dot(-enteringVelocityDirectionOnCreasePlane, normalBOnCreasePlane) + 0.001f))
                    {
                        isVelocityConstrainedByCrease = true;
                    }
                }

                if (isVelocityConstrainedByCrease)
                {
                    // Flip crease direction to make it representative of the real direction our velocity would be projected to
                    if (Vector3.Dot(tmpBlockingCreaseDirection, currentCharacterVelocity) < 0f)
                    {
                        tmpBlockingCreaseDirection = -tmpBlockingCreaseDirection;
                    }

                    isValidCrease = true;
                    creaseDirection = tmpBlockingCreaseDirection;
                }
            }
        }

        /// <summary>
        /// /// <summary>
        /// 当检测到碰撞时，处理速度投影
        /// 允许你重写（自定义）速度在障碍物上的投影方式
        /// Allows you to override the way velocity is projected on an obstruction
        /// </summary>
        private void HandleVelocityProjection_OldUnused(ref Vector3 velocity, Vector3 obstructionNormal, bool stableOnHit)
        {
            if (GroundingStatus.IsStableOnGround && !MustUnground())
            {
                // On stable slopes, simply reorient the movement without any loss
                if (stableOnHit)
                {
                    velocity = GetDirectionTangentToSurface(velocity, obstructionNormal) * velocity.magnitude;
                }
                // On blocking hits, project the movement on the obstruction while following the grounding plane
                else
                {
                    Vector3 obstructionRightAlongGround = Vector3.Cross(obstructionNormal, GroundingStatus.GroundNormal).normalized;
                    Vector3 obstructionUpAlongGround = Vector3.Cross(obstructionRightAlongGround, obstructionNormal).normalized;
                    velocity = GetDirectionTangentToSurface(velocity, obstructionUpAlongGround) * velocity.magnitude;
                    velocity = Vector3.ProjectOnPlane(velocity, obstructionNormal);
                }
            }
            else
            {
                if (stableOnHit)
                {
                    // Handle stable landing
                    velocity = Vector3.ProjectOnPlane(velocity, CharacterUp);
                    velocity = GetDirectionTangentToSurface(velocity, obstructionNormal) * velocity.magnitude;
                }
                // Handle generic obstruction
                else
                {
                    velocity = Vector3.ProjectOnPlane(velocity, obstructionNormal);
                }
            }
        }

        /// <summary>
        /// 允许你重写（自定义）角色对可物理碰撞刚体的推动/交互逻辑。
        /// 如果这个交互需要影响角色的速度，就必须修改 processedVelocity。
        /// Allows you to override the way hit rigidbodies are pushed / interacted with. 
        /// ProcessedVelocity is what must be modified if this interaction affects the character's velocity.
        /// </summary>
        public virtual void HandleSimulatedRigidbodyInteraction(ref Vector3 processedVelocity, RigidbodyProjectionHit hit, float deltaTime)
        {
        }

        /// <summary>
        /// 考虑刚体碰撞对速度的影响
        /// Takes into account rigidbody hits for adding to the velocity
        /// <param name="processedVelocity">处理后的速度</param>
        /// <param name="deltaTime">时间步长</param>
        /// </summary>
        private void ProcessVelocityForRigidbodyHits(ref Vector3 processedVelocity, float deltaTime)
        {
            for (int i = 0; i < _rigidbodyProjectionHitCount; i++)
            {
                RigidbodyProjectionHit bodyHit = _internalRigidbodyProjectionHits[i];

                if (bodyHit.Rigidbody && !_rigidbodiesPushedThisMoveList.Contains(bodyHit.Rigidbody))
                {
                    if (_internalRigidbodyProjectionHits[i].Rigidbody != _attachedRigidbody)
                    {
                        // Remember we hit this rigidbody
                        _rigidbodiesPushedThisMoveList.Add(bodyHit.Rigidbody);

                        float characterMass = SimulatedCharacterMass;
                        Vector3 characterVelocity = bodyHit.HitVelocity;

                        KinematicCharacterMotor hitCharacterMotor = bodyHit.Rigidbody.GetComponent<KinematicCharacterMotor>();
                        bool hitBodyIsCharacter = hitCharacterMotor != null;
                        bool hitBodyIsDynamic = !bodyHit.Rigidbody.isKinematic;
                        float hitBodyMass = bodyHit.Rigidbody.mass;
                        float hitBodyMassAtPoint = bodyHit.Rigidbody.mass; // todo
                        Vector3 hitBodyVelocity = bodyHit.Rigidbody.linearVelocity;
                        if (hitBodyIsCharacter)
                        {
                            hitBodyMass = hitCharacterMotor.SimulatedCharacterMass;
                            hitBodyMassAtPoint = hitCharacterMotor.SimulatedCharacterMass; // todo
                            hitBodyVelocity = hitCharacterMotor.BaseVelocity;
                        }
                        else if (!hitBodyIsDynamic)
                        {
                            PhysicsMover physicsMover = bodyHit.Rigidbody.GetComponent<PhysicsMover>();
                            if (physicsMover)
                            {
                                hitBodyVelocity = physicsMover.Velocity;
                            }
                        }

                        // Calculate the ratio of the total mass that the character mass represents
                        float characterToBodyMassRatio = 1f;
                        {
                            if (characterMass + hitBodyMassAtPoint > 0f)
                            {
                                characterToBodyMassRatio = characterMass / (characterMass + hitBodyMassAtPoint);
                            }
                            else
                            {
                                characterToBodyMassRatio = 0.5f;
                            }

                            // Hitting a non-dynamic body
                            if (!hitBodyIsDynamic)
                            {
                                characterToBodyMassRatio = 0f;
                            }
                            // Emulate kinematic body interaction
                            else if (RigidbodyInteractionType == RigidbodyInteractionType.Kinematic && !hitBodyIsCharacter)
                            {
                                characterToBodyMassRatio = 1f;
                            }
                        }

                        ComputeCollisionResolutionForHitBody(
                            bodyHit.EffectiveHitNormal,
                            characterVelocity,
                            hitBodyVelocity,
                            characterToBodyMassRatio,
                            out Vector3 velocityChangeOnCharacter,
                            out Vector3 velocityChangeOnBody);

                        processedVelocity += velocityChangeOnCharacter;

                        if (hitBodyIsCharacter)
                        {
                            hitCharacterMotor.BaseVelocity += velocityChangeOnCharacter;
                        }
                        else if (hitBodyIsDynamic)
                        {
                            bodyHit.Rigidbody.AddForceAtPosition(velocityChangeOnBody, bodyHit.HitPoint, ForceMode.VelocityChange);
                        }

                        if (RigidbodyInteractionType == RigidbodyInteractionType.SimulatedDynamic)
                        {
                            HandleSimulatedRigidbodyInteraction(ref processedVelocity, bodyHit, deltaTime);
                        }
                    }
                }
            }

        }

        public void ComputeCollisionResolutionForHitBody(
            Vector3 hitNormal,
            Vector3 characterVelocity,
            Vector3 bodyVelocity,
            float characterToBodyMassRatio,
            out Vector3 velocityChangeOnCharacter,
            out Vector3 velocityChangeOnBody)
        {
            velocityChangeOnCharacter = default;
            velocityChangeOnBody = default;

            float bodyToCharacterMassRatio = 1f - characterToBodyMassRatio;
            float characterVelocityMagnitudeOnHitNormal = Vector3.Dot(characterVelocity, hitNormal);
            float bodyVelocityMagnitudeOnHitNormal = Vector3.Dot(bodyVelocity, hitNormal);

            // if character velocity was going against the obstruction, restore the portion of the velocity that got projected during the movement phase
            if (characterVelocityMagnitudeOnHitNormal < 0f)
            {
                Vector3 restoredCharacterVelocity = hitNormal * characterVelocityMagnitudeOnHitNormal;
                velocityChangeOnCharacter += restoredCharacterVelocity;
            }

            // solve impulse velocities on both bodies, but only if the body velocity would be giving resistance to the character in any way
            if (bodyVelocityMagnitudeOnHitNormal > characterVelocityMagnitudeOnHitNormal)
            {
                Vector3 relativeImpactVelocity = hitNormal * (bodyVelocityMagnitudeOnHitNormal - characterVelocityMagnitudeOnHitNormal);
                velocityChangeOnCharacter += relativeImpactVelocity * bodyToCharacterMassRatio;
                velocityChangeOnBody += -relativeImpactVelocity * characterToBodyMassRatio;
            }
        }


        /// <summary>
        /// 判断输入的碰撞器是否有效用于碰撞处理
        /// Determines if the input collider is valid for collision processing
        /// </summary>
        /// <param name="coll">碰撞器</param>
        /// <returns> Returns true if the collider is valid </returns>
        private bool InternalIsColliderValidForCollisions(Collider coll)
        {
            Rigidbody colliderAttachedRigidbody = coll.attachedRigidbody;
            if (colliderAttachedRigidbody)
            {
                bool isRigidbodyKinematic = colliderAttachedRigidbody.isKinematic;

                // If movement is made from AttachedRigidbody, ignore the AttachedRigidbody
                // 如果角色现在这次移动，是被脚下平台/附着刚体带着走，那就别再把这个刚体本身当障碍物来撞
                if (_isMovingFromAttachedRigidbody
                            && (!isRigidbodyKinematic
                            || colliderAttachedRigidbody == _attachedRigidbody))
                {
                    return false;
                }

                // don't collide with dynamic rigidbodies if our RigidbodyInteractionType is kinematic
                // 如果角色当前设置成 运动学交互模式，那它就不跟动态刚体做真实碰撞阻挡
                if (RigidbodyInteractionType == RigidbodyInteractionType.Kinematic && !isRigidbodyKinematic)
                {
                    // wake up rigidbody - 激活附加刚体
                    if (coll.attachedRigidbody)
                    {
                        coll.attachedRigidbody.WakeUp();
                    }

                    return false;
                }
            }

            // Custom checks - 业务层开发者的过滤逻辑
            bool colliderValid = CharacterController.IsColliderValidForCollisions(coll);
            if (!colliderValid)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 评估碰撞的稳定性
        /// Determines if the motor is considered stable on a given hit
        /// </summary>
        /// <param name="hitCollider">碰撞器</param>
        /// <param name="hitNormal">碰撞法线</param>
        /// <param name="hitPoint">碰撞点</param>
        /// <param name="atCharacterPosition">角色位置</param>
        /// <param name="atCharacterRotation">角色旋转</param>
        /// <param name="withCharacterVelocity">角色速度</param>
        /// <param name="stabilityReport">稳定性报告</param>
        public void EvaluateHitStability_OldUnused(Collider hitCollider,
                                         Vector3 hitNormal,
                                         Vector3 hitPoint,
                                         Vector3 atCharacterPosition,
                                         Quaternion atCharacterRotation,
                                         Vector3 withCharacterVelocity,
                                         ref HitStabilityReport stabilityReport)
        {
            if (!_solveGrounding)
            {
                stabilityReport.IsStable = false;
                return;
            }

            Vector3 atCharacterUp = atCharacterRotation * _cachedWorldUp;
            Vector3 innerHitDirection = Vector3.ProjectOnPlane(hitNormal, atCharacterUp).normalized;

            stabilityReport.IsStable = this.IsStableOnNormal(hitNormal);

            stabilityReport.FoundInnerNormal = false;
            stabilityReport.FoundOuterNormal = false;
            stabilityReport.InnerNormal = hitNormal;
            stabilityReport.OuterNormal = hitNormal;

            // Ledge handling
            if (LedgeAndDenivelationHandling)
            {
                float ledgeCheckHeight = MinDistanceForLedge;
                if (StepHandling != StepHandlingMethod.None)
                {
                    ledgeCheckHeight = MaxStepHeight;
                }

                bool isStableLedgeInner = false;
                bool isStableLedgeOuter = false;

                if (CharacterCollisionsRaycast(
                        hitPoint + (atCharacterUp * SecondaryProbesVertical) + (innerHitDirection * SecondaryProbesHorizontal),
                        -atCharacterUp,
                        ledgeCheckHeight + SecondaryProbesVertical,
                        out RaycastHit innerLedgeHit,
                        _internalCharacterHits) > 0)
                {
                    Vector3 innerLedgeNormal = innerLedgeHit.normal;
                    stabilityReport.InnerNormal = innerLedgeNormal;
                    stabilityReport.FoundInnerNormal = true;
                    isStableLedgeInner = IsStableOnNormal(innerLedgeNormal);
                }

                if (CharacterCollisionsRaycast(
                        hitPoint + (atCharacterUp * SecondaryProbesVertical) + (-innerHitDirection * SecondaryProbesHorizontal),
                        -atCharacterUp,
                        ledgeCheckHeight + SecondaryProbesVertical,
                        out RaycastHit outerLedgeHit,
                        _internalCharacterHits) > 0)
                {
                    Vector3 outerLedgeNormal = outerLedgeHit.normal;
                    stabilityReport.OuterNormal = outerLedgeNormal;
                    stabilityReport.FoundOuterNormal = true;
                    isStableLedgeOuter = IsStableOnNormal(outerLedgeNormal);
                }

                stabilityReport.LedgeDetected = (isStableLedgeInner != isStableLedgeOuter);
                if (stabilityReport.LedgeDetected)
                {
                    stabilityReport.IsOnEmptySideOfLedge = isStableLedgeOuter && !isStableLedgeInner;
                    stabilityReport.LedgeGroundNormal = isStableLedgeOuter ? stabilityReport.OuterNormal : stabilityReport.InnerNormal;
                    stabilityReport.LedgeRightDirection = Vector3.Cross(hitNormal, stabilityReport.LedgeGroundNormal).normalized;
                    stabilityReport.LedgeFacingDirection = Vector3.ProjectOnPlane(Vector3.Cross(stabilityReport.LedgeGroundNormal, stabilityReport.LedgeRightDirection), CharacterUp).normalized;
                    stabilityReport.DistanceFromLedge = Vector3.ProjectOnPlane((hitPoint - (atCharacterPosition + (atCharacterRotation * _characterTransformToCapsuleBottom))), atCharacterUp).magnitude;
                    stabilityReport.IsMovingTowardsEmptySideOfLedge = Vector3.Dot(withCharacterVelocity.normalized, stabilityReport.LedgeFacingDirection) > 0f;
                }

                if (stabilityReport.IsStable)
                {
                    stabilityReport.IsStable = IsStableWithSpecialCases(ref stabilityReport, withCharacterVelocity);
                }
            }

            // Step handling
            if (StepHandling != StepHandlingMethod.None && !stabilityReport.IsStable)
            {
                // Stepping not supported on dynamic rigidbodies
                Rigidbody hitRigidbody = hitCollider.attachedRigidbody;
                if (!(hitRigidbody && !hitRigidbody.isKinematic))
                {
                    DetectSteps(atCharacterPosition, atCharacterRotation, hitPoint, innerHitDirection, ref stabilityReport);

                    if (stabilityReport.ValidStepDetected)
                    {
                        stabilityReport.IsStable = true;
                    }
                }
            }

            CharacterController.ProcessHitStabilityReport(hitCollider, hitNormal, hitPoint, atCharacterPosition, atCharacterRotation, ref stabilityReport);
        }


        /// <summary>
        /// 检测台阶
        /// Detects steps
        /// </summary>
        /// <param name="characterPosition">角色位置</param>
        /// <param name="characterRotation">角色旋转</param>
        /// <param name="hitPoint">碰撞点</param>
        /// <param name="innerHitDirection">内向碰撞方向</param>
        /// <param name="stabilityReport">稳定性报告</param>
        private void DetectSteps_OldUnused(Vector3 characterPosition, Quaternion characterRotation, Vector3 hitPoint, Vector3 innerHitDirection, ref HitStabilityReport stabilityReport)
        {
            int nbStepHits = 0;
            Collider tmpCollider;
            RaycastHit outerStepHit;
            Vector3 characterUp = characterRotation * _cachedWorldUp;
            Vector3 verticalCharToHit = Vector3.Project((hitPoint - characterPosition), characterUp);
            Vector3 horizontalCharToHitDirection = Vector3.ProjectOnPlane((hitPoint - characterPosition), characterUp).normalized;
            Vector3 stepCheckStartPos = (hitPoint - verticalCharToHit) + (characterUp * MaxStepHeight) + (horizontalCharToHitDirection * CollisionOffset * 3f);

            // Do outer step check with capsule cast on hit point
            nbStepHits = CharacterCollisionsSweep(
                            stepCheckStartPos,
                            characterRotation,
                            -characterUp,
                            MaxStepHeight + CollisionOffset,
                            out outerStepHit,
                            _internalCharacterHits,
                            0f,
                            true);

            // Check for overlaps and obstructions at the hit position
            if (CheckStepValidity(nbStepHits, characterPosition, characterRotation, innerHitDirection, stepCheckStartPos, out tmpCollider))
            {
                stabilityReport.ValidStepDetected = true;
                stabilityReport.SteppedCollider = tmpCollider;
            }

            if (StepHandling == StepHandlingMethod.Extra && !stabilityReport.ValidStepDetected)
            {
                // Do min reach step check with capsule cast on hit point
                stepCheckStartPos = characterPosition + (characterUp * MaxStepHeight) + (-innerHitDirection * MinRequiredStepDepth);
                nbStepHits = CharacterCollisionsSweep(
                                stepCheckStartPos,
                                characterRotation,
                                -characterUp,
                                MaxStepHeight - CollisionOffset,
                                out outerStepHit,
                                _internalCharacterHits,
                                0f,
                                true);

                // Check for overlaps and obstructions at the hit position
                if (CheckStepValidity(nbStepHits, characterPosition, characterRotation, innerHitDirection, stepCheckStartPos, out tmpCollider))
                {
                    stabilityReport.ValidStepDetected = true;
                    stabilityReport.SteppedCollider = tmpCollider;
                }
            }
        }

        /// <summary>
        /// 检查台阶的有效性
        /// Checks the validity of a step
        /// </summary>
        /// <param name="nbStepHits">台阶命中数</param>
        /// <param name="characterPosition">角色位置</param>
        /// <param name="characterRotation">角色旋转</param>
        /// <param name="innerHitDirection">内向碰撞方向</param>
        /// <param name="stepCheckStartPos">台阶检查起始位置</param>
        /// <param name="hitCollider">碰撞器</param>
        /// <returns> Returns true if the step is valid </returns>
        private bool CheckStepValidity_OldUnused(int nbStepHits, Vector3 characterPosition, Quaternion characterRotation, Vector3 innerHitDirection, Vector3 stepCheckStartPos, out Collider hitCollider)
        {
            hitCollider = null;
            Vector3 characterUp = characterRotation * Vector3.up;

            // Find the farthest valid hit for stepping
            bool foundValidStepPosition = false;

            while (nbStepHits > 0 && !foundValidStepPosition)
            {
                // Get farthest hit among the remaining hits
                RaycastHit farthestHit = new RaycastHit();
                float farthestDistance = 0f;
                int farthestIndex = 0;
                for (int i = 0; i < nbStepHits; i++)
                {
                    float hitDistance = _internalCharacterHits[i].distance;
                    if (hitDistance > farthestDistance)
                    {
                        farthestDistance = hitDistance;
                        farthestHit = _internalCharacterHits[i];
                        farthestIndex = i;
                    }
                }

                Vector3 characterPositionAtHit = stepCheckStartPos + (-characterUp * (farthestHit.distance - CollisionOffset));

                int atStepOverlaps = CharacterCollisionsOverlap(characterPositionAtHit, characterRotation, _internalProbedColliders);
                if (atStepOverlaps <= 0)
                {
                    // Check for outer hit slope normal stability at the step position
                    if (CharacterCollisionsRaycast(
                            farthestHit.point + (characterUp * SecondaryProbesVertical) + (-innerHitDirection * SecondaryProbesHorizontal),
                            -characterUp,
                            MaxStepHeight + SecondaryProbesVertical,
                            out RaycastHit outerSlopeHit,
                            _internalCharacterHits,
                            true) > 0)
                    {
                        if (IsStableOnNormal(outerSlopeHit.normal))
                        {
                            // Cast upward to detect any obstructions to moving there
                            if (CharacterCollisionsSweep(
                                                characterPosition, // position
                                                characterRotation, // rotation
                                                characterUp, // direction
                                                MaxStepHeight - farthestHit.distance, // distance
                                                out RaycastHit tmpUpObstructionHit, // closest hit
                                                _internalCharacterHits) // all hits
                                    <= 0)
                            {
                                // Do inner step check...
                                bool innerStepValid = false;
                                RaycastHit innerStepHit;

                                if (AllowSteppingWithoutStableGrounding)
                                {
                                    innerStepValid = true;
                                }
                                else
                                {
                                    // At the capsule center at the step height
                                    if (CharacterCollisionsRaycast(
                                            characterPosition + Vector3.Project((characterPositionAtHit - characterPosition), characterUp),
                                            -characterUp,
                                            MaxStepHeight,
                                            out innerStepHit,
                                            _internalCharacterHits,
                                            true) > 0)
                                    {
                                        if (IsStableOnNormal(innerStepHit.normal))
                                        {
                                            innerStepValid = true;
                                        }
                                    }
                                }

                                if (!innerStepValid)
                                {
                                    // At inner step of the step point
                                    if (CharacterCollisionsRaycast(
                                            farthestHit.point + (innerHitDirection * SecondaryProbesHorizontal),
                                            -characterUp,
                                            MaxStepHeight,
                                            out innerStepHit,
                                            _internalCharacterHits,
                                            true) > 0)
                                    {
                                        if (IsStableOnNormal(innerStepHit.normal))
                                        {
                                            innerStepValid = true;
                                        }
                                    }
                                }

                                // Final validation of step
                                if (innerStepValid)
                                {
                                    hitCollider = farthestHit.collider;
                                    foundValidStepPosition = true;
                                    return true;
                                }
                            }
                        }
                    }
                }

                // Discard hit if not valid step
                if (!foundValidStepPosition)
                {
                    nbStepHits--;
                    if (farthestIndex < nbStepHits)
                    {
                        _internalCharacterHits[farthestIndex] = _internalCharacterHits[nbStepHits];
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 获取刚体的真实线性速度（考虑旋转速度）
        /// Get true linear velocity (taking into account rotational velocity) on a given point of a rigidbody
        /// </summary>
        /// <param name="interactiveRigidbody">交互刚体</param>
        /// <param name="atPoint">点</param>
        /// <param name="deltaTime">时间步长</param>
        /// <param name="linearVelocity">线性速度</param>
        /// <param name="angularVelocity">旋转速度</param>
        public void GetVelocityFromRigidbodyMovement(Rigidbody interactiveRigidbody,
                                                     Vector3 atPoint,
                                                     float deltaTime,
                                                     out Vector3 linearVelocity,
                                                     out Vector3 angularVelocity)
        {
            if (deltaTime > 0f)
            {
                linearVelocity = interactiveRigidbody.linearVelocity;
                angularVelocity = interactiveRigidbody.angularVelocity;
                if (interactiveRigidbody.isKinematic)
                {
                    PhysicsMover physicsMover = interactiveRigidbody.GetComponent<PhysicsMover>();
                    if (physicsMover)
                    {
                        linearVelocity = physicsMover.Velocity;
                        angularVelocity = physicsMover.AngularVelocity;
                    }
                }

                if (angularVelocity != Vector3.zero)
                {
                    Vector3 centerOfRotation = interactiveRigidbody.transform.TransformPoint(interactiveRigidbody.centerOfMass);

                    Vector3 centerOfRotationToPoint = atPoint - centerOfRotation;
                    Quaternion rotationFromInteractiveRigidbody = Quaternion.Euler(Mathf.Rad2Deg * angularVelocity * deltaTime);
                    Vector3 finalPointPosition = centerOfRotation + (rotationFromInteractiveRigidbody * centerOfRotationToPoint);
                    linearVelocity += (finalPointPosition - atPoint) / deltaTime;
                }
            }
            else
            {
                linearVelocity = default;
                angularVelocity = default;
                return;
            }
        }

        /// <summary>
        /// 获取碰撞器的交互刚体
        /// Determines if a collider has an attached interactive rigidbody
        /// </summary>
        /// <param name="onCollider">碰撞器</param>
        /// <returns> Returns the interactive rigidbody </returns>
        /// </summary>
        private Rigidbody GetInteractiveRigidbody(Collider onCollider)
        {
            // 拿到碰撞器的附着刚体
            Rigidbody colliderAttachedRigidbody = onCollider.attachedRigidbody;
            if (colliderAttachedRigidbody)
            {
                // 如果附着刚体有物理移动组件 或者是 非静态刚体
                if (colliderAttachedRigidbody.gameObject.GetComponent<PhysicsMover>()
                    || !colliderAttachedRigidbody.isKinematic)
                {
                    return colliderAttachedRigidbody;
                }
            }
            return null;
        }

        /// <summary>
        /// 检测角色胶囊体是否与任何可碰撞物体重叠
        /// Detect if the character capsule is overlapping with anything collidable
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <param name="overlappedColliders">重叠碰撞器</param>
        /// <param name="inflate">膨胀值</param>
        /// <param name="acceptOnlyStableGroundLayer">是否只检测稳定地面</param>
        /// <returns> Returns number of overlaps </returns>
        public int CharacterCollisionsOverlap(Vector3 position,
                                              Quaternion rotation,
                                              Collider[] overlappedColliders,
                                              float inflate = 0f,
                                              bool acceptOnlyStableGroundLayer = false)
        {
            // 设置查询层
            int queryLayers = CollidableLayers;
            // 是否将稳定地面层加入查询层
            if (acceptOnlyStableGroundLayer)
                queryLayers = CollidableLayers & StableGroundLayers;

            // 计算胶囊体的两个圆心
            Vector3 bottom = position + (rotation * _characterTransformToCapsuleBottomHemi);
            Vector3 top = position + (rotation * _characterTransformToCapsuleTopHemi);

            // 如果膨胀值不为0则增加胶囊体的底和头 将数值提升上去
            if (inflate != 0f)
            {
                bottom += rotation * Vector3.down * inflate;
                top += rotation * Vector3.up * inflate;
            }

            int nbHits = 0;
            // 进行胶囊体扫略检测
            int nbUnfilteredHits = Physics.OverlapCapsuleNonAlloc(
                        bottom,
                        top,
                        Capsule.radius + inflate,
                        overlappedColliders,
                        queryLayers,
                        QueryTriggerInteraction.Ignore);

            // Filter out invalid colliders - 过滤掉无效的碰撞体
            nbHits = nbUnfilteredHits;
            for (int i = nbUnfilteredHits - 1; i >= 0; i--)
            {
                if (!CheckIfColliderValidForCollisions(overlappedColliders[i]))
                {
                    nbHits--;
                    if (i < nbHits)
                    {
                        // 倒序是为了高效删除元素 属于小算法 并没有什么特殊其他原因
                        overlappedColliders[i] = overlappedColliders[nbHits];
                    }
                }
            }

            return nbHits;
        }

        /// <summary>
        /// 检测角色胶囊体是否与任何可碰撞物体重叠
        /// Detect if the character capsule is overlapping with anything
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <param name="overlappedColliders">重叠碰撞器</param>
        /// <param name="layers">层</param>
        /// <param name="triggerInteraction">触发交互</param>
        /// <param name="inflate">膨胀值</param>
        /// <returns> Returns number of overlaps </returns>
        public int CharacterOverlap(Vector3 position, Quaternion rotation, Collider[] overlappedColliders, LayerMask layers, QueryTriggerInteraction triggerInteraction, float inflate = 0f)
        {
            Vector3 bottom = position + (rotation * _characterTransformToCapsuleBottomHemi);
            Vector3 top = position + (rotation * _characterTransformToCapsuleTopHemi);
            if (inflate != 0f)
            {
                bottom += (rotation * Vector3.down * inflate);
                top += (rotation * Vector3.up * inflate);
            }

            int nbHits = 0;
            int nbUnfilteredHits = Physics.OverlapCapsuleNonAlloc(
                        bottom,
                        top,
                        Capsule.radius + inflate,
                        overlappedColliders,
                        layers,
                        triggerInteraction);

            // Filter out the character capsule itself
            nbHits = nbUnfilteredHits;
            for (int i = nbUnfilteredHits - 1; i >= 0; i--)
            {
                if (overlappedColliders[i] == Capsule)
                {
                    nbHits--;
                    if (i < nbHits)
                    {
                        overlappedColliders[i] = overlappedColliders[nbHits];
                    }
                }
            }

            return nbHits;
        }

        /// <summary>
        /// 角色胶囊体扫略检测
        /// Sweeps the capsule's volume to detect collision hits
        /// </summary>
        /// <returns> Returns the number of hits 返回命中数</returns>
        public int CharacterCollisionsSweep(
                            Vector3 position, // 角色扫略的起始世界坐标
                            Quaternion rotation, // 角色扫略的旋转（朝向）
                            Vector3 direction, // 扫略方向（比如移动方向、向下地面检测方向）
                            float distance, // 扫略最大距离
                            out RaycastHit closestHit, // 存储扫略到的最近有效碰撞
                            RaycastHit[] hits, // 缓存碰撞结果数组（性能优化）
                            float inflate = 0f, // 胶囊体膨胀值（微调防穿模）
                            bool acceptOnlyStableGroundLayer = false // 是否只检测稳定地面
        )
        {
            int queryLayers = CollidableLayers;
            if (acceptOnlyStableGroundLayer)
            {
                queryLayers = CollidableLayers & StableGroundLayers;
            }

            // 计算胶囊体的底和头
            Vector3 bottom = position + (rotation * _characterTransformToCapsuleBottomHemi) - (direction * SweepProbingBackstepDistance);
            Vector3 top = position + (rotation * _characterTransformToCapsuleTopHemi) - (direction * SweepProbingBackstepDistance);

            if (inflate != 0f)
            {
                bottom += rotation * Vector3.down * inflate;
                top += rotation * Vector3.up * inflate;
            }

            // Capsule cast
            int nbHits = 0;
            int nbUnfilteredHits = Physics.CapsuleCastNonAlloc(
                    bottom,
                    top,
                    Capsule.radius + inflate,
                    direction,
                    hits,
                    distance + SweepProbingBackstepDistance,
                    queryLayers,
                    QueryTriggerInteraction.Ignore);

            // Hits filter
            closestHit = new RaycastHit();
            float closestDistance = Mathf.Infinity;
            nbHits = nbUnfilteredHits;
            for (int i = nbUnfilteredHits - 1; i >= 0; i--)
            {
                hits[i].distance -= SweepProbingBackstepDistance;

                RaycastHit hit = hits[i];
                float hitDistance = hit.distance;

                // Filter out the invalid hits
                if (hitDistance <= 0f || !CheckIfColliderValidForCollisions(hit.collider))
                {
                    nbHits--;
                    if (i < nbHits)
                    {
                        hits[i] = hits[nbHits];
                    }
                }
                else
                {
                    // Remember closest valid hit
                    if (hitDistance < closestDistance)
                    {
                        closestHit = hit;
                        closestDistance = hitDistance;
                    }
                }
            }

            return nbHits;
        }

        /// <summary>
        /// 角色地面扫略
        /// Casts the character volume in the character's downward direction to detect ground
        /// 沿着角色自身向下方向，投射角色胶囊体的碰撞体积，检测是否接触地面
        /// </summary>
        /// <returns> Returns the number of hits 返回命中数</returns>
        private bool CharacterGroundSweep(Vector3 position,
                                          Quaternion rotation,
                                          Vector3 direction,
                                          float distance,
                                          out RaycastHit closestHit)
        {
            closestHit = new RaycastHit();

            // Capsule cast - 胶囊体投射!
            int nbUnfilteredHits = Physics.CapsuleCastNonAlloc(
                // 分别传入两个圆心: 脚下位置 + 正确旋转量(角色局部空间里的偏移) - 探测方向修正距离
                position + (rotation * _characterTransformToCapsuleBottomHemi) - (direction * GroundProbingBackstepDistance),
                position + (rotation * _characterTransformToCapsuleTopHemi) - (direction * GroundProbingBackstepDistance),
                Capsule.radius,
                direction,
                _internalCharacterHits,
                distance + GroundProbingBackstepDistance,
                CollidableLayers & StableGroundLayers,// 检测碰撞体层和稳定层
                QueryTriggerInteraction.Ignore);

            // Hits filter 是否命中了东西
            bool foundValidHit = false;
            float closestDistance = Mathf.Infinity;

            // 从检测结果数组里面一个一个挑出来碰撞信息迭出来最近的那个碰撞
            for (int i = 0; i < nbUnfilteredHits; i++)
            {
                RaycastHit hit = _internalCharacterHits[i];
                float hitDistance = hit.distance;

                // Find the closest valid hit 找到最近的有效命中
                if (hitDistance > 0f && CheckIfColliderValidForCollisions(hit.collider))
                {
                    // 如果和命中碰撞体的距离小于最近距离 
                    if (hitDistance < closestDistance)
                    {
                        // 更新最近距离
                        closestHit = hit;
                        closestHit.distance -= GroundProbingBackstepDistance;
                        closestDistance = hitDistance;

                        // 找到有效碰撞
                        foundValidHit = true;
                    }
                }
            }

            // 返回结果
            return foundValidHit;
        }

        /// <summary>
        /// 角色胶囊体射线检测
        /// Raycasts to detect collision hits
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="direction">方向</param>
        /// <param name="distance">距离</param>
        /// <param name="closestHit">最近碰撞</param>
        /// <param name="hits">碰撞结果</param>
        /// <param name="acceptOnlyStableGroundLayer">是否只检测稳定地面</param>
        /// <returns> Returns the number of hits - 返回搜索结果的条数</returns>
        public int CharacterCollisionsRaycast(Vector3 position,
                                              Vector3 direction,
                                              float distance,
                                              out RaycastHit closestHit,
                                              RaycastHit[] hits,
                                              bool acceptOnlyStableGroundLayer = false)
        {
            int queryLayers = CollidableLayers;
            if (acceptOnlyStableGroundLayer)
            {
                queryLayers = CollidableLayers & StableGroundLayers;
            }

            // Raycast 射线检测
            int nbHits = 0;
            int nbUnfilteredHits = Physics.RaycastNonAlloc(
                position,
                direction,
                hits,
                distance,
                queryLayers,
                QueryTriggerInteraction.Ignore);

            // Hits filter - 命中过滤
            closestHit = new RaycastHit();
            float closestDistance = Mathf.Infinity;
            nbHits = nbUnfilteredHits;

            for (int i = nbUnfilteredHits - 1; i >= 0; i--)
            {
                RaycastHit hit = hits[i];
                float hitDistance = hit.distance;

                // Filter out the invalid hits - 过滤掉无效的命中结果
                if (hitDistance <= 0f ||
                    !CheckIfColliderValidForCollisions(hit.collider))
                {
                    nbHits--;
                    if (i < nbHits)
                    {
                        hits[i] = hits[nbHits];
                    }
                }
                else
                {
                    // Remember closest valid hit - 记住最近的准确命中点
                    if (hitDistance < closestDistance)
                    {
                        closestHit = hit;
                        closestDistance = hitDistance;
                    }
                }
            }

            return nbHits;
        }
    }
}