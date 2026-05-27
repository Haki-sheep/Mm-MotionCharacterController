using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KinematicCharacterController
{
    public interface IMoverController
    {
        /// <summary>
        /// This is called to let you tell the PhysicsMover where it should be right now
        /// 这是为了让您告知“物理移动器”它当前应处于的位置
        /// </summary>
        void UpdateMovement(out Vector3 goalPosition, out Quaternion goalRotation, float deltaTime);
    }
}