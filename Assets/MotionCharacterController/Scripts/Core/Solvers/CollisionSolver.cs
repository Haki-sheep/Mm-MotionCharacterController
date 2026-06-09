using UnityEngine;

namespace MotionCharacterController
{
    /// <summary>
    /// 碰撞检测与处理
    /// </summary>
    public class CollisionSolver
    {
        private readonly MccMotorContext context;
        private readonly StepSolver stepSolver;
        private readonly RigidbodySolver rigidbodySolver;


        /// <summary>
        /// 碰撞求解需要依赖台阶求解器与刚体求解器
        /// </summary>
        /// <param name="context">上下文</param>
        /// <param name="stepSolver">台阶求解器</param>
        /// <param name="rigidbodySolver">刚体求解器</param>
        public CollisionSolver(MccMotorContext context, StepSolver stepSolver, RigidbodySolver rigidbodySolver)
        {
            this.context = context;
            this.stepSolver = stepSolver;
            this.rigidbodySolver = rigidbodySolver;
        }

        /// <summary>
        /// 解算初始重叠
        /// </summary>
        public void ResolveInitialOverlaps()
        {
            // 遍历最大重叠解算次数
            for (int iteration = 0; iteration < context.Config.maxDecollisionIterations; iteration++)
            {
                // 获取重叠数量
                int count = CharacterCollisionsOverlap(context.TransientPosition, context.TransientRotation, context.InternalColliders);
                bool solved = true;

                for (int i = 0; i < count; i++)
                {
                    Collider other = context.InternalColliders[i];
                    // 如果碰撞体无效 则跳过
                    if (!context.IsColliderValidForCollisions(other))
                        continue;

                    // 计算碰撞分离信息
                    if (Physics.ComputePenetration(
                        context.Capsule,
                        context.TransientPosition,
                        context.TransientRotation,
                        other,
                        other.transform.position,
                        other.transform.rotation,
                        out Vector3 direction,
                        out float distance))
                    {
                        // 判断碰撞是否稳定
                        bool stable = context.IsStableOnNormal(direction);
                        // 获取障碍物法线
                        var resolutionNormal = GetObstructionNormal(direction, stable);
                        // 计算移动距离
                        var movement = resolutionNormal * (distance + MccConfig.COLLISION_OFFSET);
                        // 积分移动 解决重叠
                        context.TransientPosition += movement;

                        // 记录重叠信息
                        RememberOverlap(resolutionNormal, other);
                        solved = false;
                        break;
                    }
                }

                if (solved)
                {
                    // 没有重叠了 则结束迭代
                    break;
                }
            }
        }

