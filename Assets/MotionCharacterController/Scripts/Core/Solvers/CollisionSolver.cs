using UnityEngine;

namespace MotionCharacterController
{
    /// <summary>
    /// 碰撞检测与处理
    /// </summary>
    public class CollisionSolver
    {
        private readonly MccMotorContext context;
        private readonly StepSolver stepSolver;
        private readonly RigidbodySolver rigidbodySolver;


        /// <summary>
        /// 碰撞求解需要依赖台阶求解器、刚体求解器
        /// </summary>
        /// <param name="context">上下文</param>
        /// <param name="stepSolver">台阶求解器</param>
        /// <param name="rigidbodySolver">刚体求解器</param>
        public CollisionSolver(MccMotorContext context, StepSolver stepSolver, RigidbodySolver rigidbodySolver)
        {
            this.context = context;
            this.stepSolver = stepSolver;
            this.rigidbodySolver = rigidbodySolver;
        }

        public void ResolveInitialOverlaps()
        {
            for (int iteration = 0; iteration < context.Config.maxDecollisionIterations; iteration++)
            {
                int count = CharacterCollisionsOverlap(context.TransientPosition, context.TransientRotation, context.InternalColliders);
                bool solved = true;

                for (int i = 0; i < count; i++)
                {
                    Collider other = context.InternalColliders[i];
                    if (!context.IsColliderValidForCollisions(other))
                    {
                        continue;
                    }

                    if (Physics.ComputePenetration(
                        context.Capsule,
                        context.TransientPosition,
                        context.TransientRotation,
                        other,
                        other.transform.position,
                        other.transform.rotation,
                        out Vector3 direction,
                        out float distance))
                    {
                        bool stable = context.IsStableOnNormal(direction);
                        Vector3 resolutionNormal = GetObstructionNormal(direction, stable);
                        Vector3 movement = resolutionNormal * (distance + MccConfig.COLLISION_OFFSET);
                        context.TransientPosition += movement;
                        RememberOverlap(resolutionNormal, other);
                        solved = false;
                        break;
                    }
                }

                if (solved)
                {
                    break;
                }
            }
        }

        public bool Move(ref Vector3 velocity, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return false;
            }

            if (context.Config.hasPlanarConstraint)
            {
                velocity = Vector3.ProjectOnPlane(velocity, context.Config.planarConstraintAxis.normalized);
            }

            bool completed = true;
            Vector3 originalDirection = velocity.normalized;
            Vector3 remainingDirection = originalDirection;
            float remainingMagnitude = velocity.magnitude * deltaTime;
            Vector3 movedPosition = context.TransientPosition;
            MovementSweepState sweepState = MovementSweepState.Initial;
            bool previousHitStable = false;
            Vector3 previousVelocity = Vector3.zero;
            Vector3 previousObstructionNormal = Vector3.zero;

            ProjectAgainstKnownOverlaps(ref velocity, ref remainingMagnitude, ref remainingDirection, originalDirection, ref sweepState, ref previousHitStable, ref previousVelocity, ref previousObstructionNormal);

