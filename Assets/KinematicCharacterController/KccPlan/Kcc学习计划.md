# KinematicCharacterController 源码解读计划

## 关于本计划

这是一份为初级程序员设计的 KCC 源码学习路径。目标是让你彻底理解 KinematicCharacterMotor 的每一个设计决策，不只是"会用"，而是"能改"。

本计划按「你实际需要理解的深度」分层，每个阶段对应源码中的具体职责模块和关键函数。每个阶段结束时，你都应该能回答该阶段的核心问题。

> **文件结构说明**：KCC 核心代码按职责分为以下文件夹：
>
> * `Core/System&WholeSetting/` — 系统调度层（System 单例 + 全局配置）
> * `Core/Motor/` — 物理核（Motor 主文件 + 6 个分片文件）
> * `Core/Mover/` — 移动平台（PhysicsMover + IMoverController 接口）

***

## 第一阶段：建立全局地图

### 目标

在动手读代码之前，先用最少的精力建立一张"思维地图"，知道每个文件、每个方法各负责什么。

### 任务

阅读以下三个文件的最前面 150 行（只看结构，不深究细节）：

文件一：`Assets/KinematicCharacterController/Core/System&WholeSetting/KinematicCharacterSystem.cs`

* 行 1-120：看静态字段和 `EnsureCreation()`，理解这是一个单例管理所有 Motor 和 Mover
* 行 129-158：看 `FixedUpdate` 和 `LateUpdate`，记住模拟顺序
* 行 198-234：看 `Simulate()` 方法，读懂四步顺序

文件二：`Assets/KinematicCharacterController/Core/Motor/ICharacterController.cs`

* 行 1-62：把接口的 10 个方法名和参数抄一遍，不需要懂，只需要"脸熟"

文件三：`Assets/KinematicCharacterController/Core/Motor/KinematicCharacterMotor.cs`

* 行 1-220：看枚举、struct、数据结构，知道有哪些数据类型
* 行 220-500：快速浏览所有 Inspector 配置项，不需要记，只需要知道"这些是公开配置"

### 完成后能回答的问题

* KCC 有哪几个核心类？各自职责是什么？
  KCC 有 5 个核心类/文件：
  * `KinematicCharacterSystem` — 执行调度层，单例驱动模拟循环
  * `KinematicCharacterMotor` — 物理核，2170 行，包含所有物理计算
  * `ICharacterController` — 角色逻辑接口，10 个方法由用户实现
  * `PhysicsMover` — 物理移动器，管理移动平台的位移和速度
  * `IMoverController` — 移动器控制接口，由用户实现控制平台运动
* `Simulate()` 方法的执行顺序是什么？
  ```
  1. 更新所有 PhysicsMover 的速度（VelocityUpdate）
  2. 更新所有 Motor 的 Phase1（地面探测 + 刚体绑定）
  3. 更新所有 PhysicsMover 的位移（移动到 TransientPosition）
  4. 更新所有 Motor 的 Phase2（旋转 + 速度求解 + 碰撞移动）
  ```
* `ICharacterController` 接口有几个方法？它们分别在哪两个阶段被调用？
  共 10 个方法。调用规律如下：

  | 方法                             | 调用时机                 |
  | ------------------------------ | -------------------- |
  | `BeforeCharacterUpdate`        | Phase1 最开始，行 271     |
  | `PostGroundingUpdate`          | Phase1，地面探测完成后，行 417 |
  | `UpdateRotation`               | Phase2 最开始，行 514     |
  | `UpdateVelocity`               | Phase2，速度计算时，行 637   |
  | `AfterCharacterUpdate`         | Phase2 最后，行 682      |
  | `IsColliderValidForCollisions` | 碰撞检测内部过滤，行 1490      |
  | `OnGroundHit`                  | 地面探测命中时，行 793        |
  | `OnMovementHit`                | 移动碰撞命中时，行 1029       |
  | `ProcessHitStabilityReport`    | 稳定性评估之后，行 1591       |
  | `OnDiscreteCollisionDetected`  | 离散碰撞检测，行 678         |

### 给下一个 AI 窗口的提示词

```
请帮助我学习 KinematicCharacterController 的源码。

我已经浏览过文件结构，知道有以下核心文件：
- KinematicCharacterSystem.cs（执行调度层，312行）
- KinematicCharacterMotor.cs（物理核��2170行）
- ICharacterController.cs（角色逻辑接口）
- PhysicsMover.cs（物理移动器）
- IMoverController.cs（移动器控制接口）

现在进入第一阶段：我想建立全局地图。请按以下顺序带我过一遍：
1. KinematicCharacterSystem.Simulate() 的四步执行顺序，用流程图或伪代码表示
2. KinematicCharacterMotor 有哪些核心数据结构（struct），它们分别装什么数据
3. ICharacterController 接口的10个方法分别在哪一行被 Motor 调用

我的当前状态：刚浏览完代码框架，对物理核的内部原理还不熟悉。
```

