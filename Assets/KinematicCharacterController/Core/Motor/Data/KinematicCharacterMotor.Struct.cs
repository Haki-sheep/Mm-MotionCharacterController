
using System;
using System.ComponentModel;
using Sirenix.OdinInspector;
using UnityEngine;

namespace KinematicCharacterController
{

        /*
        我将Kcc的数据结构信息全部整理到了此脚本 并无特殊含义 只是为了更加清晰
        注意此脚本的内容不一定全完单独服务于KccMotor
        */

        #region 枚举
        /// <summary>
        /// 刚体交互类型 
        /// </summary>
        public enum RigidbodyInteractionType
        {
            [LabelText("无交互")]
            None, 
            [LabelText("运动学的(霸体刚体)")]
            Kinematic, 
            [LabelText("模拟动态刚体")]
            SimulatedDynamic, 
        }

        /// <summary>
        /// 处理台阶的枚举 Q:台阶的不同情况
        /// </summary>
        public enum StepHandlingMethod
        {
            [LabelText("无处理")]
            None,
            [LabelText("标准处理")]
            Standard,
            [LabelText("额外处理")]
            Extra
        }

        /// <summary>
        /// 移动扫掠状态 Q:该枚举所代表的含义是?
        /// </summary>
        public enum MovementSweepState
        {
            [LabelText("初始状态")]
            Initial,// Q:初始状态是指?
            [LabelText("第一次碰撞后")]
            AfterFirstHit,
            [LabelText("发现阻挡斜坡")]
            FoundBlockingCrease,
            [LabelText("发现阻挡角落")]
            FoundBlockingCorner,
        }


        #endregion

        #region  结构体

        #region 角色核心状态结构
        /// <summary>
        /// Represents the entire state of a character motor that is pertinent for simulation.
        /// Use this to save state or revert to past state
        /// 角色运动的整个状态类
        /// 用于保存状态或恢复到过去的状态
        /// </summary>
        [System.Serializable]
        public struct KinematicCharacterMotorState
        {
            public Vector3 Position;// 位置
            public Quaternion Rotation;// 旋转
            public Vector3 BaseVelocity;// 基础速度

            public bool MustUnground;// 必须离地
            public float MustUngroundTime;// 必须离地时间
            public bool LastMovementIterationFoundAnyGround;// 上次移动迭代是否发现任何地面
            public CharacterTransientGroundingReport GroundingStatus;// 接地状态

            public Rigidbody AttachedRigidbody;// 附着的刚体
            public Vector3 AttachedRigidbodyVelocity;// 附着的刚体速度
        }


        #endregion


        #region 地面报告

        // ============================地面信息====================================
        /// <summary>
        /// Contains all the information for the motor's grounding status
        /// 包含角色接地状态的所有信息
        /// </summary>
        public struct CharacterGroundingReport
        {
            public bool FoundAnyGround;// 是否发现任何地面
            public bool IsStableOnGround;// 是否稳定在地面上
            public bool SnappingPrevented;// 是否阻止了 snapping 吸附
            public Vector3 GroundNormal;// 地面法线

            // Q:为什么地面发现还分内外?
            public Vector3 InnerGroundNormal;// 内部地面法线
            public Vector3 OuterGroundNormal;// 外部地面法线

            // Q:为什么要记录地面碰撞体是谁?
            public Collider GroundCollider;// 地面碰撞体
            public Vector3 GroundPoint;// 地面点

            /// <summary>
            /// 从瞬态接地报告中复制信息
            /// </summary>
            /// <param name="transientGroundingReport"></param>
            public void CopyFrom(CharacterTransientGroundingReport transientGroundingReport)
            {
                FoundAnyGround = transientGroundingReport.FoundAnyGround;
                IsStableOnGround = transientGroundingReport.IsStableOnGround;
                SnappingPrevented = transientGroundingReport.SnappingPrevented;
                GroundNormal = transientGroundingReport.GroundNormal;
                InnerGroundNormal = transientGroundingReport.InnerGroundNormal;
                OuterGroundNormal = transientGroundingReport.OuterGroundNormal;

                GroundCollider = null;
                GroundPoint = Vector3.zero;
            }
        }