            int sweeps = 0;
            while (remainingMagnitude > MccConfig.MIN_CANMOVE_DISTANCE && sweeps <= context.Config.maxMovementIterations)
            {
                if (CharacterCollisionsSweep(movedPosition, context.TransientRotation, remainingDirection, remainingMagnitude + MccConfig.COLLISION_OFFSET, out RaycastHit hit, context.InternalHits) <= 0)
                {
                    movedPosition += remainingDirection * remainingMagnitude;
                    remainingMagnitude = 0f;
                    break;
                }

                Vector3 sweepMovement = remainingDirection * Mathf.Max(0f, hit.distance - MccConfig.COLLISION_OFFSET);
                movedPosition += sweepMovement;
                remainingMagnitude -= sweepMovement.magnitude;

                HitStabilityReport report = stepSolver.EvaluateHitStability(hit.collider, hit.normal, hit.point, movedPosition, context.TransientRotation, velocity);
                bool stepped = false;
                if (context.SolveGrounding && context.Config.stepHandling != StepHandlingMethod.None && report.ValidStepDetected)
                {
                    stepped = stepSolver.TryStep(ref movedPosition, ref velocity, hit, report);
                    if (stepped)
                    {
                        remainingDirection = velocity.normalized;
                        remainingMagnitude = velocity.magnitude * deltaTime;
                    }
                }

                if (!stepped)
                {
                    Vector3 obstructionNormal = GetObstructionNormal(hit.normal, report.IsStable);
                    context.Owner.Controller?.OnMovementHit(hit.collider, hit.normal, hit.point, ref report);
                    if (hit.collider != null && hit.collider.attachedRigidbody != null)
                    {
                        rigidbodySolver.StoreHit(hit.collider.attachedRigidbody, velocity, hit.point, obstructionNormal);
                    }

                    bool stableOnHit = report.IsStable && !context.Owner.MustUnground();
                    Vector3 velocityBeforeProjection = velocity;
                    HandleVelocityProjection(stableOnHit, hit.normal, obstructionNormal, originalDirection, ref sweepState, previousHitStable, previousVelocity, previousObstructionNormal, ref velocity, ref remainingMagnitude, ref remainingDirection);
                    previousHitStable = stableOnHit;
                    previousVelocity = velocityBeforeProjection;
                    previousObstructionNormal = obstructionNormal;
                }

                sweeps++;
                if (sweeps > context.Config.maxMovementIterations)
                {
                    completed = false;
                    if (context.Config.killRemainingMovementWhenExceedMaxMovementIterations)
                    {
                        remainingMagnitude = 0f;
                    }
                    if (context.Config.killVelocityWhenExceedMaxMovementIterations)
                    {
                        velocity = Vector3.zero;
                    }
                }
            }

            movedPosition += remainingDirection * remainingMagnitude;
            context.TransientPosition = movedPosition;
            return completed;
        }

        public void ProcessDiscreteCollisionEvents()
        {
            if (!context.Config.discreteCollisionEvents)
            {
                return;
            }

            int count = CharacterCollisionsOverlap(context.TransientPosition, context.TransientRotation, context.InternalColliders, MccConfig.COLLISION_OFFSET * 2f);
            for (int i = 0; i < count; i++)
            {
                context.Owner.Controller?.OnDiscreteCollisionDetected(context.InternalColliders[i]);
            }
        }

        public int CharacterCollisionsOverlap(Vector3 position, Quaternion rotation, Collider[] colliders, float inflate = 0f, bool acceptOnlyStableGroundLayer = false)
        {
            int queryLayers = acceptOnlyStableGroundLayer ? context.CollidableLayers & context.Config.stableGroundLayers : context.CollidableLayers;
            Vector3 bottom = context.GetCapsuleBottomHemiAt(position, rotation, inflate);
            Vector3 top = context.GetCapsuleTopHemiAt(position, rotation, inflate);
            int rawCount = Physics.OverlapCapsuleNonAlloc(bottom, top, context.Capsule.radius + inflate, colliders, queryLayers, QueryTriggerInteraction.Ignore);
            return FilterColliders(colliders, rawCount);
        }

        public int CharacterCollisionsSweep(Vector3 position, Quaternion rotation, Vector3 direction, float distance, out RaycastHit closestHit, RaycastHit[] hits, float inflate = 0f, bool acceptOnlyStableGroundLayer = false)
        {
            closestHit = default;
            if (direction.sqrMagnitude <= 0f || distance <= 0f)
            {
                return 0;
            }

            int queryLayers = acceptOnlyStableGroundLayer ? context.CollidableLayers & context.Config.stableGroundLayers : context.CollidableLayers;
            Vector3 normalizedDirection = direction.normalized;
            Vector3 bottom = context.GetCapsuleBottomHemiAt(position, rotation, inflate) - normalizedDirection * MccConfig.SWEEP_BACKSTEP_DISTANCE;
            Vector3 top = context.GetCapsuleTopHemiAt(position, rotation, inflate) - normalizedDirection * MccConfig.SWEEP_BACKSTEP_DISTANCE;
            int rawCount = Physics.CapsuleCastNonAlloc(bottom, top, context.Capsule.radius + inflate, normalizedDirection, hits, distance + MccConfig.SWEEP_BACKSTEP_DISTANCE, queryLayers, QueryTriggerInteraction.Ignore);
            return FilterHits(hits, rawCount, MccConfig.SWEEP_BACKSTEP_DISTANCE, out closestHit);
        }

