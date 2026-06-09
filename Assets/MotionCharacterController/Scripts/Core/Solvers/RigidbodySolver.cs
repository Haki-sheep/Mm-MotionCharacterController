using UnityEngine;

namespace MotionCharacterController
{
    /// <summary>
    /// 刚体碰撞投影求解器
    /// </summary>
    public class RigidbodySolver
    {
        private readonly MccMotorContext context;
        // 本帧刚体碰撞记录数组
        private readonly RigidbodyProjectionHit[] hitArray = new RigidbodyProjectionHit[MccConfig.MAX_RIGIDBODY_HITS];
        // 本帧已记录数量
        private int hitCount;

        public RigidbodySolver(MccMotorContext context)
        {
            this.context = context;
        }

        /// <summary>
        /// 清空本帧刚体碰撞记录
        /// </summary>
        public void Clear()
        {
            hitCount = 0;
        }

        /// <summary>
        /// 存储刚体碰撞信息
        /// </summary>
        /// <param name="body">刚体</param>
        /// <param name="velocity">速度</param>
        /// <param name="point">碰撞点</param>
        /// <param name="normal">碰撞法线</param>
        public void StoreHit(Rigidbody body, Vector3 velocity, Vector3 point, Vector3 normal)
        {
            // 如果刚体为空或数组已满 则跳过
            if (body is null || hitCount >= hitArray.Length)
                return;

            hitArray[hitCount] = new RigidbodyProjectionHit
            {
                Rigidbody = body,
                HitVelocity = velocity,
                HitPoint = point,
                EffectiveHitNormal = normal,
            };
            hitCount++;
        }

        /// <summary>
        /// 根据本帧记录的碰撞 处理角色与刚体的速度交换
        /// </summary>
        /// <param name="velocity">角色速度</param>
        /// <param name="deltaTime">时间差</param>
        public void ProcessVelocityForHits(ref Vector3 velocity, float deltaTime)
        {
            // 如果未开启刚体交互 则直接返回
            if (!context.Config.interactiveRigidbodyHandling)
            {
                return;
            }

            for (int i = 0; i < hitCount; i++)
            {
                RigidbodyProjectionHit hit = hitArray[i];
                // 跳过空引用或当前站立的平台刚体
                if (hit.Rigidbody == null || hit.Rigidbody == context.AttachedRigidbody)
                {
                    continue;
                }

                // 仅处理动态刚体
                bool dynamicBody = !hit.Rigidbody.isKinematic;
                if (!dynamicBody)
                {
                    continue;
                }

                Vector3 bodyVelocity = hit.Rigidbody.linearVelocity;
                // 计算角色与刚体在法线方向上的速度分配
                ComputeCollisionResolution(
                    hit.EffectiveHitNormal,
                    hit.HitVelocity,
                    bodyVelocity,
                    context.Config.rigidbodyInteractionType == RigidbodyInteractionType.SimulatedDynamic ? 0.5f : 1f,
                    out Vector3 characterVelocityChange,
                    out Vector3 bodyVelocityChange);

                // 更新角色速度并对刚体施加冲量
                velocity += characterVelocityChange;
                hit.Rigidbody.AddForceAtPosition(bodyVelocityChange, hit.HitPoint, ForceMode.VelocityChange);
            }
        }

        /// <summary>
        /// 计算角色与刚体在碰撞法线上的速度变化
        /// </summary>
        /// <param name="hitNormal">碰撞法线</param>
        /// <param name="characterVelocity">角色速度</param>
        /// <param name="bodyVelocity">刚体速度</param>
        /// <param name="characterToBodyMassRatio">角色质量占比</param>
        /// <param name="velocityChangeOnCharacter">角色速度变化</param>
        /// <param name="velocityChangeOnBody">刚体速度变化</param>
        private static void ComputeCollisionResolution(Vector3 hitNormal, Vector3 characterVelocity, Vector3 bodyVelocity, float characterToBodyMassRatio, out Vector3 velocityChangeOnCharacter, out Vector3 velocityChangeOnBody)
        {
            velocityChangeOnCharacter = Vector3.zero;
            velocityChangeOnBody = Vector3.zero;
            float bodyToCharacterMassRatio = 1f - characterToBodyMassRatio;
            // 分解角色与刚体在法线方向上的速度分量
            float characterVelocityOnNormal = Vector3.Dot(characterVelocity, hitNormal);
            float bodyVelocityOnNormal = Vector3.Dot(bodyVelocity, hitNormal);

            // 消去角色沿法线朝内的速度
            if (characterVelocityOnNormal < 0f)
            {
                velocityChangeOnCharacter += hitNormal * characterVelocityOnNormal;
            }

            // 按质量比分配相对碰撞速度
            if (bodyVelocityOnNormal > characterVelocityOnNormal)
            {
                Vector3 relativeImpactVelocity = hitNormal * (bodyVelocityOnNormal - characterVelocityOnNormal);
                velocityChangeOnCharacter += relativeImpactVelocity * bodyToCharacterMassRatio;
                velocityChangeOnBody += -relativeImpactVelocity * characterToBodyMassRatio;
            }
        }
    }
}
