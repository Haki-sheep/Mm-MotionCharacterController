using UnityEngine;

namespace KinematicCharacterController
{
    public partial class KinematicCharacterMotor
    {
        /// <summary>
        /// 根据位移和时间反推出速度。
        /// Converts a movement delta to velocity.
        /// </summary>
        /// <param name="movement">位移量</param>
        /// <param name="deltaTime">时间步长</param>
        /// <returns>对应速度</returns>
        public Vector3 GetVelocityFromMovement(Vector3 movement, float deltaTime)
        {
            return deltaTime <= 0f ? Vector3.zero : movement / deltaTime;
        }

        /// <summary>
        /// 获取方向向量在某个表面切线方向上的重定向结果。
        /// Gets a direction tangent to the given surface.
        /// </summary>
        /// <param name="direction">原始方向</param>
        /// <param name="surfaceNormal">表面法线</param>
        /// <returns>沿表面切线的方向</returns>
        public Vector3 GetDirectionTangentToSurface(Vector3 direction, Vector3 surfaceNormal)
        {
            Vector3 directionRight = Vector3.Cross(direction, _characterUp);
            return Vector3.Cross(surfaceNormal, directionRight).normalized;
        }

        /// <summary>
        /// 根据当前命中情况，计算用于阻挡投影的法线
        /// Computes the effective obstruction normal used for movement projection.
        /// 让角色撞墙以后既能挡住前进，又尽量别把角色从地面上拱起来
        /// </summary>
        /// <param name="hitNormal">原始命中法线</param>
        /// <param name="stableOnHit">该命中是否稳定</param>
        /// <returns>用于投影速度的阻挡法线</returns>
        private Vector3 GetObstructionNormal(Vector3 hitNormal, bool stableOnHit)
        {
            Vector3 obstructionNormal = hitNormal;

            // 如果地面状态稳定 且 非强制离地 且 不是稳定的地面
            if (GroundingStatus.IsStableOnGround && !MustUnground() && !stableOnHit)
            {
                // 把这个阻挡方向沿着地面重新修正一下 
                Vector3 obstructionLeftAlongGround = Vector3.Cross(GroundingStatus.GroundNormal,
                                                                   obstructionNormal).normalized;

                obstructionNormal = Vector3.Cross(obstructionLeftAlongGround, _characterUp).normalized;
            }

            return obstructionNormal.sqrMagnitude == 0f ? hitNormal : obstructionNormal;
        }

        /// <summary>
        /// 判断某个碰撞体是否应参与角色碰撞处理。
        /// Checks whether a collider should be considered for character collisions.
        /// </summary>
        /// <param name="coll">要检查的碰撞体</param>
        /// <returns>是否参与碰撞处理</returns>
        private bool CheckIfColliderValidForCollisions(Collider coll)
        {
            // 如果碰撞体是胶囊体本身则返回false
            if (coll == Capsule)
            {
                return false;
            }

            return InternalIsColliderValidForCollisions(coll);
        }

