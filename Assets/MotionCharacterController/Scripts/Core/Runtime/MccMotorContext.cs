using UnityEngine;

namespace MotionCharacterController
{
    public class MccMotorContext
    {
        public MotionCC Owner;
        public MccConfig Config;
        public Transform Transform;
        public CapsuleCollider Capsule;

        public Vector3 TransientPosition;
        private Quaternion transientRotation = Quaternion.identity;
        public Quaternion TransientRotation
        {
            get => transientRotation;
            set
            {
                transientRotation = value.normalized;
                RefreshCharacterAxes();
            }
        }

        public Vector3 CharacterUp = Vector3.up;
        public Vector3 CharacterForward = Vector3.forward;
        public Vector3 CharacterRight = Vector3.right;
        public Vector3 InitialSimulationPosition;
        public Quaternion InitialSimulationRotation = Quaternion.identity;
        public Vector3 InitialTickPosition;
        public Quaternion InitialTickRotation = Quaternion.identity;

        public Vector3 CapsuleCenter;
        public Vector3 TransformToCapsuleCenter;
        public Vector3 TransformToCapsuleBottom;
        public Vector3 TransformToCapsuleTop;
        public Vector3 TransformToCapsuleBottomHemi;
        public Vector3 TransformToCapsuleTopHemi;

        public CharacterGroundingReport GroundingStatus = new CharacterGroundingReport();
        public CharacterTransientGroundingReport LastGroundingStatus = new CharacterTransientGroundingReport();
        public LayerMask CollidableLayers = -1;
        public Vector3 BaseVelocity;
        public Rigidbody AttachedRigidbody;
        public Rigidbody LastAttachedRigidbody;
        public Rigidbody AttachedRigidbodyOverride;
        public Vector3 AttachedRigidbodyVelocity;
        public bool LastMovementIterationFoundAnyGround;
        public bool SolveMovementCollisions = true;
        public bool SolveGrounding = true;
        public bool MovePositionDirty;
        public Vector3 MovePositionTarget;
        public bool MoveRotationDirty;
        public Quaternion MoveRotationTarget = Quaternion.identity;
        public bool IsMovingFromAttachedRigidbody;
        public bool MustUnground;
        public float MustUngroundTimeCounter;
        public int OverlapsCount;
        public readonly OverlapResult[] Overlaps = new OverlapResult[MccConfig.MAX_COLLISION_OVERLAPS];
        public readonly RaycastHit[] InternalHits = new RaycastHit[MccConfig.MAX_COLLISION_HITS];
        public readonly Collider[] InternalColliders = new Collider[MccConfig.MAX_COLLISION_OVERLAPS];

        public void BeginSimulation()
        {
            TransientPosition = Transform.position;
            TransientRotation = Transform.rotation;
            InitialSimulationPosition = TransientPosition;
            InitialSimulationRotation = TransientRotation;
            OverlapsCount = 0;
            LastGroundingStatus.CopyFrom(GroundingStatus);
            GroundingStatus = new CharacterGroundingReport { GroundNormal = CharacterUp, InnerGroundNormal = CharacterUp, OuterGroundNormal = CharacterUp };
        }

        public void RefreshCapsuleData()
        {
            if (Capsule == null)
            {
                return;
            }

            CapsuleCenter = Capsule.center;
            TransformToCapsuleCenter = Capsule.center;
            TransformToCapsuleBottom = Capsule.center + Vector3.down * (Capsule.height * 0.5f);
            TransformToCapsuleTop = Capsule.center + Vector3.up * (Capsule.height * 0.5f);
            TransformToCapsuleBottomHemi = Capsule.center + Vector3.down * (Capsule.height * 0.5f - Capsule.radius);
            TransformToCapsuleTopHemi = Capsule.center + Vector3.up * (Capsule.height * 0.5f - Capsule.radius);
        }

        public void RefreshCharacterAxes()
        {
            CharacterUp = transientRotation * Vector3.up;
            CharacterForward = transientRotation * Vector3.forward;
            CharacterRight = transientRotation * Vector3.right;
        }

        public void RefreshCollidableLayers()
        {
            CollidableLayers = 0;
            int layer = Transform != null ? Transform.gameObject.layer : 0;
            for (int i = 0; i < 32; i++)
            {
                if (!Physics.GetIgnoreLayerCollision(layer, i))
                {
                    CollidableLayers |= 1 << i;
                }
            }
        }

        public void SanitizeVelocity()
        {
            if (float.IsNaN(BaseVelocity.x) || float.IsNaN(BaseVelocity.y) || float.IsNaN(BaseVelocity.z))
            {
                BaseVelocity = Vector3.zero;
            }

            if (float.IsNaN(AttachedRigidbodyVelocity.x) || float.IsNaN(AttachedRigidbodyVelocity.y) || float.IsNaN(AttachedRigidbodyVelocity.z))
            {
                AttachedRigidbodyVelocity = Vector3.zero;
            }
        }

        public bool IsStableOnNormal(Vector3 normal)
        {
            return Vector3.Angle(CharacterUp, normal) <= Config.maxStableSlopeAngle;
        }

        public bool IsColliderValidForCollisions(Collider coll)
        {
            if (coll == null || coll == Capsule)
            {
                return false;
            }

            Rigidbody attachedRigidbody = coll.attachedRigidbody;
            if (attachedRigidbody != null)
            {
                if (IsMovingFromAttachedRigidbody && attachedRigidbody == AttachedRigidbody)
                {
                    return false;
                }

                if (Config.rigidbodyInteractionType == RigidbodyInteractionType.Kinematic && !attachedRigidbody.isKinematic)
                {
                    attachedRigidbody.WakeUp();
                    return false;
                }
            }

            return Owner.Controller == null || Owner.Controller.IsColliderValidForCollisions(coll);
        }

        public Vector3 GetDirectionTangentToSurface(Vector3 direction, Vector3 surfaceNormal)
        {
            if (direction.sqrMagnitude <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 directionRight = Vector3.Cross(direction, CharacterUp);
            Vector3 tangent = Vector3.Cross(surfaceNormal, directionRight);
            return tangent.sqrMagnitude > 0f ? tangent.normalized : Vector3.ProjectOnPlane(direction, surfaceNormal).normalized;
        }

        public Vector3 GetCapsuleBottomHemiAt(Vector3 position, Quaternion rotation, float inflate = 0f)
        {
            return position + rotation * TransformToCapsuleBottomHemi + rotation * Vector3.down * inflate;
        }

        public Vector3 GetCapsuleTopHemiAt(Vector3 position, Quaternion rotation, float inflate = 0f)
        {
            return position + rotation * TransformToCapsuleTopHemi + rotation * Vector3.up * inflate;
        }
    }
}
