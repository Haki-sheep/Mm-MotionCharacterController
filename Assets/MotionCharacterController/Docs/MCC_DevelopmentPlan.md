# MotionCharacterController 开发与交接计划

## 目标
参考 KCC 的核心思想，自主实现一套更适合当前项目理解和维护的 `MotionCharacterController`，简称 `MCC`。

当前新的命名与方向约定：
- 对外核心主控制器命名为 `ModuleCC`
- 核心能力采用模块式结构
- 目标是做到 **组件式、可插拔、低侵入、可维护**

原则：
- 不直接复刻 KCC 全量实现
- 先做最小可运行版本，再逐步增强
- 保持结构清晰，避免像 KCC 一样拆成很多零散脚本
- 核心运动层与玩法状态层分离
- 让后续任何 AI / 人类开发者都能快速接手
- 保留明确的 `v1 / v2 / v3` 阶段规划，不一步到位

---

## 当前已达成的共识

### 1. 角色本体不依赖 Rigidbody
推荐方案：
- 角色本体使用：`Transform + CapsuleCollider + ModuleCC`
- 不给角色本体挂 `Rigidbody`

原因：
- 更纯粹地实现运动学角色控制器
- 避免和 Unity 刚体系统相互打架
- 更方便学习核心碰撞与地面判定逻辑

### 2. 不依赖 Unity CharacterController 组件
推荐方案：
- 不使用 Unity 自带 `CharacterController`
- 直接使用 `Physics` 查询：
  - `CapsuleCast`
  - `OverlapCapsule`
  - `Raycast`
  - `ComputePenetration`

原因：
- 这样才能真正掌握 MCC 的底层实现
- 不会被 Unity CC 帮忙做掉核心逻辑

### 3. KCC 的剩余源码不必全部读完再开工
当前已经读到 KCC 的 `Phase1`、接地、边缘、平台交互基础思路。

结论：
- 不需要等彻底读完 `Phase2 + 全部物理交互` 再开始写 MCC
- 正确方式是：边写边回头查 KCC

建议节奏：
- 先启动 `ModuleCC v1`
- 卡住再定点回看 KCC 对应部分

---

## 架构决策

### 总体架构原则
- 少文件，但不能把所有逻辑塞成一个巨型脚本
- 强分层
- 职责单一
- 不用 `partial class`
- 不做 KCC 那种分散式碎片拆文件结构
- 主流程固定，功能模块可插拔
- 模块之间尽量不直接互相调用，而是通过共享数据协作

### 核心结论 1：MCC 核心层不要做成 FSM
原因：
- 运动求解层本质是连续数学与物理查询流程
- 不是行为切换系统
- 如果核心层也做状态类，容易导致：
  - 各状态重复做碰撞
  - 接地数据不同步
  - 跳跃/落地/台阶/平台互相打架

### 核心结论 2：FSM 只用于玩法层
正确分层：
- `MCC 核心层`：负责移动、地面、碰撞、跳跃、台阶、平台
- `玩法层状态机`：负责 `Idle / Run / Jump / Fall / Dash / Climb` 等玩法行为

### 核心结论 3：采用 ModuleCC 组件式架构
推荐结构：
- `ModuleCC`：唯一主入口，挂在角色物体上
- `ModuleCCData`：共享运行时数据
- `ModuleCCConfig`：核心参数配置
- `ModuleCCContext`：公共引用上下文
- `IModuleCCModule`：统一模块接口
- 多个能力模块：按功能插拔

模块化目标：
- 能开关
- 能扩展
- 不绑死玩家脚本
- 不侵入玩法层

---


## 各脚本职责约定

### `ModuleCC.cs`
核心主类。
职责：
- 统一更新入口
- 初始化各模块
- 维护模块执行顺序
- 保存核心公共引用
- 驱动各模块执行
- 对外提供高层接口

不要把所有细节算法都塞进来。

### `ModuleCCData.cs`
共享运行时数据。
职责：
- 速度
- 输入方向
- 跳跃请求
- 接地状态
- 地面法线
- 本帧位移
- 临时标志位

所有模块主要通过它交换信息。

### `ModuleCCConfig.cs`
纯参数配置。
职责：
- 胶囊尺寸
- 重力
- 移动速度
- 跳跃速度
- 最大坡度
- Ground probe 距离
- 碰撞余量
- 台阶高度（后续）

### `ModuleCCContext.cs`
公共引用上下文。
职责：
- `Transform`
- `CapsuleCollider`
- `LayerMask`
- 调试开关
- 其他底层共用引用

