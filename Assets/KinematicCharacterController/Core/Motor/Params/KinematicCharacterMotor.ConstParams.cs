namespace KinematicCharacterController
{
    using Sirenix.OdinInspector;
    using UnityEngine;
    using System.Collections.Generic;
    /// <summary>
    /// 常量 不建议随便动的微调参数
    /// </summary>
    public partial class KinematicCharacterMotor
    {
        // 警告：除非你完全清楚自己在做什么，否则请勿修改这些常量！
        // 最大射线检测结果缓存数量
        public const int MaxHitsBudget = 16;

        // 最大碰撞体检测缓存数量
        public const int MaxCollisionBudget = 16;

        // 地面检测扫描最大迭代次数
        public const int MaxGroundingSweepIterations = 2;

        // 台阶攀爬扫描最大迭代次数
        public const int MaxSteppingSweepIterations = 3;

        // 最大刚体重叠检测数量
        public const int MaxRigidbodyOverlapsCount = 16;

        // 碰撞偏移量（防止角色与碰撞体紧贴穿透）
        public const float CollisionOffset = 0.01f;

        // 地面探测回弹距离（优化接地稳定性）
        public const float GroundProbeReboundDistance = 0.02f;

        // 地面探测最小距离
        public const float MinimumGroundProbingDistance = 0.005f;

        // 地面探测回退距离（避免起始点重叠）
        public const float GroundProbingBackstepDistance = 0.1f;

        // 扫描探测回退距离
        public const float SweepProbingBackstepDistance = 0.002f;

        // 次级探测垂直偏移量
        public const float SecondaryProbesVertical = 0.02f;

        // 次级探测水平偏移量
        public const float SecondaryProbesHorizontal = 0.001f;

        // 最小有效速度阈值（低于此值视为静止）
        public const float MinVelocityMagnitude = 0.01f;

        // 台阶攀爬前向检测距离
        public const float SteppingForwardDistance = 0.03f;

        // 边缘判定最小距离
        public const float MinDistanceForLedge = 0.05f;

        // 垂直障碍物关联判定阈值
        public const float CorrelationForVerticalObstruction = 0.01f;

        // 额外台阶攀爬前向距离
        public const float ExtraSteppingForwardDistance = 0.01f;
        
        // 额外台阶高度补偿（防止攀爬失败）
        public const float ExtraStepHeightPadding = 0.01f;
    }
}