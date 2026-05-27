using UnityEngine;

namespace MotionCharacterController
{
    public class PlatformSolver
    {
        private readonly MccMotorContext context;
        private readonly CollisionSolver collisionSolver;

        public PlatformSolver(MccMotorContext context, CollisionSolver collisionSolver)
        {
            this.context = context;
            this.collisionSolver = collisionSolver;
        }

        public void UpdateAttachment(float deltaTime)
        {
            if (!context.Config.interactiveRigidbodyHandling)
            {
                context.AttachedRigidbody = null;
                context.AttachedRigidbodyVelocity = Vector3.zero;
                return;
            }

            context.LastAttachedRigidbody = context.AttachedRigidbody;
            context.AttachedRigidbody = context.AttachedRigidbodyOverride;

            if (context.AttachedRigidbody == null && context.GroundingStatus.IsStableOnGround && context.GroundingStatus.GroundCollider != null)
            {
                context.AttachedRigidbody = GetInteractiveRigidbody(context.GroundingStatus.GroundCollider);
            }

            Vector3 currentVelocity = Vector3.zero;
            Vector3 currentAngularVelocity = Vector3.zero;
            if (context.AttachedRigidbody != null)
            {
                GetVelocityFromRigidbody(context.AttachedRigidbody, context.TransientPosition, deltaTime, out currentVelocity, out currentAngularVelocity);
            }

            if (context.Config.preserveAttachedRigidbodyMomentum && context.LastAttachedRigidbody != null && context.AttachedRigidbody != context.LastAttachedRigidbody)
            {
                context.BaseVelocity += context.AttachedRigidbodyVelocity;
                context.BaseVelocity -= currentVelocity;
            }

            context.AttachedRigidbodyVelocity = currentVelocity;
            if (context.AttachedRigidbodyVelocity.sqrMagnitude > 0f)
            {
                context.IsMovingFromAttachedRigidbody = true;
                Vector3 velocity = context.AttachedRigidbodyVelocity;
                if (context.SolveMovementCollisions)
                {
                    collisionSolver.Move(ref velocity, deltaTime);
                }
                else
                {
                    context.TransientPosition += velocity * deltaTime;
                }
                context.IsMovingFromAttachedRigidbody = false;
            }

            if (currentAngularVelocity.sqrMagnitude > 0f)
            {
                Vector3 newForward = Vector3.ProjectOnPlane(Quaternion.Euler(Mathf.Rad2Deg * currentAngularVelocity * deltaTime) * context.CharacterForward, context.CharacterUp).normalized;
                if (newForward.sqrMagnitude > 0f)
                {
                    context.TransientRotation = Quaternion.LookRotation(newForward, context.CharacterUp);
                }
            }
        }

        private static Rigidbody GetInteractiveRigidbody(Collider collider)
        {
            Rigidbody body = collider.attachedRigidbody;
            if (body == null)
            {
                return null;
            }

            return body.GetComponent<MccPhysicsMover>() != null || !body.isKinematic ? body : null;
        }

        private static void GetVelocityFromRigidbody(Rigidbody body, Vector3 point, float deltaTime, out Vector3 linearVelocity, out Vector3 angularVelocity)
        {
            linearVelocity = body.linearVelocity;
            angularVelocity = body.angularVelocity;

            MccPhysicsMover mover = body.GetComponent<MccPhysicsMover>();
            if (mover != null)
            {
                linearVelocity = mover.Velocity;
                angularVelocity = mover.AngularVelocity;
            }

            if (deltaTime <= 0f || angularVelocity.sqrMagnitude <= 0f)
            {
                return;
            }

            Vector3 center = body.transform.TransformPoint(body.centerOfMass);
            Vector3 offset = point - center;
            Quaternion rotation = Quaternion.Euler(Mathf.Rad2Deg * angularVelocity * deltaTime);
            Vector3 finalPoint = center + rotation * offset;
            linearVelocity += (finalPoint - point) / deltaTime;
        }
    }
}
