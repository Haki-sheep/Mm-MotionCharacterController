using UnityEngine;

namespace MotionCharacterController
{
    // 对外主控制接口：给上层逻辑实现
    public interface IMccController
    {
        #region 渲染帧
        // 输入更新
        void InputVectorUpdate(ref Vector3 inputDirection, ref bool jumpRequested);

        #endregion

        #region 物理帧
        // 角色更新前的准备阶段
        void BeforeCharacterUpdate(float deltaTime);

        // 更新角色旋转
        void UpdateRotation(ref Quaternion currentRotation, float deltaTime);

        // 更新角色速度
        void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime);

        // 接地检测完成后调用，适合重置跳跃次数、切换落地状态
        void PostGroundingUpdate(float deltaTime);

        // 角色更新后的收尾阶段
        void AfterCharacterUpdate(float deltaTime);

        #endregion

        #region 开发者控制
        // 判断某个碰撞体是否参与角色碰撞
        bool IsColliderValidForCollisions(Collider coll);

        // 地面探测命中时调用
        void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport);

        // 移动扫掠命中时调用
        void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport);

        // 给玩法层最后一次修改稳定性报告的机会
        void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport);

        // 离散碰撞事件
        void OnDiscreteCollisionDetected(Collider hitCollider);
        #endregion
    }
}
