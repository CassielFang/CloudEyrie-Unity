# 《云岫》项目说明

> 2D 横版动作解谜 · Unity 6。原名《云岫·狐引》（那是数智链大赛参赛垂直切片的用名）。
> 上一阶段是 **17 天赶比赛的切片**，其中若干决策是临时性的 —— 凡本文标注 ⚠️ **临时** 的地方，
> 都是"当时为了赶工期这么干"，不代表设计意图，正在被逐步取代。

## 环境

| 项 | 值 |
|---|---|
| 引擎 | Unity **6000.6.5f1**（Unity 6 LTS），2D / URP |
| 产品名 | `云岫`；公司名 `栖川工作室`（`ProjectSettings/ProjectSettings.asset`） |
| 远端 | GitHub `CassielFang/CloudEyrie-Unity`，**启用 Git LFS**（`.png`/`.wav`/`.dll`/`.ttf` 走 LFS） |
| 提交规范 | 见 `docs/dev/gitrules.md` |

## 目录导航

```
Assets/Scripts/       全部游戏代码（约 60 个 .cs，见下「架构」）
Assets/Scenes/        Bootstrap（启动）→ Title → Main（关卡）→ Ending
Assets/Spine/         spine-unity 3.8.0 运行时（第三方，勿改）
Assets/Art/           立绘 / Spine 骨骼 / 灰盒占位（非 Resources）
Assets/Resources/     运行时 Resources.Load 加载的一切
美术素材/              美术交付的原图与母版（程序从不加载）
docs/                 文档，入口见 docs/INDEX.md
tools/                开发工具脚本（Python）
```

## 架构

青禾是**一个外壳 + 两条委托**，改任何一处前先认清责任边界：

```
QingheController        只做：读输入快照 → 物理 → 着地判定 → 立绘翻转。
   ├─ CompanionStateMachine   形态规则：切换成本/硬直/冷却/强制切雾
   │     └─ EntityForm / MistForm / MergeForm   （实现 ICompanionForm）
   └─ CombatController        战斗：轻击三连/蓄力重击/格挡/灵弹/灵合光环与爆发
```

- **三形态**：实体 `Entity`（走/二段跳/踏云闪/近战）、灵雾 `Mist`（八向飞/穿屏障/灵弹）、
  灵合 `Merge`（悬浮/净化）。形态**不切换碰撞层** —— "灵雾穿墙"是关卡侧按形态开关屏障碰撞体。
- **敌人**：`EnemyAI` 一个扁平状态机管全部小怪（近战/远程靠 `Ranged` 开关区分），
  `MinionVisual` 是表现层。**不是行为树** —— `docs/archive/` 里那份行为树设计文档从未落地。
- **Boss**：`BossAI` 三阶段（幻影/狂乱/归心）+ 召唤物 `FogFire`（狐火）、`CorruptionField`（浊湮领域）。
- **事件**：`EventBus` 解耦；`GameFlow.Push/Pop` 是演出闸门（`Time.timeScale = 0`），
  演出期间逻辑与玩家输入全停。

## 数值

**`Assets/Resources/GameConfig.asset` 是唯一数值真相源**（ScriptableObject，字段定义在 `Core/GameConfig.cs`）。
分 `Spirit / Tide / Combat / Enemy / Boss` 五组，运行时 `Resources.Load`，缺省回落代码默认值。

⚠️ **改了 `GameConfig.cs` 里的默认值，对已存在的 `.asset` 无效** —— 默认值只在资源缺失时生效，
真值在 asset 里。要生效必须改 Inspector 或重建资源。

⚠️ 同理：**场景里 MonoBehaviour 的 Inspector 值会冻结进 `.unity`**，改 C# 默认值对已存场景无效。
排查"改了代码没反应"时，先看 `.unity` 里存的是什么。

汇总表见 `docs/design/数值.md`。

## 物理分组（改碰撞前必读）

`Core/PhysicsGroups.cs` 把「谁挡谁」和「谁能打谁」拆开，运行时写进 Unity 碰撞矩阵：

| 层 | 与谁碰撞 |
|---|---|
| WORLD (8) | WORLD / PLAYER / ENEMY —— 地面、机关、屏障 |
| PLAYER (9) | WORLD + ARROW —— 不挡小怪，也不被小怪挡 |
| ENEMY (10) | WORLD + HITBOX |
| HITBOX (11) | ENEMY |
| ARROW (12) | PLAYER |

⚠️ **所有游戏对象必须归到这 5 层之一**。留在 Default 层的话，`Apply()` 会把游戏层与"其它一切层"
的碰撞全部断开 —— 那个物体对谁都拦不住、也碰不到（曾经让灵界屏障完全失效）。

为什么不用"敌人做成 trigger"这种简单做法：`AttackHitBox` 本身就是 trigger，
而 **Box2D 不产生「触发器与触发器」之间的接触** —— 敌人也变 trigger 的话玩家就再也打不到它。

## 关卡 ⚠️ 临时方案，正在被取代