        /// <summary>
        /// 移动
        /// 该方法主要解决角色移动时与碰撞体的碰撞问题
        /// </summary>
        /// <param name="velocity">速度</param>
        /// <param name="deltaTime">时间差</param>
        /// <returns>是否完成</returns>
        public bool Move(ref Vector3 velocity, float deltaTime)
        {
            if (deltaTime <= 0f)
                return false;

            // 如果存在平台约束 则将速度直接投影到被约束平面上
            if (context.Config.hasPlanarConstraint)
                velocity = Vector3.ProjectOnPlane(velocity, context.Config.planarConstraintAxis.normalized);

            // 初始化迭代完成状态与方向
            var completed = true;
            var originalDirection = velocity.normalized;

            // 迭代后剩余信息
            var remainingDirection = originalDirection;
            var remainingMagnitude = velocity.magnitude * deltaTime;

            // 移动与扫掠信息
            var movedPosition = context.TransientPosition;
            var sweepState = MovementSweepState.Initial;

            // 上一次扫掠的稳定状态、速度、障碍物法线
            var previousHitStable = false;
            var previousVelocity = Vector3.zero;
            var previousObstructionNormal = Vector3.zero;

            // 处理已知的重叠信息
            ProjectAgainstKnownOverlaps(ref velocity,
                                        ref remainingMagnitude,
                                        ref remainingDirection,
                                        ref sweepState,
                                        ref previousHitStable,
                                        ref previousVelocity,
                                        ref previousObstructionNormal);

            int sweeps = 0;
            while (remainingMagnitude > MccConfig.MIN_CANMOVE_DISTANCE && sweeps <= context.Config.maxMovementIterations)
            {
                // 过滤碰撞体并进行扫略检测
                if (CharacterCollisionsSweep(movedPosition,
                                             context.TransientRotation,
                                             remainingDirection,
                                             remainingMagnitude + MccConfig.COLLISION_OFFSET,
                                             out RaycastHit hit,
                                             context.InternalHits) <= 0)
                {
                    // 如果没有扫到东西 则直接累计剩余距离
                    movedPosition += remainingDirection * remainingMagnitude;
                    remainingMagnitude = 0f;
                    break;
                }

                // 扫略结束后 计算扫略移动距离
                Vector3 sweepMovement = remainingDirection
                                            * Mathf.Max(0f, hit.distance - MccConfig.COLLISION_OFFSET);
                movedPosition += sweepMovement;
                remainingMagnitude -= sweepMovement.magnitude;

                // 评估扫略稳定性 判断是台阶 坡 还是墙体
                HitStabilityReport report = HitStabilityEvaluator.Evaluate(
                    context,
                    this,
                    stepSolver,
                    hit.collider,
                    hit.normal,
                    hit.point,
                    movedPosition,
                    context.TransientRotation,
                    velocity);
                
                // 处理台阶
                bool stepped = false;
                if (context.SolveGrounding 
                    && context.Config.stepHandling != StepHandlingMethod.None 
                        && report.ValidStepDetected)
                {
                    stepped = stepSolver.TryStep(this, ref movedPosition, ref velocity, hit, report);
                    if (stepped)
                    {
                        remainingDirection = velocity.normalized;
                        remainingMagnitude = velocity.magnitude * deltaTime;
                    }
                }

                // 处理坡体、墙体
                if (!stepped)
                {
                    // 获取障碍物法线
                    var obstructionNormal = GetObstructionNormal(hit.normal, report.IsStable);
                    // 外部回调
                    context.Owner.Controller?.OnMovementHit(hit.collider, hit.normal, hit.point, ref report);
                    
                    // 可能要处理刚体碰撞
                    if (hit.collider is not null && hit.collider.attachedRigidbody is not null)
                    {
                        rigidbodySolver.StoreHit(hit.collider.attachedRigidbody, velocity, hit.point, obstructionNormal);
                    }

                    // 处理速度投影 将剩余速度投影到障碍物切线方向实现沿墙滑
                    bool stableOnHit = report.IsStable && !context.Owner.MustUnground();
                    Vector3 velocityBeforeProjection = velocity;
                    HandleVelocityProjection(stableOnHit,
                                             hit.normal,
                                             ref sweepState,
                                             previousHitStable,
                                             previousVelocity,
                                             previousObstructionNormal,
                                             ref velocity,
                                             ref remainingMagnitude,
                                             ref remainingDirection);

                    // 更新上一次扫略的稳定状态、速度、障碍物法线
                    previousHitStable = stableOnHit;
                    previousVelocity = velocityBeforeProjection;
                    previousObstructionNormal = obstructionNormal;
                }

                // 迭代次数累加
                sweeps++;
                // 如果迭代次数超过最大迭代次数 则直接设置剩余距离为0
                if (sweeps > context.Config.maxMovementIterations)
                {
                    completed = false;
                    if (context.Config.killRemainingMovementWhenExceedMaxMovementIterations)
                    {
                        remainingMagnitude = 0f;
                    }
                    if (context.Config.killVelocityWhenExceedMaxMovementIterations)
                    {
                        velocity = Vector3.zero;
                    }
                }
            }

            // 累计剩余距离
            movedPosition += remainingDirection * remainingMagnitude;
            // 更新位置
            context.TransientPosition = movedPosition;
            return completed;
        }

        /// <summary>
        /// 处理离散碰撞事件
        /// </summary>
        public void ProcessDiscreteCollisionEvents()
        {
            // 如果未开启离散碰撞事件 则直接返回
            if (!context.Config.discreteCollisionEvents)
            {
                return;
            }

            // 检测当前位置是否与碰撞体重叠
            int count = CharacterCollisionsOverlap(context.TransientPosition, context.TransientRotation, context.InternalColliders, MccConfig.COLLISION_OFFSET * 2f);
            for (int i = 0; i < count; i++)
            {
                context.Owner.Controller?.OnDiscreteCollisionDetected(context.InternalColliders[i]);
            }
        }