        public int CharacterCollisionsRaycast(Vector3 position, Vector3 direction, float distance, out RaycastHit closestHit, RaycastHit[] hits, bool acceptOnlyStableGroundLayer = false)
        {
            int queryLayers = acceptOnlyStableGroundLayer ? context.CollidableLayers & context.Config.stableGroundLayers : context.CollidableLayers;
            int rawCount = Physics.RaycastNonAlloc(position, direction, hits, distance, queryLayers, QueryTriggerInteraction.Ignore);
            return FilterHits(hits, rawCount, 0f, out closestHit);
        }

        public Vector3 GetObstructionNormal(Vector3 hitNormal, bool stableOnHit)
        {
            Vector3 obstructionNormal = hitNormal;
            if (context.GroundingStatus.IsStableOnGround && !context.Owner.MustUnground() && !stableOnHit)
            {
                Vector3 obstructionLeftAlongGround = Vector3.Cross(context.GroundingStatus.GroundNormal, obstructionNormal).normalized;
                obstructionNormal = Vector3.Cross(obstructionLeftAlongGround, context.CharacterUp).normalized;
            }

            return obstructionNormal.sqrMagnitude > 0f ? obstructionNormal : hitNormal;
        }

        private void ProjectAgainstKnownOverlaps(ref Vector3 velocity, ref float remainingMagnitude, ref Vector3 remainingDirection, Vector3 originalDirection, ref MovementSweepState sweepState, ref bool previousHitStable, ref Vector3 previousVelocity, ref Vector3 previousObstructionNormal)
        {
            for (int i = 0; i < context.OverlapsCount; i++)
            {
                Vector3 overlapNormal = context.Overlaps[i].Normal;
                if (Vector3.Dot(remainingDirection, overlapNormal) >= 0f)
                {
                    continue;
                }

                bool stable = context.IsStableOnNormal(overlapNormal) && !context.Owner.MustUnground();
                Vector3 obstructionNormal = GetObstructionNormal(overlapNormal, stable);
                Vector3 velocityBeforeProjection = velocity;
                HandleVelocityProjection(stable, overlapNormal, obstructionNormal, originalDirection, ref sweepState, previousHitStable, previousVelocity, previousObstructionNormal, ref velocity, ref remainingMagnitude, ref remainingDirection);
                previousHitStable = stable;
                previousVelocity = velocityBeforeProjection;
                previousObstructionNormal = obstructionNormal;
            }
        }

        private void HandleVelocityProjection(bool stableOnHit, Vector3 hitNormal, Vector3 obstructionNormal, Vector3 originalDirection, ref MovementSweepState sweepState, bool previousHitStable, Vector3 previousVelocity, Vector3 previousObstructionNormal, ref Vector3 velocity, ref float remainingMagnitude, ref Vector3 remainingDirection)
        {
            if (velocity.sqrMagnitude <= 0f)
            {
                return;
            }

            Vector3 velocityBeforeProjection = velocity;
            if (stableOnHit)
            {
                context.LastMovementIterationFoundAnyGround = true;
                ProjectVelocity(ref velocity, obstructionNormal, true);
            }
            else if (sweepState == MovementSweepState.Initial)
            {
                ProjectVelocity(ref velocity, obstructionNormal, false);
                sweepState = MovementSweepState.AfterFirstHit;
            }
            else if (sweepState == MovementSweepState.AfterFirstHit)
            {
                EvaluateCrease(velocity, previousVelocity, obstructionNormal, previousObstructionNormal, stableOnHit, previousHitStable, context.GroundingStatus.IsStableOnGround && !context.Owner.MustUnground(), out bool foundCrease, out Vector3 creaseDirection);
                if (foundCrease)
                {
                    velocity = context.GroundingStatus.IsStableOnGround ? Vector3.zero : Vector3.Project(velocity, creaseDirection);
                    sweepState = context.GroundingStatus.IsStableOnGround ? MovementSweepState.FoundBlockingCorner : MovementSweepState.FoundBlockingCrease;
                }
                else
                {
                    ProjectVelocity(ref velocity, obstructionNormal, false);
                }
            }
            else if (sweepState == MovementSweepState.FoundBlockingCrease)
            {
                velocity = Vector3.zero;
                sweepState = MovementSweepState.FoundBlockingCorner;
            }

            if (context.Config.hasPlanarConstraint)
            {
                velocity = Vector3.ProjectOnPlane(velocity, context.Config.planarConstraintAxis.normalized);
            }

            float velocityFactor = velocityBeforeProjection.magnitude > 0f ? velocity.magnitude / velocityBeforeProjection.magnitude : 0f;
            remainingMagnitude *= velocityFactor;
            remainingDirection = velocity.sqrMagnitude > 0f ? velocity.normalized : Vector3.zero;
        }

