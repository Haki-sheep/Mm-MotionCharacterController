using System.Collections.Generic;
using UnityEngine;

namespace MotionCharacterController
{
    /// <summary>
    /// 运动学角色系统 
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MccSystem : MonoBehaviour
    {
        private static MccSystem instance;
        // 插值开始时间
        private static float interpolationStartTime;
        // 插值时间间隔
        private static float interpolationDeltaTime = 0.02f;

        // 角色列表
        public static readonly List<MotionCC> Characters = new List<MotionCC>(32);
        // 物理移动器列表
        public static readonly List<MccPhysicsMover> Movers = new List<MccPhysicsMover>(16);



        private void Awake()
        {
            instance = this;
        }

        private void FixedUpdate()
        {
            float deltaTime = Time.fixedDeltaTime;
            bool interpolate = ShouldInterpolate();

            // 记录本帧起始位置
            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].PreSimulationTick(deltaTime);
            }

            // 算平台速度
            for (int i = 0; i < Movers.Count; i++)
            {
                Movers[i].VelocityUpdate(deltaTime);
            }

            // 接地过程 平台附着 
            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].UpdatePhase1(deltaTime);
            }

            // 平台先移动到目标位置
            for (int i = 0; i < Movers.Count; i++)
            {
                Movers[i].CommitMovement();
            }

            // 更新角色速度 移动和碰撞
            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].UpdatePhase2(deltaTime);
            }

            // 插值
            interpolationStartTime = Time.time;
            interpolationDeltaTime = deltaTime;

            // 提交模拟
            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].CommitSimulation(interpolate);
            }
        }

        private void LateUpdate()
        {
            if (!ShouldInterpolate())
                return; 

            float factor = Mathf.Clamp01((Time.time - interpolationStartTime) / interpolationDeltaTime);
            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].InterpolationUpdate(factor);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        public static void CreatMccSystem()
        {
            if (instance != null)
            {
                return;
            }

            GameObject go = new GameObject("MccSystem");
            instance = go.AddComponent<MccSystem>();
            go.hideFlags = HideFlags.NotEditable;
            DontDestroyOnLoad(go);
        }

        /// <summary>
        /// 注册角色
        /// </summary>
        /// <param name="character"></param>
        public static void RegisterCharacter(MotionCC character)
        {
            CreatMccSystem();
            if (!Characters.Contains(character))
            {
                Characters.Add(character);
            }
        }

        /// <summary>
        /// 注销角色
        /// </summary>
        /// <param name="character"></param>
        public static void UnregisterCharacter(MotionCC character)
        {
            Characters.Remove(character);
        }

        /// <summary>
        /// 注册物理移动器
        /// </summary>
        /// <param name="mover"></param>
        public static void RegisterMover(MccPhysicsMover mover)
        {
            CreatMccSystem();
            if (!Movers.Contains(mover))
            {
                Movers.Add(mover);
            }
        }

        /// <summary>
        /// 注销物理移动器
        /// </summary>
        /// <param name="mover"></param>
        public static void UnregisterMover(MccPhysicsMover mover)
        {
            Movers.Remove(mover);
        }

        /// <summary>
        /// 是否需要插值
        /// </summary>
        /// <returns></returns>
        private static bool ShouldInterpolate()
        {
            for (int i = 0; i < Characters.Count; i++)
            {
                if (Characters[i] != null && Characters[i].Config.interpolate)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
