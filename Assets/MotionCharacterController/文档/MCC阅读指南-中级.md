# MCC 阅读指南（中级）

阅读顺序：**内核（难度递增）→ 移动平台 → 多角色 → 网络同步**。

## 怎么跳转（重要）

Cursor 对 Markdown 链接支持不稳。本文采用两种写法：

1. **可点链接**：相对本文档的路径（不要用 `/Assets` 开头，Windows 会当成 `C:\Assets`）
2. **保底**：链接文字里已写 `文件名:行号` → 按 `Ctrl+P` 输入文件名 → 打开后 `Ctrl+G` 输入行号

单角色本地内核够用时，可先不看后三章。

---

## 总览

| 阶段 | # | 方面 | 难度 | 一句话 |
|---|---|---|:---:|---|
| **内核** | K1 | 形状与查询 | ★1 | 用什么形状问世界 |
| | K2 | 瞬时位姿 | ★1 | 算完再提交 Transform |
| | K3 | 数值安全 | ★1 | NaN / 微抖 / 超迭代 |
| | K4 | 玩法速度 | ★2 | 手感在 IMcc 不在电机 |
| | K5 | 回调过滤 | ★2 | 玩法插手碰谁、稳不稳 |
| | K6 | 接地 | ★3 | 「碰到」≠「站得住」 |
| | K7 | 碰撞移动 | ★4 | 速度 → 不穿模位移 |
| | K8 | 台阶与边缘 | ★4 | 小坎能上 悬崖别硬吸 |
| | K9 | 墙角折线 | ★5 | 两面墙怎么滑/卡死 |
| | K10 | 动态刚体 | ★5 | 推箱子质量比 |
| **扩展** | E1 | 移动平台 | ★5 | 台与人谁先 Commit |
| | E2 | 多角色批处理 | ★4 | 同帧同一模拟顺序 |
| | E3 | 网络同步 | ★6 | 状态袋 / 谁权威 / 回滚 |

内核主序：Phase1 解重叠+接地 → Phase2 旋转+速度+Move。  
整帧入口：[MccSystem.cs:55 Simulate](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs)

---

# 第一部分：内核（难度递增）

## K1. 形状与查询 ★1

**要解决什么**  
角色有体积。Cast 几何必须与最终站位一致。

**MCC 怎么解决**

- 定尺寸：[MccConfig.Field.cs:10](../Scripts/Core/Config/MccConfig.Field.cs) → [MotionCC.cs:250 ValidateData](../Scripts/Core/Runtime/Chontroller/MotionCC.cs) → [MotionCC.Function.cs:149 SetCapsuleDimensions](../Scripts/Core/Runtime/Chontroller/MotionCC.Function.cs)
- 端点缓存：[MccMotorContext.cs:155 RefreshCapsuleData](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs) / [MccMotorContext.cs:289 GetCapsuleBottomHemiAt](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs)
- 安全距：[MccConfig.Constant.cs:10 COLLISION_OFFSET](../Scripts/Core/Config/MccConfig.Constant.cs) / [MccConfig.Constant.cs:16 GROUND_BACKSTEP](../Scripts/Core/Config/MccConfig.Constant.cs)
- 层掩码：[MccMotorContext.cs:182 RefreshCollidableLayers](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs)
- 查询：[CollisionSolver.cs:372 Overlap](../Scripts/Core/Solvers/CollisionSolver.cs) / [CollisionSolver.cs:411 Sweep](../Scripts/Core/Solvers/CollisionSolver.cs)  
  地面：[GroundSolver.cs:148 CharacterGroundSweep](../Scripts/Core/Solvers/GroundSolver.cs)

---

## K2. 瞬时位姿 ★1

**要解决什么**  
每步改 Transform 会污染查询顺序，插值也做不干净。

**MCC 怎么解决**

- 字段：[MccMotorContext.cs:21 TransientPosition](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs) / [MccMotorContext.cs:27 TransientRotation](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs)
- 帧初：[MccMotorContext.cs:137 BeginSimulation](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs)
- 插值起点：[MotionCC.cs:109 PreSimulationTick](../Scripts/Core/Runtime/Chontroller/MotionCC.cs)
- Solver 只写 Transient：[CollisionSolver.cs:171 Move 末尾](../Scripts/Core/Solvers/CollisionSolver.cs)
- 提交：[MotionCC.cs:208 CommitSimulation](../Scripts/Core/Runtime/Chontroller/MotionCC.cs) → [MotionCC.cs:224 InterpolationUpdate](../Scripts/Core/Runtime/Chontroller/MotionCC.cs)