### `IModuleCCModule.cs`
统一模块接口。
职责：
- 约束模块生命周期
- 统一初始化方式
- 统一帧内调用时机

### `GroundModule.cs`
只负责接地逻辑。
职责：
- 地面探测
- 可稳定站立判定
- 坡度判断
- 离地 / 接地切换
- 后续边缘与强制离地扩展

### `MoveModule.cs`
只负责水平移动。
职责：
- 输入方向转速度
- 地面 / 空中移动参数分流
- 基础期望水平速度生成

### `GravityModule.cs`
只负责重力。
职责：
- 处理垂直方向速度
- 处理离地后的下落趋势

### `JumpModule.cs`
只负责跳跃。
职责：
- 消耗跳跃请求
- 在允许跳跃时施加起跳速度
- 后续扩展二段跳 / 缓冲 / 土狼时间

### `RotationModule.cs`
只负责转向。
职责：
- 根据输入或相机方向更新朝向
- 保持朝向逻辑与位移逻辑分离

### `CollisionModule.cs`
只负责碰撞与位移修正。
职责：
- 胶囊 cast
- overlap 检查
- 计算滑动方向
- 修正最终位移

### `Advanced` 模块
只放增强能力：
- `StepModule`
- `PlatformModule`
- `LedgeModule`

它们不属于 v1 必做内容。

### `Gameplay` 层
只负责玩法，不介入底层物理查询实现。

---

## 模块化约束规则

为了保证 `ModuleCC` 真正可插拔，所有模块尽量遵守下面规则：

1. 可以单独启用 / 禁用
2. 删除单个非核心模块后，主控仍能运行
3. 不直接依赖 `PlayerBrain`
4. 不直接依赖具体输入系统实现
5. 不直接操作别的模块私有字段
6. 主要通过 `ModuleCCData` 交换信息
7. 统一由 `ModuleCC` 调度执行顺序

---

## 推荐执行顺序

### 每帧大流程
1. 输入阶段
2. 探测阶段
3. 速度计算阶段
4. 位移求解阶段
5. 收尾阶段

### 推荐模块顺序
1. `GroundModule`
2. `MoveModule`
3. `GravityModule`
4. `JumpModule`
5. `CollisionModule`
6. `RotationModule`

说明：
- 模块可以插拔
- 但主流程顺序必须统一
- 不能让每个模块自己随意决定执行时机

---

## 开发阶段规划

## MCC v1 - 最小可运行版
目标：
- 先做出一个能稳定走、跳、落地、上下坡的基础控制器
- 建立 `ModuleCC` 主控 + 模块式骨架

### v1 必做功能
- `ModuleCC` 主入口
- `ModuleCCData / Config / Context`
- `GroundModule`
- `MoveModule`
- `GravityModule`
- `JumpModule`
- `CollisionModule`
- `RotationModule`
- 胶囊体尺寸缓存
- 基础输入移动接口
- 基础旋转
- 重力
- 地面检测
- 稳定地面判定
- 基础碰撞滑动
- 跳跃
- 基础 gizmos 调试

### v1 暂时不做
- 台阶
- 边缘处理
- 平台跟随
- 刚体交互
- 两阶段更新
- 惯性保留
- 复杂 overlap 解算
- 完整事件系统
- 复杂依赖注入框架

### v1 验收标准
- 平地走动正常
- 跳跃和落地正常
- 斜坡不会明显乱抖
- 离地 / 接地状态切换清晰
- 不依赖 `Rigidbody` 和 `CharacterController`
- 能以模块式结构继续扩展

---

## MCC v2 - 实用增强版
目标：
- 接近真正可用于项目的角色控制器
- 在不破坏模块结构的前提下增强稳定性

### v2 计划加入
- 初始 overlap 修正
- 多次 sweep
- 贴地优化
- 强制离地
- 边缘检测
- 台阶检测
- 落地速度修正
- 更完整的调试输出
- 模块启停与优先级整理

### v2 验收标准
- 墙角和斜坡稳定性明显提升
- 上下台阶自然
- 边缘判定合理
- 跳跃离地不容易被错误吸回地面
- 模块间协作依旧清晰

---

## MCC v3 - 完整交互版
目标：
- 达到接近 KCC 的成熟交互能力
- 在模块式结构中支持复杂平台与外部系统接入

### v3 计划加入
- 移动平台跟随
- 平台旋转带动
- 附着刚体速度继承
- 惯性保留
- 刚体交互
- 两阶段更新
- 更丰富的命中 / 接地报告
- 外部回调扩展点
- 更规范的模块注册方式