        /// <summary>
        /// Contains the simulation-relevant information for the motor's grounding status
        /// 包含有关控制器接地状态的仿真相关数据
        /// </summary>
        public struct CharacterTransientGroundingReport
        {
            public bool FoundAnyGround;// 是否发现任何地面
            public bool IsStableOnGround;// 是否稳定在地面上
            public bool SnappingPrevented;// 是否阻止了 snapping
            public Vector3 GroundNormal;// 地面法线
            public Vector3 InnerGroundNormal;// 内部地面法线
            public Vector3 OuterGroundNormal;// 外部地面法线

            public void CopyFrom(CharacterGroundingReport groundingReport)
            {
                FoundAnyGround = groundingReport.FoundAnyGround;
                IsStableOnGround = groundingReport.IsStableOnGround;
                SnappingPrevented = groundingReport.SnappingPrevented;
                GroundNormal = groundingReport.GroundNormal;
                InnerGroundNormal = groundingReport.InnerGroundNormal;
                OuterGroundNormal = groundingReport.OuterGroundNormal;
            }
        }


        #endregion

        #region 碰撞信息
        // ============================碰撞稳定性信息====================================
        /// <summary>
        /// Contains all the information from a hit stability evaluation
        /// 包含了一次 碰撞稳定性 评估中所包含的所有信息
        /// Q: 这个报告在什么阶段赋值? 为什么㤇这个报告?
        /// </summary>
        public struct HitStabilityReport
        {
            public bool IsStable;// 是否稳定

            public bool FoundInnerNormal;// 是否发现内部法线
            public Vector3 InnerNormal;// 内部法线
            
            public bool FoundOuterNormal;// 是否发现外部法线
            public Vector3 OuterNormal;// 外部法线

            public bool ValidStepDetected;// 是否检测到有效步骤
            public Collider SteppedCollider;// 步骤碰撞体

            // 是否检测到边缘 
            // 也就是一边能站一边不能站这种情况 其为true
            public bool LedgeDetected;

            ///是否在边缘的空侧
            // 后面决定要不要取消稳定站立
            // 防止角色半个身体出平台还被当成稳稳站住
            public bool IsOnEmptySideOfLedge;

            // 算角色底部离边缘有多远
            public float DistanceFromLedge;

            // 判断角色当前速度是不是正朝 空侧 冲
            public bool IsMovingTowardsEmptySideOfLedge;
            
            // 真正那块还能站的地面法线
            public Vector3 LedgeGroundNormal;

            // 沿着边缘"横"着走的方向 
            // 这里规定为Right就是一个名字 其实不一定是左是右
            public Vector3 LedgeRightDirection;

            // 边缘朝 空侧 的方向
            public Vector3 LedgeFacingDirection;
        }

        // ============================刚体碰撞投影信息====================================
        /// <summary>
        /// Contains the information of hit rigidbodies during the movement phase, so they can be processed afterwards
        /// 刚体碰撞投影,包含了运动阶段中被碰撞的刚体的相关信息，以便之后对其进行处理
        /// Q: 这个东西干什么用的?
        /// </summary>
        public struct RigidbodyProjectionHit
        {
            public Rigidbody Rigidbody;// Q:谁的刚体?
            public Vector3 HitPoint;// 碰撞点
            public Vector3 EffectiveHitNormal;// 有效碰撞法线
            public Vector3 HitVelocity;// 碰撞速度
            public bool StableOnHit;// 是否稳定在碰撞体上
        }

        // ============================重叠信息====================================
        /// <summary>
        /// 描述了角色胶囊与另一个碰撞体之间的重叠情况
        /// Describes an overlap between the character capsule and another collider
        /// </summary>
        public struct OverlapResult
        {
            public Vector3 Normal;// 法线
            public Collider Collider;// 碰撞体

            public OverlapResult(Vector3 normal, Collider collider)
            {
                Normal = normal;
                Collider = collider;
            }
        }
        #endregion

        #endregion
}