***

## 第二阶段：吃透执行调度层

### 目标

彻底理解 `KinematicCharacterSystem` 如何驱动整个模拟循环，以及插值系统的工作原理。

### 任务

详细阅读 `KinematicCharacterSystem.cs`（全文 312 行）。

重点关注以下四个问题：

问题一：为什么需要插值系统？

* `PreSimulationInterpolationUpdate()`（行 164）把角色位置记录到 `InitialTickPosition`，然后把 Transform 设回这个位置
* `LateUpdate` 里的 `CustomInterpolationUpdate()`（行 276）从 `InitialTickPosition` lerp 到 `TransientPosition`
* 理解：FixedUpdate 里物理算完了，但渲染帧不一定对齐，所以需要在两帧之间再平滑一次

问题二：`Simulate()` 的四步为什么是这个顺序？

* 第1步 Mover 更新速度 → 第2步 Motor Phase1 探测 → 第3步 Mover 移动 → 第4步 Motor Phase2 碰撞
* 核心原因：Mover 速度要先算出来，这样 Motor 在 Phase1 探测地面时，AttachedRigidbody 的速度是最新的

问题三：`CharacterMotors` 和 `PhysicsMovers` 两个静态列表何时写入、何时清空？

* Motor/Mover 的 `OnEnable` 注册，`OnDisable` 注销
* 场景切换时不会残留

问题四：什么时候 `AutoSimulation = false`？

* 用于手动物理步进（如慢动作回放、帧精确调试）

### 完成后能回答的问题

* 解释"模拟帧（FixedUpdate）和渲染帧（LateUpdate）之间的插值"机制
  通过fixedupdate计算出的速度和旋转,在LateUpdate中进行插值,插值后的位置和旋转被应用到角色身上
  并且如果不启用插值 那么角色会产生抖动

* 如果我想禁用插值，改哪个配置项
  在Setting里面的Settings.Interpolate = false

* `Simulate()` 的四步顺序能否颠倒？为什么？
  不能颠倒,因为Mover的速度要先算出来,这样Motor在Phase1探测地面时,AttachedRigidbody的速度是最新的
  虽然我不知道AttachedRigidbody是什么,但是我知道它是一个物理刚体,它需要速度来计算碰撞

### 给下一个 AI 窗口的提示词

```
请帮助我深入理解 KinematicCharacterSystem.cs 的执行调度机制。

我已经知道 Simulate() 的四步顺序，现在想深入理解以下几个点：

1. 插值系统的设计意图：
   PreSimulationInterpolationUpdate 把角色设回 InitialTickPosition，
   LateUpdate 的 CustomInterpolationUpdate 从这里 lerp 到 TransientPosition。
   请解释：为什么不能直接在 FixedUpdate 里把 Transform 设到最终位置？
   什么场景下插值会产生视觉 bug？

2. Simulate() 的执行顺序为什么必须是：
   Mover速度 -> Motor Phase1 -> Mover移动 -> Motor Phase2
   如果把 Mover 移动放到 Phase2 之后会发生什么？

3. AutoSimulation 什么时候应该关闭？请举例一个具体使用场景。

4. 我的游戏是多玩家 FPS，网络需要服务器 authoritative 物理。
   这种场景下 KCC 的插值系统是否仍���适用？需要做哪些修改？

我的背景：初级程序员，读过第一阶段的文件结构，现在想理解调度层的设计意图。
```

***

## 第三阶段：理解地面检测机制

### 目标

理解 `ProbeGround()` 这一整套地面检测链路，这是 KCC 区别于 Unity CharacterController 的核心——它不只是单次射线检测，而是一个智能的、迭代的地面探测系统。

### 任务

重点阅读下面这些文件和函数，不需要再盯着具体行号：

* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.cs`
  * 重点看 `UpdatePhase1()` 里“地面探测 / AttachedRigidbody 绑定 / PostGroundingUpdate”这部分流程
* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.Grounding.cs`
  * 重点看 `ProbeGround()` 全文，这是第三阶段最核心的函数
  * 重点看 `EvaluateHitStability()` 里 ledge 检测、稳定性判定、台阶入口这几部分
  * 先快速看 `MustUnground()`、`IsStableOnNormal()`、`IsStableWithSpecialCases()`，理解它们分别在给谁做辅助判断

### 推荐阅读顺序

1. 先看 `UpdatePhase1()`：搞清楚它在什么时候调用 `ProbeGround()`，以及地面检测结果后面会影响什么
2. 再看 `ProbeGround()`：这是本阶段主角
3. 最后看 `EvaluateHitStability()`：理解 KCC 怎么判断“这是不是稳定地面”