        /// <summary>
        /// 记录一次刚体命中，供后续速度处理阶段使用。
        /// Stores a rigidbody hit for later velocity processing.
        /// </summary>
        /// <param name="hitRigidbody">命中的刚体</param>
        /// <param name="hitVelocity">命中时的角色速度</param>
        /// <param name="hitPoint">命中点</param>
        /// <param name="obstructionNormal">有效阻挡法线</param>
        /// <param name="hitStabilityReport">命中稳定性报告</param>
        private void StoreRigidbodyHit(Rigidbody hitRigidbody, Vector3 hitVelocity, Vector3 hitPoint, Vector3 obstructionNormal, HitStabilityReport hitStabilityReport)
        {
            if (_rigidbodyProjectionHitCount < _internalRigidbodyProjectionHits.Length && !hitRigidbody.GetComponent<KinematicCharacterMotor>())
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

        /// <summary>
        /// 根据当前碰撞结果重新投影速度和剩余移动量。
        /// Handles velocity projection after a movement hit.
        /// </summary>
        /// <param name="stableOnHit">当前命中是否稳定</param>
        /// <param name="hitNormal">原始命中法线</param>
        /// <param name="obstructionNormal">有效阻挡法线</param>
        /// <param name="originalDirection">原始移动方向</param>
        /// <param name="sweepState">当前扫掠状态</param>
        /// <param name="previousHitIsStable">上一次命中是否稳定</param>
        /// <param name="previousVelocity">上一次投影前速度</param>
        /// <param name="previousObstructionNormal">上一次阻挡法线</param>
        /// <param name="transientVelocity">当前临时速度</param>
        /// <param name="remainingMovementMagnitude">剩余移动长度</param>
        /// <param name="remainingMovementDirection">剩余移动方向</param>
        private void InternalHandleVelocityProjection(bool stableOnHit, Vector3 hitNormal, Vector3 obstructionNormal, Vector3 originalDirection,
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
            else if (sweepState == MovementSweepState.Initial)
            {
                HandleVelocityProjection(ref transientVelocity, obstructionNormal, stableOnHit);
                sweepState = MovementSweepState.AfterFirstHit;
            }
            else if (sweepState == MovementSweepState.AfterFirstHit)
            {
                EvaluateCrease(transientVelocity, previousVelocity, obstructionNormal, previousObstructionNormal, stableOnHit, previousHitIsStable, GroundingStatus.IsStableOnGround && !MustUnground(), out bool foundCrease, out Vector3 creaseDirection);
                if (foundCrease)
                {
                    // 第二次阻挡如果形成“夹角折线”，就要改成沿折线方向运动，或者直接视为卡角停止。
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
            else if (sweepState == MovementSweepState.FoundBlockingCrease)
            {
                transientVelocity = Vector3.zero;
                sweepState = MovementSweepState.FoundBlockingCorner;
            }

            if (HasPlanarConstraint)
            {
                transientVelocity = Vector3.ProjectOnPlane(transientVelocity, PlanarConstraintAxis.normalized);
            }

            float newVelocityFactor = transientVelocity.magnitude / velocityBeforeProjection.magnitude;
            remainingMovementMagnitude *= newVelocityFactor;
            remainingMovementDirection = transientVelocity.normalized;
        }

        /// <summary>
        /// 判断两次阻挡是否形成会限制移动的折线边缘。
        /// Evaluates whether two hits form a valid blocking crease.
        /// </summary>
        /// <param name="currentCharacterVelocity">当前角色速度</param>
        /// <param name="previousCharacterVelocity">上一次角色速度</param>
        /// <param name="currentHitNormal">当前命中法线</param>
        /// <param name="previousHitNormal">上一次命中法线</param>
        /// <param name="currentHitIsStable">当前命中是否稳定</param>
        /// <param name="previousHitIsStable">上一次命中是否稳定</param>
        /// <param name="characterIsStable">角色当前是否稳定着地</param>
        /// <param name="isValidCrease">输出：是否形成有效折线</param>
        /// <param name="creaseDirection">输出：折线方向</param>
        private void EvaluateCrease(Vector3 currentCharacterVelocity, Vector3 previousCharacterVelocity, Vector3 currentHitNormal, Vector3 previousHitNormal, bool currentHitIsStable, bool previousHitIsStable, bool characterIsStable, out bool isValidCrease, out Vector3 creaseDirection)
        {
            isValidCrease = false;
            creaseDirection = default;
            if (!characterIsStable || !currentHitIsStable || !previousHitIsStable)
            {
                Vector3 tmpBlockingCreaseDirection = Vector3.Cross(currentHitNormal, previousHitNormal).normalized;
                float dotPlanes = Vector3.Dot(currentHitNormal, previousHitNormal);
                bool isVelocityConstrainedByCrease = false;
                if (dotPlanes < 0.999f)
                {
                    // 这里不是简单比较两条法线，而是把它们都投影到折线垂直平面里，再判断速度是否真的被夹住。
                    Vector3 normalAOnCreasePlane = Vector3.ProjectOnPlane(currentHitNormal, tmpBlockingCreaseDirection).normalized;
                    Vector3 normalBOnCreasePlane = Vector3.ProjectOnPlane(previousHitNormal, tmpBlockingCreaseDirection).normalized;
                    float dotPlanesOnCreasePlane = Vector3.Dot(normalAOnCreasePlane, normalBOnCreasePlane);
                    Vector3 enteringVelocityDirectionOnCreasePlane = Vector3.ProjectOnPlane(previousCharacterVelocity, tmpBlockingCreaseDirection).normalized;
                    if (dotPlanesOnCreasePlane <= (Vector3.Dot(-enteringVelocityDirectionOnCreasePlane, normalAOnCreasePlane) + 0.001f) && dotPlanesOnCreasePlane <= (Vector3.Dot(-enteringVelocityDirectionOnCreasePlane, normalBOnCreasePlane) + 0.001f))
                    {
                        isVelocityConstrainedByCrease = true;
                    }
                }

                if (isVelocityConstrainedByCrease)
                {
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
        /// 根据阻挡表面重新投影速度。
        /// Allows you to override the way velocity is projected on an obstruction.
        /// </summary>
        /// <param name="velocity">输入/输出速度</param>
        /// <param name="obstructionNormal">阻挡法线</param>
        /// <param name="stableOnHit">命中是否稳定</param>
        public virtual void HandleVelocityProjection(ref Vector3 velocity, Vector3 obstructionNormal, bool stableOnHit)
        {
            if (GroundingStatus.IsStableOnGround && !MustUnground())
            {
                if (stableOnHit)
                {
                    velocity = GetDirectionTangentToSurface(velocity, obstructionNormal) * velocity.magnitude;
                }
                else
                {
                    Vector3 obstructionRightAlongGround = Vector3.Cross(obstructionNormal, GroundingStatus.GroundNormal).normalized;
                    Vector3 obstructionUpAlongGround = Vector3.Cross(obstructionRightAlongGround, obstructionNormal).normalized;
                    velocity = GetDirectionTangentToSurface(velocity, obstructionUpAlongGround) * velocity.magnitude;
                    velocity = Vector3.ProjectOnPlane(velocity, obstructionNormal);
                }
            }
            else if (stableOnHit)
            {
                velocity = Vector3.ProjectOnPlane(velocity, CharacterUp);
                velocity = GetDirectionTangentToSurface(velocity, obstructionNormal) * velocity.magnitude;
            }
            else
            {
                velocity = Vector3.ProjectOnPlane(velocity, obstructionNormal);
            }
        }
    }
}