### v3 验收标准
- 站在移动平台上稳定
- 平台旋转时角色表现合理
- 离开平台时惯性自然
- 平台和角色时序稳定
- 对外扩展点清楚

---

## 关键设计原则

### 1. 先写最小正确版本，不追求一步到位
避免一开始就复刻 KCC 的完整复杂度。

### 2. 先自己设计，再参考 KCC
正确顺序：
1. 自己先设计思路
2. 自己先实现一版
3. 出现问题时再回看 KCC 对照

不要先把 KCC 改名抄一遍。

### 3. 所有难点都要用 Gizmos / Debug 画出来
尤其是：
- 胶囊 bottom / top 半球圆心
- 地面法线
- 投影方向
- 边缘 inner / outer 探测
- 台阶检测路径

### 4. 数据和行为要分开
- `Config / Data / Report / Context` 是数据
- `Ground / Move / Jump / Collision` 是行为
- FSM 只放在玩法层

### 5. 模块按能力拆，不按状态拆
正确拆法：
- `MoveModule`
- `JumpModule`
- `GravityModule`
- `GroundModule`
- `CollisionModule`
- `PlatformModule`

不建议拆成：
- `IdleModule`
- `RunModule`
- `FallStateModule`

---

## 建议的开发顺序

### Step 1
创建最小目录结构与脚本骨架：
- `ModuleCC`
- `ModuleCCConfig`
- `ModuleCCData`
- `ModuleCCContext`
- `IModuleCCModule`
- `GroundModule`
- `MoveModule`
- `GravityModule`
- `JumpModule`
- `RotationModule`
- `CollisionModule`

### Step 2
先做胶囊尺寸缓存与关键点缓存：
- center
- bottom
- top
- bottom hemi
- top hemi

### Step 3
先打通地面检测：
- overlap / cast / raycast 基础方法
- ground hit
- stable on ground 判定

### Step 4
接入基础移动：
- 输入方向
- 速度
- 重力
- 跳跃

### Step 5
接入基础碰撞滑动：
- 命中法线
- 投影速度
- 简单位移修正

### Step 6
加调试可视化

### Step 7
再开始做 v2 的台阶、边缘、强制离地

---

## 与 KCC 的关系约定
MCC 的目标不是照抄 KCC，而是：
- 参考 KCC 的成熟思路
- 用更清晰、可控、适合当前作者维护的方式重写

### 可以参考 KCC 的部分
- Capsule 关键点缓存
- 接地探测思路
- 稳定坡度判定
- 边缘检测思路
- 台阶检测思路
- 平台跟随时序

### 不建议直接照搬的部分
- 过度拆分的 `partial class` 结构
- 一开始就做太多特殊情况
- 全量复制 KCC 的完整参数体系

---

## 当前阅读 KCC 的收获摘要
已明确理解的重点：
- `position / _transientPosition` 是角色 Transform 基准点，不是胶囊底点
- `bottom / top` 在 Physics API 中指的是两个半球圆心，不是最底点 / 最顶点
- `stableOnHit` 表示当前命中面是否可视为稳定可站立面
- overlap 结果记录是为了后续运动投影，避免刚推出去又顶回去
- `Phase1 / Phase2` 是分阶段组织角色与平台时序的关键设计
- 可交互刚体处理的本质是：角色跟随平台移动 / 旋转，并在切换平台或离开平台时保留合理惯性

---

## 给后续接手者的说明
如果你是后续接手的 AI 或开发者，请遵守以下原则：

1. 不要直接把 KCC 大段复制进 MCC
2. 优先保证 MCC 结构清晰，而不是功能堆得快
3. 先完成 v1 再讨论 v2 / v3
4. MCC 核心层不要状态机化
5. 如果需要参考 KCC，请只定点参考当前要实现的那一小块
6. 任何新增功能都要先写明它属于 v1 / v2 / v3 哪个阶段
7. 任何新增模块都要先说明它是否属于核心模块还是可选模块

---

## 下一步建议
下一步直接开始：
- 建立 `Assets/MotionCharacterController/Docs/`
- 建立 `Assets/MotionCharacterController/Scripts/`
- 创建 `ModuleCC` 模块式脚手架
- 先不写复杂逻辑，只把结构固定下来

如果继续由 AI 接管，推荐优先任务顺序：
1. 搭脚手架
2. 创建文档与目录结构
3. 创建核心脚本骨架
4. 讨论模块实例的创建方式
5. 再决定后续使用哪种 C# 到 `MonoBehaviour` 的组织方式