### 逐步拆解

#### 第一步：理解地面探测为什么不是一次 Raycast

Unity 的 CharacterController 用的是单次 `Raycast`，问题在于：

* 斜坡上 raycast 只能返回一个点
* 台阶边缘 raycast 会穿过空气
* 凹凸不平的地面 raycast 只能摸到一个顶点

KCC 用的是 `CharacterGroundSweep`（内部基于 CapsuleCast）：

* Capsule 有半径，能感知水平方向的坡度变化
* CapsuleCast 返回的第一个 hit 不一定是最重要的（因为可能是墙角或凹槽）
* `ProbeGround()` 里有一个 while 循环，最多迭代 `MaxGroundingSweepIterations`

#### 第二步：理解 `ProbeGround()` 的 while 循环

看 `KinematicCharacterMotor.Grounding.cs` 里的 `ProbeGround()`，重点盯住这几段：

1. 循环条件：`groundProbeDistanceRemaining > 0`、`groundSweepsMade <= MaxGroundingSweepIterations`
2. `CharacterGroundSweep(...)` 成功后的处理
3. `groundHitStabilityReport.IsStable` 为 true 时的“吸附地面”逻辑
4. `groundHitStabilityReport.IsStable` 为 false 时的“沿表面重新定向继续探测”逻辑

你可以把它理解成：

```text
每次循环：
1. 做一次向下地面探测（内部是 CapsuleCast）
2. 如果 hit 且稳定：
   - 填充 GroundingStatus
   - 如果允许 snapping，把位置吸附到地面
   - 触发 OnGroundHit
   - 结束循环
3. 如果 hit 但不稳定：
   - 沿当前命中面重新投影探测方向
   - 从新位置继续探测
4. 如果没 hit：结束循环
```

这就是为什么 KCC 在陡坡上能“滑下来”而不是直接卡死。

#### 第三步：理解 `GroundNormal` 和 `Inner/OuterGroundNormal`

这部分要配合 `EvaluateHitStability()` 一起看：

* `GroundNormal`：当前主要命中面的法线
* `InnerGroundNormal`：朝角色内侧探测到的法线
* `OuterGroundNormal`：朝角色外侧探测到的法线

重点理解：

* 为什么只看一个 `GroundNormal` 不够
* 为什么 ledge 检测必须比较“内外两边”的地面情况

#### 第四步：理解 `EvaluateHitStability()` 在做什么

阅读 `EvaluateHitStability()` 时，重点分成 4 段来看：

1. **基础稳定性判断**
   * `IsStableOnNormal(hitNormal)`
   * 这是最基础的“坡度角度是否合法”判断

2. **ledge 检测**
   * 看 `CharacterCollisionsRaycast(...)` 的两次附加探测
   * 看 `LedgeDetected`、`IsOnEmptySideOfLedge`、`DistanceFromLedge`

3. **特殊规则修正**
   * 看 `IsStableWithSpecialCases(...)`
   * 理解为什么“坡度本身稳定”仍然有可能被判定为不稳定

4. **台阶检测入口**
   * 看 `if (StepHandling != StepHandlingMethod.None && !stabilityReport.IsStable)` 这段
   * 这里只需要知道：它会在“不稳定命中”时尝试把它重新解释成“可跨越台阶”

#### 第五步：理解 `GroundSnapping`

在 `ProbeGround()` 里重点看这些内容：

* `groundingReport.SnappingPrevented`
* `probingPosition = ... (groundSweepHit.distance - CollisionOffset)`
* `IsStableWithSpecialCases(...)` 对 Snapping 的影响

你只要理解：

* Snapping 的本质就是“把角色轻轻吸到地面上”
* 不是所有地面命中都允许吸附
* 速度太快、站在悬崖边、地形落差过大时，都会阻止这件事发生

### 完成后能回答的问题

* `CapsuleCast` 相比 `Raycast` 在地面检测上的优势是什么？
* `ProbeGround()` 的 while 循环什么时候会提前退出？
* 为什么 KCC 在斜坡上能“滑下来”而不是卡住？
* `GroundNormal`、`InnerGroundNormal`、`OuterGroundNormal` 分别用于什么场景？
* `EvaluateHitStability()` 为什么不能只靠 `IsStableOnNormal()` 一个判断？

### 给下一个 AI 窗口的提示词