        /// <summary>
        /// 角色碰撞体重叠
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <param name="colliders">碰撞体数组</param>
        /// <param name="inflate">膨胀</param>
        /// <param name="acceptOnlyStableGroundLayer">只接受稳定地面层</param>
        /// <returns>碰撞数</returns>
        public int CharacterCollisionsOverlap(Vector3 position,
                                              Quaternion rotation,
                                              Collider[] colliders,
                                              float inflate = 0f,
                                              bool acceptOnlyStableGroundLayer = false)
        {
            // 获取查询层
            int queryLayers = acceptOnlyStableGroundLayer ? context.CollidableLayers & context.Config.stableGroundLayers : context.CollidableLayers;
            // 获取胶囊体底部半球位置
            var bottom = context.GetCapsuleBottomHemiAt(position,
                                                            rotation,
                                                            inflate);
            // 获取胶囊体顶部半球位置
            var top = context.GetCapsuleTopHemiAt(position,
                                                      rotation,
                                                      inflate);
            // 进行胶囊体重叠检测
            int rawCount = Physics.OverlapCapsuleNonAlloc(bottom,
                                                          top,
                                                          context.Capsule.radius + inflate,
                                                          colliders,
                                                          queryLayers,
                                                          QueryTriggerInteraction.Ignore);
            // 过滤碰撞体
            return FilterColliders(colliders, rawCount);
        }

        /// <summary>
        /// 角色碰撞体扫略检测
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <param name="direction">方向</param>
        /// <param name="distance">距离</param>
        /// <param name="closestHit">最近的碰撞</param>
        /// <param name="hits">碰撞数组</param>
        /// <param name="inflate">膨胀</param>
        /// <param name="acceptOnlyStableGroundLayer">只接受稳定地面层</param>
        /// <returns>碰撞数</returns>
        public int CharacterCollisionsSweep(Vector3 position,
                                            Quaternion rotation,
                                            Vector3 direction,
                                            float distance,
                                            out RaycastHit closestHit,
                                            RaycastHit[] hits,
                                            float inflate = 0f,
                                            bool acceptOnlyStableGroundLayer = false)
        {
            closestHit = default;
            int queryLayers = 0;
            if (direction.sqrMagnitude <= 0f || distance <= 0f)
                return 0;
            // 获取查询层
            if (acceptOnlyStableGroundLayer)
                queryLayers = context.CollidableLayers & context.Config.stableGroundLayers;
            else
                queryLayers = context.CollidableLayers;

            // 获取标准化方向
            var normalizedDirection = direction.normalized;
            // 获取胶囊体底部半球位置 稍微往里收一点 避免扫掠时卡在胶囊体内部
            var bottom = context.GetCapsuleBottomHemiAt(position, rotation, inflate)
                                                        - normalizedDirection * MccConfig.SWEEP_BACKSTEP_DISTANCE;
            // 获取胶囊体顶部半球位置 稍微往里收一点 避免扫掠时卡在胶囊体内部
            var top = context.GetCapsuleTopHemiAt(position, rotation, inflate)
                                                        - normalizedDirection * MccConfig.SWEEP_BACKSTEP_DISTANCE;
            // 进行胶囊体扫掠
            var rawCount = Physics.CapsuleCastNonAlloc(bottom,
                                                       top,
                                                       context.Capsule.radius + inflate,
                                                       normalizedDirection,
                                                       hits,
                                                       distance + MccConfig.SWEEP_BACKSTEP_DISTANCE,
                                                       queryLayers,
                                                       QueryTriggerInteraction.Ignore);
            // 过滤碰撞
            return FilterHits(hits,
                              rawCount,
                              MccConfig.SWEEP_BACKSTEP_DISTANCE,
                              out closestHit);
        }

        /// <summary>
        /// 角色碰撞体射线检测
        /// </summary>
        /// <param name="position">起点</param>
        /// <param name="direction">方向</param>
        /// <param name="distance">距离</param>
        /// <param name="closestHit">最近碰撞</param>
        /// <param name="hits">碰撞数组</param>
        /// <param name="acceptOnlyStableGroundLayer">只接受稳定地面层</param>
        /// <returns>碰撞数</returns>
        public int CharacterCollisionsRaycast(Vector3 position, Vector3 direction, float distance, out RaycastHit closestHit, RaycastHit[] hits, bool acceptOnlyStableGroundLayer = false)
        {
            // 获取查询层
            int queryLayers = acceptOnlyStableGroundLayer ? context.CollidableLayers & context.Config.stableGroundLayers : context.CollidableLayers;
            // 进行射线检测
            int rawCount = Physics.RaycastNonAlloc(position, direction, hits, distance, queryLayers, QueryTriggerInteraction.Ignore);
            // 过滤碰撞
            return FilterHits(hits, rawCount, 0f, out closestHit);
        }

