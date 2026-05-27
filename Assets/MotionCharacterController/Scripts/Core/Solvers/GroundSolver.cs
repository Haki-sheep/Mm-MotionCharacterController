using UnityEngine;

namespace MotionCharacterController
{
    public class GroundSolver
    {
        private readonly MccMotorContext context;
        private readonly CollisionSolver collisionSolver;
        private readonly StepSolver stepSolver;
        private readonly LedgeSolver ledgeSolver;

        public GroundSolver(MccMotorContext context, CollisionSolver collisionSolver, StepSolver stepSolver, LedgeSolver ledgeSolver)
        {
            this.context = context;
            this.collisionSolver = collisionSolver;
            this.stepSolver = stepSolver;
            this.ledgeSolver = ledgeSolver;
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
                HitStabilityReport stability = EvaluateHitStability(hit.collider, hit.normal, hit.point, targetPosition, rotation, context.BaseVelocity);
                report.FoundAnyGround = true;
                report.GroundNormal = hit.normal;
                report.InnerGroundNormal = stability.InnerNormal;
                report.OuterGroundNormal = stability.OuterNormal;
                report.GroundCollider = hit.collider;
                report.GroundPoint = hit.point;
                report.SnappingPrevented = false;

                if (stability.IsStable)
                {
                    report.SnappingPrevented = !ledgeSolver.IsStableWithSpecialCases(ref stability, context.BaseVelocity);
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

        public HitStabilityReport EvaluateHitStability(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, Vector3 velocity)
        {
            HitStabilityReport report = new HitStabilityReport
            {
                IsStable = context.SolveGrounding && context.IsStableOnNormal(hitNormal),
                InnerNormal = hitNormal,
                OuterNormal = hitNormal,
            };

            if (!context.SolveGrounding)
            {
                return report;
            }

            ledgeSolver.ProcessLedgeStability(hitNormal, hitPoint, atCharacterPosition, atCharacterRotation, velocity, ref report);

            if (context.Config.stepHandling != StepHandlingMethod.None && !report.IsStable)
            {
                Rigidbody body = hitCollider != null ? hitCollider.attachedRigidbody : null;
                if (!(body != null && !body.isKinematic))
                {
                    stepSolver.DetectSteps(atCharacterPosition, atCharacterRotation, hitPoint, Vector3.ProjectOnPlane(hitNormal, atCharacterRotation * Vector3.up).normalized, ref report);
                    if (report.ValidStepDetected)
                    {
                        report.IsStable = true;
                    }
                }
            }

            context.Owner.Controller?.ProcessHitStabilityReport(hitCollider, hitNormal, hitPoint, atCharacterPosition, atCharacterRotation, ref report);
            return report;
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