```text
请帮助我深入理解 KCC 的地面检测机制，重点是这些函数：

1. KinematicCharacterMotor.cs 里的 UpdatePhase1（只看地面检测相关流程）
2. KinematicCharacterMotor.Grounding.cs 里的 ProbeGround
3. KinematicCharacterMotor.Grounding.cs 里的 EvaluateHitStability
4. KinematicCharacterMotor.Grounding.cs 里的 IsStableWithSpecialCases

我现在已经知道：
- ProbeGround 不是单次 Raycast，而是迭代地面探测
- EvaluateHitStability 负责判断命中面是否算稳定地面
- ledge 检测需要比较 InnerGroundNormal 和 OuterGroundNormal

但我还没完全想明白下面这些点：

1. GroundProbingBackstepDistance 是干什么的？为什么探测前要回退一点？
2. GroundProbeReboundDistance 在 ProbeGround 的 while 里起什么作用？
3. 为什么命中一个坡度合法的面，最后仍然可能被 IsStableWithSpecialCases 判成不稳定？
4. GroundSnapping 到底是在什么条件下会被阻止？
5. 如果角色高速下落并掠过一个较矮台阶，ProbeGround 有没有可能漏检？

我的背景：初级程序员，已经理解 Simulate 的执行顺序，现在正在学习地面检测。
```

***

## 第四阶段：理解碰撞移动求解器

### 目标

理解 `InternalCharacterMove()` 和 `HandleVelocityProjection()`，这是 KCC 的灵魂——它用 Sweep + 速度投影的迭代解决了“角色穿模”问题。

### 任务

重点阅读下面这些文件和函数：

* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.cs`
  * 重点看 `UpdatePhase2()` 里“旋转更新 / 速度更新 / 调用 InternalCharacterMove / 离散碰撞检测”这部分流程
  * 重点看 `InternalCharacterMove()` 全文
* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.CollisionAndPhysics.cs`
  * 重点看 `InternalHandleVelocityProjection()`
  * 重点看 `HandleVelocityProjection()`
  * 配合看 `GetObstructionNormal()`、`EvaluateCrease()`
* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.Grounding.cs`
  * 只需要回头看 `EvaluateHitStability()` 里“台阶检测入口”那一小段
* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.StepHandling.cs`
  * 只需要知道 `DetectSteps()` / `CheckStepValidity()` 在这里，第四阶段先不用吃太细

### 推荐阅读顺序

1. 先看 `UpdatePhase2()`：搞清楚 `InternalCharacterMove()` 在整个模拟中的位置
2. 再看 `InternalCharacterMove()`：这是第四阶段核心
3. 然后看 `InternalHandleVelocityProjection()`
4. 最后看 `HandleVelocityProjection()` 和 `EvaluateCrease()`

### 逐步拆解

#### 第一步：为什么需要“迭代求解”？

如果你有一个速度向量 V，期望角色从 A 点移动到 B 点（A + Vdt = B），但中间有一堵墙。

简单做法：直接 `transform.position = B`，穿模。

KCC 的做法：

1. 用 CapsuleCast 沿 V 方向探测，得到最近的 hit 点 C
2. 如果有 hit，把角色移到 C（加 `CollisionOffset` 推开）
3. 把剩余速度沿 hit 面法线投影，得到新速度 V'
4. 从当前位置继续探测，直到没有 hit 或迭代次数用完

这叫“滑动碰撞”，核心思想来自经典刚体碰撞求解。

#### 第二步：理解 `InternalCharacterMove()` 的 while 循环结构

阅读 `InternalCharacterMove()` 时，重点盯住这几段：

1. 初始化 `remainingMovementDirection`、`remainingMovementMagnitude`
2. 处理当前 overlaps 的那段逻辑
3. 主 while 循环里“先 Overlap 再 Sweep”的命中流程
4. 命中后如何更新位置、剩余距离、稳定性报告
5. 什么时候调用 `InternalHandleVelocityProjection()`
6. 什么时候直接结束并把剩余移动量一次走完

你可以把它理解成：

```text
while (还有剩余移动 && 迭代次数没超):
    1. 先检查当前位置是否已经重叠
    2. 如果没重叠，再沿剩余方向做 sweep
    3. 如果命中：
       - 先移动到命中点附近
       - 计算命中稳定性
       - 必要时处理台阶
       - 把速度重新投影
       - 继续下一轮
    4. 如果没命中：
       - 直接把剩余距离走完
       - 结束
```

#### 第三步：理解速度投影的三种情况

看 `HandleVelocityProjection()` 时，把它分成 3 种情况：

1. **稳定接地 + 命中稳定面**
   * 速度沿斜面切线重定向
   * 基本不会丢速度

2. **稳定接地 + 命中不稳定面（墙、陡坡）**
   * 速度先考虑地面，再考虑阻挡面
   * 结果通常是“沿墙根滑走”

3. **空中 + 命中任意面**
   * 速度直接沿命中法线投影
   * 这是最直观的“撞到就被挡住”效果

#### 第四步：理解 `InternalHandleVelocityProjection()` 和 `EvaluateCrease()`

这里是第四阶段最容易卡住的地方，重点看：

