# 山海幻灵 · 项目现状全景（0.0003 回退后基线 · 现行 0.0006）

> 复核日期 2026-11-15　｜　依据：`b33c71b` 之后仓库实际代码，逐文件核对。
> **文件名沿用历史命名 `overview-v0.003.md`，但内容已按回退提交 `e815b5c` 之后的真实基线整体重写。**
> 旧 v0.003 战斗版（19 个 C#、唤灵/灵韵/昼夜、24/24 断言、tag +0.001）只保留在
> `docs/archive/v0.003/`，**不是现行代码**，不要照它写新功能。

---

## 0. 一句话总览

**当前是一个「横板平台跳跃最小骨架」**：开始页 → 随机方块地图 → 主角移动跳跃；另有独立白箱场景用来调手感。
没有战斗、没有成长、没有昼夜、没有敌人、没有战斗 UI、没有存档。地图生成规则（MAP-001~009）与移动手感
（JUMP / DROP / CLIMB + 粘液）是当前两块主要复杂度，全部靠无头验证链守住。

## 1. 技术栈与版本

| 维度 | 现状 |
|---|---|
| 引擎 | Godot 4.7.2-stable_mono_win64，`gl_compatibility`，1280×720 / `canvas_items` / `expand` |
| 语言 | 全量 C#，`net9.0`，`Godot.NET.Sdk/4.7.2`，程序集与 namespace `ShanhaiSpiritsSurvivors` |
| 版本 | `0.0006`（2026-11-17 由 `0.0005` 升版；`project.godot` / README / 静态自检 / 启动日志一致），附注 tag `v0.0006`；本次更新内容：**优化代码，改写为 net9**。升版流程见 `docs/versioning.md` |
| 规模 | 13 个 `.cs`（`scripts/csharp/` 11 个 + `tools/` 2 个探针）约 3.1k 行；13 个 `.tscn`；零 GDScript、零 autoload、零第三方插件 |
| 主场景 | `res://scenes/StartPage.tscn`；游戏 `res://scenes/Main.tscn`；白箱 `res://scenes/Whitebox.tscn` |
| 架构 | **无静态状态类、无事件总线**：`Signals` / `Run` / `Meta` 均已随回退拆除；节点间靠 `+=` 信号与直接方法调用 |
| 构建/导出 | 构建 `dotnet build ShanhaiSpiritsSurvivors.sln`；**导出必须有 `.sln`**（含 `ExportDebug` / `ExportRelease` 配置），否则 C# 程序集不会被编译打包，导出的 exe 只是空壳；预设 `export_presets.cfg` 已入库（Windows Desktop / `embed_pck` 单 exe / 只启 OpenGL3，模板来自 SnakeSchool）；导出自包含，产物 `data_*/` 自带 .NET 9 运行时 |

## 2. 脚本清单与职责

| 文件 | 行数 | 职责 |
|---|---:|---|
| `core/GameDefs.cs` | 160 | 唯一数值表：世界几何、碰撞层、地图生成常量、移动手感、粘液参数、相机背景 |
| `core/GameRoot.cs` | 409 | 主场景自举、相机跟随、种子徽章、**约 200 行 `--smoke` 冒烟注入与 16 条断言** |
| `core/ArtBank.cs` | 67 | PNG 缓存 + 运行时 2×2 纯白纹理（`PlainTexture` / `MakeBox`） |
| `core/Rng.cs` | 57 | `DeriveSeed64` 种子派生；地图生成用实例 `_rng`，不用全局随机 |
| `gameplay/PlatformMover.cs` | 510 | **移动内核**，游戏与白箱共用；含纯函数供探针断言 |
| `gameplay/BlockField.cs` | 958 | 方块地图：类型、生成、修剪、BFS 可达性、实例化、触碰、配色、调试 |
| `gameplay/Master.cs` | 141 | 正式主角：调用内核、弹跳请求、落地/起跳视觉、灰尘、坠落重生 |
| `tools/WhiteboxPlayer.cs` | 117 | 白箱主角：共用内核 + 实测跳跃最高点 |
| `tools/WhiteboxRoot.cs` | 432 | 白箱调参 UI、图例、换种子/地形、种子徽章、截图 |
| `ui/StartPage.cs` | 84 | 开始页四个按钮、`GrabFocus` 上下焦点链、版本号、`--smoke` 延迟切场景 |
| `ui/UiSkin.cs` | 122 | 统一 UI 外观、右上角/固定位置种子徽章 |
| `tools/CSharpToolchainProbe.cs` | — | C# / Godot 运行时探针 `ok=1 engine=4.7.2-stable` |
| `tools/MapSeedProbe.cs` | — | 10 组种子回归：净空率、单块率、路径数、粘液数量与分组 |
| `tools/check_project.py` | — | 离线静态自检，输出 `docs/self-check.json` |

## 3. 场景结构

- `StartPage.tscn`：Control + 4 Button + Help/版本号 Label。**Help 文案是占位，按键说明按用户规划另处实现**；
  该文件由用户自行维护，提交时排除。按钮焦点由 `StartPage.cs` 在 `_Ready()` 设置，与 `.tscn` 默认值无关。
- `Main.tscn`：`WorldRoot`(GameRoot) → `Camera2D`(含 Backdrop 背景 PNG) / `BlockField` / `Player` 实例。
- `Player.tscn`：`Node2D` → `CharacterBody2D`(Master.cs) + 胶囊碰撞 + `Sprite2D`(player.png)。
- `Whitebox.tscn`：`Whitebox` → `BlockField` / `Player`(WhiteboxPlayer) / Camera2D；**零 PNG**。
- `scenes/blocks/*.tscn` 7 个：solid / oneway / fragile / bounce / buildable / treasure / slime；
  `scenes/fx/*.tscn` 2 个：dust_puff / loot_drop。方块与掉落预制体全部零 PNG，运行时纯色上色。

