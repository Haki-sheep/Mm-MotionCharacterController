
/*
    初始化与设置
*/
using UnityEngine;

namespace KinematicCharacterController
{
    public partial class KinematicCharacterMotor
    {

        /// <summary>
        /// 校验胶囊体和配置数据
        /// Handle validating all required values
        /// </summary>
        public void ValidateData()
        {
            // Get or refresh the capsule collider reference used by the motor
            // 获取或刷新 Motor 使用的胶囊体组件引用
            Capsule = GetComponent<CapsuleCollider>();

            // Clamp radius so it can never be larger than half the capsule height
            // 限制半径，避免半径超过胶囊体高度的一半，导致形状非法
            CapsuleRadius = Mathf.Clamp(CapsuleRadius, 0f, CapsuleHeight * 0.5f);

            // Force the capsule to use the Y axis as its main direction
            // 强制胶囊体沿 Y 轴方向拉伸
            Capsule.direction = 1;

            // Apply the selected physics material to the capsule collider
            // 给胶囊体应用当前设置的物理材质
            Capsule.sharedMaterial = CapsulePhysicsMaterial;

            // Apply the current capsule size settings to the real collider
            // 把当前胶囊体尺寸设置真正同步到碰撞体上
            SetCapsuleDimensions(CapsuleRadius, CapsuleHeight, CapsuleYOffset);

            // 限制台阶高度和深度
            MaxStepHeight = Mathf.Clamp(MaxStepHeight, 0f, Mathf.Infinity);
            MinRequiredStepDepth = Mathf.Clamp(MinRequiredStepDepth, 0f, CapsuleRadius);

            // 限制边缘距离
            MaxStableDistanceFromLedge = Mathf.Clamp(MaxStableDistanceFromLedge, 0f, CapsuleRadius);

            // 限制缩放
            transform.localScale = Vector3.one;

#if UNITY_EDITOR
            Capsule.hideFlags = HideFlags.NotEditable;
            if (!Mathf.Approximately(transform.lossyScale.x, 1f)
                    || !Mathf.Approximately(transform.lossyScale.y, 1f)
                    || !Mathf.Approximately(transform.lossyScale.z, 1f))
            {
                Debug.LogError("Character's lossy scale is not (1,1,1). This is not allowed. Make sure the character's transform and all of its parents have a (1,1,1) scale.", this.gameObject);
            }
#endif
        }

        /// <summary>
        /// 设置胶囊体尺寸
        /// Resizes capsule. Also caches important capsule size data
        /// </summary>
        /// <param name="radius">胶囊体半径</param>
        /// <param name="height">胶囊体高度</param>
        /// <param name="yOffset">胶囊体Y偏移</param>
        public void SetCapsuleDimensions(float radius, float height, float yOffset)
        {
            // Prevent invalid capsule geometry by ensuring height is always a bit larger than diameter
            // 防止胶囊体几何非法：高度至少要比直径大一点点
            height = Mathf.Max(height, (radius * 2f) + 0.01f);

            // Cache the requested capsule dimensions on the motor
            // 把这次设置的胶囊体尺寸缓存到 Motor 自己的字段里
            CapsuleRadius = radius;
            CapsuleHeight = height;
            CapsuleYOffset = yOffset;

            // Apply dimensions to the real Unity capsule collider
            // 把尺寸真正应用到 Unity 的胶囊碰撞体组件上
            Capsule.radius = CapsuleRadius;
            Capsule.height = Mathf.Clamp(CapsuleHeight, CapsuleRadius * 2f, CapsuleHeight);
            Capsule.center = new Vector3(0f, CapsuleYOffset, 0f);

            // Cache important local-space points used later by collision queries and movement solving
            // 缓存后续碰撞检测和移动解算要反复使用的本地空间关键点
            _characterTransformToCapsuleCenter = Capsule.center;
            _characterTransformToCapsuleBottom = Capsule.center + (-_cachedWorldUp * (Capsule.height * 0.5f));
            _characterTransformToCapsuleTop = Capsule.center + (_cachedWorldUp * (Capsule.height * 0.5f));
            _characterTransformToCapsuleBottomHemi = Capsule.center + (-_cachedWorldUp * (Capsule.height * 0.5f)) + (_cachedWorldUp * Capsule.radius);
            _characterTransformToCapsuleTopHemi = Capsule.center + (_cachedWorldUp * (Capsule.height * 0.5f)) + (-_cachedWorldUp * Capsule.radius);
        }
    }
}
