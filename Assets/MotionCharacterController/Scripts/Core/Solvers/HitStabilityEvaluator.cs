using UnityEngine;

namespace MotionCharacterController
{
    /// <summary>
    /// 此类为工具类
    /// 为了解决Mcc的Init里 GroundSolver 与 CollisionSolver 循环依赖问题
    /// </summary>
    public static class HitStabilityEvaluator
    {
        /// <summary>
        /// 评估碰撞面稳定性
        /// </summary>
        /// <param name="context">上下文</param>
        /// <param name="queries">碰撞求解器</param>
        /// <param name="stepSolver">台阶求解器</param>
        /// <param name="hitCollider">碰撞体</param>
        /// <param name="hitNormal">碰撞法线</param>
        /// <param name="hitPoint">碰撞点</param>
        /// <param name="atCharacterPosition">角色位置</param>
        /// <param name="atCharacterRotation">角色旋转</param>
        /// <param name="velocity">速度</param>
        /// <returns>稳定性报告</returns>
        public static HitStabilityReport Evaluate(
            MccMotorContext context,
            CollisionSolver queries,
            StepSolver stepSolver,
            Collider hitCollider,
            Vector3 hitNormal,
            Vector3 hitPoint,
            Vector3 atCharacterPosition,
            Quaternion atCharacterRotation,
            Vector3 velocity)
        {
            // 初始化碰撞面稳定性报告
            HitStabilityReport report = new HitStabilityReport
            {
                IsStable = context.SolveGrounding && context.IsStableOnNormal(hitNormal),
                InnerNormal = hitNormal,
                OuterNormal = hitNormal,
            };

            // 如果不需要地面检测 则直接返回
            if (!context.SolveGrounding)
            {
                return report;
            }

            // 处理边缘稳定性
            LedgeSolver.ProcessLedgeStability(context, queries, hitNormal, hitPoint, atCharacterPosition, atCharacterRotation, velocity, ref report);

            // 处理台阶稳定性
            if (context.Config.stepHandling is not StepHandlingMethod.None && !report.IsStable)
            {
                // 获取碰撞体的刚体
                Rigidbody body = hitCollider is not null ? hitCollider.attachedRigidbody : null;
                if (!(body is not null && !body.isKinematic))
                {
                    // 检测台阶
                    stepSolver.DetectSteps(
                        queries,
                        atCharacterPosition,
                        atCharacterRotation,
                        hitPoint,
                        // 法线在水平面上朝外的投影向量
                        Vector3.ProjectOnPlane(hitNormal, atCharacterRotation * Vector3.up).normalized,
                        ref report);
                    // 如果检测到台阶 则设置为稳定
                    if (report.ValidStepDetected)
                        report.IsStable = true;
                }
            }

            // 外部开发者处理稳定性报告
            context.Owner.Controller?.ProcessHitStabilityReport(
                hitCollider,
                hitNormal,
                hitPoint,
                atCharacterPosition,
                atCharacterRotation,
                ref report);

            // 返回稳定性报告
            return report;
        }
    }
}
