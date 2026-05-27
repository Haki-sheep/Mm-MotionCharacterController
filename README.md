# PlayerController 项目说明

## 项目目标
这是一个用于练习和实现 `MotionCharacterController`（简称 MCC）角色控制器的 Unity 项目。

当前重点：
- 角色本体不依赖 Unity 的 `Rigidbody` 和 `CharacterController`
- `MotionCC` 作为主入口，内部使用 KCC 同款两阶段运动学解算
- 优先保证移动、接地、碰撞滑动、台阶、边缘、移动平台行为接近 KCC

## 当前目录约定
- `Assets/Scenes`：场景
- `Assets/MotionCharacterController`：主开发目录
- `Assets/Test`：小测试脚本
- `Assets/Resources`：资源（后续需要时再放）
- `Assets/Prefabs`：预设体（后续需要时再放）
- `Assets/UI`：界面（后续需要时再放）

## MotionCharacterController 当前规划
主目录：`Assets/MotionCharacterController`

建议结构：
- `Docs`：文档
- `Scripts/Core/Runtime`：`MotionCC`、`MccSystem`、`MccPhysicsMover`
- `Scripts/Core/Config`：`MccConfig` 参数
- `Scripts/Core/Interfaces`：`IMccController`、`IMccMoverController`
- `Scripts/Mcc`：示例玩家控制和相机

## 角色推荐组件
角色本体建议挂：
- `Transform`
- `CapsuleCollider`
- `MotionCC`
- `PlayerController`（示例脚本，可替换成自己的控制脚本）

先不要挂：
- `Rigidbody`
- `CharacterController`

## 核心设置
- Unity 版本：建议使用项目当前 Unity 版本打开。
- 胶囊体：默认半径 `0.5`，高度 `2`，Y 偏移 `1`。
- 稳定斜坡：默认最大 `60` 度。
- 台阶：默认开启标准台阶，最大高度 `0.5`。
- 移动平台：平台物体挂 `Rigidbody`、`MccPhysicsMover`，再写一个实现 `IMccMoverController` 的脚本控制它移动。
- 摄像机：示例用 `ExampleMCharacterCamera`，跟随玩家即可。

## 操作方式（规划）
先按最常见的方式约定：
- `WASD`：移动
- `Space`：跳跃
- `Mouse`：转向（后续再定）

## 开发阶段
- `v1`：基础走、跳、接地、碰撞滑动
- `v2`：台阶、边缘、强制离地、多次 sweep
- `v3`：移动平台、惯性、完整交互
- 当前：`MotionCC` 已通过 KCC 适配内核获得 v1-v3 的核心运动能力。

## 快速测试
1. 打开 `Assets/MotionCharacterController/Scene/MainTestScene.unity`。
2. 玩家物体上确认有 `CapsuleCollider`、`MotionCC`、`PlayerController`。
3. 按播放，使用 `WASD` 移动，`Space` 跳跃。
4. 想测试台阶，就在场景里放一个高度不超过 `MotionCC` 的 `maxStepHeight` 的小方块。
5. 想测试移动平台，就给平台加 `Rigidbody` 和 `MccPhysicsMover`，再用 `IMccMoverController` 脚本输出目标位置。

## 一键打包提示
- PC：`File > Build Profiles > Windows > Build`。
- 安卓：先安装 Android Build Support，再选择 `Android > Build`。
- 打包前先在编辑器播放一次，确认控制台没有红色报错。
