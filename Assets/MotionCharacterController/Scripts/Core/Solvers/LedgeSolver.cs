using UnityEngine;

namespace MotionCharacterController
{
    /// <summary>
    /// 边缘检测与处理
    /// </summary>
    public static class LedgeSolver
    {
        /// <summary>
        /// 处理边缘稳定性
        /// </summary>
        /// <param name="context">上下文</param>
        /// <param name="queries">碰撞求解器</param>
        /// <param name="hitNormal">碰撞法线</param>
        /// <param name="hitPoint">碰撞点</param>
        /// <param name="atCharacterPosition">角色位置</param>
        /// <param name="atCharacterRotation">角色旋转</param>
        /// <param name="velocity">速度</param>
        /// <param name="report">稳定性报告</param>
        public static void ProcessLedgeStability(
            MccMotorContext context,
            CollisionSolver queries,
            Vector3 hitNormal,
            Vector3 hitPoint,
            Vector3 atCharacterPosition,
            Quaternion atCharacterRotation,
            Vector3 velocity,
            ref HitStabilityReport report)
        {
            // 如果未开启边缘处理 则直接返回
            if (!context.Config.ledgeAndDenivelationHandling)
            {
                return;
            }

            // 获取角色朝上方向
            Vector3 up = atCharacterRotation * Vector3.up;
            // 获取法线在水平面上的投影 作为内外探测方向
            Vector3 innerDirection = Vector3.ProjectOnPlane(hitNormal, up).normalized;
            // 如果水平方向无效 则无法做边缘探测
            if (innerDirection.sqrMagnitude <= 0f)
            {
                return;
            }

            // 计算探测高度
            float checkHeight = context.Config.stepHandling != StepHandlingMethod.None ? context.Config.maxStepHeight : MccConfig.MIN_GROUND_PROBING_DISTANCE;
            bool innerStable = false;
            bool outerStable = false;

            // 内侧探测 沿 innerDirection 偏移后向下打射线
            if (queries.CharacterCollisionsRaycast(hitPoint + up * MccConfig.SECONDARY_PROBES_VERTICAL + innerDirection * MccConfig.SECONDARY_PROBES_HORIZONTAL, -up, checkHeight + MccConfig.SECONDARY_PROBES_VERTICAL, out RaycastHit innerHit, context.InternalHits) > 0)
            {
                report.InnerNormal = innerHit.normal;
                report.FoundInnerNormal = true;
                innerStable = context.IsStableOnNormal(innerHit.normal);
            }

            // 外侧探测 沿 -innerDirection 偏移后向下打射线
            if (queries.CharacterCollisionsRaycast(hitPoint + up * MccConfig.SECONDARY_PROBES_VERTICAL - innerDirection * MccConfig.SECONDARY_PROBES_HORIZONTAL, -up, checkHeight + MccConfig.SECONDARY_PROBES_VERTICAL, out RaycastHit outerHit, context.InternalHits) > 0)
            {
                report.OuterNormal = outerHit.normal;
                report.FoundOuterNormal = true;
                outerStable = context.IsStableOnNormal(outerHit.normal);
            }

            // 内外一侧可站一侧不可站 则判定为边缘
            report.LedgeDetected = innerStable != outerStable;
            if (report.LedgeDetected)
            {
                // 设置边缘相关方向与距离
                report.IsOnEmptySideOfLedge = outerStable && !innerStable;
                report.LedgeGroundNormal = outerStable ? report.OuterNormal : report.InnerNormal;
                report.LedgeRightDirection = Vector3.Cross(hitNormal, report.LedgeGroundNormal).normalized;
                report.LedgeFacingDirection = Vector3.ProjectOnPlane(Vector3.Cross(report.LedgeGroundNormal, report.LedgeRightDirection), context.CharacterUp).normalized;
                report.DistanceFromLedge = Vector3.ProjectOnPlane(hitPoint - (atCharacterPosition + atCharacterRotation * context.TransformToCapsuleBottom), up).magnitude;
                report.IsMovingTowardsEmptySideOfLedge = Vector3.Dot(velocity.normalized, report.LedgeFacingDirection) > 0f;
            }

            // 如果当前判定为稳定 则再做边缘特殊规则校验
            if (report.IsStable)
            {
                report.IsStable = IsStableWithSpecialCases(context, ref report, velocity);
            }
        }

        /// <summary>
        /// 边缘与落差特殊稳定性判定
        /// </summary>
        /// <param name="context">上下文</param>
        /// <param name="report">稳定性报告</param>
        /// <param name="velocity">速度</param>
        /// <returns>是否仍可视为稳定</returns>
        public static bool IsStableWithSpecialCases(MccMotorContext context, ref HitStabilityReport report, Vector3 velocity)
        {
            // 如果未开启边缘处理 则直接视为稳定
            if (!context.Config.ledgeAndDenivelationHandling)
            {
                return true;
            }

            if (report.LedgeDetected)
            {
                // 如果朝悬崖外移动过快 则不稳定
                if (report.IsMovingTowardsEmptySideOfLedge && Vector3.Project(velocity, report.LedgeFacingDirection).magnitude >= context.Config.maxVelocityForLedgeSnap)
                {
                    return false;
                }

                // 如果站在悬空侧且离边缘过远 则不稳定
                if (report.IsOnEmptySideOfLedge && report.DistanceFromLedge > context.Config.maxStableDistanceFromLedge)
                {
                    return false;
                }
            }

            // 如果内外法线落差过大 则不稳定
            if (context.LastGroundingStatus.FoundAnyGround && report.InnerNormal.sqrMagnitude > 0f && report.OuterNormal.sqrMagnitude > 0f)
            {
                if (Vector3.Angle(report.InnerNormal, report.OuterNormal) > context.Config.maxStableDenivelationAngle)
                {
                    return false;
                }

                if (Vector3.Angle(context.LastGroundingStatus.InnerGroundNormal, report.OuterNormal) > context.Config.maxStableDenivelationAngle)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
