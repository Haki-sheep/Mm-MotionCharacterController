using UnityEngine;

namespace MotionCharacterController
{
    /// <summary>
    /// 地面检测与处理
    /// </summary>
    public class GroundSolver
    {
        private readonly MccMotorContext context;
        private readonly CollisionSolver collisionSolver;
        private readonly StepSolver stepSolver;

        public GroundSolver(MccMotorContext context, CollisionSolver collisionSolver, StepSolver stepSolver)
        {
            this.context = context;
            this.collisionSolver = collisionSolver;
            this.stepSolver = stepSolver;
        }

        public void UpdateGrounding(float deltaTime)
        {
            if (!context.SolveGrounding)
            {
                return;
            }

            if (context.Owner.MustUnground())
            {
                context.TransientPosition += context.CharacterUp * (MccConfig.MIN_GROUND_PROBING_DISTANCE * 1.5f);
                context.GroundingStatus = new CharacterGroundingReport { GroundNormal = context.CharacterUp };
            }
            else
            {
                float distance = MccConfig.MIN_GROUND_PROBING_DISTANCE + context.Config.groundDetectionExtraDistance;
                if (!context.LastGroundingStatus.SnappingPrevented && (context.LastGroundingStatus.IsStableOnGround || context.LastMovementIterationFoundAnyGround))
                {
                    distance = Mathf.Max(context.Capsule.radius, context.Config.maxStepHeight) + context.Config.groundDetectionExtraDistance;
                }

                Vector3 probePosition = context.TransientPosition;
                ProbeGround(ref probePosition, context.TransientRotation, distance, ref context.GroundingStatus);
                context.TransientPosition = probePosition;

                if (!context.LastGroundingStatus.IsStableOnGround && context.GroundingStatus.IsStableOnGround)
                {
                    context.BaseVelocity = Vector3.ProjectOnPlane(context.BaseVelocity, context.CharacterUp);
                    context.BaseVelocity = context.GetDirectionTangentToSurface(context.BaseVelocity, context.GroundingStatus.GroundNormal) * context.BaseVelocity.magnitude;
                }
            }

            context.LastMovementIterationFoundAnyGround = false;
            if (context.MustUngroundTimeCounter > 0f)
            {
                context.MustUngroundTimeCounter -= deltaTime;
            }
            context.MustUnground = false;
        }

        public void ProbeGround(ref Vector3 probingPosition, Quaternion rotation, float probingDistance, ref CharacterGroundingReport report)
        {
            probingDistance = Mathf.Max(probingDistance, MccConfig.MIN_GROUND_PROBING_DISTANCE);
            Vector3 sweepPosition = probingPosition;
            Vector3 sweepDirection = rotation * Vector3.down;
            float remainingDistance = probingDistance;

            for (int i = 0; i <= MccConfig.MAX_GROUNDING_SWEEP_ITERATIONS && remainingDistance > 0f; i++)
            {
                if (!CharacterGroundSweep(sweepPosition, rotation, sweepDirection, remainingDistance, out RaycastHit hit))
                {
                    break;
                }

                Vector3 targetPosition = sweepPosition + sweepDirection * hit.distance;
                HitStabilityReport stability = HitStabilityEvaluator.Evaluate(
                    context,
                    collisionSolver,
                    stepSolver,
                    hit.collider,
                    hit.normal,
                    hit.point,
                    targetPosition,
                    rotation,
                    context.BaseVelocity);
                report.FoundAnyGround = true;
                report.GroundNormal = hit.normal;
                report.InnerGroundNormal = stability.InnerNormal;
                report.OuterGroundNormal = stability.OuterNormal;
                report.GroundCollider = hit.collider;
                report.GroundPoint = hit.point;
                report.SnappingPrevented = false;

                if (stability.IsStable)
                {
                    report.SnappingPrevented = !LedgeSolver.IsStableWithSpecialCases(context, ref stability, context.BaseVelocity);
                    report.IsStableOnGround = true;
                    if (!report.SnappingPrevented)
                    {
                        probingPosition = sweepPosition + sweepDirection * Mathf.Max(0f, hit.distance - MccConfig.COLLISION_OFFSET);
                    }

                    context.Owner.Controller?.OnGroundHit(hit.collider, hit.normal, hit.point, ref stability);
                    break;
                }

                Vector3 sweepMovement = sweepDirection * hit.distance + context.CharacterUp * Mathf.Max(MccConfig.COLLISION_OFFSET, hit.distance);
                sweepPosition += sweepMovement;
                remainingDistance = Mathf.Min(MccConfig.GROUND_REBOUND_DISTANCE, Mathf.Max(remainingDistance - sweepMovement.magnitude, 0f));
                sweepDirection = Vector3.ProjectOnPlane(sweepDirection, hit.normal).normalized;
            }
        }

        private bool CharacterGroundSweep(Vector3 position, Quaternion rotation, Vector3 direction, float distance, out RaycastHit closestHit)
        {
            Vector3 bottom = context.GetCapsuleBottomHemiAt(position, rotation) - direction * MccConfig.GROUND_BACKSTEP_DISTANCE;
            Vector3 top = context.GetCapsuleTopHemiAt(position, rotation) - direction * MccConfig.GROUND_BACKSTEP_DISTANCE;
            int count = Physics.CapsuleCastNonAlloc(bottom, top, context.Capsule.radius, direction, context.InternalHits, distance + MccConfig.GROUND_BACKSTEP_DISTANCE, context.CollidableLayers & context.Config.stableGroundLayers, QueryTriggerInteraction.Ignore);

            closestHit = default;
            float closestDistance = Mathf.Infinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = context.InternalHits[i];
                hit.distance -= MccConfig.GROUND_BACKSTEP_DISTANCE;
                if (hit.distance > 0f && context.IsColliderValidForCollisions(hit.collider) && hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    closestHit = hit;
                    found = true;
                }
            }

            return found;
        }
    }
}
