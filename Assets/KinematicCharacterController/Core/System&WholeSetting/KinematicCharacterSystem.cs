using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace KinematicCharacterController
{
    /// <summary>
    /// 管理运动学角色马达和物理移动器模拟的系统
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class KinematicCharacterSystem : MonoBehaviour
    {
        // 单例实例
        private static KinematicCharacterSystem _instance;

        // 所有角色马达
        public static List<KinematicCharacterMotor> CharacterMotors = new List<KinematicCharacterMotor>();
        // 所有物理移动器
        public static List<PhysicsMover> PhysicsMovers = new List<PhysicsMover>();

        // 上一次自定义插值开始时间
        private static float _lastCustomInterpolationStartTime = -1f;
        // 上一次自定义插值的增量时间
        private static float _lastCustomInterpolationDeltaTime = -1f;

        // 系统配置
        public static KCCSettings Settings;

        /// <summary>
        /// 如果不存在实例，则创建运动学角色系统实例
        /// </summary>
        public static void EnsureCreation()
        {
            if (_instance == null)
            {
                GameObject systemGameObject = new GameObject("KinematicCharacterSystem");
                _instance = systemGameObject.AddComponent<KinematicCharacterSystem>();

                systemGameObject.hideFlags = HideFlags.NotEditable;
                _instance.hideFlags = HideFlags.NotEditable;

                Settings = ScriptableObject.CreateInstance<KCCSettings>();

                GameObject.DontDestroyOnLoad(systemGameObject);
            }
        }

        /// <summary>
        /// 获取运动学角色系统的实例（如果存在）
        /// </summary>
        /// <returns></returns>
        public static KinematicCharacterSystem GetInstance()
        {
            return _instance;
        }

        /// <summary>
        /// 设置角色马达列表的最大容量，防止添加角色时产生内存分配
        /// </summary>
        /// <param name="capacity">容量</param>
        public static void SetCharacterMotorsCapacity(int capacity)
        {
            if (capacity < CharacterMotors.Count)
            {
                capacity = CharacterMotors.Count;
            }
            CharacterMotors.Capacity = capacity;
        }

        /// <summary>
        /// 将运动学角色注册到System的List系统中
        /// </summary>
        public static void RegisterCharacterMotor(KinematicCharacterMotor motor)
        {
            CharacterMotors.Add(motor);
        }

        /// <summary>
        /// 将运动学角色马达从系统中注销
        /// </summary>
        public static void UnregisterCharacterMotor(KinematicCharacterMotor motor)
        {
            CharacterMotors.Remove(motor);
        }

        /// <summary>
        /// 设置物理移动器列表的最大容量，防止添加移动器时产生内存分配
        /// </summary>
        /// <param name="capacity">容量</param>
        public static void SetPhysicsMoversCapacity(int capacity)
        {
            if (capacity < PhysicsMovers.Count)
            {
                capacity = PhysicsMovers.Count;
            }
            PhysicsMovers.Capacity = capacity;
        }

        /// <summary>
        /// 将物理移动器注册到系统中
        /// </summary>
        public static void RegisterPhysicsMover(PhysicsMover mover)
        {
            PhysicsMovers.Add(mover);

            // 刚体插值模式设置为None
            mover.Rigidbody.interpolation = RigidbodyInterpolation.None;
        }

        /// <summary>
        /// 将物理移动器从系统中注销
        /// </summary>
        public static void UnregisterPhysicsMover(PhysicsMover mover)
        {
            PhysicsMovers.Remove(mover);
        }

        // 防止脚本重新编译时单例游戏对象重复创建
        private void OnDisable()
        {
            Destroy(this.gameObject);
        }

        private void Awake()
        {
            _instance = this;
            
        }

        // void Start()
        // {
        //     Settings.Interpolate = true;
        //     Settings.AutoSimulation = true;
        // }

        private void FixedUpdate()
        {
            if (Settings.AutoSimulation)
            {
                float deltaTime = Time.fixedDeltaTime;

                // 如果设置的差值效果开启
                if (Settings.Interpolate)
                {
                    PreSimulationInterpolationUpdate(deltaTime);
                }

                // 模拟
                Simulate(deltaTime, CharacterMotors, PhysicsMovers);

                if (Settings.Interpolate)
                {
                    PostSimulationInterpolationUpdate(deltaTime);
                }
            }
        }

        private void LateUpdate()
        {
            if (Settings.Interpolate)
            {
                CustomInterpolationUpdate();
            }
        }
////////////////////////////上一帧我称为 - 1,当前帧我称为 0 ,下一帧我称为 1//////////////////////////////////////////////////////////////
        /// <summary>
        /// 模拟前插值更新
        /// </summary>
        public static void PreSimulationInterpolationUpdate(float deltaTime)
        {
            // 保存模拟前的姿态，并将变换设置为瞬时姿态
            for (int i = 0; i < CharacterMotors.Count; i++)
            {
                KinematicCharacterMotor motor = CharacterMotors[i];

                // 将 -1 时的最终位置给了 0 时的初始位置 (都是纯逻辑)
                motor.InitialTickPosition = motor.TransientPosition;
                motor.InitialTickRotation = motor.TransientRotation;

                // 引擎真实表现
                motor.Transform.SetPositionAndRotation(motor.TransientPosition,motor.TransientRotation);
            }

            for (int i = 0; i < PhysicsMovers.Count; i++)
            {
                PhysicsMover mover = PhysicsMovers[i];

                // 和上同理
                mover.InitialTickPosition = mover.TransientPosition;
                mover.InitialTickRotation = mover.TransientRotation;

                mover.Transform.SetPositionAndRotation(mover.TransientPosition, mover.TransientRotation);
                
                // 引擎真实表现
                mover.Rigidbody.position = mover.TransientPosition;
                mover.Rigidbody.rotation = mover.TransientRotation.normalized;
            }
        }

        /// <summary>
        /// 执行角色和/或移动器的逻辑更新
        /// </summary>
        /// <param name="deltaTime">模拟步长(一般是物理步长)</param>
        /// <param name="motors">角色列表</param>
        /// <param name="movers">移动器列表</param>
        public static void Simulate(float deltaTime, List<KinematicCharacterMotor> motors, List<PhysicsMover> movers)
        {
            // 更新物理移动器的速度
            for (int i = 0; i <  movers.Count; i++)
            {
                movers[i].VelocityUpdate(deltaTime);
            }

            // 角色控制器第一阶段更新
            for (int i = 0; i < motors.Count; i++)
            {
                motors[i].UpdatePhase1(deltaTime);
            }

            // 模拟物理移动器的位移
            for (int i = 0; i < movers.Count; i++)
            {
                PhysicsMover mover = movers[i];

                mover.Transform.SetPositionAndRotation(mover.TransientPosition, mover.TransientRotation);
                mover.Rigidbody.position = mover.TransientPosition;
                mover.Rigidbody.rotation = mover.TransientRotation.normalized;
            }

            // 角色控制器第二阶段更新与移动
            for (int i = 0; i < motors.Count; i++)
            {
                KinematicCharacterMotor motor = motors[i];

                motor.UpdatePhase2(deltaTime);

                motor.Transform.SetPositionAndRotation(motor.TransientPosition, motor.TransientRotation);
            }
        }

        /// <summary>
        /// 模拟后插值更新
        /// </summary>
        public static void PostSimulationInterpolationUpdate(float deltaTime)
        {
            // 记录0时插值开始时间 和 插值增量时间 用给插值公式算插值系数
            _lastCustomInterpolationStartTime = Time.time;
            _lastCustomInterpolationDeltaTime = deltaTime;

            // 将0时的初始位置和旋转 直接设置到0时角色身上
            for (int i = 0; i < CharacterMotors.Count; i++)
            {
                KinematicCharacterMotor motor = CharacterMotors[i];

                motor.Transform.SetPositionAndRotation(motor.InitialTickPosition, motor.InitialTickRotation);
            }

            for (int i = 0; i < PhysicsMovers.Count; i++)
            {
                PhysicsMover mover = PhysicsMovers[i];

                if (mover.MoveWithPhysics)
                {
                    // 将 Rigidbody 瞬移到 帧0 起始位置
                    mover.Rigidbody.position = mover.InitialTickPosition;
                    mover.Rigidbody.rotation = mover.InitialTickRotation;

                    // 告诉物理引擎,在本物理步内从当前位置移动到 TransientPosition
                    mover.Rigidbody.MovePosition(mover.TransientPosition);
                    mover.Rigidbody.MoveRotation(mover.TransientRotation.normalized);
                }
                else
                {
                    mover.Rigidbody.position = (mover.TransientPosition);
                    mover.Rigidbody.rotation = (mover.TransientRotation);
                }
            }
        }

        /// <summary>
        /// 自定义插值更新
        /// </summary>
        private static void CustomInterpolationUpdate()
        {
            // 插值系数 = (当前时间 - 插值开始时间) / 物理帧增量时间
            float interpolationFactor = Mathf.Clamp01(
                (Time.time - _lastCustomInterpolationStartTime) / _lastCustomInterpolationDeltaTime);

            // 处理角色插值
            for (int i = 0; i < CharacterMotors.Count; i++)
            {
                KinematicCharacterMotor motor = CharacterMotors[i];

                // 将电机 0时 起始位置 插值到 0时最终位置
                motor.Transform.SetPositionAndRotation(
                    Vector3.Lerp(motor.InitialTickPosition, motor.TransientPosition, interpolationFactor),
                    Quaternion.Slerp(motor.InitialTickRotation, motor.TransientRotation, interpolationFactor));
            }

            // 处理物理移动器插值
            for (int i = 0; i < PhysicsMovers.Count; i++)
            {
                PhysicsMover mover = PhysicsMovers[i];
                
                mover.Transform.SetPositionAndRotation(
                    Vector3.Lerp(mover.InitialTickPosition, mover.TransientPosition, interpolationFactor),
                    Quaternion.Slerp(mover.InitialTickRotation, mover.TransientRotation, interpolationFactor));

                // 获取0时位置和旋转
                Vector3 newPos = mover.Transform.position;
                Quaternion newRot = mover.Transform.rotation;

                // 位置 与 插值
                // 做移动平台的记录
                // 插值位置增量 = 0时位置 - (-1时位置)
                mover.PositionDeltaFromInterpolation = newPos - mover.LatestInterpolationPosition;


                // 旋转
                // 旋转增量 = 上一帧-1旋转 的反向旋转 * 当前帧0旋转
                mover.RotationDeltaFromInterpolation = Quaternion.Inverse(mover.LatestInterpolationRotation) * newRot;

                // 更新缓存：把当前帧0存起来 → 给下一帧1当「上一帧-1」用
                mover.LatestInterpolationPosition = newPos;
                mover.LatestInterpolationRotation = newRot;
            }
        }
    }
}