**当前关卡几何全部由 `World/LevelDirector.cs` 代码搭**（作者原话："本项目根本没有 prefab，本来就得程序化建"）。

- 一关到底：`LevelLeft=0 ~ LevelRight=3200`，出生 `x=200`，物理地面顶边 `GroundTopY=-295`，Boss 战场 `x=2600`。
- 段落按玩家 x 推进（`LevelSegment` 枚举）；道具用 `MakeProp` / `MakeSpring` / `MakePropHalf` 建。
- ⚠️ **整关没有一个平台** —— `LevelDirector.cs` 里写着「平台必须先有美术才有意义」，
  之前放过三个"只有碰撞体没贴图"的平台，玩家撞空气，已删。

**下一阶段要推翻这个做法**：关卡必须变成可编辑的资源（不靠代码自建）。相关计划见 `docs/plan/路线图.md`。

## 美术管线

**两层命名约定**，交接前必读：

| 层 | 位置 | 命名 |
|---|---|---|
| 交付层 | `美术素材/` | 中文 + 拼音子目录，含 `@2x` 与 `(1)~(4)` 变体 |
| 引擎层 | `Assets/Resources/` | 英文/拼音 snake_case（`sfx_`/`bar_`/`title_btn_`） |

- `Resources/ui/` 的 21 张图是 `美术素材/*_slices/` 主图的**逐字节拷贝**（只改名）——
  这是**有意的管线约定，不是冗余**，不要"清理"掉任意一边。
- `@2x` 与 `(n)` 状态变体**只留在交付层**，引擎层只取 1x 主图。
- 序列帧动画（`Resources/video_fx/`，289 MB）由 `Fx/SequencePlayer` 播放，走 `manifest.json` 索引。

⚠️ **`Assets/Spine/` 打过就地补丁**（spine-unity 3.8.0 适配 Unity 6.6 的 `EntityId` 迁移，10 个源文件）。
补丁清单见 `Assets/Spine/UNITY6.6-PATCH-NOTES.md`。**任何重新导入 Spine 的操作都会覆盖补丁**，
症状是"安全模式 + CS0619"。复发时按那份 notes 重打。

## 编辑器工具（`Assets/Editor/`）

| 工具 | 什么时候用 |
|---|---|
| **CloudEyrie/校验当前场景** | **改完关卡/场景后、提交前跑一次**。抓 `Default` 层漏网的碰撞体（对谁都碰不到，零报错）、关键组件重复、空 sprite |
| **CloudEyrie/关卡工具/按设计稿重排机关** | 预演 / 应用两档，当前场景 / 全部关卡两种范围。按 `LevelLayoutData` 挪敌人/碎片/筒台等根节点（不碰 Art 子节点），并删掉与移动平台重叠的静态平台碰撞体 |
| **CloudEyrie/关卡工具/按设计稿重建地面碰撞** | 设计稿数据一改就重跑（全部关卡）。只重建 `GroundSegments` / `PlatformColliders` 两类碰撞体 |
| **CloudEyrie/工程设置/注册层名与排序层** | 自动跑（域重载时）。把 `WORLD/PLAYER/…` 与 11 个排序层写进 `TagManager.asset` |
| **CloudEyrieTextureImporter** | 自动。对**任意层级**的 `Resources/` 与 `Assets/Art/` 设 PPU=1 |

⚠️ **关卡美术必须放在某个 `Resources/` 目录下**（可以是嵌套的，如
`Assets/Levels/<关>/Resources/…`）。放到别处会按 Unity 默认 PPU=100 导入 →
`Resources.Load<Sprite>` 返回 null 且**不报错**。

## 已知的坑

完整清单见 `docs/dev/避坑清单.md`。最容易踩的几条：

- **判定盒 `BoxCollider2D.offset` 必须为 0** —— 左右镜像只翻节点位置，offset 不跟着翻。
- **`AttackHitBox` 刚体必须是 `Animated` + `allowSleep = false`** —— `Static` 的 fixture 不跟随节点，
  `Dynamic` 会休眠而休眠后接触事件不触发。两者都表现为"攻击时灵时不灵"。
- **连击窗口从「收招」计时**，不是起手 —— 从起手算的话窗口(0.3s) < 攻击时长(0.4s)，连段永远接不上。
- **着地判定按 `WORLD` 层 + 接触法线朝上**（`QingheController.IsGroundContact`），
  所以运行时新建的平台只要归 WORLD 层就能站。
- **换美术素材要改「天然朝向」常量** —— 判错会让角色攻击打到背后。
- **回调不能设在内部 `stop()` 之前**，否则刚设的回调被自己清掉。

## 文档

入口是 **`docs/INDEX.md`** —— 哪份是活文档、哪份已归档、各自负责什么，都在那里。
`docs/archive/` 里是比赛期产物与 Cocos 时代的文档，**仅供追溯，不要当现状读**。

> **接手前先读 `docs/plan/交接-2026-10-17.md`** —— 最近一轮做到哪、下一步是什么、
> 哪些结论是拍过的（例如设计稿 SVG 才是摆放真相源、纵向换算系数 4.0），都在那里。
