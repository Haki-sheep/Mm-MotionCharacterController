# MCC 阅读指南（中级 / 读过 KCC）

面向：以前读懂过 KCC（Phase2 除外），隔了两三个月想快速捡回，并对照 MCC 源码。

预计总时长：**3～5 天**（每天 2～3 小时）。你不是从零学，是「地图换皮 + 补 Phase2」。

---

## 0. 先建立一张对照表（30 分钟）

| KCC | MCC | 说明 |
|---|---|---|
| `KinematicCharacterSystem` | `MccSystem` | 批处理入口 四步顺序同款 |
| `KinematicCharacterMotor` | `MotionCC` + `MccMotorContext` | 电机拆成入口 + 状态袋 |
| Motor 内一坨方法 | `Solvers/*` | 按职责拆文件 |
| `ICharacterController` | `IMcc` | 多一个 `InputVectorUpdate` |
| `PhysicsMover` / `IMoverController` | `MccPhysicsMover` / `IMover` | 移动平台 |
| `InternalCharacterMove` | `CollisionSolver.Move` | Phase2 核心 你以前欠的就是这块 |

MCC **不是**包一层 KCC，是同算法重写。读的时候只对照思想，不要两边混着改。

---

## 1. 第 0 天：唤醒记忆（1～2 小时）

只看顺序，不看细节：

1. [`MccSystem.cs`](../Scripts/Core/Runtime/Mover&System/MccSystem.cs) 的 `Simulate`
2. [`MotionCC.cs`](../Scripts/Core/Runtime/Chontroller/MotionCC.cs) 的 `UpdatePhase1` / `UpdatePhase2`
3. [`IMcc.cs`](../Scripts/Core/Interfaces/IMcc.cs) 回调名单

你应能默写：

```text
PreSimulationTick
→ Mover.VelocityUpdate
→ Phase1（解重叠 / 接地 / 跟平台）
→ Mover.CommitMovement
→ Phase2（旋转 / 速度 / 碰撞移动）
→ CommitSimulation
```

若这四步还能脱口而出，说明 KCC 记忆还在，直接进第 2 天。

---

## 2. 第 1 天：Phase1 快速对位（2～3 小时）

按调用链读，不要按文件夹乱逛：

| 顺序 | 文件 | 对应你以前的 KCC 印象 |
|---|---|---|
| 1 | `CollisionSolver.ResolveInitialOverlaps` | ComputePenetration 解重叠 |
| 2 | `GroundSolver.UpdateGrounding` / `ProbeGround` | 迭代向下扫 + 吸附 |
| 3 | `HitStabilityEvaluator` + `LedgeSolver` | 稳不稳 / 边缘 |
| 4 | `StepSolver.DetectSteps` | 不稳定命中才试台阶 |
| 5 | `PlatformSolver.UpdateAttachment` | AttachedRigidbody 速度 |

读完自问：

- 未接地时 `groundProbeDistance` 干什么？已接地为什么改用 `max(radius, maxStepHeight)`？
- `ForceUnground` 后为什么先抬一点再清接地？

---

## 3. 第 2～3 天：补 Phase2（重点 4～6 小时）

这是你以前空的部分。只盯 [`CollisionSolver.Move`](../Scripts/Core/Solvers/CollisionSolver.cs)：

主循环四步（现在就是按人话写的）：

```text
1 TryFindNextMovementHit   先重叠(可选) 再 CapsuleCast
2 走到命中点前（扣 CollisionOffset）
3 上台阶 or ResolveSlide（速度投影）
4 超迭代则杀速度/剩余位移
```

再读投影链：

- `HandleVelocityProjection`
- `EvaluateCrease` / `MovementSweepState`
- `GetObstructionNormal`

对照场景理解：

| SweepState | 人话 |
|---|---|
| `Initial` | 第一次撞 |
| `AfterFirstHit` | 已经滑过一面 |
| `FoundBlockingCrease` | 两面夹成折线 沿折线走 |
| `FoundBlockingCorner` | 真卡死 速度清零 |

建议边读边开 `Tools/MCC/调试器`，贴墙角跑，看 **扫掠状态(SweepState)** 怎么跳。

Phase2 后半截扫一眼即可：

- `IMcc.UpdateRotation` / `UpdateVelocity`（玩法层）
- `RigidbodySolver.ProcessVelocityForHits`（推箱子质量比）
- `ProcessDiscreteCollisionEvents`

---

## 4. 第 4 天：平台 + 系统开关（2 小时）

- `MccPhysicsMover`：先算速度 后 Commit 位姿
- 为什么平台 Commit 夹在 Phase1 和 Phase2 中间（你以前应该懂，现在只是换文件名）
- `autoSimulation` / `MccSystem.Simulate`：关自动后手动步进（调试器用）

---

## 5. 第 5 天：改一处验证懂了（半天）

任选一个小改动手，证明不是「看过」而是「握住」：

1. 调大 `maxStableSlopeAngle` 看陡坡能不能站
2. 关 `checkMovementInitialOverlaps` 贴墙高速蹭 对比穿模感
3. `SimulatedDynamic` + 改 `simulatedCharacterMass` 推轻重箱子

改完行为对得上预期，中级阅读目标就算达成。

---

## 推荐阅读顺序（抄这份清单）

```text
1 MccSystem.Simulate
2 MotionCC Phase1 / Phase2
3 IMcc
4 MccMotorContext（扫字段名 别深抠）
5 GroundSolver → HitStabilityEvaluator → LedgeSolver → StepSolver
6 PlatformSolver
7 CollisionSolver.Move（Phase2 主菜）
8 RigidbodySolver
9 PlayerController（看玩法怎么写速度）
10 Tools/MCC/调试器 对着跑
```

---

## 刻意不要先读的

- `MotionCC.Function.cs` 整文件 API（用到再查）
- Editor 脚本实现细节
- KCC 源码并排逐行 diff（浪费时间 算法同构即可）

---

## 卡壳时用调试器看什么

| 现象 | 看字段 |
|---|---|
| 脚下发飘 | 稳定站立(IsStableOnGround) / 地面探测距离(GroundProbeDistance) |
| 卡墙角抖 | 扫掠状态(SweepState) / 移动是否完成(MoveCompleted) |
| 推箱子没力 | 刚体交互类型 + 模拟角色质量(simulatedCharacterMass) |
| 平台上滑 | 附着刚体(AttachedRigidbody) / 附着速度 |

菜单：`Tools/MCC/调试器`