* `sweepState == MovementSweepState.Initial`
* `sweepState == MovementSweepState.AfterFirstHit`
* `foundCrease`
* `MovementSweepState.FoundBlockingCrease`
* `MovementSweepState.FoundBlockingCorner`

理解目标不是一口气看懂全部数学，而是先搞清楚：

* 第一次撞墙后怎么投影速度
* 第二次再撞另一个面时，为什么要判断是不是形成“折线边”
* 什么时候角色还能沿折线滑动
* 什么时候会被判定为真正卡角，速度直接归零

#### 第五步：理解台阶检测为什么发生在“不稳定命中之后”

回看这条链：

* `InternalCharacterMove()` 命中障碍
* `EvaluateHitStability()` 判断这个命中不稳定
* 这时才尝试 `DetectSteps()`

你要理解的是：

* 如果一个面本来就稳定，那它就是正常地面，不需要台阶逻辑
* 只有“看起来像墙，但又可能刚好是台阶边缘”的命中，才值得进入台阶检测

### 完成后能回答的问题

* `MaxMovementIterations` 太小会导致什么现象？
* `CollisionOffset` 的作用是什么？调大会怎样？
* 速度投影里的“切平面投影”在这里实际解决了什么问题？
* `EvaluateCrease()` 为什么是处理墙角 / 折角的关键？
* 台阶检测为什么是在发现不稳定 hit 之后，而不是提前？

### 给下一个 AI 窗口的提示词

```text
请帮助我深入理解 KCC 的碰撞移动求解器，重点是这些函数：

1. KinematicCharacterMotor.cs 里的 UpdatePhase2（只看移动求解相关流程）
2. KinematicCharacterMotor.cs 里的 InternalCharacterMove
3. KinematicCharacterMotor.CollisionAndPhysics.cs 里的 InternalHandleVelocityProjection
4. KinematicCharacterMotor.CollisionAndPhysics.cs 里的 HandleVelocityProjection
5. KinematicCharacterMotor.CollisionAndPhysics.cs 里的 EvaluateCrease

我现在已经知道：
- InternalCharacterMove 是一个迭代 sweep 求解器
- 命中障碍后不会直接停下，而是会重新投影速度
- 墙角场景要靠 EvaluateCrease 判断是“沿折线滑动”还是“直接卡住”

但我还想搞清楚：

1. CollisionOffset 的实际物理意义是什么？
2. 为什么角色不能紧贴墙面移动（offset = 0 会出什么问题）？
3. HandleVelocityProjection 里“稳定接地”和“空中”为什么要分开处理？
4. EvaluateCrease 的几何含义到底是什么？
5. 哪些情况下 MaxMovementIterations = 5 会不够用？

我的背景：初级程序员，已经理解地面检测机制，现在正在学习碰撞移动求解器。
```

***

## 第五阶段：理解稳定性评估和 ledge / 台阶检测

### 目标

理解 `EvaluateHitStability()`、`DetectSteps()`、`CheckStepValidity()` 和 `EvaluateCrease()`。这是 KCC 处理台阶、悬崖、折角的核心算法，也是理解“角色什么情况下会停下来”的关键。

### 任务

重点阅读下面这些函数：

* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.Grounding.cs`
  * `IsStableOnNormal()`
  * `IsStableWithSpecialCases()`
  * `EvaluateHitStability()`
* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.StepHandling.cs`
  * `DetectSteps()`
  * `CheckStepValidity()`
* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.CollisionAndPhysics.cs`
  * `EvaluateCrease()`

### 推荐阅读顺序

1. 先看 `IsStableOnNormal()`：它是整个稳定性系统的起点
2. 再看 `EvaluateHitStability()`：理解稳定性、ledge、台阶入口
3. 再看 `DetectSteps()` 和 `CheckStepValidity()`：理解一个不稳定命中怎样变成可跨越台阶
4. 最后回看 `EvaluateCrease()`：理解折角 / 墙角为什么也会影响角色运动状态

### 逐步拆解

#### 第一步：稳定性判断的核心公式

先看这个函数：

```csharp
private bool IsStableOnNormal(Vector3 normal)
{
    return Vector3.Angle(_characterUp, normal) <= MaxStableSlopeAngle;
}
```

这行代码是整个稳定性系统的基石。

* `_characterUp` 是角色当前的“向上方向”
* `normal` 是命中面的法线
* `Vector3.Angle` 会得到两者夹角
* 如果夹角小于等于 `MaxStableSlopeAngle`，就认为这是一个稳定面

推论：

* 水平地面：稳定
* 缓坡：稳定
* 陡坡 / 墙：不稳定

#### 第二步：理解 ledge 检测的原理

在 `EvaluateHitStability()` 里，重点看：

* 两次 `CharacterCollisionsRaycast(...)`
* `isStableLedgeInner`
* `isStableLedgeOuter`
* `stabilityReport.LedgeDetected = isStableLedgeInner != isStableLedgeOuter`

你要理解的是：

* KCC 不只是问“当前这个点稳不稳”
* 它还会问“这个点的内侧和外侧是不是一样稳”
* 如果一边有稳定地面、一边没有，就说明角色站在边缘附近

#### 第三步：理解台阶检测的三步法

阅读 `DetectSteps()` 和 `CheckStepValidity()` 时，重点分成三层：

1. **先找台阶候选点**
   * 向上抬到 `MaxStepHeight`
   * 再往下 Sweep 看能不能落到某个面上

2. **再检查这个候选位置能不能站下去**
   * 有没有 overlap
   * 外侧坡面稳不稳
   * 上方有没有空间

3. **最后检查台阶内侧是否也合理**
   * 有些情况下还会继续补做 inner step 检测
   * 全都通过才算 `ValidStepDetected`

#### 第四步：理解为什么是“最远有效 hit”

在 `CheckStepValidity()` 里，代码会优先挑当前候选里“最远”的命中。

你要重点思考：

* 最近的命中不一定是真正的台阶面
* 它可能只是边角、凸起、小障碍
* 取最远有效 hit，往往更接近“角色最终真正能踩上的平台面”

#### 第五步：理解 denivelation 和 ledge 的区别

这两个很容易混：

* **ledge**：更像“边缘 / 悬崖”问题，本质是左右 / 内外探测结果不同
* **denivelation**：更像“相邻地面法线变化过大”的问题，本质是地形过渡太突兀

这部分主要看 `IsStableWithSpecialCases()` 里对法线夹角的判断。

### 完成后能回答的问题

* `MaxStableSlopeAngle = 60` 为什么是稳定性判断的核心参数？
* `LedgeDetected = isStableLedgeInner != isStableLedgeOuter` 的几何意义是什么？
* 台阶检测为什么要选“最远有效 hit”而不是最近 hit？
* `CheckStepValidity()` 到底过滤掉了哪些“看起来像台阶，其实不能踩”的情况？
* denivelation 和 ledge 的区别是什么？

### 给下一个 AI 窗口的提示词

```text
请帮助我深入理解 KCC 的稳定性评估、ledge 检测和台阶检测机制，重点是这些函数：

1. KinematicCharacterMotor.Grounding.cs 里的 IsStableOnNormal
2. KinematicCharacterMotor.Grounding.cs 里的 IsStableWithSpecialCases
3. KinematicCharacterMotor.Grounding.cs 里的 EvaluateHitStability
4. KinematicCharacterMotor.StepHandling.cs 里的 DetectSteps
5. KinematicCharacterMotor.StepHandling.cs 里的 CheckStepValidity
6. KinematicCharacterMotor.CollisionAndPhysics.cs 里的 EvaluateCrease

我现在已经知道：
- IsStableOnNormal 是基础坡度判断
- EvaluateHitStability 会做 ledge 检测和台阶入口判断
- DetectSteps / CheckStepValidity 负责判断一个不稳定命中能不能被解释成台阶

但我还没完全想明白：

1. LedgeDetected = (isStableLedgeInner != isStableLedgeOuter) 的几何含义是什么？
2. 为什么“坡度本身稳定”的命中，最后仍然可能因为特殊规则变成不稳定？
3. CheckStepValidity 为什么要选最远有效 hit？
4. denivelation 和 ledge 到底区别在哪？
5. EvaluateCrease 和稳定性系统之间是什么关系？

我的背景：初级程序员，已经理解碰撞移动求解器，现在想搞清楚什么算稳定、什么算台阶、什么算边缘。
```

***

## 第六阶段：理解移动平台和刚体交互

### 目标

理解 `PhysicsMover`、`InteractiveRigidbodyHandling` 和刚体交互相关函数。这对于未转变者 / 七日杀这类游戏非常重要——电梯、传送带、移动平台都是基于这套机制。

### 任务

重点阅读：

* `Assets/KinematicCharacterController/Core/Mover/PhysicsMover.cs`
  * 全文都值得读，重点看速度更新、位移同步、插值相关部分
* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.cs`
  * 重点看 `UpdatePhase1()` 里“站在移动平台上时如何绑定 attached rigidbody”这部分
* `Assets/KinematicCharacterController/Core/Motor/Function/KinematicCharacterMotor.CollisionAndPhysics.cs`
  * `StoreRigidbodyHit()`
  * 如果后面你继续把刚体相关函数拆出来，也重点看 `ProcessVelocityForRigidbodyHits()`、`ComputeCollisionResolutionForHitBody()`、`GetVelocityFromRigidbodyMovement()`

### 推荐阅读顺序

1. 先看 `PhysicsMover.cs`：理解平台自己怎么动
2. 再看 `UpdatePhase1()`：理解角色怎样识别“脚下这是一个会动的平台”
3. 再看刚体命中记录和速度处理链路

