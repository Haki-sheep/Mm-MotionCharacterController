using System.Collections.Generic;
using UnityEngine;

namespace MotionCharacterController
{
    [DefaultExecutionOrder(-100)]
    public class MccSystem : MonoBehaviour
    {
        private static MccSystem instance;
        private static float interpolationStartTime;
        private static float interpolationDeltaTime = 0.02f;

        public static readonly List<MotionCC> Characters = new List<MotionCC>(32);
        public static readonly List<MccPhysicsMover> Movers = new List<MccPhysicsMover>(16);

        public static void EnsureCreation()
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

        public static void RegisterCharacter(MotionCC character)
        {
            EnsureCreation();
            if (!Characters.Contains(character))
            {
                Characters.Add(character);
            }
        }

        public static void UnregisterCharacter(MotionCC character)
        {
            Characters.Remove(character);
        }

        public static void RegisterMover(MccPhysicsMover mover)
        {
            EnsureCreation();
            if (!Movers.Contains(mover))
            {
                Movers.Add(mover);
            }
        }

        public static void UnregisterMover(MccPhysicsMover mover)
        {
            Movers.Remove(mover);
        }

        private void Awake()
        {
            instance = this;
        }

        private void FixedUpdate()
        {
            float deltaTime = Time.fixedDeltaTime;
            bool interpolate = ShouldInterpolate();

            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].PreSimulationTick(deltaTime);
            }

            for (int i = 0; i < Movers.Count; i++)
            {
                Movers[i].VelocityUpdate(deltaTime);
            }

            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].UpdatePhase1(deltaTime);
            }

            for (int i = 0; i < Movers.Count; i++)
            {
                Movers[i].CommitMovement();
            }

            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].UpdatePhase2(deltaTime);
            }

            interpolationStartTime = Time.time;
            interpolationDeltaTime = deltaTime;

            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].CommitSimulation(interpolate);
            }
        }

        private void LateUpdate()
        {
            if (!ShouldInterpolate())
            {
                return;
            }

            float factor = Mathf.Clamp01((Time.time - interpolationStartTime) / interpolationDeltaTime);
            for (int i = 0; i < Characters.Count; i++)
            {
                Characters[i].InterpolationUpdate(factor);
            }
        }

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