        private void ProjectVelocity(ref Vector3 velocity, Vector3 obstructionNormal, bool stableOnHit)
        {
            if (context.GroundingStatus.IsStableOnGround && !context.Owner.MustUnground())
            {
                if (stableOnHit)
                {
                    velocity = context.GetDirectionTangentToSurface(velocity, obstructionNormal) * velocity.magnitude;
                }
                else
                {
                    Vector3 obstructionRightAlongGround = Vector3.Cross(obstructionNormal, context.GroundingStatus.GroundNormal).normalized;
                    Vector3 obstructionUpAlongGround = Vector3.Cross(obstructionRightAlongGround, obstructionNormal).normalized;
                    velocity = context.GetDirectionTangentToSurface(velocity, obstructionUpAlongGround) * velocity.magnitude;
                    velocity = Vector3.ProjectOnPlane(velocity, obstructionNormal);
                }
            }
            else if (stableOnHit)
            {
                velocity = Vector3.ProjectOnPlane(velocity, context.CharacterUp);
                velocity = context.GetDirectionTangentToSurface(velocity, obstructionNormal) * velocity.magnitude;
            }
            else
            {
                velocity = Vector3.ProjectOnPlane(velocity, obstructionNormal);
            }
        }

        private static void EvaluateCrease(Vector3 currentVelocity, Vector3 previousVelocity, Vector3 currentNormal, Vector3 previousNormal, bool currentStable, bool previousStable, bool characterStable, out bool validCrease, out Vector3 creaseDirection)
        {
            validCrease = false;
            creaseDirection = Vector3.zero;
            if (characterStable && currentStable && previousStable)
            {
                return;
            }

            Vector3 tmpCreaseDirection = Vector3.Cross(currentNormal, previousNormal).normalized;
            if (tmpCreaseDirection.sqrMagnitude <= 0f || Vector3.Dot(currentNormal, previousNormal) >= 0.999f)
            {
                return;
            }

            Vector3 normalA = Vector3.ProjectOnPlane(currentNormal, tmpCreaseDirection).normalized;
            Vector3 normalB = Vector3.ProjectOnPlane(previousNormal, tmpCreaseDirection).normalized;
            Vector3 enteringVelocity = Vector3.ProjectOnPlane(previousVelocity, tmpCreaseDirection).normalized;
            float dotPlanes = Vector3.Dot(normalA, normalB);
            if (dotPlanes <= Vector3.Dot(-enteringVelocity, normalA) + 0.001f && dotPlanes <= Vector3.Dot(-enteringVelocity, normalB) + 0.001f)
            {
                validCrease = true;
                creaseDirection = Vector3.Dot(tmpCreaseDirection, currentVelocity) < 0f ? -tmpCreaseDirection : tmpCreaseDirection;
            }
        }

        private int FilterColliders(Collider[] colliders, int count)
        {
            int validCount = count;
            for (int i = count - 1; i >= 0; i--)
            {
                if (!context.IsColliderValidForCollisions(colliders[i]))
                {
                    validCount--;
                    if (i < validCount)
                    {
                        colliders[i] = colliders[validCount];
                    }
                }
            }
            return validCount;
        }

        private int FilterHits(RaycastHit[] hits, int count, float backstep, out RaycastHit closestHit)
        {
            closestHit = default;
            float closestDistance = Mathf.Infinity;
            int validCount = count;
            for (int i = count - 1; i >= 0; i--)
            {
                hits[i].distance -= backstep;
                if (hits[i].distance <= 0f || !context.IsColliderValidForCollisions(hits[i].collider))
                {
                    validCount--;
                    if (i < validCount)
                    {
                        hits[i] = hits[validCount];
                    }
                }
                else if (hits[i].distance < closestDistance)
                {
                    closestDistance = hits[i].distance;
                    closestHit = hits[i];
                }
            }
            return validCount;
        }

        private void RememberOverlap(Vector3 normal, Collider collider)
        {
            if (context.OverlapsCount >= context.Overlaps.Length)
            {
                return;
            }

            context.Overlaps[context.OverlapsCount] = new OverlapResult(normal, collider);
            context.OverlapsCount++;
        }
    }
}
