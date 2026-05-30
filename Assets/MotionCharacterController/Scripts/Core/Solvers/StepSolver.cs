using UnityEngine;

namespace MotionCharacterController
{
    /// <summary>
    /// 台阶检测与处理
    /// </summary>
    public class StepSolver
    {
        private readonly MccMotorContext context;

        public StepSolver(MccMotorContext context)
        {
            this.context = context;
        }

        /// <summary>
        /// 检测台阶
        /// </summary>
        /// <param name="queries">碰撞求解器</param>
        /// <param name="characterPosition">角色位置</param>
        /// <param name="characterRotation">角色旋转</param>
        /// <param name="hitPoint">碰撞点</param>
        /// <param name="innerHitDirection"> 法线在水平面上朝外的投影向量 
        /// 该值一般是在没上台阶时 检测到的台阶竖着面的法线在水平面上朝外的投影向量</param>
        /// <param name="report">稳定性报告</param>
        public void DetectSteps(
            CollisionSolver queries,
            Vector3 characterPosition,
            Quaternion characterRotation,
            Vector3 hitPoint,
            Vector3 innerHitDirection,
            ref HitStabilityReport report)
        {
            if (innerHitDirection.sqrMagnitude <= 0f)
                return;

            // 获取角色朝上方向
            Vector3 up = characterRotation * Vector3.up;
            // 获取碰撞点到角色位置的垂直方向
            Vector3 verticalToHit = Vector3.Project(hitPoint - characterPosition, up);
            // 获取碰撞点到角色位置的平面方向
            Vector3 horizontalDirection = Vector3.ProjectOnPlane(hitPoint - characterPosition, up).normalized;
            // 获取台阶检测起点
            Vector3 start = hitPoint - verticalToHit + // 将检测到的台阶碰撞点拉低到角色水平位置
                            up * context.Config.maxStepHeight + // 从标准位置向上抬一个可跨越高度
                            horizontalDirection * MccConfig.COLLISION_OFFSET * 3f; // 稍微往台阶外偏一点点 避免起点卡在台阶的垂直面上

            // 检测台阶
            int count = queries.CharacterCollisionsSweep(start, characterRotation, -up, context.Config.maxStepHeight + MccConfig.COLLISION_OFFSET, out _, context.InternalHits, 0f, true);
            // 检查台阶是否有效
            if (CheckStepValidity(queries, count, characterPosition, characterRotation, innerHitDirection, start, out Collider hitCollider))
            {
                report.ValidStepDetected = true;
                report.SteppedCollider = hitCollider;
                return;
            }

            // 如果台阶处理方式不是加强台阶处理 则直接返回
            if (context.Config.stepHandling != StepHandlingMethod.Extra)
            {
                return;
            }

            // 获取台阶检测起点
            start = characterPosition + up * context.Config.maxStepHeight - innerHitDirection * context.Config.minRequiredStepDepth;
            // 检测台阶
            count = queries.CharacterCollisionsSweep(start, characterRotation, -up, context.Config.maxStepHeight - MccConfig.COLLISION_OFFSET, out _, context.InternalHits, 0f, true);
            // 检查台阶是否有效
            if (CheckStepValidity(queries, count, characterPosition, characterRotation, innerHitDirection, start, out hitCollider))
            {
                report.ValidStepDetected = true;
                report.SteppedCollider = hitCollider;
            }
        }

        public bool TryStep(CollisionSolver queries, ref Vector3 movedPosition, ref Vector3 velocity, RaycastHit moveHit, HitStabilityReport report)
        {
            if (report.SteppedCollider == null)
            {
                return false;
            }

            float obstructionCorrelation = Mathf.Abs(Vector3.Dot(moveHit.normal, context.CharacterUp));
            if (obstructionCorrelation > 0.15f)
            {
                return false;
            }

            Vector3 forward = Vector3.ProjectOnPlane(-moveHit.normal, context.CharacterUp).normalized;
            Vector3 start = movedPosition + forward * MccConfig.STEPPING_FORWARD_DISTANCE + context.CharacterUp * context.Config.maxStepHeight;
            int count = queries.CharacterCollisionsSweep(start, context.TransientRotation, -context.CharacterUp, context.Config.maxStepHeight, out _, context.InternalHits, 0f, true);

            for (int i = 0; i < count; i++)
            {
                if (context.InternalHits[i].collider == report.SteppedCollider)
                {
                    movedPosition = start - context.CharacterUp * Mathf.Max(0f, context.InternalHits[i].distance - MccConfig.COLLISION_OFFSET);
                    velocity = Vector3.ProjectOnPlane(velocity, context.CharacterUp);
                    return true;
                }
            }

            return false;
        }

        private bool CheckStepValidity(CollisionSolver queries, int hitCount, Vector3 characterPosition, Quaternion characterRotation, Vector3 innerHitDirection, Vector3 stepCheckStart, out Collider hitCollider)
        {
            hitCollider = null;
            Vector3 up = characterRotation * Vector3.up;

            while (hitCount > 0)
            {
                int farthestIndex = 0;
                float farthestDistance = 0f;
                for (int i = 0; i < hitCount; i++)
                {
                    if (context.InternalHits[i].distance > farthestDistance)
                    {
                        farthestDistance = context.InternalHits[i].distance;
                        farthestIndex = i;
                    }
                }

                RaycastHit farthestHit = context.InternalHits[farthestIndex];
                Vector3 candidatePosition = stepCheckStart - up * Mathf.Max(0f, farthestHit.distance - MccConfig.COLLISION_OFFSET);

                bool clearAtStep = queries.CharacterCollisionsOverlap(candidatePosition, characterRotation, context.InternalColliders) <= 0;
                bool stableOuter = queries.CharacterCollisionsRaycast(farthestHit.point + up * MccConfig.SECONDARY_PROBES_VERTICAL - innerHitDirection * MccConfig.SECONDARY_PROBES_HORIZONTAL, -up, context.Config.maxStepHeight + MccConfig.SECONDARY_PROBES_VERTICAL, out RaycastHit outerHit, context.InternalHits, true) > 0
                                   && context.IsStableOnNormal(outerHit.normal);
                bool clearAbove = queries.CharacterCollisionsSweep(characterPosition, characterRotation, up, context.Config.maxStepHeight - farthestHit.distance, out _, context.InternalHits) <= 0;
                bool stableInner = context.Config.allowSteppingWithoutStableGrounding
                                   || queries.CharacterCollisionsRaycast(characterPosition + Vector3.Project(candidatePosition - characterPosition, up), -up, context.Config.maxStepHeight, out RaycastHit innerHit, context.InternalHits, true) > 0
                                   && context.IsStableOnNormal(innerHit.normal);

                if (clearAtStep && stableOuter && clearAbove && stableInner)
                {
                    hitCollider = farthestHit.collider;
                    return true;
                }

                hitCount--;
                if (farthestIndex < hitCount)
                {
                    context.InternalHits[farthestIndex] = context.InternalHits[hitCount];
                }
            }

            return false;
        }
    }
}