        /// <summary>
        /// 获取障碍物法线
        /// </summary>
        /// <param name="hitNormal">碰撞法线</param>
        /// <param name="isStable">是否稳定</param>
        /// <returns>障碍物法线</returns>
        public Vector3 GetObstructionNormal(Vector3 hitNormal, bool isStable)
        {
            var obstructionNormal = hitNormal;
            // 如果角色稳定在地面上 且 没有离地 且 碰撞不稳定 则计算障碍物法线
            if (context.GroundingStatus.IsStableOnGround && !context.Owner.MustUnground() && !isStable)
            {
                var obstructionLeftAlongGround = Vector3.Cross(context.GroundingStatus.GroundNormal, obstructionNormal).normalized;
                obstructionNormal = Vector3.Cross(obstructionLeftAlongGround, context.CharacterUp).normalized;
            }

            return obstructionNormal.sqrMagnitude > 0f ? obstructionNormal : hitNormal;
        }


        /// <summary>
        /// 投影已知重叠信息
        /// 先根据 本帧已经记下的重叠信息，把速度/剩余位移处理一遍，避免刚被推开，又立刻往墙里钻的情况
        /// </summary>
        /// <param name="velocity">速度</param>
        /// <param name="remainingMagnitude">剩余距离</param>
        /// <param name="remainingDirection">剩余方向</param>
        /// <param name="sweepState">扫掠状态</param>
        /// <param name="previousHitStable">上一帧稳定状态</param>
        /// <param name="previousVelocity">上一帧速度</param>
        /// <param name="previousObstructionNormal">上一帧障碍物法线</param>
        private void ProjectAgainstKnownOverlaps(ref Vector3 velocity,
                                                 ref float remainingMagnitude,
                                                 ref Vector3 remainingDirection,
                                                 ref MovementSweepState sweepState,
                                                 ref bool previousHitStable,
                                                 ref Vector3 previousVelocity,
                                                 ref Vector3 previousObstructionNormal)
        {
            for (int i = 0; i < context.OverlapsCount; i++)
            {
                var overlapNormal = context.Overlaps[i].Normal;

                // 如果剩余方向与重叠法线方向相同或相反 则跳过
                if (Vector3.Dot(remainingDirection, overlapNormal) >= 0f)
                    continue;

                // 判断重叠是否稳定
                var isStable = context.IsStableOnNormal(overlapNormal) && !context.Owner.MustUnground();
                // 获取障碍物法线
                var obstructionNormal = GetObstructionNormal(overlapNormal, isStable);
                // 获取速度投影前的速度
                var velocityBeforeProjection = velocity;

                // 处理速度投影
                HandleVelocityProjection(isStable,
                                         obstructionNormal,
                                         ref sweepState,
                                         previousHitStable,
                                         previousVelocity,
                                         previousObstructionNormal,
                                         ref velocity,
                                         ref remainingMagnitude,
                                         ref remainingDirection);

                // 更新上一帧稳定状态、速度、障碍物法线
                previousHitStable = isStable;
                previousVelocity = velocityBeforeProjection;
                previousObstructionNormal = obstructionNormal;
            }
        }

