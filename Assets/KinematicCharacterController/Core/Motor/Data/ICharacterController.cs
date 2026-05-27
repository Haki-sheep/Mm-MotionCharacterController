using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KinematicCharacterController
{
    public interface ICharacterController
    {
        /// <summary>
        /// 当电机想要知道当前时刻角色旋转应该是什么时调用
        /// </summary>
        void UpdateRotation(ref Quaternion currentRotation, float deltaTime);

        /// <summary>
        /// 当电机想要知道当前时刻角色速度应该是什么时调用
        /// </summary>
        void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime);

        /// <summary>
        /// 在电机做任何事情之前调用
        /// </summary>
        void BeforeCharacterUpdate(float deltaTime);

        /// <summary>
        /// 在电机完成地面探测之后，但在处理 PhysicsMover/Velocity 等之前调用
        ///  通常用于:
        ///  根据是否接地切换状态
        ///  落地后重置跳跃次数
        ///  播放落地动画/脚步逻辑
        ///  根据地面法线调整角色输入或移动参数
        /// </summary>
        void PostGroundingUpdate(float deltaTime);

        /// <summary>
        /// 在电机完成其更新中的所有操作之后调用
        /// </summary>
        void AfterCharacterUpdate(float deltaTime);

        /// <summary>
        /// 当电机想要知道该碰撞体是否可以发生碰撞时调用（或者我们是否直接穿过它）
        /// </summary>
        bool IsColliderValidForCollisions(Collider coll);

        /// <summary>
        /// 当电机的地面探测检测到地面碰撞时调用    
        /// </summary>
        void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
                                    ref HitStabilityReport hitStabilityReport);
        /// <summary>
        /// 当电机的移动逻辑检测到碰撞时调用
        /// </summary>
        void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
                                    ref HitStabilityReport hitStabilityReport);

        /// <summary>
        /// 每次移动碰撞后调用，让你有机会按自己的需求修改 HitStabilityReport
        /// </summary>
        void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal,
                                     Vector3 hitPoint, Vector3 atCharacterPosition,
                                     Quaternion atCharacterRotation,
                                     ref HitStabilityReport hitStabilityReport);

        /// <summary>
        /// 当角色检测到离散碰撞时调用（不是由电机移动时的胶囊体投射产生的碰撞）
        /// </summary>
        void OnDiscreteCollisionDetected(Collider hitCollider);
    }
}