---

## K3. 数值安全 ★1

**要解决什么**  
浮点误差、死循环迭代、NaN 会毁掉整帧。

**MCC 怎么解决**

- [MccMotorContext.cs:202 SanitizeVelocity](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs)
- 微速度归零：[MotionCC.cs:181 MIN_VELOCITY](../Scripts/Core/Runtime/Chontroller/MotionCC.cs)
- 超迭代兜底：[CollisionSolver.cs:156](../Scripts/Core/Solvers/CollisionSolver.cs)
- 平面约束：[CollisionSolver.cs:96 hasPlanarConstraint](../Scripts/Core/Solvers/CollisionSolver.cs)
- 调试字段：[MccMotorContext.cs:110 Debug*](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs)

---

## K4. 玩法速度 ★2

**要解决什么**  
走/跳/重力是手感，不该焊进碰撞内核。

**MCC 怎么解决**

- 接口：[IMcc.cs:37 UpdateVelocity](../Scripts/Core/Interfaces/IMcc.cs) / [IMcc.cs:30 UpdateRotation](../Scripts/Core/Interfaces/IMcc.cs)
- 调用：[MotionCC.cs:165 UpdatePhase2](../Scripts/Core/Runtime/Chontroller/MotionCC.cs)
- 示例：[PlayerController.cs:49 UpdateVelocity](../Scripts/MccStandPlayerController/PlayerController.cs)  
  跳：[PlayerController.cs:84](../Scripts/MccStandPlayerController/PlayerController.cs) → [MotionCC.Function.cs:56 ForceUnground](../Scripts/Core/Runtime/Chontroller/MotionCC.Function.cs)
- 输入：[MotionCC.cs:99 Update](../Scripts/Core/Runtime/Chontroller/MotionCC.cs) → [PlayerController.cs:24 InputVectorUpdate](../Scripts/MccStandPlayerController/PlayerController.cs)
- 参数：[MccConfig.Field.cs:16 移动跳跃](../Scripts/Core/Config/MccConfig.Field.cs)

---

## K5. 回调过滤 ★2

**要解决什么**  
内核不知「碰谁」「算不算自定义地面」。

**MCC 怎么解决**

- 名单：[IMcc.cs](../Scripts/Core/Interfaces/IMcc.cs)
- 过滤：[MccMotorContext.cs:231 IsColliderValid](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs) → [IMcc.cs:59](../Scripts/Core/Interfaces/IMcc.cs)
- 回调：[GroundSolver.cs:124 OnGroundHit](../Scripts/Core/Solvers/GroundSolver.cs) / [CollisionSolver.cs:310 OnMovementHit](../Scripts/Core/Solvers/CollisionSolver.cs) / [CollisionSolver.cs:347 Discrete](../Scripts/Core/Solvers/CollisionSolver.cs)
- 稳定性最后一票：[HitStabilityEvaluator.cs:74](../Scripts/Core/Solvers/HitStabilityEvaluator.cs)

---

## K6. 接地 ★3

**要解决什么**  
碰到 ≠ 站得住；要管坡度、探测距、吸附、跳后别立刻吸回。

**MCC 怎么解决**

- 入口：[MotionCC.cs:149](../Scripts/Core/Runtime/Chontroller/MotionCC.cs) → [GroundSolver.cs:25 UpdateGrounding](../Scripts/Core/Solvers/GroundSolver.cs)
- 强制离地抬高：[GroundSolver.cs:34](../Scripts/Core/Solvers/GroundSolver.cs)
- 探测距：[GroundSolver.cs:179 GetSelectedGroundProbeDistance](../Scripts/Core/Solvers/GroundSolver.cs)
- 向下扫吸附：[GroundSolver.cs:75 ProbeGround](../Scripts/Core/Solvers/GroundSolver.cs)
- 坡度：[MccMotorContext.cs:221 IsStableOnNormal](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs) / [MccConfig.Field.cs:38](../Scripts/Core/Config/MccConfig.Field.cs)
- 边缘禁吸：[LedgeSolver.cs:95](../Scripts/Core/Solvers/LedgeSolver.cs)（细节见 K8）
- 落地刹竖直：[GroundSolver.cs:52](../Scripts/Core/Solvers/GroundSolver.cs)
- 报告：[MccTypes.cs:78 CharacterGroundingReport](../Scripts/Core/Runtime/Chontroller/MccTypes.cs)

---

## K7. 碰撞移动 ★4

**要解决什么**  
`BaseVelocity` 如何变成不穿墙的位移（一次 Cast 不够）。