        /// <summary>
        /// 处理速度投影
        /// 去掉往障碍物里面卡的速度 投影成沿墙壁滑行的速度
        /// </summary>
        /// <param name="isStable">是否稳定</param>
        /// <param name="obstructionNormal">障碍物法线</param>
        /// <param name="sweepState">扫掠状态</param>
        /// <param name="previousHitStable">上一帧稳定状态</param>
        /// <param name="previousVelocity">上一帧速度</param>
        /// <param name="previousObstructionNormal">上一帧障碍物法线</param>
        /// <param name="velocity">速度</param>
        /// <param name="remainingMagnitude">剩余距离</param>
        /// <param name="remainingDirection">剩余方向</param>
        private void HandleVelocityProjection(bool isStableOnHit,
                                              Vector3 obstructionNormal,
                                              ref MovementSweepState sweepState,
                                              bool previousHitStable,
                                              Vector3 previousVelocity,
                                              Vector3 previousObstructionNormal,
                                              ref Vector3 velocity,
                                              ref float remainingMagnitude,
                                              ref Vector3 remainingDirection)
        {
            if (velocity.sqrMagnitude <= 0f)
                return;

            // 获取速度投影前的速度
            var velocityBeforeProjection = velocity;
            // 如果碰撞稳定 则直接投影
            if (isStableOnHit)
            {
                context.LastMovementIterationFoundAnyGround = true;
                ProjectVelocity(ref velocity, obstructionNormal, true);
            }
            // 如果扫掠状态为初始 则直接投影
            else if (sweepState == MovementSweepState.Initial)
            {
                ProjectVelocity(ref velocity, obstructionNormal, false);
                sweepState = MovementSweepState.AfterFirstHit;
            }
            // 如果扫掠状态为首次命中后 则评估内角折线阻挡
            else if (sweepState == MovementSweepState.AfterFirstHit)
            {
                EvaluateCrease(velocity, previousVelocity, obstructionNormal, previousObstructionNormal, isStableOnHit, previousHitStable, context.GroundingStatus.IsStableOnGround && !context.Owner.MustUnground(), out bool foundCrease, out Vector3 creaseDirection);
                if (foundCrease)
                {
                    velocity = context.GroundingStatus.IsStableOnGround ? Vector3.zero : Vector3.Project(velocity, creaseDirection);
                    sweepState = context.GroundingStatus.IsStableOnGround ? MovementSweepState.FoundBlockingCorner : MovementSweepState.FoundBlockingCrease;
                }
                else
                {
                    ProjectVelocity(ref velocity, obstructionNormal, false);
                }
            }
            // 如果扫掠状态为发现内角折线阻挡 则直接设置速度为0
            else if (sweepState == MovementSweepState.FoundBlockingCrease)
            {
                velocity = Vector3.zero;
                sweepState = MovementSweepState.FoundBlockingCorner;
            }

            // 如果开启了平面约束 则投影到约束平面上
            if (context.Config.hasPlanarConstraint)
            {
                velocity = Vector3.ProjectOnPlane(velocity, context.Config.planarConstraintAxis.normalized);
            }

            // 计算剩余速度和剩余方向
            float velocityFactor = velocityBeforeProjection.magnitude > 0f ? velocity.magnitude / velocityBeforeProjection.magnitude : 0f;
            remainingMagnitude *= velocityFactor;
            remainingDirection = velocity.sqrMagnitude > 0f ? velocity.normalized : Vector3.zero;
        }
        /// <summary>
        /// 投影速度
        /// </summary>
        /// <param name="velocity">速度</param>
        /// <param name="obstructionNormal">障碍物法线</param>
        /// <param name="stableOnHit">是否稳定</param>
        private void ProjectVelocity(ref Vector3 velocity, Vector3 obstructionNormal, bool stableOnHit)
        {
            // 如果角色稳定在地面上 且 没有离地 则投影速度
            if (context.GroundingStatus.IsStableOnGround && !context.Owner.MustUnground())
            {
                // 如果命中稳定 则投影速度
                if (stableOnHit)
                {
                    velocity = context.GetDirectionTangentToSurface(velocity, obstructionNormal) * velocity.magnitude;
                }
                else
                {
                    // 获取障碍物右侧方向
                    Vector3 obstructionRightAlongGround = Vector3.Cross(obstructionNormal, context.GroundingStatus.GroundNormal).normalized;
                    // 获取障碍物上方方向
                    Vector3 obstructionUpAlongGround = Vector3.Cross(obstructionRightAlongGround, obstructionNormal).normalized;
                    // 投影速度
                    velocity = context.GetDirectionTangentToSurface(velocity, obstructionUpAlongGround) * velocity.magnitude;
                    velocity = Vector3.ProjectOnPlane(velocity, obstructionNormal);
                }
            }
            else if (stableOnHit)
            {
                velocity = Vector3.ProjectOnPlane(velocity, context.CharacterUp);
                velocity = context.GetDirectionTangentToSurface(velocity, obstructionNormal) * velocity.magnitude;
            }
            else
            {
                velocity = Vector3.ProjectOnPlane(velocity, obstructionNormal);
            }
        }

