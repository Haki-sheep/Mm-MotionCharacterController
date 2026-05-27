using UnityEngine;

namespace MotionCharacterController
{
    public class StepSolver
    {
        private readonly MccMotorContext context;
        private CollisionSolver collisionSolver;
        private GroundSolver groundSolver;

        public StepSolver(MccMotorContext context)
        {
            this.context = context;
        }

        public void Bind(CollisionSolver collisionSolver, GroundSolver groundSolver)
        {
            this.collisionSolver = collisionSolver;
            this.groundSolver = groundSolver;
        }

        public HitStabilityReport EvaluateHitStability(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, Vector3 velocity)
        {
            return groundSolver.EvaluateHitStability(hitCollider, hitNormal, hitPoint, atCharacterPosition, atCharacterRotation, velocity);
        }

        public void DetectSteps(Vector3 characterPosition, Quaternion characterRotation, Vector3 hitPoint, Vector3 innerHitDirection, ref HitStabilityReport report)
        {
            if (innerHitDirection.sqrMagnitude <= 0f)
            {
                return;
            }

            Vector3 up = characterRotation * Vector3.up;
            Vector3 verticalToHit = Vector3.Project(hitPoint - characterPosition, up);
            Vector3 horizontalDirection = Vector3.ProjectOnPlane(hitPoint - characterPosition, up).normalized;
            Vector3 start = hitPoint - verticalToHit + up * context.Config.maxStepHeight + horizontalDirection * MccConfig.COLLISION_OFFSET * 3f;

            int count = collisionSolver.CharacterCollisionsSweep(start, characterRotation, -up, context.Config.maxStepHeight + MccConfig.COLLISION_OFFSET, out _, context.InternalHits, 0f, true);
            if (CheckStepValidity(count, characterPosition, characterRotation, innerHitDirection, start, out Collider hitCollider))
            {
                report.ValidStepDetected = true;
                report.SteppedCollider = hitCollider;
                return;
            }

            if (context.Config.stepHandling != StepHandlingMethod.Extra)
            {
                return;
            }

            start = characterPosition + up * context.Config.maxStepHeight - innerHitDirection * context.Config.minRequiredStepDepth;
            count = collisionSolver.CharacterCollisionsSweep(start, characterRotation, -up, context.Config.maxStepHeight - MccConfig.COLLISION_OFFSET, out _, context.InternalHits, 0f, true);
            if (CheckStepValidity(count, characterPosition, characterRotation, innerHitDirection, start, out hitCollider))
            {
                report.ValidStepDetected = true;
                report.SteppedCollider = hitCollider;
            }
        }

        public bool TryStep(ref Vector3 movedPosition, ref Vector3 velocity, RaycastHit moveHit, HitStabilityReport report)
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
            int count = collisionSolver.CharacterCollisionsSweep(start, context.TransientRotation, -context.CharacterUp, context.Config.maxStepHeight, out _, context.InternalHits, 0f, true);

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

        private bool CheckStepValidity(int hitCount, Vector3 characterPosition, Quaternion characterRotation, Vector3 innerHitDirection, Vector3 stepCheckStart, out Collider hitCollider)
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

                bool clearAtStep = collisionSolver.CharacterCollisionsOverlap(candidatePosition, characterRotation, context.InternalColliders) <= 0;
                bool stableOuter = collisionSolver.CharacterCollisionsRaycast(farthestHit.point + up * MccConfig.SECONDARY_PROBES_VERTICAL - innerHitDirection * MccConfig.SECONDARY_PROBES_HORIZONTAL, -up, context.Config.maxStepHeight + MccConfig.SECONDARY_PROBES_VERTICAL, out RaycastHit outerHit, context.InternalHits, true) > 0
                                   && context.IsStableOnNormal(outerHit.normal);
                bool clearAbove = collisionSolver.CharacterCollisionsSweep(characterPosition, characterRotation, up, context.Config.maxStepHeight - farthestHit.distance, out _, context.InternalHits) <= 0;
                bool stableInner = context.Config.allowSteppingWithoutStableGrounding
                                   || collisionSolver.CharacterCollisionsRaycast(characterPosition + Vector3.Project(candidatePosition - characterPosition, up), -up, context.Config.maxStepHeight, out RaycastHit innerHit, context.InternalHits, true) > 0
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