## 4. 输入与碰撞层

- **极简输入**：`move_left=A(65)` / `move_right=D(68)` / `move_down=S(83)` / `jump=Space(32)`，
  4 个 action 各 1 个 `physical_keycode`，无手柄、无方向键、无 W 跳。
- 开始页键盘导航走 Godot 内置 `ui_*` 焦点链（↑↓ + Enter/Space），不新增 action。
- 碰撞层：`1 = 实体地板`（GND-001 row 0 全覆盖）、`2 = 单向平台`（`one_way_collision=true`）；
  角色 `CollisionMask = 1|2 = 3`，下穿窗口内临时清掉第 2 层。
- MAP-009 粘液块靠碰撞体组 `slime_block` 被内核识别，不用 `Area2D` 触碰感应。

## 5. 移动内核配方（`PlatformMover.cs`）

| 配方 | 内容 |
|---|---|
| JUMP-002 | 蔚蓝式加速度曲线（地面/空中独立加减速、转向 ×2）、变高跳、下落加重 ×1.6、终端速度封顶 |
| DROP-001 | 类银式下穿单向平台（按住「下」+「跳」，0.18s 窗口） |
| CLIMB-001 | 几何台沿自动上攀；总开关 `MasterAutoClimbEnabled` **当前 false**，代码与参数保留 |
| 转角修正 | 上升撞方块角时小幅横移滑过（≤0.45 交叠） |
| MAP-009 | 粘液块：顶速 ×0.35、加减速 ×0.40；起跳只给 6.0 初速，顶点切 900 重力立刻黏回 |

所有手感参数硬编码在 `GameDefs`，白箱 `MoveTuning` 可运行时调参并写回 —— 这是**刻意偏离**
`player-controller` 清单「gravity 应从 ProjectSettings 读」，审查时不判为问题。

## 6. 随机地图生成（`BlockField.cs`）

- 网格 `56 列 × 27 行`，row 0 全覆盖实体地板，row 1~2 恒空，层间距 `LayerStepRows = 3`。
- 伪随机原则：上方净空、同行空档 ≤4、特殊块每 run 至多一块、种子化实例 `_rng`。
- 迭代规则：
  - **MAP-005** 上下净空：行差 ≥3（中间空 2 行）才算不挡道，达标线 80%，生成期 20% 破例 + 末尾修剪补齐。
  - **MAP-006** 少单块：`BlockRunMin = 2`，孤立单块保留 15%，占比硬上限 5%。
  - **MAP-007** 真实可达：按跳跃包络从 row 0 做 BFS，必要时补齐骨架并清理遮挡，最高可达行 ≥24。
  - **MAP-008** 多路径：按列带隔离生成 2~3 条独立蛇形骨架，统计 `ReachablePathCount / PathCount`。
  - **MAP-009** 粘液块：每 run 1% 权重、全图上限 3 块、`EnsureSlimePresence()` 每图保底 1 块（避开地面/骨架/单向）。
- 方块类型：`Normal / Fragile / Bounce / Buildable / Treasure / Slime`，另有单向连排段（2~3 常见）。
- 开发用种子徽章 `UiSkin.MakeSeedBadge()`：游戏侧右上角锚点，白箱侧固定位置，换种子时刷新。

## 7. 白箱 = 游戏

`Master` 与 `WhiteboxPlayer` 都只做「读输入 → 调 `PlatformMover.Step()` → 表现层反馈」，
手感逻辑与粘液效果全部收在共用内核里，改 `GameDefs` 一处两边同时生效。白箱额外提供调参 UI、
实测跳跃最高点、图例与换种子，用于验证同一套数值。

## 8. 验证链（`docs/verification-2026-11-15.md`）

`dotnet build`（0 警告 0 错误）→ `tools/check_project.py`（**82/82 PASS**）→ headless 编辑器导入（0 ERROR）
→ 两条 `--smoke`（**16/16 断言**，开始页路径需 `CallDeferred` 延迟一帧切场景）
→ 白箱 / 开始页各 120 帧 → `CSharpToolchainProbe` → `MapSeedProbe`（10 组种子 `fail=0`）。
冒烟注入只在 `--smoke` 分支生效，不改变正常游戏数值。

## 9. 刻意偏离（审查时不误判）

1. 手感参数硬编码在 `GameDefs`（而非 `ProjectSettings`）——为了让白箱与游戏共用 `MoveTuning`。
2. 极简输入 4 action × 1 键、无手柄绑定 —— 用户明确要求「最基础」。
3. 跳跃在 `_physics_process` 轮询 `IsActionJustPressed` + 缓冲窗口，而非 `_unhandled_input`。
4. 白箱调试热键 R / T / Tab 硬编码 `Key`、未进 Input Map。
5. 开始页不放按键说明 —— 按用户规划另处实现。

## 10. 已知遗留（未处理，等待拍板）

- `scenes/StartPage.tscn` 用户自行改动未提交；每次提交显式排除。
- `docs/archive/v0.003/` 是旧版本线归档；`assets/art/` 中一批旧战斗版 PNG（boss / enemy_* / pet / turret）
  与 `block_white.png` 在现行代码里零引用，只登记不删除。
- `BlockField.cs` 958 行单文件、`GameRoot.cs` 约 200 行冒烟 harness 混在正式脚本里 —— 属可维护性债，未重构。
- 无 `.sln`、无 `export_presets.cfg`、无 CI；当前验证链全部本地手工跑。
- 白箱面板与种子徽章用固定坐标，非 1280×720 下可能重叠。
- 静态自检里存在「源码包含某字符串」型断言，验的是实现文本而非行为。