        /// <summary>
        /// 评估内角折线阻挡
        /// </summary>
        /// <param name="currentVelocity">当前速度</param>
        /// <param name="previousVelocity">上一帧速度</param>
        /// <param name="currentNormal">当前法线</param>
        /// <param name="previousNormal">上一帧法线</param>
        /// <param name="currentStable">当前稳定状态</param>
        /// <param name="previousStable">上一帧稳定状态</param>
        /// <param name="characterStable">角色稳定状态</param>
        /// <param name="validCrease">是否有效折线</param>
        /// <param name="creaseDirection">折线方向</param>
        private static void EvaluateCrease(Vector3 currentVelocity, Vector3 previousVelocity, Vector3 currentNormal, Vector3 previousNormal, bool currentStable, bool previousStable, bool characterStable, out bool validCrease, out Vector3 creaseDirection)
        {
            validCrease = false;
            creaseDirection = Vector3.zero;
            if (characterStable && currentStable && previousStable)
            {
                return;
            }

            Vector3 tmpCreaseDirection = Vector3.Cross(currentNormal, previousNormal).normalized;
            if (tmpCreaseDirection.sqrMagnitude <= 0f || Vector3.Dot(currentNormal, previousNormal) >= 0.999f)
            {
                return;
            }

            // 获取当前法线在折线平面上的投影
            Vector3 normalA = Vector3.ProjectOnPlane(currentNormal, tmpCreaseDirection).normalized;
            // 获取上一帧法线在折线平面上的投影
            Vector3 normalB = Vector3.ProjectOnPlane(previousNormal, tmpCreaseDirection).normalized;
            // 获取上一帧速度在折线平面上的投影
            Vector3 enteringVelocity = Vector3.ProjectOnPlane(previousVelocity, tmpCreaseDirection).normalized;
            // 获取两个法线之间的夹角
            float dotPlanes = Vector3.Dot(normalA, normalB);
            if (dotPlanes <= Vector3.Dot(-enteringVelocity, normalA) + 0.001f && dotPlanes <= Vector3.Dot(-enteringVelocity, normalB) + 0.001f)
            {
                validCrease = true;
                creaseDirection = Vector3.Dot(tmpCreaseDirection, currentVelocity) < 0f ? -tmpCreaseDirection : tmpCreaseDirection;
            }
        }


        /// <summary>
        /// 过滤碰撞体
        /// </summary>
        /// <param name="colliders">碰撞体数组</param>
        /// <param name="count">碰撞体数量</param>
        /// <returns>有效的碰撞体数量</returns>
        private int FilterColliders(Collider[] colliders, int count)
        {
            int validCount = count;
            // 从后往前移除无效碰撞体
            for (int i = count - 1; i >= 0; i--)
            {
                if (!context.IsColliderValidForCollisions(colliders[i]))
                {
                    validCount--;
                    // 用尾部有效项填当前洞
                    if (i < validCount)
                    {
                        colliders[i] = colliders[validCount];
                    }
                }
            }
            return validCount;
        }

        /// <summary>
        /// 过滤碰撞
        /// </summary>
        /// <param name="hits">碰撞数组</param>
        /// <param name="count">碰撞数</param>
        /// <param name="backstep">后退距离</param>
        /// <param name="closestHit">最近的碰撞</param>
        /// <returns>有效的碰撞数</returns>
        private int FilterHits(RaycastHit[] hits, int count, float backstep, out RaycastHit closestHit)
        {
            closestHit = default;
            float closestDistance = Mathf.Infinity;
            int validCount = count;

            // 从后往前遍历碰撞
            for (int i = count - 1; i >= 0; i--)
            {
                // 减去后退距离
                hits[i].distance -= backstep;
                // 如果碰撞距离小于等于0 或碰撞体无效 则视为无效
                if (hits[i].distance <= 0f || !context.IsColliderValidForCollisions(hits[i].collider))
                {
                    validCount--;

                    // 将有效的移动到当前索引位置
                    if (i < validCount)
                    {
                        hits[i] = hits[validCount];
                    }
                }
                // 如果碰撞距离小于最近的碰撞距离 则更新最近的碰撞距离和信息
                else if (hits[i].distance < closestDistance)
                {
                    closestDistance = hits[i].distance;
                    closestHit = hits[i];
                }
            }

            // 返回有效的碰撞数
            return validCount;
        }

        /// <summary>
        /// 记录重叠信息
        /// </summary>
        /// <param name="normal">法线</param>
        /// <param name="collider">碰撞体</param>
        private void RememberOverlap(Vector3 normal, Collider collider)
        {
            if (context.OverlapsCount >= context.Overlaps.Length)
                return;

            // 创建重叠信息的数组 信息+1
            context.Overlaps[context.OverlapsCount] = new OverlapResult(normal, collider);
            context.OverlapsCount++;
        }
    }
}