### 逐步拆解

#### 第一步：角色如何“站在”移动平台上

在 `UpdatePhase1()` 里，重点看这些部分：

* 当前是否 `IsStableOnGround`
* `GroundingStatus.GroundCollider`
* `GetInteractiveRigidbody(...)`
* `_attachedRigidbody`
* `_attachedRigidbodyVelocity`

理解目标：

* 角色不是直接“焊死”在平台上
* 而是识别脚下平台刚体，并把平台在当前位置产生的速度叠加进角色运动里

#### 第二步：理解刚体交互的几种模式

重点看配置项和相关代码里对这些模式的处理：

* `RigidbodyInteractionType.None`
* `RigidbodyInteractionType.Kinematic`
* `RigidbodyInteractionType.SimulatedDynamic`

你要理解：

* `None`：几乎不参与动态刚体交互
* `Kinematic`：角色像“无限质量”的 kinematic 角色
* `SimulatedDynamic`：更接近按质量比去模拟相互作用

#### 第三步：理解角色离开平台时为什么能保留动量

重点看 `UpdatePhase1()` 里：

* `_lastAttachedRigidbody`
* `_attachedRigidbody`
* `PreserveAttachedRigidbodyMomentum`
* `BaseVelocity += ... / -= ...` 这类逻辑

理解目标：

* 角色从移动平台上跳开时，为什么不会瞬间丢掉平台速度
* 这套逻辑为什么能做出“电梯上跳起”“传送带上滑出去”的感觉

#### 第四步：理解刚体碰撞为什么不只是一句 AddForce

刚体交互不是单纯 `AddForce` 就结束，通常要经历：

1. 先记录刚体命中
2. 再在后续阶段统一处理速度
3. 根据交互模式和质量关系，决定角色和刚体各自改多少速度

### 完成后能回答的问题

* 角色站在传送带或电梯上时，`BaseVelocity` 会受到什么影响？
* `RigidbodyInteractionType.Kinematic` 和 `SimulatedDynamic` 的核心区别是什么？
* 角色从移动平台上跳下时，保留平台动量的逻辑是怎么发生的？
* 为什么平台速度更新必须先于 Motor 的地面检测？

### 给下一个 AI 窗口的提示词

```text
请帮助我理解 KCC 的移动平台和刚体交互机制，重点是这些文件 / 函数：

1. PhysicsMover.cs（重点看平台速度和位移更新）
2. KinematicCharacterMotor.cs 里的 UpdatePhase1（只看 attached rigidbody / 平台绑定相关逻辑）
3. KinematicCharacterMotor.CollisionAndPhysics.cs 里与刚体命中、速度处理相关的函数

我现在已经知道：
- 角色站在移动平台上时，不是直接跟随 Transform，而是通过平台速度来影响角色
- attached rigidbody 会影响 BaseVelocity
- PreserveAttachedRigidbodyMomentum 可以让角色离开平台时保留平台动量

但我还想搞清楚：

1. 平台速度是怎样被加到角色上的？
2. 角色从平台上跳开时，为什么不会瞬间丢失平台动量？
3. RigidbodyInteractionType.Kinematic 和 SimulatedDynamic 到底在代码里差别在哪？
4. 为什么 Mover 的速度更新必须先于 Motor 的 Phase1？
5. 如果我的游戏里有很多移动平台，这套机制的性能瓶颈可能在哪里？

我的背景：初级程序员，已经理解地面检测和碰撞求解器，现在想搞清楚平台和刚体交互。
```

***

## 第七阶段：整合——从零魔改一个 FPS 控制器

### 目标

在彻底理解物理核之后，开始动手改 `ICharacterController` 的实现，写一个自己的 FPS 角色控制器。

### 任务

### 任务一：改站立/蹲伏

利用 `SetCapsuleDimensions()` 在运行时改变胶囊体尺寸：

* 站立：Radius=0.4, Height=1.8, YOffset=0.9
* 蹲伏：Radius=0.4, Height=1.0, YOffset=0.5

需要考虑：蹲伏时是否允许移动？下蹲动画和物理体的衔接。

### 任务二：改跳跃手感

在 `BeforeCharacterUpdate` 中检测跳跃输入，然后：

```csharp
// 强制离地（0.1秒内不吸附地面）
Motor.ForceUnground(0.1f);
// 加向上速度
currentVelocity.y = JumpVelocity;
```

配合 `GroundNormal` 做斜坡跳跃：

```csharp
if (Motor.GroundingStatus.IsStableOnGround)
{
    // 跳跃方向：向上 + 保留地面移动方向
    currentVelocity = Motor.GetDirectionTangentToSurface(currentVelocity, Motor.GroundingStatus.GroundNormal) * currentVelocity.magnitude;
    currentVelocity.y = JumpVelocity;
}
```

