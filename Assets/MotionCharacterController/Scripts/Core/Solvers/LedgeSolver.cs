using UnityEngine;

namespace MotionCharacterController
{
    /// <summary>
    /// 边缘检测与处理
    /// </summary>
    public class LedgeSolver
    {
        private readonly MccMotorContext context;
        private CollisionSolver collisionSolver;

        public LedgeSolver(MccMotorContext context)
        {
            this.context = context;
        }

        /// <summary>
        /// 绑定依赖的求解器
        /// 边缘检测需要依赖碰撞求解器
        /// </summary>
        /// <param name="collisionSolver">碰撞求解器</param>
        public void Bind(CollisionSolver collisionSolver)
        {
            this.collisionSolver = collisionSolver;
        }

        public void ProcessLedgeStability(Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, Vector3 velocity, ref HitStabilityReport report)
        {
            if (!context.Config.ledgeAndDenivelationHandling)
            {
                return;
            }

            Vector3 up = atCharacterRotation * Vector3.up;
            Vector3 innerDirection = Vector3.ProjectOnPlane(hitNormal, up).normalized;
            if (innerDirection.sqrMagnitude <= 0f)
            {
                return;
            }

            float checkHeight = context.Config.stepHandling != StepHandlingMethod.None ? context.Config.maxStepHeight : MccConfig.MIN_GROUND_PROBING_DISTANCE;
            bool innerStable = false;
            bool outerStable = false;

            if (collisionSolver.CharacterCollisionsRaycast(hitPoint + up * MccConfig.SECONDARY_PROBES_VERTICAL + innerDirection * MccConfig.SECONDARY_PROBES_HORIZONTAL, -up, checkHeight + MccConfig.SECONDARY_PROBES_VERTICAL, out RaycastHit innerHit, context.InternalHits) > 0)
            {
                report.InnerNormal = innerHit.normal;
                report.FoundInnerNormal = true;
                innerStable = context.IsStableOnNormal(innerHit.normal);
            }

            if (collisionSolver.CharacterCollisionsRaycast(hitPoint + up * MccConfig.SECONDARY_PROBES_VERTICAL - innerDirection * MccConfig.SECONDARY_PROBES_HORIZONTAL, -up, checkHeight + MccConfig.SECONDARY_PROBES_VERTICAL, out RaycastHit outerHit, context.InternalHits) > 0)
            {
                report.OuterNormal = outerHit.normal;
                report.FoundOuterNormal = true;
                outerStable = context.IsStableOnNormal(outerHit.normal);
            }

            report.LedgeDetected = innerStable != outerStable;
            if (report.LedgeDetected)
            {
                report.IsOnEmptySideOfLedge = outerStable && !innerStable;
                report.LedgeGroundNormal = outerStable ? report.OuterNormal : report.InnerNormal;
                report.LedgeRightDirection = Vector3.Cross(hitNormal, report.LedgeGroundNormal).normalized;
                report.LedgeFacingDirection = Vector3.ProjectOnPlane(Vector3.Cross(report.LedgeGroundNormal, report.LedgeRightDirection), context.CharacterUp).normalized;
                report.DistanceFromLedge = Vector3.ProjectOnPlane(hitPoint - (atCharacterPosition + atCharacterRotation * context.TransformToCapsuleBottom), up).magnitude;
                report.IsMovingTowardsEmptySideOfLedge = Vector3.Dot(velocity.normalized, report.LedgeFacingDirection) > 0f;
            }

            if (report.IsStable)
            {
                report.IsStable = IsStableWithSpecialCases(ref report, velocity);
            }
        }

        public bool IsStableWithSpecialCases(ref HitStabilityReport report, Vector3 velocity)
        {
            if (!context.Config.ledgeAndDenivelationHandling)
            {
                return true;
            }

            if (report.LedgeDetected)
            {
                if (report.IsMovingTowardsEmptySideOfLedge && Vector3.Project(velocity, report.LedgeFacingDirection).magnitude >= context.Config.maxVelocityForLedgeSnap)
                {
                    return false;
                }

                if (report.IsOnEmptySideOfLedge && report.DistanceFromLedge > context.Config.maxStableDistanceFromLedge)
                {
                    return false;
                }
            }

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