**MCC 怎么解决**

- 调用：[MotionCC.cs:190 → Move](../Scripts/Core/Runtime/Chontroller/MotionCC.cs)
- 主循环：[CollisionSolver.cs:89 Move](../Scripts/Core/Solvers/CollisionSolver.cs)  
  → [CollisionSolver.cs:182 TryFindNextMovementHit](../Scripts/Core/Solvers/CollisionSolver.cs) → 上台阶或滑动 → 超迭代兜底
- 解重叠：[CollisionSolver.cs:31 ResolveInitialOverlaps](../Scripts/Core/Solvers/CollisionSolver.cs)
- 障碍法线：[CollisionSolver.cs:480 GetObstructionNormal](../Scripts/Core/Solvers/CollisionSolver.cs)
- 投影：[CollisionSolver.cs:624 ProjectVelocity](../Scripts/Core/Solvers/CollisionSolver.cs)
- 配置：[MccConfig.Field.cs:85 求解安全](../Scripts/Core/Config/MccConfig.Field.cs)

---

## K8. 台阶与边缘 ★4

**要解决什么**  
小坎能上；半脚悬空别硬吸；「站」与「蹭」同一套稳不稳。

**MCC 怎么解决**

- 共用入口：[HitStabilityEvaluator.cs:24 Evaluate](../Scripts/Core/Solvers/HitStabilityEvaluator.cs)
- 边缘：[LedgeSolver.cs:21 ProcessLedgeStability](../Scripts/Core/Solvers/LedgeSolver.cs) / [LedgeSolver.cs:95](../Scripts/Core/Solvers/LedgeSolver.cs)
- 台阶探测：[StepSolver.cs:26 DetectSteps](../Scripts/Core/Solvers/StepSolver.cs)（仅不稳定）
- 抬脚：[CollisionSolver.cs:269 TryResolveStep](../Scripts/Core/Solvers/CollisionSolver.cs) → [StepSolver.cs:84 TryStep](../Scripts/Core/Solvers/StepSolver.cs)
- 调用方：接地 [GroundSolver.cs:95](../Scripts/Core/Solvers/GroundSolver.cs) / 移动 [CollisionSolver.cs:137](../Scripts/Core/Solvers/CollisionSolver.cs)
- 配置：[MccConfig.Field.cs:48 台阶](../Scripts/Core/Config/MccConfig.Field.cs) / [MccConfig.Field.cs:58 边缘](../Scripts/Core/Config/MccConfig.Field.cs)

---

## K9. 墙角折线 ★5

**要解决什么**  
一面墙滑动；两面墙夹角要沿折线或卡死，否则抖/穿。

**MCC 怎么解决**

- 状态：[MccTypes.cs:38 MovementSweepState](../Scripts/Core/Runtime/Chontroller/MccTypes.cs)
- 状态机：[CollisionSolver.cs:559 HandleVelocityProjection](../Scripts/Core/Solvers/CollisionSolver.cs)  
  → [CollisionSolver.cs:668 EvaluateCrease](../Scripts/Core/Solvers/CollisionSolver.cs)
- 调试：[MccMotorContext.cs:112 DebugLastSweepState](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs)，菜单 `Tools/MCC/调试器`

到这里，**单角色本地「走得稳」的内核主链就齐了**。

---

## K10. 动态刚体 ★5

**要解决什么**  
运动学角色推箱子不能靠默认刚体互撞。

**MCC 怎么解决**

- 配置：[MccConfig.Field.cs:72 InteractionType](../Scripts/Core/Config/MccConfig.Field.cs) / [MccConfig.Field.cs:75 Mass](../Scripts/Core/Config/MccConfig.Field.cs)
- 滑动记账：[CollisionSolver.cs:312](../Scripts/Core/Solvers/CollisionSolver.cs) → [RigidbodySolver.cs:36 StoreHit](../Scripts/Core/Solvers/RigidbodySolver.cs)
- 帧末：[RigidbodySolver.cs:58 ProcessVelocityForHits](../Scripts/Core/Solvers/RigidbodySolver.cs) / [RigidbodySolver.cs:100 ResolveMassRatio](../Scripts/Core/Solvers/RigidbodySolver.cs)
- 清空：[MotionCC.cs:126 Clear](../Scripts/Core/Runtime/Chontroller/MotionCC.cs)
- Kinematic 过滤：[MccMotorContext.cs:248](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs)

---

# 第二部分：移动平台

## E1. 移动平台 ★5