### 任务三：改 ADS（开镜减速）

在 `UpdateVelocity` 中根据 ADS 状态乘以系数：

```csharp
float groundSharpness = Motor.GroundingStatus.IsStableOnGround ? GroundAcceleration : AirAcceleration;
if (IsADS) groundSharpness *= ADSSpeedMultiplier; // ADS 时移动速度降低

// Lerp 速度
currentVelocity = Vector3.Lerp(
    currentVelocity,
    targetVelocity,
    1 - Mathf.Exp(-groundSharpness * deltaTime)
);
```

### 任务四：改翻滚（Roll）

```csharp
// 强制离地
Motor.ForceUnground(0.3f);
// 瞬时向前冲
currentVelocity = Motor.Transform.forward * RollSpeed;
```

### 完成后能回答的问题

* 如何在不修改 Motor 源码的情况下实现站立/蹲伏切换？
* 斜坡跳跃时用 `GroundNormal` 投影方向和直接加竖直速度有什么区别？
* ADS 减速用 Lerp 和用固定速度上限哪种手感更好？为什么？
* 翻滚的 ForceUnground 时间长度怎么确定？太长和太短分别会有什么感觉？

### 给下一个 AI 窗口的提示词

```
请帮助我基于 KCC 实现一个 FPS 角色控制器，具体包含以下功能：

1. 站立/蹲伏切换：
   - Motor.SetCapsuleDimensions() 如何在运行时调用而不产生穿模？
   - 蹲下动画和站立动画如何与物理体高度变化同步？
   - 从站立到蹲伏的过渡时间设为多少毫秒手感最好？

2. 跳跃手感：
   - 普通跳跃：ForceUnground(0.1f) + y += JumpVelocity
   - 斜坡跳跃：我写了 Motor.GetDirectionTangentToSurface(dir, GroundNormal)，
     但发现跳跃方向变得很奇怪，请检查这个用法是否正确
   - 高速下落时（> 5m/s）落地有一种"顿挫感"，怎么消除？

3. ADS 减速：
   - 当前方案：在 UpdateVelocity 里 lerp 速度乘以系数
   - 问题：ADS 进入时速度立即降为 0.3 倍，视觉上很突兀
   - 请推荐一个更平滑的过渡方案，带具体代码

4. 翻滚：
   - ForceUnground(0.3f) + 向前冲速度
   - 翻滚时如果撞到墙，应该立即停止还是继续走完动画？
   - 如何在撞墙时播放一个停顿动画（类似七日杀/未转变者）？

我的背景：已经读完 KCC 物理核，对 Motor API 较为熟悉，正在写第一人称射击游戏的角色控制器。
```

***

## 学习路线总结

```
第一阶段  建立全局地图          约 2-3 小时
第二阶段  执行调度层            约 3-4 小时
第三阶段  地面检测              约 5-6 小时
第四阶段  碰撞移动求解器         约 6-8 小时
第五阶段  稳定性与 ledge/台阶   约 4-5 小时
第六阶段  移动平台和刚体交互     约 3-4 小时
第七阶段  魔改 FPS 控制器        约 8-10 小时
                                    ───────────
总计                              约 31-40 小时
```

每阶段完成后，在新的 AI 窗口粘贴对应提示词即可继续。
如果你在某阶段卡住超过 30 分钟，优先检查：该阶段对应的关键函数是否有遗漏；该问的问题是否已在“完成后能回答的问题”中有线索。

***

## 附录：源码行号速查表

| 主题 | 文件 | 行号区间 |
| --- | --- | --- |
| KinematicCharacterSystem 入口 | KCCSystem | 61-79 |
| Simulate 四步顺序 | KCCSystem | 198-234 |
| 插值系统 | KCCSystem | 164-193, 239-271, 276-310 |
| UpdatePhase1 地面探测 | Motor | 249-499 |
| ProbeGround 核心 | Motor | 744-816 |
| UpdatePhase2 速度求解 | Motor | 511-683 |
| InternalCharacterMove 迭代循环 | Motor | 847-1095 |
| InternalHandleVelocityProjection | Motor | 1146-1219 |
| HandleVelocityProjection | Motor | 1274-1311 |
| EvaluateCrease | Motor | 1220-1273 |
| EvaluateHitStability | Motor | 1502-1573 |
| DetectSteps | Motor | 1645-1768 |
| ProcessVelocityForRigidbodyHits | Motor | 1319-1408 |
| ComputeCollisionResolutionForHitBody | Motor | 1409-1443 |
| CharacterCollisionsOverlap | Motor | 2478-2537 |
| CharacterCollisionsSweep | Motor | 2583-2643 |
| CharacterGroundSweep | Motor | 2708-2746 |
