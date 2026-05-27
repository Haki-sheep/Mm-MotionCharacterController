using UnityEngine;

namespace KinematicCharacterController
{
    public partial class KinematicCharacterMotor
    {
        /// <summary>
        /// 判断当前帧是否必须强制脱离地面。
        /// Determines whether the motor must currently unground itself.
        /// </summary>
        /// <returns>如果应该强制脱离地面则返回 true</returns>
        private bool MustUnground()
        {
            return _mustUnground || _mustUngroundTimeCounter > 0f;
        }

        /// <summary>
        /// 判断某个法线是否处于可稳定站立的坡度范围内。
        /// Determines if motor can be considered stable on a given normal.
        /// </summary>
        /// <param name="normal">要判断的表面法线</param>
        /// <returns>是否可稳定站立</returns>
        private bool IsStableOnNormal(Vector3 normal)
        {
            return Vector3.Angle(_characterUp, normal) <= MaxStableSlopeAngle;
        }

        /// <summary>
        /// 在基础坡度判定之外，处理边缘和地形落差等特殊稳定性规则
        /// Determines if a ground hit remains stable after special-case checks.
        /// </summary>
        /// <param name="stabilityReport">命中稳定性报告</param>
        /// <param name="velocity">当前角色速度</param>
        /// <returns>经过特殊规则判断后是否仍可视为稳定地面</returns>
        private bool IsStableWithSpecialCases(ref HitStabilityReport stabilityReport, Vector3 velocity)
        {
            if (LedgeAndDenivelationHandling)
            {
                if (stabilityReport.LedgeDetected)
                {
                    if (stabilityReport.IsMovingTowardsEmptySideOfLedge)
                    {
                        // 朝悬空边冲过去时，如果沿边缘法线方向的速度过大，就不允许继续吸附在地面上。
                        Vector3 velocityOnLedgeNormal = Vector3.Project(velocity, stabilityReport.LedgeFacingDirection);
                        if (velocityOnLedgeNormal.magnitude >= MaxVelocityForLedgeSnap)
                        {
                            return false;
                        }
                    }

                    if (stabilityReport.IsOnEmptySideOfLedge && stabilityReport.DistanceFromLedge > MaxStableDistanceFromLedge)
                    {
                        return false;
                    }
                }

                if (LastGroundingStatus.FoundAnyGround && stabilityReport.InnerNormal.sqrMagnitude != 0f && stabilityReport.OuterNormal.sqrMagnitude != 0f)
                {
                    float denivelationAngle = Vector3.Angle(stabilityReport.InnerNormal, stabilityReport.OuterNormal);
                    if (denivelationAngle > MaxStableDenivelationAngle)
                    {
                        return false;
                    }

                    denivelationAngle = Vector3.Angle(LastGroundingStatus.InnerGroundNormal, stabilityReport.OuterNormal);
                    if (denivelationAngle > MaxStableDenivelationAngle)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 探测角色脚下的有效地面，并在允许时执行地面吸附。
        /// Probes for valid ground and modifies the input position if ground snapping occurs.
        /// </summary>
        /// <param name="probingPosition">探测位置，会在吸附地面时被改写</param>
        /// <param name="atRotation">探测时使用的旋转</param>
        /// <param name="probingDistance">探测距离</param>
        /// <param name="groundingReport">输出的地面报告</param>
        public void ProbeGround(ref Vector3 probingPosition,
                                Quaternion atRotation,
                                float probingDistance,
                                ref CharacterGroundingReport groundingReport)
        {
            // 保证最小探测距离
            if (probingDistance < MinimumGroundProbingDistance)
                probingDistance = MinimumGroundProbingDistance;

            // 扫描迭代次数 与 完成标志
            int groundSweepsMade = 0;
            bool groundSweepingIsOver = false;

            RaycastHit groundSweepHit = new RaycastHit();

            // 扫描起点 与 方向
            Vector3 groundSweepPosition = probingPosition;
            Vector3 groundSweepDirection = atRotation * -_cachedWorldUp;

            // 剩余探测距离 每扫一次就扣一点剩余距离
            float groundProbeDistanceRemaining = probingDistance;


            while (groundProbeDistanceRemaining > 0
                        && groundSweepsMade <= MaxGroundingSweepIterations
                        && !groundSweepingIsOver)
            {

                // 寻找到有效碰撞 并将信息其碰撞信息 groundSweepHit 带出来
                if (CharacterGroundSweep(groundSweepPosition,
                                         atRotation,
                                         groundSweepDirection,
                                         groundProbeDistanceRemaining,
                                         out groundSweepHit))
                {
                    // 预测出来的 角色位置 = 扫描起点 + 探测方向 * 命中距离
                    Vector3 targetPosition = groundSweepPosition + (groundSweepDirection * groundSweepHit.distance);
                    // 创建并填充碰撞报告
                    HitStabilityReport groundHitStabilityReport = new HitStabilityReport();
                    EvaluateHitStability(groundSweepHit.collider, // 碰撞到的物体的碰撞体
                                         groundSweepHit.normal, // 物体的法线
                                         groundSweepHit.point, // 碰撞点
                                         targetPosition, // 角色到达这次地面命中时的位置
                                         _transientRotation,// 角色旋转
                                         BaseVelocity, // 基础速度
                                         ref groundHitStabilityReport // 碰撞报告
                                         );

                    groundingReport.FoundAnyGround = true;
                    groundingReport.GroundNormal = groundSweepHit.normal;
                    groundingReport.InnerGroundNormal = groundHitStabilityReport.InnerNormal;
                    groundingReport.OuterGroundNormal = groundHitStabilityReport.OuterNormal;
                    groundingReport.GroundCollider = groundSweepHit.collider;
                    groundingReport.GroundPoint = groundSweepHit.point;
                    groundingReport.SnappingPrevented = false;

                    // 如果碰撞报告稳定
                    if (groundHitStabilityReport.IsStable)
                    {
                        // 
                        groundingReport.SnappingPrevented =
                                        !IsStableWithSpecialCases(ref groundHitStabilityReport, BaseVelocity);
                        // 稳定站立在了地面上               
                        groundingReport.IsStableOnGround = true;

                        if (!groundingReport.SnappingPrevented)
                        {
                            probingPosition = groundSweepPosition
                                        + (groundSweepDirection * (groundSweepHit.distance - CollisionOffset));
                        }

                        CharacterController.OnGroundHit
                                        (groundSweepHit.collider, groundSweepHit.normal, groundSweepHit.point, ref groundHitStabilityReport);

                        groundSweepingIsOver = true;
                    }
                    else
                    {
                        // 没找到稳定地面时，会沿当前命中面重新投影方向继续尝试，避免一次探测直接失败。
                        Vector3 sweepMovement =
                                    (groundSweepDirection * groundSweepHit.distance)
                                    + ((atRotation * _cachedWorldUp) * Mathf.Max(CollisionOffset, groundSweepHit.distance));

                        groundSweepPosition += sweepMovement;

                        groundProbeDistanceRemaining =
                                    Mathf.Min(GroundProbeReboundDistance,
                                    Mathf.Max(groundProbeDistanceRemaining - sweepMovement.magnitude, 0f));

                        groundSweepDirection = Vector3.ProjectOnPlane
                                    (groundSweepDirection,
                                    groundSweepHit.normal).normalized;
                    }
                }
                else
                {
                    groundSweepingIsOver = true;
                }

                groundSweepsMade++;
            }
        }

        /// <summary>
        /// 评估一次命中是否可被视为稳定地面，并填充稳定性报告。
        /// Determines if the motor is considered stable on a given hit.
        /// 专门做悬崖 / 边缘检测的 通俗来讲就是我这次撞到的地方，最终到底该不该当成可站立地
        /// </summary>
        /// <param name="hitCollider">命中的碰撞器</param>
        /// <param name="hitNormal">命中法线</param>
        /// <param name="hitPoint">命中点</param>
        /// <param name="atCharacterPosition">角色位置</param>
        /// <param name="atCharacterRotation">角色旋转</param>
        /// <param name="withCharacterVelocity">角色速度</param>
        /// <param name="stabilityReport">输出的稳定性报告</param>
        public void EvaluateHitStability(Collider hitCollider,
                                         Vector3 hitNormal,
                                         Vector3 hitPoint,
                                         Vector3 atCharacterPosition,
                                         Quaternion atCharacterRotation,
                                         Vector3 withCharacterVelocity,
                                         ref HitStabilityReport stabilityReport)
        {
            // 如果不开启地面解算直接滚回去
            if (!_solveGrounding)
            {
                stabilityReport.IsStable = false;
                return;
            }

            // 角色当前的上方 
            Vector3 atCharacterUp = atCharacterRotation * _cachedWorldUp;
            // 把命中法线投影到水平面后得到的方向 - 通常来讲是朝地面实体那边的水平向量
            Vector3 innerHitDirection = Vector3.ProjectOnPlane(hitNormal, atCharacterUp).normalized;

            // 初始化碰撞稳定性报告
            stabilityReport.IsStable = IsStableOnNormal(hitNormal);
            stabilityReport.FoundInnerNormal = false;
            stabilityReport.FoundOuterNormal = false;

            // 这两个意思就是往平台外面测一点 往平台里面测一点
            stabilityReport.InnerNormal = hitNormal;
            stabilityReport.OuterNormal = hitNormal;

            // 如果开启了精确检测边缘功能
            if (LedgeAndDenivelationHandling)
            {
                // 边缘检测高度
                float ledgeCheckHeight = StepHandling != StepHandlingMethod.None ? MaxStepHeight : MinDistanceForLedge;

                // 内侧 边缘地面是否稳定可站立
                bool isStableLedgeInner = false;

                // 外侧 边缘地面是否稳定可站立
                bool isStableLedgeOuter = false;

                // 先内测
                if (CharacterCollisionsRaycast(hitPoint +
                                (atCharacterUp * SecondaryProbesVertical)
                                + (innerHitDirection * SecondaryProbesHorizontal),
                                //起点 = 碰撞点位置 + 角色向上方向的修正 + 水平投影上的修正

                                -atCharacterUp,// 向下投射射线
                                ledgeCheckHeight + SecondaryProbesVertical,
                                out RaycastHit innerLedgeHit,
                                _internalCharacterHits) > 0)

                {
                    // 发现内法线 
                    stabilityReport.InnerNormal = innerLedgeHit.normal;
                    stabilityReport.FoundInnerNormal = true;
                    // 判断其是否稳定站立
                    isStableLedgeInner = IsStableOnNormal(innerLedgeHit.normal);
                }

                if (CharacterCollisionsRaycast(hitPoint + (atCharacterUp * SecondaryProbesVertical)
                                            + (-innerHitDirection * SecondaryProbesHorizontal),
                                            -atCharacterUp,
                                            ledgeCheckHeight + SecondaryProbesVertical,
                                            out RaycastHit outerLedgeHit,
                                            _internalCharacterHits) > 0)
                {
                    stabilityReport.OuterNormal = outerLedgeHit.normal;
                    stabilityReport.FoundOuterNormal = true;
                    isStableLedgeOuter = IsStableOnNormal(outerLedgeHit.normal);
                }

                // 一边稳定一边悬空，说明角色站在边缘附近，需要进入边缘判定逻辑。
                stabilityReport.LedgeDetected = isStableLedgeInner != isStableLedgeOuter;

                if (stabilityReport.LedgeDetected)
                {
                    // 角色是否更偏向边缘 空的一侧 

                    // 外侧有可站面，而内侧没有可站面
                    stabilityReport.IsOnEmptySideOfLedge =
                                isStableLedgeOuter && !isStableLedgeInner;

                    // 记录边缘真正可站立那一侧的地面法线
                    stabilityReport.LedgeGroundNormal =
                                isStableLedgeOuter ? stabilityReport.OuterNormal : stabilityReport.InnerNormal;

                    // 算出沿着边缘横着走的方向 用于 后面继续推出边缘朝外的方向
                    stabilityReport.LedgeRightDirection =
                                Vector3.Cross(hitNormal, stabilityReport.LedgeGroundNormal).normalized;

                    // 边缘朝向 空侧 的方向 用于 判断角色是不是正朝悬空那边移动
                    stabilityReport.LedgeFacingDirection =
                                Vector3.ProjectOnPlane(
                                        Vector3.Cross(stabilityReport.LedgeGroundNormal, stabilityReport.LedgeRightDirection), CharacterUp).normalized;
                    
                    // 角色底部到当前边缘命中点的水平距离
                    stabilityReport.DistanceFromLedge =
                                Vector3.ProjectOnPlane(hitPoint -
                                (atCharacterPosition + (atCharacterRotation * _characterTransformToCapsuleBottom)), 
                                atCharacterUp).magnitude;

                    // 当前移动方向是否正朝着边缘的空侧前进
                    stabilityReport.IsMovingTowardsEmptySideOfLedge =
                                Vector3.Dot(withCharacterVelocity.normalized, stabilityReport.LedgeFacingDirection) > 0f;
                }

                // 如果基础坡度判定本来可站立
                // 还要再过一遍边缘/地形落差等特殊规则 因为 坡度能站 不代表 边缘情况也该站
                if (stabilityReport.IsStable)
                {
                    // 最后再得出稳不稳
                    stabilityReport.IsStable = IsStableWithSpecialCases(ref stabilityReport, withCharacterVelocity);
                }
            }

            // 开启台阶处理且当前命中还不稳定时，尝试把这次命中当作 可跨越台阶 处理
            if (StepHandling != StepHandlingMethod.None && !stabilityReport.IsStable)
            {
                // 动态刚体不做台阶检测，避免踩上会乱动的物体 
                // 也就是说过你场景移动的刚体不应该是随便一个台阶形状的物体 
                // 想要把角色"铲"上去 估计是不太可以的 会推走角色
                Rigidbody hitRigidbody = hitCollider.attachedRigidbody;

                if (!(hitRigidbody && !hitRigidbody.isKinematic))
                {
                    // 检查这次命中是否满足台阶条件
                    DetectSteps(atCharacterPosition, atCharacterRotation, hitPoint, innerHitDirection, ref stabilityReport);
                    if (stabilityReport.ValidStepDetected)
                    {
                        // 找到有效台阶后，把这次命中视为稳定可站立
                        stabilityReport.IsStable = true;
                    }
                }
            }

            //  把最终稳定性报告交给角色控制器做自定义补充处理
            CharacterController.ProcessHitStabilityReport(hitCollider,
                                                          hitNormal,
                                                          hitPoint,
                                                          atCharacterPosition,
                                                          atCharacterRotation,
                                                          ref stabilityReport);
        }
    }
}
