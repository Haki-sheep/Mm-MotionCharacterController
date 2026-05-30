using UnityEngine;

namespace MotionCharacterController
{
    /// <summary>
    /// 刚体碰撞投影求解器
    /// </summary>
    public class RigidbodySolver
    {
        private readonly MccMotorContext context;
        private readonly RigidbodyProjectionHit[] hits = new RigidbodyProjectionHit[MccConfig.MAX_RIGIDBODY_HITS];
        private int hitCount;

        public RigidbodySolver(MccMotorContext context)
        {
            this.context = context;
        }

        public void Clear()
        {
            hitCount = 0;
        }

        public void StoreHit(Rigidbody body, Vector3 velocity, Vector3 point, Vector3 normal)
        {
            if (body == null || hitCount >= hits.Length)
            {
                return;
            }

            hits[hitCount] = new RigidbodyProjectionHit
            {
                Rigidbody = body,
                HitVelocity = velocity,
                HitPoint = point,
                EffectiveHitNormal = normal,
            };
            hitCount++;
        }

        public void ProcessVelocityForHits(ref Vector3 velocity, float deltaTime)
        {
            if (!context.Config.interactiveRigidbodyHandling)
            {
                return;
            }

            for (int i = 0; i < hitCount; i++)
            {
                RigidbodyProjectionHit hit = hits[i];
                if (hit.Rigidbody == null || hit.Rigidbody == context.AttachedRigidbody)
                {
                    continue;
                }

                bool dynamicBody = !hit.Rigidbody.isKinematic;
                if (!dynamicBody)
                {
                    continue;
                }

                Vector3 bodyVelocity = hit.Rigidbody.linearVelocity;
                ComputeCollisionResolution(
                    hit.EffectiveHitNormal,
                    hit.HitVelocity,
                    bodyVelocity,
                    context.Config.rigidbodyInteractionType == RigidbodyInteractionType.SimulatedDynamic ? 0.5f : 1f,
                    out Vector3 characterVelocityChange,
                    out Vector3 bodyVelocityChange);

                velocity += characterVelocityChange;
                hit.Rigidbody.AddForceAtPosition(bodyVelocityChange, hit.HitPoint, ForceMode.VelocityChange);
            }
        }

        private static void ComputeCollisionResolution(Vector3 hitNormal, Vector3 characterVelocity, Vector3 bodyVelocity, float characterToBodyMassRatio, out Vector3 velocityChangeOnCharacter, out Vector3 velocityChangeOnBody)
        {
            velocityChangeOnCharacter = Vector3.zero;
            velocityChangeOnBody = Vector3.zero;
            float bodyToCharacterMassRatio = 1f - characterToBodyMassRatio;
            float characterVelocityOnNormal = Vector3.Dot(characterVelocity, hitNormal);
            float bodyVelocityOnNormal = Vector3.Dot(bodyVelocity, hitNormal);

            if (characterVelocityOnNormal < 0f)
            {
                velocityChangeOnCharacter += hitNormal * characterVelocityOnNormal;
            }

            if (bodyVelocityOnNormal > characterVelocityOnNormal)
            {
                Vector3 relativeImpactVelocity = hitNormal * (bodyVelocityOnNormal - characterVelocityOnNormal);
                velocityChangeOnCharacter += relativeImpactVelocity * bodyToCharacterMassRatio;
                velocityChangeOnBody += -relativeImpactVelocity * characterToBodyMassRatio;
            }
        }
    }
}
