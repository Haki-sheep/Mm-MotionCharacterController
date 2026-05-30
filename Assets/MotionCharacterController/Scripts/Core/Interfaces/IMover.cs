using UnityEngine;

namespace MotionCharacterController
{
    public interface IMover
    {
        void UpdateMovement(out Vector3 goalPosition, out Quaternion goalRotation, float deltaTime);
    }
}