**要解决什么**  
人跟着动台走；同帧提交顺序错会穿台/甩飞。

**MCC 怎么解决**

```text
平台 VelocityUpdate（只算速度）
  → 角色 Phase1 附着 Move
  → 平台 CommitMovement
  → 角色 Phase2 自己走
```

- 平台算速：[MccPhysicsMover.cs:72 VelocityUpdate](../Scripts/Core/Runtime/Mover%26System/MccPhysicsMover.cs) / [IMover.cs:7](../Scripts/Core/Interfaces/IMover.cs)
- 附着：[PlatformSolver.cs:28 UpdateAttachment](../Scripts/Core/Solvers/PlatformSolver.cs)  
  [PlatformSolver.cs:96](../Scripts/Core/Solvers/PlatformSolver.cs) → [PlatformSolver.cs:116](../Scripts/Core/Solvers/PlatformSolver.cs) → [PlatformSolver.cs:71 Move](../Scripts/Core/Solvers/PlatformSolver.cs)
- 动量：[PlatformSolver.cs:57](../Scripts/Core/Solvers/PlatformSolver.cs)
- 平台落地：[MccPhysicsMover.cs:107 CommitMovement](../Scripts/Core/Runtime/Mover%26System/MccPhysicsMover.cs)（夹在 [MccSystem.cs:75](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs)）
- 忽略自身平台：[MccMotorContext.cs:242](../Scripts/Core/Runtime/Chontroller/MccMotorContext.cs)

依赖 K6 接地 + K7 Move。

---

# 第三部分：多角色

## E2. 多角色批处理 ★4

**要解决什么**  
多个角色/平台各自 FixedUpdate 顺序不定 → 同帧结果不稳定。

**MCC 怎么解决**

- 单入口：[MccSystem.cs:55 Simulate](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs)  
  全员 PreSim → 全平台速度 → 全员 Phase1 → 全平台 Commit → 全员 Phase2 → 全员 Commit
- 驱动：[MccSystem.cs:27 FixedUpdate](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs) + [MccSystem.cs:14 AutoSimulation](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs)
- 注册表：[MccSystem.cs:114 RegisterCharacter](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs) / [MccSystem.cs:139 RegisterMover](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs)
- 手动步进：[MccConfig.Field.cs:99 autoSimulation](../Scripts/Core/Config/MccConfig.Field.cs) → [MccSystem.cs:160 Sync](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs)
- 插值：[MccSystem.cs:176](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs) / [MccSystem.cs:37 LateUpdate](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs)

尚未内建角色互推/避让。

---

# 第四部分：网络同步

## E3. 网络同步 ★6

**要解决什么**  
谁权威、传什么、延迟与回滚。

**MCC 已具备**

- 角色快照：[MccTypes.cs:59 MotionCharacterMotorState](../Scripts/Core/Runtime/Chontroller/MccTypes.cs)
- 读写：[MotionCC.Function.cs:19 GetState](../Scripts/Core/Runtime/Chontroller/MotionCC.Function.cs) / [MotionCC.Function.cs:40 ApplyState](../Scripts/Core/Runtime/Chontroller/MotionCC.Function.cs)
- 平台快照：[MccPhysicsMover.cs:168](../Scripts/Core/Runtime/Mover%26System/MccPhysicsMover.cs) / [GetState:149](../Scripts/Core/Runtime/Mover%26System/MccPhysicsMover.cs) / [ApplyState:160](../Scripts/Core/Runtime/Mover%26System/MccPhysicsMover.cs)
- 手动 Simulate：[MccSystem.cs:55](../Scripts/Core/Runtime/Mover%26System/MccSystem.cs)

**需上层网络层做**

| 问题 | 常见做法 |
|---|---|
| 权威端 | 服务器/主机跑 Simulate；客户端预测 |
| 传什么 | 输入 + 定期 State；或两端同逻辑只传输入 |
| 附着刚体 | Rigidbody 引用 → 网络 ID |
| 回滚 | 历史 State → ApplyState → 重跑 Simulate |
| 远端显示 | 快照插值 |
| 平台 | 与角色同 tick 打包（E1+E2） |

---

## 推荐阅读路径

```text
内核：K1 → K2 → K3 → K4 → K6 → K7 → K5 → K8 → K9 →（可选）K10
扩展：E1 → E2 → E3
```

调试：`Tools/MCC/调试器`

---

## 代码量

| 范围 | 约行 |
|---|---:|
| 最小自制运动学原型 | 400～800 |
| MCC Core | ~3100 |
| + 示例 | ~4100 |
| Editor | ~550 |
