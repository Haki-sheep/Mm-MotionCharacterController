using UnityEngine;

namespace MotionCharacterController
{
    public partial class MotionCC
    {
        public void ConsumeJumpRequest()
        {
            jumpRequested = false;
        }

        public MotionCharacterMotorState GetState()
        {
            return new MotionCharacterMotorState
            {
                Position = context.TransientPosition,
                Rotation = context.TransientRotation,
                BaseVelocity = context.BaseVelocity,
                MustUnground = context.MustUnground,
                MustUngroundTime = context.MustUngroundTimeCounter,
                LastMovementIterationFoundAnyGround = context.LastMovementIterationFoundAnyGround,
                GroundingStatus = context.GroundingStatus.ToTransient(),
                AttachedRigidbody = context.AttachedRigidbody,
                AttachedRigidbodyVelocity = context.AttachedRigidbodyVelocity,
            };
        }

        public void ApplyState(MotionCharacterMotorState state, bool bypassInterpolation = true)
        {
            SetPositionAndRotation(state.Position, state.Rotation, bypassInterpolation);
            context.BaseVelocity = state.BaseVelocity;
            context.MustUnground = state.MustUnground;
            context.MustUngroundTimeCounter = state.MustUngroundTime;
            context.LastMovementIterationFoundAnyGround = state.LastMovementIterationFoundAnyGround;
            context.GroundingStatus.CopyFrom(state.GroundingStatus);
            context.AttachedRigidbody = state.AttachedRigidbody;
            context.AttachedRigidbodyVelocity = state.AttachedRigidbodyVelocity;
        }

        public void ForceUnground(float time = 0.1f)
        {
            context.MustUnground = true;
            context.MustUngroundTimeCounter = time;
        }

        public bool MustUnground()
        {
            return context.MustUnground || context.MustUngroundTimeCounter > 0f;
        }

        public void MoveCharacter(Vector3 toPosition)
        {
            context.MovePositionDirty = true;
            context.MovePositionTarget = toPosition;
        }

        public void RotateCharacter(Quaternion toRotation)
        {
            context.MoveRotationDirty = true;
            context.MoveRotationTarget = toRotation;
        }

        public void SetPosition(Vector3 position, bool bypassInterpolation = true)
        {
            context.Transform.position = position;
            context.InitialSimulationPosition = position;
            context.TransientPosition = position;
            if (bypassInterpolation)
            {
                context.InitialTickPosition = position;
            }
        }

        public void SetRotation(Quaternion rotation, bool bypassInterpolation = true)
        {
            context.Transform.rotation = rotation;
            context.InitialSimulationRotation = rotation;
            context.TransientRotation = rotation.normalized;
            if (bypassInterpolation)
            {
                context.InitialTickRotation = rotation.normalized;
            }
        }

        public void SetPositionAndRotation(Vector3 position, Quaternion rotation, bool bypassInterpolation = true)
        {
            context.Transform.SetPositionAndRotation(position, rotation);
            context.InitialSimulationPosition = position;
            context.InitialSimulationRotation = rotation.normalized;
            context.TransientPosition = position;
            context.TransientRotation = rotation.normalized;
            if (bypassInterpolation)
            {
                context.InitialTickPosition = position;
                context.InitialTickRotation = rotation.normalized;
            }
        }

        public void SetCapsuleDimensions(float radius, float height, float yOffset)
        {
            if (context.Capsule == null)
            {
                return;
            }

            height = Mathf.Max(height, radius * 2f + MccConfig.COLLISION_OFFSET);
            config.capsuleRadius = Mathf.Clamp(radius, 0f, height * 0.5f);
            config.capsuleHeight = height;
            config.capsuleYOffset = yOffset;

            context.Capsule.radius = config.capsuleRadius;
            context.Capsule.height = config.capsuleHeight;
            context.Capsule.center = new Vector3(0f, config.capsuleYOffset, 0f);
            context.RefreshCapsuleData();
        }

        public void SetCapsuleCollisionsActivation(bool collisionsActive)
        {
            if (context.Capsule != null)
            {
                context.Capsule.isTrigger = !collisionsActive;
            }
        }

        public void SetMovementCollisionsSolvingActivation(bool active)
        {
            context.SolveMovementCollisions = active;
        }

        public void SetGroundSolvingActivation(bool active)
        {
            context.SolveGrounding = active;
        }

        public Vector3 GetVelocityFromMovement(Vector3 movement, float deltaTime)
        {
            return deltaTime <= 0f ? Vector3.zero : movement / deltaTime;
        }

        public Vector3 GetDirectionTangentToSurface(Vector3 direction, Vector3 surfaceNormal)
        {
            return context.GetDirectionTangentToSurface(direction, surfaceNormal);
        }

        public int CharacterCollisionsOverlap(Vector3 position, Quaternion rotation, Collider[] overlappedColliders, float inflate = 0f, bool acceptOnlyStableGroundLayer = false)
        {
            return collisionSolver.CharacterCollisionsOverlap(position, rotation, overlappedColliders, inflate, acceptOnlyStableGroundLayer);
        }

        public int CharacterCollisionsSweep(Vector3 position, Quaternion rotation, Vector3 direction, float distance, out RaycastHit closestHit, RaycastHit[] hits, float inflate = 0f, bool acceptOnlyStableGroundLayer = false)
        {
            return collisionSolver.CharacterCollisionsSweep(position, rotation, direction, distance, out closestHit, hits, inflate, acceptOnlyStableGroundLayer);
        }

        public int CharacterCollisionsRaycast(Vector3 position, Vector3 direction, float distance, out RaycastHit closestHit, RaycastHit[] hits, bool acceptOnlyStableGroundLayer = false)
        {
            return collisionSolver.CharacterCollisionsRaycast(position, direction, distance, out closestHit, hits, acceptOnlyStableGroundLayer);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGroundGizmos)
            {
                return;
            }

            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                return;
            }

            Vector3 up = transform.up;
            Vector3 bottomHemi = transform.position + transform.rotation * (capsule.center + Vector3.down * (capsule.height * 0.5f - capsule.radius));
            Vector3 probeOrigin = bottomHemi + up * MccConfig.GROUND_START_OFFSET;
            float probeRadius = Mathf.Max(MccConfig.SKIN_WIDTH, capsule.radius - MccConfig.SKIN_WIDTH);
            float probeDistance = config.groundProbeDistance + MccConfig.GROUND_START_OFFSET;
            Vector3 probeEnd = probeOrigin - up * probeDistance;

            Gizmos.color = Color.white;
            Gizmos.DrawSphere(probeOrigin, 0.025f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(probeOrigin, probeRadius);
            Gizmos.DrawLine(probeOrigin, probeEnd);

            bool grounded = Physics.SphereCast(probeOrigin, probeRadius, -up, out RaycastHit hit, probeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                            && Vector3.Angle(up, hit.normal) <= config.maxStableSlopeAngle;
            Gizmos.color = grounded ? Color.green : Color.red;
            Gizmos.DrawSphere(grounded ? hit.point : probeEnd, 0.04f);
        }
    }
}
