using UnityEngine;

namespace MotionCharacterController
{
    public interface IMccMoverController
    {
        void UpdateMovement(out Vector3 goalPosition, out Quaternion goalRotation, float deltaTime);
    }
}
