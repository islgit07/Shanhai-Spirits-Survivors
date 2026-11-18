# 2026-11-15 验证记录（交接整改）

范围：统一英文名、键鼠映射精简、开始页适配、白箱零 PNG、版本口径补齐。**玩法未改动**。
引擎：Godot 4.7.2.stable.mono / C# `net9.0`（§28 起；§1–27 验证时为 `net8.0`）/ 程序集 `ShanhaiSpiritsSurvivors`。

## 1. 验证结果（全部实跑，非纸面）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build ShanhaiSpiritsSurvivors.csproj -v minimal --nologo` | 0 警告 0 错误 |
| 2 | `python tools/check_project.py` | **62/62 PASS** → `docs/self-check.json` |
| 3 | `godot --headless --path <项目> --editor --quit` | exit 0，0 条 ERROR |
| 4 | `godot --headless --path <项目> -- --smoke` | exit 0，**PASS**，version 0.0003，0 条 ERROR |
| 5 | `... res://scenes/Main.tscn -- --smoke` | exit 0，**PASS**，platforms=359，7/7 断言 |
| 6 | `... --quit-after 120 res://scenes/Whitebox.tscn` | exit 0，0 条 ERROR，blocks=361 |
| 7 | `... --quit-after 120 res://scenes/StartPage.tscn` | exit 0，0 条 ERROR |
| 8 | `... --script res://tools/CSharpToolchainProbe.cs` | `ok=1 engine=4.7.2-stable` |

冒烟断言：`platforms_built` / `ground_continuous` / `player_grounded` / `player_moved` /
`player_jumped` / `player_drop_through` / `climb_logic_ok` 全部 true。

## 2. 关键修复

- 主场景改为 `StartPage.tscn` 后，旧冒烟命令 `-- --smoke` 会停在开始页**挂死不退出**。
  `StartPage` 现在只在 `--smoke` 分支延迟一帧切到 `Main.tscn`（延迟是为避开
  `Parent node is busy adding/removing children`），旧命令恢复可用。
- 白箱链路（`Whitebox.tscn` / `scenes/blocks/*.tscn` / `scenes/fx/loot_drop.tscn`）
  **零 PNG 引用**：纹理改由 `ArtBank.PlainTexture()` 运行时生成 2×2 白图 + modulate 上色；
  游戏侧 `Main` 的背景与主角美术保留。

## 3. 刻意偏离（审查时不误判）

- 用户要求「键鼠映射最基础」→ 4 个 action 各只绑 1 键（A / D / S / Space），
  **不提供手柄绑定**，偏离 `input-handling` 清单「每个 action 应同时有键盘与手柄」。
- `GameDefs` 硬编码手感参数为项目既有架构（见 GodotPrompter 路由卡）。
  > 更正（第 9 节）：本节原写「静态 `Signals` 事件总线为项目既有架构」，属回退前的过期描述。
  > `e815b5c` 之后 `Signals` / `Run` / `Meta` 均已不存在，现行代码零静态状态类、零事件总线。
- 跳跃在 `_physics_process` 用 `IsActionJustPressed` 轮询 + `PlatformMover` 跳跃缓冲，
  而非 `_unhandled_input`，为既有移动内核设计。
- 白箱调试热键 R / T / Tab 仍是硬编码 `Key`，未进 Input Map（避免反向增加映射复杂度）。

## 4. 诚实边界与遗留

- 键盘焦点交互仅有 `GrabFocus` / 上下焦点链的静态检查 + 场景独立运行无错。
- `docs/overview-v0.003.md` 当时仍是旧 v0.003 战斗版全景文档（本轮第 9 节已重写为 0.0003 回退后基线）。
- `assets/art/block_white.png` 已零引用，文件保留未删（等确认）。
- `scenes/StartPage.tscn` 的外部未提交改动由用户自行维护：Help 文案是占位，**按键说明按用户规划在别处实现**，
  不要恢复原按键文案、不要改写、不要提交该文件；提交时继续排除。
- 版本号保持 `0.0003`（用户拍板保留），**未打 tag、未升版**。

---

## 5. 同日第二轮：MAP-005 上下净空限制

> 第 1–4 节为第一轮「交接整改」的历史记录（当时 62/62、359 块、7 断言）。
> 本节为第二轮：随机地图生成加限制「80% 方块上下隔开两个格子，避免挡道」。
> 版本号仍为 `0.0003`，未打 tag。

### 5.1 规则口径（全部集中在 GameDefs，可复现）

| 常量 | 值 | 含义 |
|---|---|---|
| `BlockVerticalClearRows` | 2 | 行差 1~2 视为没隔开；**行差 ≥3 = 中间空 2 行**才合规 |
| `BlockVerticalClearCols` | 2 | 水平 ±2 列内视为同一条上下通道 |
| `BlockVerticalClearRatio` | 0.80 | 全图合规率**硬指标**，写进冒烟断言 |
| `BlockVerticalClearBreakChance` | 0.20 | 生成期贴太近的装饰块 20% 概率破例保留，维持随机性 |
| `LayerStepRows` | 2 → **3** | 骨架层间空 2 行；步幅由 2~5 列收紧为 1~2 列（dr3 水平可达半径 ≤2） |

### 5.2 实现链

1. `HasNearVerticalNeighbor(r,c)`：生成期 / 统计期共用的邻近判定。
2. `PlaceRun`：装饰格贴太近时按 80% 概率拒绝；骨架 `purePlatform` 豁免以保住地面→顶部主路径。
3. `MeasureVerticalClearance`：Build 末尾统计合规块 / 总块 / 单向块数。
4. `EnforceVerticalClearance`：合规率不达标时从高层往低层修剪违规装饰块，每删一块重算，达标即停；骨架 / 地面由 `_backboneCell` 保护。
5. `GameRoot` 冒烟新增第 8 条断言 `vertical_clear_80pct`，JSON 输出合规率与单向块数。
6. 顺带修复既有缺陷：`Build` 可重复调用时漏清 `_reserved`（白箱换种子会让地图越来越稀）。
7. 新增验证工具 `tools/MapSeedProbe.cs`（+ `.uid`）：10 组种子一次性回归，非游戏功能。

### 5.3 验证结果（全部实跑）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build ...` | 0 警告 0 错误 |
| 2 | `python tools/check_project.py` | **69/69 PASS** → `docs/self-check.json` |
| 3 | `godot --headless --path ... --editor --quit` | exit 0，0 条 ERROR |
| 4 | `godot --headless --path ... -- --smoke` | exit 0，**PASS**（开始页延迟切主场景） |
| 5 | `... res://scenes/Main.tscn -- --smoke` | exit 0，**PASS**，blocks=237，**8/8 断言**，合规率 190/237 = 80.2% |
| 6 | `... --quit-after 120 res://scenes/Whitebox.tscn` | exit 0，0 条 ERROR，blocks=243，合规率 195/243 = 80.2% |
| 7 | `... --quit-after 120 res://scenes/StartPage.tscn` | exit 0，0 条 ERROR |
| 8 | `... --script res://tools/CSharpToolchainProbe.cs` | `ok=1 engine=4.7.2-stable` |
| 9 | `... --script res://tools/MapSeedProbe.cs` | **10/10 种子达标**，合规率 80.0%~81.1%，`fail=0` |

冒烟 8 断言：`platforms_built` / `ground_continuous` / `player_grounded` / `player_moved` /
`player_jumped` / `player_drop_through` / `climb_logic_ok` / `vertical_clear_80pct` 全部 true。
多种子探针同时校验地面连续、单向块数量 > 0（下穿验证不会退化成跳过）。

### 5.4 边界

- 地图比第一轮更稀疏：方块数 359 → 223~243（10 种子区间），这是「避免挡道」的预期结果。
- 骨架可达性由构造时的 `RunReachable` 保证；修剪只删非骨架装饰块，**未做生成后全图连通性 BFS**。
- 20% 破例块是刻意的：`EnforceVerticalClearance` 达标即停，因此合规率稳定落在 80%~81%，
  不会把地图剪成 100% 规则化。
- 只改地图生成与验证链路，未动移动手感、输入映射、开始页、白箱美术、版本号。

---

## 6. 同日第三轮：MAP-006 少出现单个方块 + MAP-007 完整登顶路径

### 6.1 规则口径

| 常量 | 值 | 含义 |
|---|---|---|
| `BlockRunMin` | 1 → **2** | 生成期 run 长度至少 2 格 |
| `BlockSingleKeepChance` | 0.15 | 生成/修剪后残留的孤立单块仅 15% 概率保留 |
| `BlockSingleRatioMax` | 0.05 | 孤立单块占总块数 ≤ 5% 硬指标（冒烟断言 `few_single_blocks`） |
| `BlockTopReachMinRow` | 24 | 从 row 0 起 BFS 跳跃可达的最高行下限（冒烟断言 `path_to_top_ok`） |

### 6.2 实现与验证

- `PruneIsolatedSingles`：清理非骨架孤立单块，并将单块率严格压在 ≤ 5%。
- `EnsureFullPathToTop` + `ComputeTrueReachability`：按真实可站立净空（上方 2 格无实心块）与跳跃包络从地面 row 0 做 BFS 校验，若断路则自动补齐骨架段并清理遮挡。
- 验证结果：静态自检 **71/71 PASS**；无头冒烟 **10/10 断言 PASS**（`blocks=199, vertical_clear=93.5%, singles=4.0%, max_reachable_row=26, path_to_top=true`）；`MapSeedProbe` 10 组种子全部达标（`fail=0`，单块率 1.4%~4.9%，最高可达行 24~26）。

---

## 7. 同日第五轮：MAP-008 多条登顶路径 + 开发用种子 UI

### 7.1 多条登顶路径

- `GameDefs` 新增 `BackbonePathMin = 2` / `BackbonePathMax = 3`。
- `BuildBackbone()` 由单条蛇形骨架改为**每次随机 2~3 条独立骨架通道**：`BlockCols = 56` 按路径数均分成列带，每条通道只在自己的列带内左右交替推进、撞带边界折返，互不交叉；每条仍是「每隔 3 行（层间空 2 行）一段 3~4 格连排平台」，步幅 1~2 列匹配 dr3 跳跃可达半径。
- `ComputeTrueReachability()` 在 BFS 后新增 `ReachablePathCount` 统计：每条通道的顶端段只要有可达块，即算一条贯通路径；`HasPathToTop` 硬保证 ≥1 条，`multi_path_ok` 断言要求 ≥2 条，`EnsureFullPathToTop()` 在条数不足时补齐所有骨架段并清理遮挡。
- 实测 10 组种子：`paths=2/2` 或 `3/3`，全部通道贯通；固定种子 `blocks=216 / vertical_clear=94.4% / singles=2.8% / top_row=24 / paths=2/2`。

### 7.2 开发用地图种子 UI

- 复用 `UiSkin`（不新增脚本文件 / UID）：新增 `MakeSeedBadge(parent, seed, topLeft?)` / `SeedText(seed)` / `RefreshSeed(label, seed)`，用 `CanvasLayer` + `ColorRect` + `Label` 做常驻角落徽章，默认右上角锚定、可指定固定位置。
- 游戏内：`GameRoot` 在 `BeginRun` 后创建徽章，显示本次 `TimeSeed()` / 冒烟固定种子；冒烟新增断言 `seed_ui_ok` 校验显示文本与种子一致，JSON 输出 `seed` / `seed_ui`。
- 白箱：`WhiteboxRoot` 在图例下方 (16, 270) 显示种子，按 `T` 换地形时 `UiSkin.RefreshSeed` 同步刷新。**仅显示，不提供输入 / 手动指定种子**。

### 7.3 验证结果（全部实跑）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build ...` | **0 警告 0 错误** |
| 2 | `python tools/check_project.py` | **73/73 PASS** |
| 3 | `godot --headless --path ... --editor --quit` | exit 0，0 ERROR |
| 4 | `godot --headless --path ... -- --smoke` | exit 0，**PASS** |
| 5 | `... res://scenes/Main.tscn -- --smoke` | exit 0，**12/12 断言 PASS**，含 `multi_path_ok` / `seed_ui_ok` |
| 6 | `... --quit-after 120 res://scenes/Whitebox.tscn` | exit 0，0 ERROR，`paths=3/3` |
| 7 | `... --quit-after 120 res://scenes/StartPage.tscn` | exit 0，0 ERROR |
| 8 | `... --script res://tools/CSharpToolchainProbe.cs` | `ok=1 engine=4.7.2-stable` |
| 9 | `... --script res://tools/MapSeedProbe.cs` | **10/10 达标 `fail=0`**，净空 89.0%~96.2%，单块 1.0%~3.1%，`paths=2/2 或 3/3`，最高行 24~26 |

### 7.4 边界与遗留

- `scenes/StartPage.tscn` 在本轮开始前已有**外部未提交改动**（Help 文案被改为「这是一条演示的文字」，Godot 编辑器同时补了 uid / unique_id 并省略默认 `focus_mode`）。本轮**未改动、未提交该文件**；静态自检相应从「锁死按键说明文案」改为「校验 Help 节点存在」。
- 种子徽章的可见性由节点创建 + 文本断言 + 场景独立运行无错验证，未在图形界面里目视确认。
- 顶层骨架落在 row 24（地图共 27 行，层间距 3 决定 `3 + 3k` 最远到 24）；BFS 实际可达最高行 24~26（有装饰块时更高）。
- 版本号保持 `0.0003`，未升版、未打 tag。

---

## 8. 同日第六轮：MAP-009 粘液块（减速 + 跳起被立刻黏回）

> 需求原文：`随机地图新增方块： 粘液块 玩家走上去会减速 无法跳跃（做一个跳起来被立刻黏住回去的那种效果）出现概率非常小`
> 版本号仍为 `0.0003`，未打 tag。

### 8.1 方案取舍（选了啥 / 否决了啥 / 为什么）

- **选**：新增 `BlockType.Slime` 实心块、**每 run 仅 1% 命中**、全图硬上限 3 块；效果全收进共用的 `PlatformMover` 内核，靠「地板碰撞体所在组 `slime_block`」判断脚下是不是粘液，因此游戏主角与白箱**零改动自动同时生效**。
- **减速**：粘液顶速 ×0.35（8 → 2.8 格/秒）、加减速 ×0.40（起步黏滞、松键更滑）。
- **黏跳**：站在粘液上按跳只给 `SlimeHopSpeed = 6.0`（正常 18.4）的极小上跳，上升段用 `SlimeHopGravity = 60` 压低最高点，到顶点立刻切 `SlimeSnapGravity = 900` 把角色拍回台面；下压速度封顶 16，`SlimeSnapTime = 0.20s` 兜底防卡空中；黏回窗口内封掉土狼 / 缓冲二次起跳。
  - 验算：起跳帧位移 -0.1 + 逐帧 -5/-4/-3/-2/-1/0 累计 **0.35 格**，运行时实测 **0.373 格**（物理帧 delta 抖动），上限 `SlimeHopApexMax = 0.45` —— 连 1 格都上不去，等价于「跳不起来」但保留了起跳动作反馈。
- **否决**：① `Area2D` 触碰感应（那是「碰一下」事件，粘液是「站着持续生效」，还要多一套进入/退出判定）；② 在 `Master` / `WhiteboxPlayer` 各写一份（破坏「白箱手感 == 游戏手感」共用内核）；③ 完全禁止跳跃（没有起跳动作，不符合「跳起来被黏回去」）；④ 新增输入 / 纹理 / UI 资源（白箱零 PNG 纪律，配色走运行时纯色）。
- **保底 1 块**：`EnsureSlimePresence()` 在生成末把一个「非地面 / 非单向 / 非骨架」的普通装饰块改成粘液块，保证任意种子都能看到新方块且运行时断言有对象可用；只改 `_type`，不动 `_alive` / `_oneWay` / `_reachable`，因此 MAP-005~008 的净空、单块率、BFS 可达统计完全不受影响。10 组种子实测 9 组 1 块、1 组 2 块，占全图 219~286 块的 **0.35%~0.7%**，仍是「出现概率非常小」。

### 8.2 实现链

| 文件 | 改动 |
|---|---|
| `scripts/csharp/core/GameDefs.cs` | MAP-009 常量组：权重 / 组名 / 上限 / 减速 / 黏跳 / 黏回 / 高度上限；原则⑩注释改为「保底 1 块」口径 |
| `scripts/csharp/gameplay/BlockField.cs` | `BlockType.Slime`、`SceneSlime` 懒加载、`PickType` 极低权重 + 硬上限、`EnsureSlimePresence` 保底、`CountSlimeBlocks` 最终统计、`SlimeGroupedCount` 实例化分组计数、`SpawnNode` 打组、`Tint` 荧光青绿、`GetFirstSlime` 冒烟查询 |
| `scenes/blocks/block_slime.tscn` | 新建零 PNG 纯色预制体（`StaticBody2D` / `CollisionShape2D` / `Visual`，`collision_layer = 1`） |
| `scripts/csharp/gameplay/PlatformMover.cs` | `MoveTuning` 加参数并补齐 `Clone()`；`Step` 接入减速 / 极小起跳 / 黏回重力 / 封二次跳；`ApplyHorizontal` 带 `slime` 参数；`UpdateFloorContact` 遍历全部地板碰撞按组识别；`ResetWindows` 清状态；纯函数 `ComputeSlimeVertical` / `ComputeSlimeTargetSpeed` / `SimulateSlimeHop` |
| `scripts/csharp/core/GameRoot.cs` | 读取 `SlimeCount` / `SlimeGroupedCount`；新增 3.2~4.4s 冒烟阶段（传送到粘液块 → 测地面顶速 → 按跳测黏回）；新增 4 条断言与 10 个 JSON 证据字段 |
| `tools/MapSeedProbe.cs` | 10 组种子追加「粘液数量 ≤ 上限 / 分组数 == 类型数」检查，并复用 `SimulateSlimeHop` 跑黏跳纯逻辑 |
| `tools/check_project.py` | 新增 9 条 MAP-009 静态检查（常量 / 预制体 / 生成端 / 分组 / 内核 / 冒烟 / 探针 / 单一模型 / 图例） |
| `scripts/csharp/tools/WhiteboxRoot.cs` | 图例新增粘液块行（荧光青绿）+ 概率 / 保底文案；图例面板加高、种子徽章下移到 (16,314) |

### 8.3 验证结果（全部实跑）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build ShanhaiSpiritsSurvivors.csproj` | **0 警告 0 错误** |
| 2 | `python tools/check_project.py` | **82/82 PASS** → `docs/self-check.json` |
| 3 | `godot --headless --path ... --editor --quit` | exit 0，**0 ERROR** |
| 4 | `godot --headless --path ... -- --smoke` | exit 0，**PASS**（开始页延迟切主场景，16/16 断言） |
| 5 | `... res://scenes/Main.tscn -- --smoke` | exit 0，**16/16 断言 PASS** |
| 6 | `... --quit-after 120 res://scenes/Whitebox.tscn` | exit 0，0 ERROR，`blocks=263 / paths=3/3 / slime=1 slime_grouped=1` |
| 7 | `... --quit-after 120 res://scenes/StartPage.tscn` | exit 0，0 ERROR |
| 8 | `... --script res://tools/CSharpToolchainProbe.cs` | `ok=1 engine=4.7.2-stable` |
| 9 | `... --script res://tools/MapSeedProbe.cs` | **10/10 `fail=0`**，`slime=1~2` 且 `分组数==类型数` |

固定种子 `20261113` 冒烟 JSON 关键值：
`blocks=262 / vertical_clear=88.9% / singles=1.9% / paths=2/2 / slime_blocks="1/3" / slime_grouped=1`、
`slime_ground_speed=2.8`（正常顶速 8.0）、`slime_top_speed=2.8`、
`slime_hop_rise=0.373`（纯逻辑 `0.350`，上限 `0.45`）、`slime_landed_back=true`。
四条新增断言 `slime_blocks_ok` / `slime_slow_ok` / `slime_no_jump_ok` / `slime_snap_ok` 全 true。
多种子探针：`[SLIME_PROBE] target_speed=2.80 (normal=8.00) apex=0.350 snapped=True slow_ok=1 hop_ok=1`，
10 组种子净空 87.3%~95.4%、单块 0.7%~3.2%、`paths=2/2 或 3/3`、最高行 24~26，粘液 1~2 块且分组数全部相等。

### 8.4 代码审查（`godot-code-review`）

- **Critical**：无。
- **Improvements（本轮已修）**：
  1. 黏跳纯逻辑曾在 `GameRoot` 与 `MapSeedProbe` 各写一份积分，口径易漂移 → 收进 `PlatformMover.SimulateSlimeHop()` 单份实现，两处共用；首版还漏算「起跳帧当帧位移」，已补齐并据此把 `SlimeHopApexMax` 由 0.35 调到 0.45 容纳运行时 delta 抖动。
  2. `ComputeSlimeVertical` 原用 `Mathf.Min(vy + g*dt, maxFall)` 对带符号速度封顶，语义绕 → 改为显式 `next > maxFall ? maxFall : next`。
  3. `PlatformMover.OnSlimeFloor` 是未被任何调用方使用的公开属性 → 删除，状态保持私有。
- **Positive**：`StringName` 静态缓存用于每帧组判断；预制体零 PNG、纹理运行时生成；生成期用实例 `_rng` 不用全局随机；冒烟注入全在 `--smoke` 分支，不碰正常数值；`SlimeGroupedCount == SlimeCount` 作为「内核确实能识别」的硬断言。

### 8.5 边界与遗留

- 首次编辑器导入曾出现一条 `Unable to start the timer because it's not inside the scene tree`（编辑器重开上次布局时的瞬时告警，退出码 0）；重跑与最终验证链均为 **0 ERROR**，未见复现。
- 首次冒烟 `slime_no_jump_ok` 因 0.35 上限过紧失败（实测 0.373），修正模型与上限后全绿；该失败为断言口径问题，非功能缺陷。
- `scenes/StartPage.tscn` 的外部未提交改动仍在，**未改、未提交**；提交时继续排除。
- 粘液效果只改共用移动内核与地图生成，`Master` / `WhiteboxPlayer` / 输入映射 / 开始页 / 版本口径均未动。
- 保底块刻意避开骨架与地面，所以不会把唯一登顶通路变成「跳不上去」；多条路径断言保持全绿。
- 版本保持 `0.0003`，未升版、未打 tag、未 push。

---

## 9. 同日第七轮：P0 元工程整改（不含玩法改动）

范围：按 `godot-project-setup` / `godot-code-review` / `csharp-godot` / `scene-organization` /
`godot-optimization` / `godot-testing` / `export-pipeline` / `responsive-ui` 做完工程体检后，
只落地用户拍板的 P0 两项元工程整改。**未改任何 C# 逻辑、场景、数值或版本。**

### 9.1 行尾统一（P0-1）

- 根因：仓库无 `.gitattributes`，本机 `core.autocrlf=true`，`git diff` 直接告警
  `LF will be replaced by CRLF the next time Git touches it`；实测工作区 17 个文本文件 CRLF / LF 混用。
- 处置：新增 `.gitattributes`（`* text=auto eol=lf`、Godot/C#/文档文本显式 `eol=lf`、图片 `binary`）；
  仓库级 `git config --local core.autocrlf false`；把工作区 17 个残留 CRLF 文件转成 LF。
- 结果：`git update-index --really-refresh` + `git add --renormalize`（显式排除 `scenes/StartPage.tscn`）后确认
  **索引里本来就是 LF，行尾归一化没有产生任何内容 diff**，工作区只剩用户那份外部改动。
- 新增静态检查 3 条：`.gitattributes` 规则、图片标 binary、工作区无 CRLF 残留。
- 收尾修复：自检脚本原本用 `Path.write_text()` 写 `docs/self-check.json`，Windows 默认换行会把它写成 CRLF，
  使新增的「无 CRLF 残留」检查在下一次运行时**自检自伤**（复跑实测 84/85 FAIL）。
  已改为 `open(..., newline="\n")`；连续跑两次自检均 **85/85 PASS**、产物 CRLF 计数 0。

### 9.2 文档与路由卡同步（P0-2）

- `GodotPrompter` 路由卡（用户级 skill，不在本仓库）：程序集名 `ShanHaiHuanLing` → `ShanhaiSpiritsSurvivors`；
  删除「静态 `Signals` 事件总线」这条已不存在的项目架构描述；补齐 13 个 C# / 零 autoload / 零 GDScript /
  回退后基线、MAP-001~009、CLIMB-001、粘液块、版本纪律、白箱零 PNG、行尾、冒烟命令，
  以及「按键说明不放开始页」这条用户新约定。
- `docs/verification-2026-11-15.md` 第 3 节给「静态 Signals」加更正注记，第 4 节补上开始页约定。
- `docs/overview-v0.003.md` 整体重写为 0.0003 回退后基线（文件名沿用历史命名，顶部明确标注）；
  旧战斗版内容仍留在 `docs/archive/v0.003/`。
- `tools/check_project.py`：把 `overview 全景文档在 docs 根` 这条**假阳性检查**（只验文件存在，旧战斗版也能 PASS）
  改成内容校验——必须含新基线锚点、且不含旧版章节标题；开始页 Help 检查措辞改为「按键说明另处实现，不锁死文案」。

### 9.3 验证结果（全部实跑）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build ShanhaiSpiritsSurvivors.csproj --nologo` | **0 警告 0 错误** |
| 2 | `python tools/check_project.py` | **85/85 PASS**（原 82 + 新增 3）→ `docs/self-check.json` |
| 3 | headless `--editor --quit` | exit 0，**0 ERROR** |
| 4 | `-- --smoke`（开始页 → 主场景） | exit 0，**PASS**，粘液四条断言全 true |
| 5 | `res://scenes/Main.tscn -- --smoke` | exit 0，**16/16 断言 PASS**，JSON 中 0 个 false |
| 6 | Whitebox 120f | exit 0，0 ERROR，`blocks=263 / paths=3/3 / slime=1 slime_grouped=1` |
| 7 | StartPage 120f | exit 0，0 ERROR |
| 8 | `CSharpToolchainProbe.cs` | `ok=1 engine=4.7.2-stable` |
| 9 | `MapSeedProbe.cs` | **10/10 `fail=0`**；`[SLIME_PROBE] apex=0.350 snapped=True slow_ok=1 hop_ok=1` |

`godot-code-review`：本轮无 C# 逻辑改动，改动面仅 `.gitattributes` / 文档 / Python 自检脚本；
按清单核对无 Critical、无 Improvement（新增检查不进热路径、不触碰 Godot API 边界）。

### 9.4 边界

- `scenes/StartPage.tscn` 是用户自己的改动：**未改、未提交**，提交时继续排除。
  按键说明按用户规划在别处实现，不恢复 Help 原文案。
- 版本保持 `0.0003`，未升版、未打 tag、未 push。
- 本轮只做 P0；P1（灰尘特效复用、冒烟 harness 拆分、`StringName` 缓存、`[BLOCKDBG]` 清理）
  与 P2（拆 `BlockField`、`.sln` / 导出预设 / CI、UI 锚点、死资源清单）均只登记，未动。

## 10. 同日第八轮：BUGFIX-FRAGILE 脆弱木板踩不碎

### 10.1 复现（改场景之前的真实运行输出）

先只加观测点、不碰场景，给 `--smoke` 增加「传送到脆弱块上方 → 自由落下」阶段，实跑得到：

```text
[BLOCKS] ... fragile=2 fragile_wired=2
[SMOKE] 传送到脆弱木板上方 + 自由落下
  "result": "FAIL",
  "fragile_blocks": 2,
  "fragile_touch_wired": 2,
  "fragile_broken": 0,
  "fragile_blocks_left": 262,
    "fragile_break_ok": false
```

`fragile_wired=2` 说明 `Touch.BodyEntered` 两条信号都接上了，玩家也在 `master` 组、
`CollisionLayer = 1` 与 `collision_mask = 1` 匹配 → **排除「信号没接上」**，锁定感应区几何。

### 10.2 根因

`scenes/blocks/block_fragile.tscn` 的 `Touch` 感应区原本 `position = (0,-0.3)`、`size = (1,0.4)`，
覆盖 y ∈ [-0.5, -0.1]；而 1×1 方块的**顶面是 y = -0.5** —— 感应区完全埋在方块内部。
玩家站在顶面时碰撞体全在其上方，与感应区无重叠，`Area2D.BodyEntered` 永不发出，
`OnTouched → BreakBlock` 从不执行。

另有一份从未被调用的 `BlockField.AddTouchArea()` 死实现，`topOnly` 分支用的是同一组错误数值，
属于「两份几何、其中一份是错的」的误导源。

### 10.3 修复

| 文件 | 改动 |
|---|---|
| `scenes/blocks/block_fragile.tscn` | 感应区 `size = (1,0.5)`、`position = (0,-0.55)` → 覆盖 y ∈ [-0.8,-0.3]，**跨过顶面 -0.5**；补 `load_steps=3` 头，保留原 uid、零 PNG、无注释 |
| `scripts/csharp/gameplay/BlockField.cs` | 新增 `FragileCount` / `FragileTouchWiredCount` / `FragileBrokenCount` 三项可观测计数、`GetFirstFragile()` 冒烟查询；删除 `AddTouchArea()` 死代码 |
| `scripts/csharp/gameplay/BlockField.cs` | 新增 `DeferredRemove()`，`BreakBlock` / `OpenTreasure` 改用它，去掉物理回调里的同步 `RemoveChild`（否则 Godot 报 `Removing a CollisionObject node during a physics callback is not allowed`） |
| `scripts/csharp/core/GameRoot.cs` | `--smoke` 4.5~5.4s 新增脆弱块阶段：传送到可踩脆弱块上方 0.6 格、不注入输入、靠真实物理落下；新增 `fragile_break_ok` 断言与 4 个报告字段 |
| `tools/MapSeedProbe.cs` | 10 组种子追加 `FragileTouchWiredCount == FragileCount && FragileBrokenCount == 0` |
| `tools/check_project.py` | 新增 6 条检查；感应区那条是**几何行为断言**（解析 position/size 验证跨过 y=-0.5），不锁死具体数值 |
| `docs/self-check.json` | 重新生成 |

修复后同一命令：`fragile_broken=1` / `fragile_blocks_left=261`（262→261）/ `fragile_break_ok=true` / `result=PASS`，
且原先那条物理回调 ERROR 归零。

### 10.4 验证结果（全部实跑）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build ShanhaiSpiritsSurvivors.csproj --nologo` | **0 警告 0 错误** |
| 2 | `python tools/check_project.py` | **91/91 PASS**（原 85 + 新增 6）→ `docs/self-check.json` |
| 3 | headless `--editor --quit` | exit 0，**0 ERROR**（改过 `.tscn`，单独验导入） |
| 4 | `res://scenes/Main.tscn -- --smoke` | exit 0，**17/17 断言 PASS**，运行时 **0 ERROR** |
| 5 | `-- --smoke`（开始页 → 主场景） | exit 0，**PASS** |
| 6 | Whitebox 120f | exit 0，0 ERROR，`blocks=263 / paths=3/3 / slime=1 slime_grouped=1` |
| 7 | StartPage 120f | exit 0，0 ERROR |
| 8 | `CSharpToolchainProbe.cs` | `ok=1 engine=4.7.2-stable` |
| 9 | `MapSeedProbe.cs` | **10/10 `fail=0`**，每种子 `fragile=N/N fragile_ok=1` |

固定种子 `20261113`：`fragile=2 fragile_wired=2 fragile_broken=1`，`[BLOCKS] fragile broken (37,7)`。

`godot-code-review`：无 Critical。本轮按清单核对了 C# `partial` / PascalCase、信号接线（`BodyEntered` 只连一次、
lambda 捕获的是值类型参数）、`QueueFree` 而非 `Free`、`.tscn` 头 `format=3` / 无注释 / 未手写 UID、
冒烟注入只在 `SmokeMode` 生效。Improvement 已处理：物理回调内同步移除碰撞体改延迟释放、
删除带错误几何的 `AddTouchArea()` 死代码。

### 10.5 边界

- **弹跳块 / 宝箱块的 `Touch` 感应区仍是「整块 1×1、完全埋在方块内部」**，本次只修脆弱木板，未动那两个行为；
  若之后要修，应沿用同样的「跨过顶面」思路并各自补回归断言。
- 白箱默认种子 `20261112` 该图 `fragile=0`（没有脆弱木板），`wired == count` 仍成立；
  踩碎验证用固定种子 `20261113`，该图有 2 块。
- `scenes/StartPage.tscn` 是用户自己的改动：未改、未提交，提交时继续排除。
- 版本保持 `0.0003`，未升版、未打 tag、未 push。（本节记录的是第 10 轮当时的事实。）

---

## 11. 版本更新 0.0003 → 0.0004（优化地图生成）

用户明确指令：「项目版本更新 3->4 然后把目前的 push 一下 更新内容：优化地图生成」。

### 11.1 版本口径同步

| 位置 | 变更 |
|---|---|
| `project.godot` `application/config/version` | `0.0003` → `0.0004`（`StartPage.cs` 从 ProjectSettings 读取，开始页版本号自动跟随） |
| `README.md` | `Current: v0.0004`，新增 `Updates: v0.0004 — 优化地图生成` |
| `tools/check_project.py` | 文件头 `v0.0004`、`VERSION = "0.0004"` |
| `GameRoot.cs` | BOOT 日志 `v0.0004`、冒烟 JSON `["version"] = "0.0004"` |
| `docs/overview-v0.003.md` | 标题补「现行 0.0004」、版本表改为 `0.0004`；文件名与「0.0003 回退后基线」历史标题沿用 |
| `docs/self-check.json` | 由静态自检重生成，`version` 与检查名随 `VERSION` 更新 |

`tools/check_project.py` 里保留的 `"0.0003 回退后基线"` 是 overview 历史基线标题的校验锚点，不是当前版本号声明。
前 10 节里「版本保持 0.0003」是各轮当时的既成事实，按证据文档口径不回改。
版本线说明：`v0.0001` / `v0.0002` / 本次 `v0.0004` 属回退后四位小数线；
`v0.001` / `v0.002` / `v0.003` 是回退前旧战斗版 tag，`0.0003` 轮按用户拍板未打 tag。

### 11.2 本次「优化地图生成」涵盖的提交

`bd1c318` 基线整改 → `eb89a43` MAP-005 上下净空 → `5511f9b` MAP-006/007 少单块 + 登顶可达 →
`b3ccf6c` MAP-008 多路径 + 种子 UI → `b33c71b` MAP-009 粘液块 →
`f8cf84a` / `3f64561` P0 元工程整改 → `025f425` 脆弱木板踩碎修复，共 8 个此前未推送的提交。

### 11.3 升版后实测（全部实跑）

| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **91/91 PASS**（`version=0.0004`） |
| 3 | `Main.tscn --smoke` | **PASS，17/17 断言全 true**，`version=0.0004`，运行时 0 ERROR |
| 4 | 开始页 → `Main --smoke` | **PASS**，`version=0.0004` |
| 5 | 编辑器导入 | 0 ERROR |
| 6 | 白箱 / 开始页各 120 帧 | 0 ERROR；白箱 `blocks=263 / 净空 92.8% / paths=3/3 / slime=1 grouped=1` |
| 7 | `CSharpToolchainProbe` | `ok=1 engine=4.7.2-stable` |
| 8 | `MapSeedProbe` | 10 组种子 `fail=0`；`SLIME_PROBE slow_ok=1 hop_ok=1` |

### 11.4 tag / push

按版本纪律打附注 tag `v0.0004`（tag 说明「优化地图生成」），连同 `master` 上全部本地提交推送 `origin`。
`scenes/StartPage.tscn` 仍是用户自己的未提交改动，本次提交显式排除，不在推送范围内。

---

## 12. 同日第九轮：手柄支持（Input Map 补事件，游戏代码零改动）

### 12.1 决策：这是「新增」而非「优化」

此前输入是刻意极简的 4 action × 1 键（键盘）。用户拍板后按 `input-handling` §5 的推荐做法，
把手柄事件**加进同一批 Input Map action**，键盘与手柄触发同一 action —— 消费端
`Master.cs` / `WhiteboxPlayer.cs` 走 `Input.GetAxis()` / `IsActionJustPressed()` 高级 API，
**游戏代码、移动内核、白箱代码一行未改**。

- 选：只改 `project.godot` 的 `[input]`，4 个 action 补 joypad 事件，沿用现有 `deadzone=0.2`。
- 否决：在 `Master` / `WhiteboxPlayer` 里直接读 `Input.GetJoy*` 原始值（重复实现、破坏白箱与游戏共用内核）。
- 否决：新增 Autoload 输入管理器 / 事件总线（项目零 autoload 是刻意基线）。
- 否决：改 `StartPage.tscn` 加手柄提示（该文件是用户自己的改动，且按键说明另处实现；`ui_*` 是 Godot 默认映射，本身就带手柄导航）。

### 12.2 映射表

| action | 键盘 | 十字键 | 左摇杆 |
|---|---|---|---|
| `move_left` | A (physical 65) | DpadLeft (13) | LeftX −1.0 |
| `move_right` | D (physical 68) | DpadRight (14) | LeftX +1.0 |
| `move_down` | S (physical 83) | DpadDown (12) | LeftY +1.0 |
| `jump` | Space (physical 32) | A (0) | — |

### 12.3 运行时探针（临时，跑完已删）

写了一次性 `tools/JoyMapProbe.cs`，用 `InputEvent.IsActionPressed()` 纯查询验证：
Godot 4.7 实际解析出 `move_left/right/down` 各 3 事件（key + btn + axis）、`jump` 2 事件（key + btn）；
7 个手柄事件全部命中自己的 action（`result=PASS fail=0`），反向检查 A 键不判成移动、摇杆左不判成 jump
（无串味）。编译曾报 3 处签名问题（4.7 的 `ButtonIndex`/`Axis` 是枚举、`InputMap.EventIsActionPressed`
不存在），改用 `InputEvent.IsActionPressed()` 后通过。探针与 `.uid` 已删除，仓库无残留。

### 12.4 验证（全部实跑）

| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **97/97 PASS**（新增 6 条手柄映射检查：枚举值 + 方向 + 事件数 + 类型） |
| 3 | 编辑器导入 | 0 ERROR |
| 4 | `Main.tscn --smoke` | **PASS，17/17 断言**，`version=0.0004`，运行时 0 ERROR |
| 5 | 开始页 → `Main --smoke` | **PASS** |
| 6 | 白箱 / 开始页各 120 帧 | 0 ERROR；白箱 `blocks=263 / paths=3/3 / slime=1 grouped=1` |
| 7 | `CSharpToolchainProbe` | `ok=1 engine=4.7.2-stable` |
| 8 | `MapSeedProbe` | 10 组种子 `fail=0` |

### 12.5 代码审查

`godot-code-review` 第 5 节 Input Handling 清单全过：action 全走 Input Map 名、无硬编码键码、
连续移动在 `_physics_process`、无新增 autoload。无 Critical / Improvement。

- 版本保持 `0.0004`，未升版、未打 tag、未 push。
- `scenes/StartPage.tscn` 用户改动未碰、未提交、显式排除。

---

## 13. 同日第十轮：地图地形类型系统（第 1 阶段 · 骨架 + 参数接线）

### 13.1 先 grill 拍板，再动手

用户：「以后会有其他地图的类型……目前的地图生成可以加一个系统，目前这个就是默认」。
按框架先 `godot-grill` 分轮拍板（用户一度表示选项太技术化，改用大白话重问），四项决策落盘在
`docs/decisions/2026-11-16-map-terrain-system.md`：

| 决策 | 选择 |
|---|---|
| 抽象层次 | 策略：每种地形一套独立算法，共用容器/查询门面 |
| 当前地图定位 | 默认地形，规则逐字不变 |
| 参数 home | Resource `.tres` 配置卡片，Inspector 可调 |
| 选择时机 | 暂固定默认，接口/注入点留好 |

据答案推导的事实（不占用户决策）：C# net8.0、策略接口 + Resource 配置类放 `gameplay/`、
`.tres` 放 `resources/maps/`、无 autoload、种子分域不变。用户再拍本轮范围 = 第 1 阶段。

### 13.2 实现（报告再动手）

- **选**：28 个「行为参数」接线到 `MapGenConfig`（run 长度/净空率/单块率/路径数/权重/头空行数…）；
  策略口 `IMapGenerator.Generate(field, seed, config)`；`DefaultMapGenerator` 薄壳转交 `Build(seed, config)`。
- **不接**：`BlockRows/BlockCols/GroundY/WorldHalfWidth/SlimeBlockGroup` —— 前四个是 8 个网格数组的
  编译期维度与坐标基准，动它们等于重构容器（第 2 阶段才开放）；组名是地图↔移动内核的代码契约。
- **否决**：把 32 个常量全塞进卡片假装可拖（会产生「拖了没反应」的假功能）。
- **兼容**：保留 `Build(seed)` 旧入口（内部走 `CreateDefault()`），既有探针/调用零改动。
- `GameRoot` / `WhiteboxRoot` 从 `resources/maps/default.tres` 加载配置（失败兜底 `CreateDefault()`），
  走生成器建图；`MapGenerator` / `MapConfig` 暴露可写属性，以后注入别的地形不用再改调用方。
- `.tres` 由 Godot 引擎 `ResourceSaver` 生成骨架（拿到正确 `[gd_resource]` / `script_class` 格式），
  再显式写入 28 个数值——因为引擎会省略「等于属性默认值」的字段，不写就是空壳卡片。

### 13.3 行为零漂移验证（临时探针，跑完已删）

`tools/MapConfigProbe.cs` 对种子 `20261113` 做逐格 SHA256 快照：

| 场景 | 哈希 | 结论 |
|---|---|---|
| 旧入口 `Build(seed)` | `54A9B7682200DA44` | 基准 |
| 新入口 `Build(seed, CreateDefault())` | `54A9B7682200DA44` | **逐格一致，零漂移** |
| 同 seed + 同 config 再建 | 同上 | 可复现 |
| 改 config（路径 2→5、净空率 0.8→0.5、粘液权重 0.01→0.5） | `2DBE15A0493BA7DE` | **地图确实变了**：blocks 262→329、paths 2/2→5/5、slime 1→3 |

另用 `tools/VerifyMapConfigAsset.cs` 验证 Godot 能加载手写 `.tres` 且 28 字段读回正确（`ok=1`）。
两个临时探针及其 `.uid` 均已删除，仓库无残留。

### 13.4 验证（全部实跑）

| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **107/107 PASS**（新增 10 条地形系统检查；`EXPECTED_CS` 11→14） |
| 3 | 编辑器导入 | 0 ERROR；生成 3 个新脚本 `.uid` |
| 4 | `Main.tscn --smoke` | **PASS，17/17 断言**，`version=0.0004`，运行时 0 ERROR |
| 5 | 开始页 → `Main --smoke` | **PASS** |
| 6 | 白箱 / 开始页各 120 帧 | 0 ERROR；白箱 `blocks=263 / paths=3/3 / slime=1 grouped=1` |
| 7 | `CSharpToolchainProbe` | `ok=1 engine=4.7.2-stable` |
| 8 | `MapSeedProbe` | 10 组种子 `fail=0` |

### 13.5 代码审查

`godot-code-review` + `resource-pattern` + `csharp-godot`：无 Critical / Improvement。
`MapGenConfig : Resource` 用 `partial`；`DefaultMapGenerator` / `IMapGenerator` 是纯 C# 类型无需 `partial`；
`_cfg` 有 null 兜底；3 个新脚本 `.uid` 齐全；零 GDScript / 零 autoload 基线保持；无 CRLF。

### 13.6 范围边界

- 本轮只做第 1 阶段。**火山/雪山的具体算法**、**白箱切换地形热键**、**第 2 阶段把 958 行算法
  从 BlockField 搬进生成器**均 deferred，见决策记录 Open / deferred。
- 版本保持 `0.0004`，未升版、未打 tag、未 push。
- `scenes/StartPage.tscn` 用户改动未碰、未提交、显式排除。

## 14. FRAGILE-BLINK：脆弱木板闪烁 1 秒后消失（2026-11-16）

### 14.1 需求与实现选择

用户：「更新一下脆弱木板 1 秒内闪烁然后消失，不要立刻消失」。

- **选**：踩中当帧判碎（碰撞失效、`FragileBrokenCount` +1，冒烟语义与时序不变），
  视觉节点多活 `FragileBlinkDuration = 1.0s`，由绑定在 `Sprite2D` 上的一次性 Tween
  做 `FragileBlinkCycles = 3` 次「木色 ↔ 白色」交替，到时 `QueueFree`。
- **否决**：① 踩上后仍能站 1 秒再碎 —— 改变「踩碎」判定与冒烟时序，而需求只要求视觉不立刻消失；
  ② AnimationPlayer 资源动画 —— 一次性程序化效果用它过重；③ 全局 Timer —— Tween 宿主为 visual，
  换种子重建地图时自动失效，不会有游离回调打到已释放节点。

### 14.2 改动文件

| 文件 | 改动 |
|---|---|
| `scripts/csharp/core/GameDefs.cs` | 新增 `FragileBlinkDuration = 1.0f` / `FragileBlinkCycles = 3` |
| `scripts/csharp/gameplay/BlockField.cs` | `BreakBlock` 改为「立即关碰撞 + 关感应（SetDeferred）+ 统计 +1」；新增 `BlinkThenFree()` Tween 闪烁；新增起止计数并随 `Build` 复位 |
| `scripts/csharp/core/GameRoot.cs` | 冒烟脆弱块阶段延长到 6.0s；踩碎当帧取样 `_fragileBlinkSeen`（证明没立刻消失），收尾断言 `fragile_blink_finished_ok`（证明 1 秒后真的释放）；新增 5 个报告字段 |
| `scripts/csharp/tools/WhiteboxRoot.cs` | 图例改为「踩碎后闪烁 1 秒再消失」 |
| `tools/check_project.py` | 新增 5 条 FRAGILE-BLINK 静态断言；同步修正原「延迟释放收口」断言（碎块节点现在由 Tween 末尾释放） |

### 14.3 验证（全部实跑）

| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **112/112 PASS** |
| 3 | 编辑器无头导入 | exit 0，0 ERROR |
| 4 | `Main.tscn --smoke` | exit 0，**PASS，19/19 断言**，运行时 **0 ERROR** |
| 5 | 开始页 → `Main --smoke` | exit 0，**PASS**，运行时 0 ERROR |
| 6 | `MapSeedProbe` | 10 组种子 `fail=0`，脆弱块接线 `N/N fragile_ok=1` |

冒烟证据（固定种子）：`fragile_broken=1`、`fragile_blink_seen=true`、
`fragile_blink_started=1`、`fragile_blink_finished=0`（踩碎当帧）→ 收尾时 `finished=1`，
`fragile_blink_ok` 与 `fragile_blink_finished_ok` 均 true。

### 14.4 排错与代码审查

- 首版在 `Area2D.BodyEntered` 回调里直接写 `touch.Monitoring = false`，
  Godot 报 `Function blocked during in/out signal. Use set_deferred("monitoring", true/false).`；
  改为 `touch.SetDeferred("monitoring", false)` 后两条冒烟均 0 ERROR。
- `godot-code-review`：无 Critical。Tween 绑定 visual 随节点释放自动停止；
  物理回调内只改属性不移除节点，`QueueFree` 统一在 1 秒后的回调里；
  `_alive` 守卫 + 碰撞层置 0 + monitoring 延迟关闭三重防重复触发；
  C# `partial` / PascalCase / 零 GDScript / 零 autoload 基线保持。
- 范围：版本保持 `0.0004`，未升版、未打 tag；`scenes/StartPage.tscn` 用户改动未碰、未提交。

### 14.5 视觉频率微调（同日）

- 用户反馈「闪得太快」：`FragileBlinkCycles` 由 10 改为 **3**（1 秒 3 次完整闪烁，单次变色 ≈0.167s），
  高亮色由警示红改为 **`Colors.White`（白色）**。
- 判定与时序一律不动：1 秒总时长、踩中当帧判碎、1 秒后 `QueueFree` 全部保持；
  静态自检补「必须白色、不得残留警示红」的行为锁。
- 复跑：`dotnet build` 0 警告 0 错误；自检 **112/112 PASS**；`Main.tscn --smoke` **19/19 PASS**、0 ERROR；
  开始页入口冒烟 PASS、0 ERROR。

### 14.6 语义调整：闪时可站、闪完才碎（同日，**覆盖 14.1 / 14.5 的判定语义**）

14.1~14.5 保留为历史记录；以本节为现行口径。

- 用户新要求：闪 2 次、1.5 秒后消失、闪完才消失，且**闪的时候方块还能踩**。
- **选**：踩中只启动闪烁 —— 碰撞、感应、`_alive`、统计全部不动，`_blinking[r,c]` 防重复触发；
  满 1.5 秒后由 Tween 回调 `FinishFragileBreak()` 统一摘 `_alive`、`BlockCount--`、
  `FragileBrokenCount++`、`FragileBlinkFinishedCount++`、`QueueFree`。玩家在闪烁期间仍有支撑。
- **否决**：① 沿用上一版「当帧判碎 + 视觉延迟」—— 不满足「闪时还能踩」；
  ② Timer 节点 / `_PhysicsProcess` 逐帧计时 —— Tween 已绑定 Sprite，换种子重建自动失效，更稳。
- 改动文件：`GameDefs.cs`（1.5s / 2 次）、`BlockField.cs`（`StartFragileBlink` / `FinishFragileBreak` /
  `_blinking` 数组随 `Build` 复位）、`GameRoot.cs`（冒烟延到 7.0s，新增 `fragile_standable_during_blink`
  断言与基线取样）、`WhiteboxRoot.cs` 图例、`tools/check_project.py` 静态行为锁。
- 验证（全部实跑）：`dotnet build` 0 警告 0 错误；自检 **112/112 PASS**；编辑器导入 0 ERROR；
  `Main.tscn --smoke` **20/20 断言 PASS**、运行时 0 ERROR；开始页入口冒烟 PASS、0 ERROR；
  `MapSeedProbe` 10 组种子 `fail=0`。证据：`fragile_blink_started=1 / finished=1`、
  `fragile_standable_during_blink=true`、`fragile_blink_duration=1.5`、`fragile_blink_cycles=2`；
  日志顺序 `fragile blink start → fragile broken after blink`。
- `godot-code-review`：无 Critical。物理回调内不移除节点；判碎与释放都收在 Tween 结束回调里；
  `_blinking` 双守卫（`_alive && !_blinking`）防重复；Tween 宿主随节点释放自动停止，
  回调内再做状态校验；C# `partial` / PascalCase / 零 GDScript / 零 autoload 基线保持。
- 范围：版本保持 `0.0004`，未升版、未打 tag、未 push；`scenes/StartPage.tscn` 用户改动未碰、未提交。

### 14.7 当前视觉口径：纯颜色闪 3 次，零缩放（覆盖 14.6 的次数 / 视觉参数）

- 用户指出上一版有放大效果：「不要放大，只要闪烁，闪烁改成 3 次」。
- **选**：`BlinkThenFree()` 删掉全部 `scale` 补间与 `Parallel()`，循环里只剩两条 `modulate` 补间
  （木色 → `Colors.White` → 木色）；`FragileBlinkCycles = 3`，1.5 秒总时长不变（单次变色 0.25s）。
- **否决**：保留微弱缩放折中 —— 用户明确不要放大；缩放是弹跳块 `Jelly()` 的职责，脆弱块不该复用。
- 静态自检新增行为锁：`BlinkThenFree` 方法体内不得出现 `"scale"` / `Parallel`，且恰好两条 `modulate` 补间。
- 判定语义不变：闪烁期间仍是实体可站，闪完才 `FinishFragileBreak()` 判碎释放。
- 验证：`dotnet build` 0 警告 0 错误；自检 **113/113 PASS**；`Main.tscn --smoke` **20/20 PASS**、0 ERROR，
  证据 `fragile_blink_cycles=3 / fragile_standable_during_blink=true / blink_started=1 / blink_finished=1`；
  开始页入口冒烟 PASS、0 ERROR。
- 版本保持 `0.0004`，未升版、未打 tag、未 push；`scenes/StartPage.tscn` 用户改动未碰、未提交。

## 15. BOUNCE-JELLY：弹跳块蹦蹦床动态效果（2026-11-16）

### 15.1 实现选择

用户：「弹性的那个方块能做出蹦蹦床弹跳的那种动态效果吗，允许创意发挥」。

- **选**：三段式动画，只动 `Visual` 子节点——① 踩沉压扁（横向 ×1.45 / 纵向 ×0.42、下沉 0.18、
  亮黄高亮，0.08s）→ ② 过冲回弹（纵向 ×1.22、上抬 0.10、Back.Out，0.10s）
  → ③ Elastic 余震颤回原样（0.36s）。另加程序化 `Line2D` 扁椭圆冲击环，从块面扩散淡出（0.32s）。
  12 个表现参数集中在 `GameDefs`；Tween 分别绑定 visual / ring，换种子重建自动停。
- **否决**：① AnimationPlayer + 关键帧资源——多一份 `.tres`，与现有 Tween 方案和零 PNG 白箱不一致；
  ② 缩放/移动 `StaticBody2D`——会连带改碰撞形状，影响弹跳判定；③ GPUParticles2D——
  gl_compatibility + 零 PNG 下 `Line2D` 更轻、更确定。

### 15.2 过程中发现并修掉的两个既有 bug

1. **弹跳块的 Touch 感应区整个埋在方块内部**（与当年脆弱木板踩不碎同一个坑）：站在顶面时玩家碰撞体
   与感应区只在边界相切、`BodyEntered` 不触发 → 弹跳动画和弹射在真实踩踏下从来没发生。
   修 `scenes/blocks/block_bounce.tscn`：新增 `touch_rect`，`position=(0,-0.55)`、`size=(1,0.5)`，跨过顶面。
2. **弹射速度下一帧被贴地分支清零**：`Bounce()` 在 Step 后设 `vy=-22`，但下一帧 `IsOnFloor()` 仍为 true，
   `PlatformMover.Step` 的贴地分支把 `vy` 置 0，player 根本飞不起来。
   修：内核新增 `ApplyExternalLaunch()` + 一次性 `_externalLaunch` 空中标记；`Master` 与
   `WhiteboxPlayer` 统一改走该入口（白箱 = 游戏）。

### 15.3 改动文件

`GameDefs.cs`（12 个 BOUNCE 常量）、`BlockField.cs`（`Jelly` 三段式 + `SpawnBounceRing` +
`BounceJellyStartedCount` + `GetFirstBounce`）、`PlatformMover.cs`（外部弹射入口）、
`Master.cs` / `WhiteboxPlayer.cs`（弹射统一走内核）、`scenes/blocks/block_bounce.tscn`（感应区）、
`GameRoot.cs`（7.2~8.0s 弹跳冒烟阶段 + `bounce_jelly_ok`）、`WhiteboxRoot.cs` 图例、
`tools/check_project.py`（8 条 BOUNCE-JELLY 静态断言）。

### 15.4 验证（全部实跑）

| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **121/121 PASS**（新增 8 条 BOUNCE-JELLY 检查） |
| 3 | 编辑器无头导入 | exit 0，0 ERROR |
| 4 | `Main.tscn --smoke` | exit 0，**PASS，21/21 断言**，运行时 0 ERROR |
| 5 | 开始页 → `Main --smoke` | exit 0，**PASS**，0 ERROR |
| 6 | `MapSeedProbe` | 10 组种子 `fail=0` |

冒烟证据：`bounce_jelly_started=2`、`bounce_jelly_seen=true`、`bounce_skipped=false`、
`bounce_launch_rise=3.2`；日志顺序 `bounce jelly start → bounce jelly start`（下落回来二次弹跳）。
`bounce_launch_rise` 基准取传送之后，排除「瞬移上升」假阳性。

### 15.5 代码审查

`godot-code-review`：无 Critical。Tween 绑定 visual / ring，随节点释放自动停止；
物理回调内不移除节点；环播完 `QueueFree`，零 PNG / 零新资源；
`_jellyBusy` 防动画重播、不影响弹射；两个玩家统一走内核弹射入口；
C# `partial` / PascalCase / 零 GDScript / 零 autoload 基线保持。

范围：版本保持 `0.0004`，未升版、未打 tag、未 push；`scenes/StartPage.tscn` 用户改动未碰、未提交。

## 16. 随从系统第 1 块：随从定义与目录底座（2026-11-16，v0.0005）

### 16.1 术语与范围
- 用户明确：**随从 = 主角养的灵兽**（即旧设定的「幻灵」，幻灵/幻兽同源、只靠 Faction 区分），已写入用户级与项目级长期记忆。
- 用户要求「一个一个来」，本块**只做数据底座**：让游戏知道有哪些本命随从、方便以后加入新随从。
- 本块**不含**：具体随从技能（用户之后自行设计）、开局选择 UI、随从界面、跟随效果、本命升级玩法。

### 16.2 实现选择
- **选**：`SpiritDef : Resource`（`[GlobalClass]`，Inspector 可编辑）存单条随从身份数据；`SpiritCatalog` 静态类扫 `res://resources/spirits/*.tres` 自动发现，按 Id 升序。加新随从 = 丢一张定义卡进目录，**零代码改动**。
- **否决**：① 硬编码 enum/数组（加随从要改代码，违背用户意图）；② autoload 单例（项目零 autoload 铁律）；③ 巨型 Resource（resource-pattern §10 反模式）；④ 本块就做 UI / 跟随（用户要逐块来）。

### 16.3 改动文件
| 文件 | 改动 |
|---|---|
| `scripts/csharp/gameplay/SpiritDef.cs` (+ `.uid`) | 新建本命随从数据卡：`Id` / `DisplayName` / `Lore`，**零技能/升级字段** |
| `scripts/csharp/gameplay/SpiritCatalog.cs` (+ `.uid`) | 新建静态加载器：目录扫描 + Id 排序 + 重复告警 + `Find(id)` + `ClearCache()` |
| `resources/spirits/{zhulong,taotie,qiongqi}.tres` | 三张本命定义卡（烛龙/饕餮/穷奇），Lore 仅一句话客观背景 |
| `scripts/csharp/core/GameRoot.cs` | 启动加载并打印随从目录；冒烟加 `spirits_catalog_ok` 断言 + 3 个证据字段 |
| `tools/check_project.py` | `EXPECTED_CS` 14→16；新增 7 条随从系统静态检查；**移除 README 版本一致检查**（README 归用户维护） |

### 16.4 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **126/126 PASS** |
| 3 | 编辑器无头导入 | exit 0，0 ERROR，识别到 `SpiritDef` 类，两个 `.uid` 自动生成 |
| 4 | `Main.tscn --smoke` | exit 0，**PASS，22/22 断言**，运行时 0 ERROR |
| 5 | 开始页 → `Main --smoke` | exit 0，**PASS**，0 ERROR |

冒烟证据：`spirits_count=3`、`spirits_ids=qiongqi,taotie,zhulong`、`spirits_names=穷奇,饕餮,烛龙`；
日志 `catalog loaded count=3` → `spirits=3`，按 Id 升序确定性加载。

### 16.5 代码审查
`godot-code-review`：无 Critical。静态工具类（非 autoload）符合项目范式；目录仅首次访问扫描并缓存，
不在热路径 `load()`；`using var dir` 正确释放；Resource 只读共享不 mutate；C# `partial` / PascalCase /
`[GlobalClass]` 齐全；零 GDScript / 零 autoload 基线保持。

### 16.6 范围边界
版本保持 `0.0005`（本轮为 0.0005 版本规划的第 1 块，不再升版、未打新 tag、未 push）；
`scenes/StartPage.tscn` 与 `project.godot` 的用户格式改动未碰、未提交；
**README 由用户自己维护，本轮未改**（仅把静态自检里依赖 README 的检查移除）。

## 17. 随从系统第 2 块：开局选择本命灵兽 + tscn 随从实体（2026-11-16，v0.0005）

### 17.1 需求与实现选择
用户：「做开局选择本命灵兽，先用 pet.png 占位，通过换颜色区分不同随从，以后会替换美术素材；
随从生成不是单纯靠代码实现，而是 tscn」。
- **选**：① 随从实体做成 `scenes/spirits/spirit_pet.tscn`（Node2D + Sprite2D 引用 pet.png），
  选择页卡片预览与游戏内实体都实例化这同一个场景；② 换色走数据卡 `SpiritDef.Tint`，
  三张定义卡各填一色（烛龙金 ffd973 / 饕餮赤 ff736b / 穷奇青蓝 73c7ff）；
  ③ 选择页 `scenes/ui/SpiritSelect.tscn` 从 `SpiritCatalog.All` 动态生成卡片（加随从零代码），
  选中写 `SceneTree` meta，GameRoot 读 meta 后从 tscn 实例化本命。
- **否决**：① 三种随从各写一套代码 / 各建一份 tscn——违背第 1 块「数据卡驱动、加随从零代码」；
  ② 在 `StartPage.tscn` 塞选择 UI——该文件是用户自己的，只改 `StartPage.cs` 的跳转路径；
  ③ 本块做跟随 AI / 技能 / 本命升级——用户「一个一个来」，跟随效果留后续块。

### 17.2 改动文件
| 文件 | 改动 |
|---|---|
| `scripts/csharp/gameplay/SpiritDef.cs` | 加 `[Export] Color Tint` 占位色字段（无技能/升级字段） |
| `resources/spirits/{zhulong,taotie,qiongqi}.tres` | 各填一个互异 Tint |
| `scripts/csharp/gameplay/SpiritPet.cs` (+ `.uid`) | 随从实体脚本：Setup 套 Tint + 按 pet.png 像素等比缩放；暴露 AppliedTint / TexturePath |
| `scenes/spirits/spirit_pet.tscn` | 随从实体场景（Sprite2D ← pet.png），UI 与游戏共用 |
| `scripts/csharp/ui/SpiritSelect.cs` (+ `.uid`) / `scenes/ui/SpiritSelect.tscn` | 本命选择页：目录驱动建卡 + tscn 预览 + meta 选中 + 键盘/手柄焦点链 + smoke 自动选 |
| `scripts/csharp/ui/StartPage.cs` | 「开始」跳转改到选择页（只改 .cs，未碰用户的 .tscn） |
| `scripts/csharp/gameplay/SpiritCatalog.cs` | 加 `ChosenIdMeta` 常量（SceneTree meta 键，零 autoload / 零静态状态） |
| `scripts/csharp/core/GameRoot.cs` | 读 meta 选本命 → 从 tscn 实例化实体 → 冒烟 2 条断言 + 6 个证据字段 |
| `tools/check_project.py` | EXPECTED_CS 16→18；新增 7 条第 2 块静态断言 |

### 17.3 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **133/133 PASS** |
| 3 | 编辑器无头导入 | exit 0，0 ERROR，两个新 `.uid` 自动生成，注册到 `SpiritPet` 全局类 |
| 4 | `Main.tscn --smoke` | exit 0，**PASS，24/24 断言**，运行时 0 ERROR |
| 5 | 开始页 → 选择 → 游戏 整链 `--smoke` | exit 0，**PASS，24/24 断言**，0 ERROR |
| 6 | 选择页卡片换色探针（临时，跑完删） | 3 卡 / 3 色，`pet.png` 来源与逐卡 Tint 全对，`fail=0` |
| 7 | `MapSeedProbe` | 10 组种子 `fail=0` |

日志证据：`[FATE] select cards=3 ids=[qiongqi,taotie,zhulong]` → `[FATE] chosen id=qiongqi`
→ `[FATE] companion spawned id=qiongqi tint=(0.45,0.78,1) scene=res://scenes/spirits/spirit_pet.tscn`；
冒烟证据 `fate_companion_texture=res://assets/art/pet.png`、`fate_companion_tint=73c7ff`、
`spirits_tints=73c7ff,ff736b,ffd973`，`fate_companion_ok` / `spirits_tints_ok` 均 true。

### 17.4 代码审查
`godot-code-review`：无 Critical。零 autoload / 零静态状态（本命用 SceneTree meta，跨场景不丢）；
所有 GetNode 指向直接子节点；无热路径查找；C# `partial` / PascalCase / Godot 4.7 API
（`StyleBoxFlat.BorderWidthLeft` 等）均正确；定义卡只读、UI 与游戏共用一份 tscn。
唯一改进项（companion 用 `Position` → `GlobalPosition`）已修，避免 WorldRoot 将来位移时偏移。

### 17.5 范围边界
版本保持 `0.0005`，未升版、未打新 tag、未 push；`scenes/StartPage.tscn` 用户改动未碰、未提交；
README 由用户自己维护，本轮未改。跟随 AI / 技能 / 本命升级仍未实现。

## 18. 随从系统第 3 块：升级界面基础版 + 单槽存档（2026-11-16，v0.0005）

### 18.1 需求与拆分
用户给出 v0.0005 完整蓝图（魂魄加点 / 主角二叉树 / 随从局外养成 / 局内吸血鬼式领取合成 / 存档），
明确「可以拆开来做」。本轮做**第 3 块：升级界面基础版**——数据底座 + 能打开的界面 + 魂魄→加点→存盘闭环。
决策留档 `docs/decisions/2026-11-16-upgrade-system.md`（含四项现场拍板口径）。

### 18.2 实现选择
- **选**：JSON 单槽存档（user:// + version + 迁移钩子）；每本命一张 `UpgradeTreeDef.tres`
  平行数组存二叉树（ Inspector 可编辑、加卡零代码）；开始页「养成」按钮 → `UpgradeScreen.tscn`，
  顶部三本命页签 + 左主角树 + 右随从局外养成（等级/血量/攻击/敏捷）；魂魄唯一货币、默认 300。
- **否决**：Resource/.tres 存玩家数据（加载会执行脚本，不安全）；autoload；硬编码 enum 树；
  本块做局内领取/合成或战斗掉落（第四/五块）；本块让局外属性作用于战斗（项目尚无 HP 概念，会假实现）。

### 18.3 改动文件
| 文件 | 改动 |
|---|---|
| `scripts/csharp/gameplay/SaveData.cs` (+uid) | `GrowthStat` 枚举 + `SpiritGrowth`（四项基值/步长/上限）+ `SaveData`（Version/Souls/HeroNodes/Growth） |
| `scripts/csharp/gameplay/SaveGame.cs` (+uid) | 单槽 JSON 存档：读档/建档/迁移钩子/补齐新随从槽/自动写盘/魂魄与加点与养成的原子扣费 |
| `scripts/csharp/gameplay/UpgradeTreeDef.cs` (+uid) | `[GlobalClass]` 树定义卡：平行数组二叉树、前置解锁、IsValid 结构自检、缺卡兜底 |
| `scripts/csharp/gameplay/UpgradeCatalog.cs` (+uid) | 扫 `resources/upgrades/*.tres` 按 SpiritId 索引，加卡零代码、缺卡兜底、重复告警 |
| `resources/upgrades/{zhulong,taotie,qiongqi}.tres` | 三本命各 5 节点二叉树，初期全加血量、结构与耗费互异 |
| `scripts/csharp/ui/UpgradeScreen.cs` (+uid) / `.tscn` | 养成界面：三本命页签 + 主角树（缩进层级）+ 四项局外养成 + 魂魄栏 + 自动冒烟 |
| `scenes/StartPage.tscn` / `scripts/csharp/ui/StartPage.cs` | 新增「养成」按钮（保留用户 Help 文案原样，只下移避让）+ 跳转接线 |
| `scripts/csharp/core/GameDefs.cs` | 魂魄初值 300 + 局外成长四项基值/步长/上限/耗费常量 |
| `scripts/csharp/core/GameRoot.cs` | 启动读档 + 树校验日志；冒烟加 `save_loaded_ok` / `upgrade_trees_ok` 与 4 个证据字段 |
| `tools/check_project.py` | EXPECTED_CS 18→23；新增 7d 段（存档/树卡/目录/界面/入口/边界） |

### 18.4 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **143/143 PASS** |
| 3 | 编辑器无头导入 | exit 0，0 ERROR，5 个新 `.uid` 生成，注册到 `UpgradeTreeDef` 全局类 |
| 4 | `UpgradeScreen.tscn --smoke` | exit 0，**6/6 PASS**，0 ERROR |
| 5 | `Main.tscn --smoke` | exit 0，**26/26 PASS**（新增 2 条），0 ERROR |
| 6 | 开始页 → 选择 → 游戏 整链 `--smoke` | exit 0，**26/26 PASS**，0 ERROR |
| 7 | `MapSeedProbe` | 10 组种子 `fail=0` |

升级界面冒烟证据（`UPGRADE_SMOKE_JSON`）：`souls_before=300 → after=222`、
`tree_count=3 ids=[qiongqi,taotie,zhulong]`、`hero_node=root level=1`、
`growth_level=2`、`reloaded_hero=1 / reloaded_growth=2`（写盘→丢缓存→重读一致）。
主场景证据：`save_version=1`、`upgrade_tree_nodes=qiongqi:5,taotie:5,zhulong:5`。
真存档文件核对：`%APPDATA%\Godot\app_userdata\山海幻灵\shanhai_save.json` 含
`Version/Souls/HeroNodes/Growth` 四段，三本命各自一份成长；冒烟结束已复位到 300 魂、零加点。

### 18.5 代码审查
`godot-code-review`：无 Critical 遗留。审查中发现并修掉一处「扣魂 + 加点」两次写盘且存在中间态的问题，
改为内部 `TrySpendSouls(cost, saveNow:false)`，复合操作一次落盘。其余：零 autoload、静态工具范式一致、
JSON 带版本号与迁移钩子、先校验余额再变更、`GetNode` 全在 `_Ready()` 缓存、动态节点 `QueueFree`、
页签从目录生成零代码；C# `partial` / PascalCase / 零 GDScript 基线保持。

### 18.6 范围边界
版本保持 `0.0005`，未升版、未打新 tag、未 push；README 由用户维护、本轮未改。
局内随从领取/合成、魂魄真实掉落、跟随 AI、技能设计均未实现（逐块推进）。
`project.godot` 的用户格式改动未碰、未提交；StartPage.tscn 本轮按用户授权修改，Help 文案原样保留。

## 19. 小插曲：局内 ESC 暂停菜单（2026-11-16，v0.0005）

### 19.1 需求与决策
用户要求「游戏内加一个按 ESC 出现 UI，里面有继续 / 设置 / 退出」。决策留档
`docs/decisions/2026-11-16-pause-menu.md`：选 `godot-ui` 的 pause menu overlay + `input-handling` 的
`_UnhandledInput` 原生 ESC；否决新增 Input Map action（不碰用户未提交的 `project.godot`）、
`_input` 抢事件、纯代码拼 UI、真关窗、设置页塞未点名配置项。

### 19.2 改动文件
| 文件 | 改动 |
|---|---|
| `scripts/csharp/ui/PauseMenu.cs` (+uid) | `CanvasLayer` 脚本：ESC 事件过滤、暂停状态、继续/设置/返回/退出、焦点环、独立冒烟九项断言 |
| `scenes/ui/PauseMenu.tscn` | 场景版式：`process_mode=3` + 半透遮罩 + 居中面板 + 三按钮 + 设置占位页；SystemFont/StyleBoxFlat 子资源 |
| `scenes/Main.tscn` | 实例挂载 `PauseMenu`，默认隐藏；不入侵 GameRoot 代码 |
| `tools/check_project.py` | EXPECTED_CS 23→24；新增 7e 段五项静态检查（场景/ProcessMode/输入/边界/冒烟） |

### 19.3 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `godot --headless --path ... --editor --quit` | exit 0，0 ERROR；新 `.cs.uid` 已生成 |
| 3 | `tools/check_project.py` | **148/148 PASS** |
| 4 | `Main.tscn -- --pause-smoke` | exit 0，**9/9 PASS**，0 ERROR |
| 5 | `Main.tscn -- --smoke` | exit 0，**26/26 PASS**（原有断言全绿） |
| 6 | 开始页 → 选择 → 游戏 整链 `-- --smoke` | exit 0，**26/26 PASS** |

暂停冒烟证据（`PAUSE_SMOKE_JSON`）：`initial_hidden`、`esc_open`、`settings_page`、`settings_back`、
`resume`、`esc_toggle`、`esc_event_filter`（按下开/松开不误触/再按关）、`focus_ready`、
`quit_to_start_target` 全 true；`process_mode_always=true`，`quit_target=res://scenes/StartPage.tscn`。

### 19.4 代码审查（`godot-code-review`）
无 Critical / Improvement 遗留。对照清单：单一职责（只管暂停菜单，无战斗/存档耦合）、
`GetNode<T>()` 全部在 `_Ready()` 缓存且逐层下钻、`partial` + PascalCase、无热路径 GetNode、
`_UnhandledInput` 让 UI 先消费、暂停节点 `ProcessMode=Always`、按钮 `FocusMode.All` 且焦点成环、
信号在 `_Ready()` 接、`CallDeferred` 只用于可见性切换后的抓焦点。
有意偏离：ESC 用原生键码而非 Input Map action（用户 `project.godot` 未提交，手柄映射后续统一做，
同项目已有的白箱热键偏离惯例）。

### 19.5 范围边界
版本保持 `0.0005`，未升版、未打新 tag、未 push；README 用户自管；
`project.godot` 与 `StartPage.tscn` 的用户改动未碰、未提交。
「设置」仅占位页；手柄暂停键、读数持久化等功能都未实现。

## 20. 局内随从界面：ESC 双栏 + 三选一领取/合成（2026-11-16，v0.0005 第四块 UI）

### 20.1 需求与拍板
用户「接下来做局内的随从界面」。按 `godot-grill` 分两轮拍板，决策留档
`docs/decisions/2026-11-16-inrun-spirit-ui.md`：三选一 + 大面板；界面 + 可跑逻辑（模拟触发）；
纯占位卡片；领取新随从 / 同名合成资质 +1；当局临时不落盘。
第二轮用户改口径：大面板不单独占键，改成「ESC 原来的内容放左边，大界面放右边」——
覆盖早先的 Tab/F 呼出方案，已写进决策记录。

### 20.2 实现选择
- **选**：`Main.tscn` 挂三个 UI 层——`CompanionHud`（layer 1，「调试：模拟升级」按钮）、
  `PauseMenu`（layer 10，改双栏）、`CompanionOffer`（layer 15，三选一）；
  数据由 `GameRoot.Companions`（纯 C# `RunRoster`，当局临时、零 autoload）单点持有。
- **否决**：autoload / 静态可变状态；单独占 Tab/F 键；写存档；编技能名或属性效果；
  常驻随从小面板（用户明确用 ESC 大栏代替）。
- **默认值（用户未点名，可改）**：队伍 4 格、候选池 A~F 六个占位随从。

### 20.3 改动文件
| 文件 | 改动 |
|---|---|
| `scripts/csharp/gameplay/RunRoster.cs` (+uid) | 当局队伍：选新加入 / 同名合成资质+1 / 随机抽 3 张；零技能零存档 |
| `scripts/csharp/ui/CompanionOffer.cs` (+uid) / `.tscn` | 三选一弹窗：暂停→占位卡→点卡写队伍→恢复；左右焦点环；`--offer-smoke` 六项断言 |
| `scripts/csharp/ui/CompanionHud.cs` (+uid) / `.tscn` | 局内「调试：模拟升级」按钮，替代未做的战斗升级触发 |
| `scripts/csharp/ui/PauseMenu.cs` / `.tscn` | ESC 改双栏：左窄继续/设置/退出，右大队伍列表（空位/领取/合成三态）；ESC 在三选一期间被吞掉 |
| `scripts/csharp/core/GameRoot.cs` | 持有 `RunRoster`，每次开局 `Clear()`，不碰存档 |
| `scenes/Main.tscn` | 新增 HUD / 弹窗两个实例 |
| `tools/check_project.py` | EXPECTED_CS 24→27；新增 7f 段七项静态检查；7e 补 `esc_blocked_by_offer` |

### 20.4 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `godot --headless --path ... --editor --quit` | exit 0，0 ERROR；3 个新 `.cs.uid` 生成 |
| 3 | `tools/check_project.py` | **155/155 PASS** |
| 4 | `Main.tscn -- --offer-smoke` | exit 0，**6/6 PASS**，0 ERROR |
| 5 | `Main.tscn -- --pause-smoke` | exit 0，**13/13 PASS**，0 ERROR |
| 6 | `Main.tscn -- --smoke` | exit 0，**26/26 PASS** |
| 7 | 开始页 → 选择 → 游戏 整链 `-- --smoke` | exit 0，**26/26 PASS** |

证据：`OFFER_SMOKE_JSON` 的 `pick_new_joins=true / pick_same_merges=true / quality_A=2`；
`PAUSE_SMOKE_JSON` 的 `roster_panel / roster_join / roster_merge / esc_blocked_by_offer` 全 true。

### 20.5 代码审查（`godot-code-review`）
无 Critical / Improvement 遗留。审查中发现并修掉一个交互漏洞：三选一弹窗打开时按 ESC 会叠出暂停菜单，
现在 `PauseMenu._UnhandledInput` 先检查弹窗 `IsOpen` 并吞掉事件；`CompanionOffer.Cancel()` 供冒烟复位。
其余：`GetNode<T>()` 全在 `_Ready()` 缓存且逐层下钻、按钮焦点成环并 `grab_focus()`、
`ProcessMode=Always` 覆盖暂停期、动态行 `RemoveChild + QueueFree`、零 autoload / 零静态可变状态、
`partial` + PascalCase、卡片文案无技能。有意偏离仍是原生键码 ESC（用户 `project.godot` 未提交）。

### 20.6 范围边界
版本保持 `0.0005`，未升版、未打新 tag、未 push；README 用户自管；
`project.godot` 用户改动未碰、未提交。战斗、魂魄掉落、真升级触发、技能设计、局内跟随 AI、
手柄 UI 映射均未实现；队伍只当局有效，退出即清。

## 21. ESC 面板改近全屏版式（2026-11-16，v0.0005）

### 21.1 需求与拍板
用户「我想要的 esc 是那种几乎全屏的，因为以后那块地方会设计特别大」。
按 GodotPrompter 先报方案再动手：
- **选**：`Center` 由 `CenterContainer` 改 `MarginContainer`（四边 24px），
  `Panel` 去掉 `960×480` 固定最小尺寸改随视口拉伸，左窄栏仍固定 240，
  右大栏横向 + 纵向 `size_flags=3`（expand-fill）吃掉所有剩余空间。
- **否决**：贴边 100% 全屏（失层次，用户说的是「几乎」）；单纯放大固定尺寸
  （不自适应分辨率，正不合「以后设计特别大」）；改 `project.godot` 窗口尺寸
  （用户改动未提交）；顺手重排按钮 / 加设置项（本轮只改版式）。

### 21.2 改动文件
| 文件 | 改动 |
|---|---|
| `scenes/ui/PauseMenu.tscn` | `Center` 改 `MarginContainer` 四边 24px；`Panel` 最小尺寸 640×360 + expand-fill；`VBox`/`Columns`/`Left`/`Right` 纵向拉伸；按钮与路径全部不变 |
| `scripts/csharp/ui/PauseMenu.cs` | `_Ready` 取节点类型同步 `MarginContainer`，缓存 `_panel`；冒烟新增 `near_fullscreen` 结构断言与版式报告字段 |
| `tools/check_project.py` | 新增近全屏版式静态检查（四边 margin、无旧固定尺寸、expand-fill、断言字段） |

### 21.3 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `godot --headless --editor --quit` | 0 ERROR |
| 3 | `tools/check_project.py` | **156/156 PASS** |
| 4 | `Main.tscn -- --pause-smoke` | **PASS**，14 项断言含 `near_fullscreen=true` |
| 5 | `Main.tscn -- --offer-smoke` | **PASS** |
| 6 | `Main.tscn -- --smoke` | **PASS** |
| 7 | 开始页整链 `-- --smoke` | **PASS** |

`near_fullscreen` 采用**结构性验证**（`Center` 全屏锚点 + 四边 24px + 面板与右栏 expand-fill），
不用实测像素：headless 下可见矩形报 `1280×1280`（非项目设定的 1280×720），且面板刚显示时
布局尚未重排，读到的 `headless_panel_size=948×383` 是旧值，不能作为判定依据；
实测数据仅留在冒烟报告里作参考。

### 21.4 代码审查（`godot-code-review`）
无 Critical / Improvement 遗留。节点路径逐层下钻并在 `_Ready()` 缓存；新增 `_panel` 字段只用于
冒烟报告与结构断言，不进热路径；`IsOpen` 仍以 `_root.Visible` 为准；ESC 语义、弹窗拦截、
焦点环、退出目标均未动。C# 绑定差异（无 `GetThemeConstantOverride`、`SizeFlags` 为枚举）
已改用 `Node.Get(...).AsInt32()` 与枚举直接比较解决。

### 21.5 范围边界
版本保持 `0.0005`，未升版、未打新 tag、未 push；README 用户自管；
`project.godot` 用户改动未碰、未提交。右栏大区域目前仍是占位队伍列表，
等用户以后自己设计具体内容；本轮没加任何设置项、技能或战斗逻辑。

## 22. 随从跟随（SPIRIT-FOLLOW，2026-11-16，v0.0005）

### 22.1 需求与拍板
用户要求做随从跟随，参考 `D:\GameProjects\BoneraiserGodot` 的随从逻辑；随后追加两条硬约束：
「是有碰撞体的，主角和随从不能叠在一起，以后加入敌人系统也是这一段逻辑」、
「玩家可以踩在随从头上」。四问拍板：本命 + 队友都跟随 / **踩平台走** / 散点编队 / 轻避障。
决策留档 `docs/decisions/2026-11-16-spirit-follow.md`。

### 22.2 实现选择
- **选**：随从改 `CharacterBody2D` + `CapsuleShape2D`（真实碰撞体）；新增 `LayerActor(4)` /
  `LayerSpirit(8)` 两层；跟随逻辑独立成 `SpiritFollow` 组件；实体同步独立成 `SpiritFollowGroup`。
- **参考 Boneraiser 并移植**：黄金角个人落点、闲逛漂移、呼吸浮动、越远越快追赶、死区停。
- **横板补充**：跨层起跳、卡住自救跳、单向平台主动下穿、掉队/坠落瞬移。
- **否决**：悬浮直飞（用户选踩平台）；随从之间硬碰撞（会互相卡死）；
  `Area2D` 当碰撞体（站不住，踩不了头）；主角 mask 不含随从层（踩不上头）；
  给队友编技能/数值（越界）。

### 22.3 碰撞与「踩头」实现
| 角色 | CollisionLayer | CollisionMask |
|---|---|---|
| 主角 | `LayerSolid \| LayerActor` (5) | `Solid \| OneWay \| Actor \| Spirit` (15) |
| 随从 | `LayerSpirit` (8) | `Solid \| OneWay \| Actor` (7) |

- 主角 layer 保留 `LayerSolid`：脆弱/弹跳/宝箱的 Area2D 用 `mask=1` 识别玩家，不能改。
- 主角 mask 含 `LayerSpirit` → `IsOnFloor()` 能检测到随从 → **可以踩在随从头上**。
- 随从 `ProcessPhysicsPriority = -10`（先于主角）；靠近主角 0.85 格内让路并给满速；
  每帧 `SeparateFrom` 沿水平轴硬分离 → 永不与主角同位。
- 被踩：头顶薄矩形形状查询命中 `LayerActor` → 水平速度归零、停止跟随，玩家离开后恢复。

### 22.4 改动文件
| 文件 | 改动 |
|---|---|
| `scripts/csharp/gameplay/SpiritFollow.cs` (+uid) | 新增：平台跟随 AI（落点/漂移/呼吸/追赶/跳跃/下穿/自救/瞬移/让路/硬分离/被踩） |
| `scripts/csharp/gameplay/SpiritFollowGroup.cs` (+uid) | 新增：本命 + `RunRoster` 实体同步、槽位重排、队友占位色、`--follow-smoke` |
| `scripts/csharp/gameplay/SpiritPet.cs` | 改继承 `CharacterBody2D`；设层与碰撞体；新增 `SetupCompanion` / `UpdateCompanionStats` |
| `scenes/spirits/spirit_pet.tscn` | 根改 `CharacterBody2D` + `CollisionShape2D`(Capsule 0.31/0.88) + `Follow` 子节点 |
| `scripts/csharp/core/GameDefs.cs` | 新增 `LayerActor` / `LayerSpirit` 与 19 个跟随参数 |
| `scripts/csharp/gameplay/Master.cs` | 碰撞层改用角色层（保留 `LayerSolid`，mask 含 `LayerSpirit`） |
| `scripts/csharp/core/GameRoot.cs` | 随从生成改由 `SpiritFollowGroup`；新增 `RefreshCompanions`；冒烟加两条跟随断言与证据 |
| `scripts/csharp/ui/CompanionOffer.cs` | 选卡成功后调用 `RefreshCompanions()`，场上实体立刻跟上 |
| `scenes/Main.tscn` | 挂 `SpiritFollowGroup` 节点 |
| `tools/check_project.py` | `EXPECTED_CS` 27→29；7f 段新增 7 项跟随检查；修正第 2 块两条旧检查 |

### 22.5 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `godot --headless --editor --quit` | 0 ERROR；2 个新 `.cs.uid` 生成 |
| 3 | `tools/check_project.py` | **163/163 PASS** |
| 4 | `Main.tscn -- --follow-smoke` | **7/7 PASS**，连跑 3 次 `max_distance` = 1.327 / 2.873 / 2.847 |
| 5 | `Main.tscn -- --smoke` | **PASS**，新增 `follow_entity_ok` / `follow_no_overlap_ok` 全 true |
| 6 | 开始页 → 选择 → 游戏 整链 `-- --smoke` | **PASS**（含跟随断言） |
| 7 | `--pause-smoke` / `--offer-smoke` | 均 **PASS**（未被改动破坏） |

证据：`FOLLOW_SMOKE_JSON` 的 `fate_plus_roster / converge_near_player / never_overlap /
catch_up_when_far / recall_when_lost / actor_layers / has_collision_body` 全 true；
主冒烟 `follow_pets=1`、`follow_layers="layer=8 mask=7"`。

### 22.6 代码审查（`godot-code-review`）
无 Critical / Improvement 遗留。审查中修掉两处：
1. `SpiritFollowGroup` 有个从未使用的 `_geometry` 字段（原计划轻避障用，物理碰撞已覆盖）→ 删除；
2. 让路时速度仍按 `Base + dist×CatchUp` 算，主角冲过来时随从会被顶着走 → 让路分支直接给满速。
其余：`GetParent<SpiritPet>()` 在 `_Ready` 缓存、节点路径逐层下钻、`QueueFree` 前先移出列表、
`Bind` 幂等、`Refresh` 幂等、`async void` 冒烟有出口、无 autoload / 无静态可变状态、
零技能零数值。有意偏离：呼吸浮动只动 Sprite（不动物理体），避免与落点互相拉扯。

### 22.7 范围边界
版本保持 `0.0005`，未升版、未打 tag、未 push；README 用户自管；
`project.godot` 用户改动未碰、未提交。
未实现：战斗、敌人、随从攻击、技能、真实掉落、随从之间的互推可视化。

## 23. 随从让路系统优化 v2（2026-11-16，v0.0005）

### 23.1 用户反馈
「目前的随从 AI 非常容易挡路。比如玩家向左走、然后随从刚好在玩家的左边，会主动让路——
意思就是你去优化一下让路系统」。

### 23.2 根因（三条，缺一不可）
1. **让路方向错了**：旧版是「从主角径向退开」。主角向左走、随从也在左边时，径向退同样是向左，
   于是它一路退、一路挡在主角前面——越让越挡。
2. **落点固定在一侧**：`_slotOffset` 永远朝 +X（槽位 0 在主角右侧 1.05 格），
   主角朝那一侧走就必然从随从身上碾过去。
3. **物理上互挡**：主角 `mask` 含 `LayerSpirit`，主角 `MoveAndSlide` 会被随从顶住；
   让路再快也快不过同一帧的物理碰撞。

### 23.3 改动
| 改动 | 做法 |
|---|---|
| 让路方向 | 新增「前进方向通道盒」：主角速度 > 0.6 时，前方 1.9 格 × 上下 0.95 格内的随从一律**让到主角身后 1.25 格**；贴太近（0.85 格）再叠一层径向兜底 |
| 落点跟随朝向 | 整组落点按主角朝向镜像到身后：`dynamicSlot.X = _slotOffset.X * -_facingX`，带 0.45 秒滞回防左右横跳 |
| 让路跳 | 让路且贴得近（前方 < 0.95 格）时直接从主角头顶跃过去，0.9 秒冷却防连跳 |
| 提前分离 | `SeparateFrom` 额外留 0.12 格余量，真正撞上之前就推开；主角在正上方（踩头 / 越过）时不横推 |
| **碰撞解耦** | 新增 `LayerSpiritTop = 16`：随从头顶一块 `StaticBody2D` 小平台（0.66×0.10）；主角 `mask` 改为含 `LayerSpiritTop`、**去掉 `LayerSpirit`** → 水平永远不被随从顶住，头上照样能踩 |

### 23.4 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **165/165 PASS** |
| 3 | `--follow-smoke` 连跑 5 次 | **PASS**，`yield_travel` 全部 **5.71 格**（满速上限约 5.7，等于零阻挡） |
| 4 | `--follow-smoke` 踩头 | `ride_on_head=true`（主角落在随从头顶、不落到地面） |
| 5 | `Main --smoke` / 整链 `--smoke` | PASS，`player_moved` / `follow_entity_ok` / `follow_no_overlap_ok` 全 true |
| 6 | `--pause-smoke` / `--offer-smoke` | 均 PASS |

冒烟细节：挡路测试先把主角与随从放到第一层全覆盖地板正中（x=0，排除地形干扰），
随从摆在主角正左方 0.45 格，按住左键 45 帧，断言主角走出 > 2.0 格。

### 23.5 范围边界
版本保持 `0.0005`，未升版、未打 tag、未 push；`project.godot` 未碰。
未实现：战斗、敌人、随从攻击、技能、掉落。

## 24. 随从原地连跳修复 + 踩踏平台单向化（2026-11-16，v0.0005）

### 24.1 用户反馈
「玩家走一下然后停住，但是随从会原地跳跳跳不停」。

### 24.2 根因（四条）
1. **槽位纵向偏移过大**：`_slotOffset.Y` 最负到约 -1.13 格，而随从站地面时中心只比主角低 0.16 格，
   于是目标永远在头顶 1.0+ 格 → 每帧都满足跨层跳条件 → 落地就跳、跳完落下再跳，原地死循环。
2. **跨层阈值太松**：`dy < -0.70` 就跳，站同一地面就能触发。
3. **让路跳没要求主角在动**：玩家停下后 `yielding`（贴身兜底）仍可能为真，继续跳。
4. **自救跳条件太松**：`|dx| > 0.4` 且无独立冷却，归位途中的微小卡顿就触发。

另发现一个连带问题：**头顶踩踏平台在主角胸口高度且是双向碰撞**，主角走过去会撞上（实测挡路、
踩头不稳）；且平台只有 0.1 格厚，主角高速下落会**隧穿**。

### 24.3 改动
| 改动 | 做法 |
|---|---|
| 槽位夹紧 | `dynamicSlot.Y = Clamp(_slotOffset.Y, -0.45, 0.10)` —— 限制在「站同一地面够得到」的范围 |
| 跨层阈值 | 从 0.70 提到 **0.90 格**，只有真正上一层才跳 |
| 让路跳 | 追加 `playerMoving` 前置条件，玩家停下就不再让路跳 |
| 自救跳 | 触发距离 0.4 → **1.0 格**，并新增独立冷却 **1.2 秒** |
| 踩踏平台 | `one_way_collision = true`（上方可踩、侧面穿过）+ 厚度 0.1 → **0.36 格**（防高速隧穿） |
| 冒烟 | `SpiritFollow.JumpCount` 暴露起跳次数；新增 `idle_no_hopping` 与 `ride_on_head` 实测 |

### 24.4 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **166/166 PASS** |
| 3 | `--follow-smoke` 连跑 **6 次** | 全部 **PASS**：`idle_jumps` 恒 **0**、`yield_travel` 恒 **5.71**、`ride_on_head` 恒 **true** |
| 4 | `Main --smoke` / 整链 `--smoke` | PASS，`player_moved` / `follow_entity_ok` / `follow_no_overlap_ok` 全 true |
| 5 | `--pause-smoke` / `--offer-smoke` | 均 PASS |

「静止不跳」的测法：玩家站第一层地板不动 2.5 秒，统计三只随从 `JumpCount` 增量，要求 ≤ 2。

### 24.5 范围边界
版本保持 `0.0005`，未升版、未打 tag、未 push；`project.godot` 未碰。
未实现：战斗、敌人、随从攻击、技能、掉落。

## 25. 随从之间加入碰撞体（2026-11-16，v0.0005）

### 25.1 需求
「随从和随从之间也是有碰撞体的，随从不会和随从重叠」。

### 25.2 改动
| 改动 | 做法 |
|---|---|
| 碰撞层 | 随从 `mask` 加入 `LayerSpirit`（7 → **15**），场景 `collision_mask` 同步；随从彼此互相碰撞 |
| 落点 | 黄金角改为**固定五格**（本命 + 最多 4 队友）：`(±1.55,-0.15) / (±0.75,-0.45) / (0,-0.75)`，任意两格最小间距 **0.81 格** > 碰撞宽度 0.62；超编时退回黄金角外扩 |
| 跳跃判定 | 从「落点高度差 `dy`」改为「**主角与随从的实际高度差 `playerDy`**」——队形落点可以抬高，但绝不因落点高度诱发跳跃（否则原地连跳会复发） |
| 软分离 | 新增 `SeparateFromPets`：按胶囊真实形状算两两距离，不足碰撞宽度就把自己推开（只推自己，避免对称加力抖动），补上物理 `safe_margin` 允许的轻微穿透 |
| 冒烟 | 满编 5 只（本命 + A/B/C/D）；新增 `spirit_no_overlap` 与 `min_pet_distance` 证据 |

### 25.3 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **167/167 PASS** |
| 3 | `--follow-smoke` 连跑 **6 次**（满编 5 只） | 全部 **PASS**，`min_pet_distance` 恒 **0.64**（碰撞宽度 0.62，稳定不重叠）、`idle_jumps` 恒 **0**、`yield_travel` 恒 **5.71** |
| 4 | `Main --smoke` / 整链 `--smoke` | PASS，`player_moved` / `follow_entity_ok` / `follow_no_overlap_ok` 全 true |
| 5 | `--pause-smoke` / `--offer-smoke` | 均 PASS |

判定口径：`min_pet_distance` 用胶囊真实形状（竖直线段 + 半径）计算，不是 AABB；
≥ 0.62（`SpiritBodyWidth`）即判定为不重叠。加软分离前实测 0.594/0.609（物理 `safe_margin` 允许的穿透），
加后稳定 0.64。

### 25.4 范围边界
版本保持 `0.0005`，未升版、未打 tag、未 push；`project.godot` 未碰。
未实现：战斗、敌人、随从攻击、技能、掉落。

## 26. 随从防卡死：BFS 寻路找回（2026-11-16，v0.0005）

### 26.1 需求
「加一个防止宠物卡死的机制，当宠物长时间离玩家很远的时候，可能是路卡死了回不去，
就 BFS 搜索一下回玩家身边的方法」。
追加：「宠物的移速、敏捷之类的以后会影响跳跃的距离、影响允许移动的距离长度，需要考虑这个，优化一下 BFS」。

### 26.2 实现
| 部分 | 做法 |
|---|---|
| 网格接口 | 新增 `IStandableGrid : IBlockGeometry`（`IsStandableCell` / `StandPoint`），`BlockField` 实现；「可站立」复用既有 `CanStandOn`（有实体块 + 上方净空） |
| BFS | 新增 `SpiritPathfinder`：节点 = 可站立方块，边 = 平地走 1 格 / 跳上 / 下落；`Dictionary + HashSet + Queue` 回溯路径；上限 3000 节点 |
| **能力参数化** | `SpiritNavProfile`（MaxRise / MaxSpan / MaxDrop）由 `Default` 或 `FromStats(移速, 敏捷)` 给出。公式与移动内核同口径：跳高 = v²/(2g)、滞空 = 2v/g、水平跨度 = 移速 × 滞空。BFS 只读档案，**不写死跳跃距离** |
| 触发 | 离玩家 > 6 格持续 2.5 秒 → 进入寻路模式（给正常跟随留追赶时间） |
| 执行 | 每 0.6 秒重算路径，沿路径点逐点走（到达 0.45 格内切下一个）；路径点在上方时照常跳 |
| 兜底 | 寻路模式超过 5 秒仍未回到 → 瞬移；原有 13 格瞬移与坠落重生保留为最后一道 |
| 属性接入 | `SpiritFollow.SetNavProfile(profile)` / `SetMoveStats(moveSpeed, agility)`：以后随从养成改数值，调一次即可，BFS 与跟随逻辑零改动 |

### 26.3 改动文件
| 文件 | 改动 |
|---|---|
| `scripts/csharp/gameplay/SpiritPathfinder.cs` (+uid) | 新增：`SpiritNavProfile` + BFS 寻路 |
| `scripts/csharp/gameplay/BlockField.cs` | 类改为实现 `IStandableGrid`；新增 `IsStandableCell` / `StandPoint` |
| `scripts/csharp/gameplay/SpiritFollow.cs` | 接入寻路模式（计时 / 重算 / 沿路径走 / 超时瞬移）；`SetNavProfile` / `SetMoveStats`；`EverFoundPath` / `SeekStarts` 诊断字段 |
| `scripts/csharp/gameplay/SpiritFollowGroup.cs` | `Bind` 接收网格并下发给每个随从；冒烟新增 `bfs_path_found` / `stuck_recovery_by_bfs` |
| `scripts/csharp/core/GameRoot.cs` | `_spiritGroup.Bind(Player!, _blocks)` |
| `tools/check_project.py` | `EXPECTED_CS` 29→30；新增 2 项寻路检查；冒烟断言 11→13 项 |

### 26.4 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **169/169 PASS** |
| 3 | `--follow-smoke`（含强制卡死）| **PASS**：`stuck_start_dist=10.05` → `stuck_final_dist=1.13~1.68`，`stuck_ever_path=true`，`stranded_seek_starts=1` |
| 4 | `Main --smoke` / 整链 `--smoke` | PASS，`player_moved` / `follow_entity_ok` / `follow_no_overlap_ok` 全 true |
| 5 | `--pause-smoke` / `--offer-smoke` | 均 PASS |
| 6 | 其余跟随指标回归 | `idle_jumps=0`、`min_pet_distance=0.648~0.652` 仍然稳定 |

说明：普通跟随能直线走回时 **不会**触发 BFS（实测 10 格距离 1.5 秒内自行走回，`_farTimer` 到不了 2.5 秒）；
端到端测试用 `ForceSeekForSmoke()` 模拟「被地形卡死、直线回不去」，验证 BFS 路径确实被采用并走通。
排查中还修掉一个冒烟自身的 bug：踩头测试恢复随从时漏传网格，把它的寻路能力清空了。

### 26.5 范围边界
版本保持 `0.0005`，未升版、未打 tag、未 push；`project.godot` 未碰。
未实现：战斗、敌人、随从攻击、技能、掉落、随从属性成长（`SetMoveStats` 已预留入口，等养成系统接）。

## 27. 弹跳 / 粘液方块对所有角色生效（2026-11-16，v0.0005）

### 27.1 需求
「弹跳方块、粘液方块这些，对所有的人、怪物、随从都是有效的」。

### 27.2 原状与改动
| 问题 | 原状 | 改法 |
|---|---|---|
| 弹跳块只认玩家 | `BlockField.OnTouched` 先 `!body.IsInGroup("master")` 直接 return；感应区 `mask=1` | 改为「角色」判定：`master / spirit / enemy` 三组；`block_bounce.tscn` 的 Touch `mask` 1 → **12**（`LayerActor \| LayerSpirit`） |
| 随从不会弹 | 随从没实现 `IBounceable` | `SpiritPet` 实现 `IBounceable`（`Bounce()` 只置标志 + `ConsumeBounce()`），`SpiritFollow` 在 `MoveAndSlide` 之后应用上抛 |
| 弹射被贴地清零 | —— | 新增 `_launchGrace`（0.2 秒离地宽限）：只保护一帧不够，首帧只离开约 0.37 格，第二帧仍在接触范围内会把 Y 清零 |
| 粘液只对玩家减速 | 减速写在主角的 `PlatformMover.Step` 里 | `SpiritFollow.UpdateFloorContact` 检测脚下 `slime_block` 组；踩上后水平上限 × `SlimeSpeedMul`，且三种跳跃（跨层 / 让路 / 自救）全部禁用 |
| 怪物（未来） | —— | `GameDefs` 新增 `MasterGroup` / `EnemyGroup` 常量；怪物只要加入 `EnemyGroup` 并实现 `IBounceable`，弹跳与粘液自动生效，方块代码不用改 |

**保持不变的边界**：脆弱木板与宝箱仍只给玩家（随从踩碎木板会让 AI 掉坑，宝箱属于玩家收益）。

### 27.3 改动文件
| 文件 | 改动 |
|---|---|
| `scripts/csharp/core/GameDefs.cs` | 新增 `MasterGroup` / `EnemyGroup` 组名常量 |
| `scripts/csharp/gameplay/BlockField.cs` | `OnTouched` 改为角色组判定；脆弱 / 宝箱限玩家，弹跳放开给所有角色 |
| `scenes/blocks/block_bounce.tscn` | Touch Area2D `collision_mask` 1 → 12 |
| `scripts/csharp/gameplay/SpiritPet.cs` | 实现 `IBounceable` + `ConsumeBounce` |
| `scripts/csharp/gameplay/SpiritFollow.cs` | 弹射消费与 `_launchGrace`；粘液减速与禁跳；`IsOnSlime` 证据 |
| `scripts/csharp/gameplay/SpiritFollowGroup.cs` | 冒烟新增 `bounce_affects_spirit` / `slime_affects_spirit` 与诊断字段 |
| `tools/check_project.py` | 新增 2 项方块效果检查；冒烟断言 13 → 15 项 |

### 27.4 验证（全部实跑）
| # | 验证 | 结果 |
|---|---|---|
| 1 | `dotnet build` | 0 警告 0 错误 |
| 2 | `tools/check_project.py` | **171/171 PASS** |
| 3 | `--follow-smoke` 连跑 6 次 | 全 **PASS**：`bounce_peak_vy=-22`（弹射速度已应用）、`slime_affects_spirit=true`（站上粘液且速度被压低） |
| 4 | `Main --smoke` | **PASS**，主角侧 `bounce_jelly_ok` / `slime_slow_ok` / `slime_no_jump_ok` / `slime_snap_ok`、`slime_hop_rise=0.376` 全部不变 |
| 5 | 整链 `--smoke` / `--pause-smoke` / `--offer-smoke` | 均 PASS |

测试口径说明：弹跳断言用「上抛速度是否到 -22」，而不是「弹起多高」——弹跳块上方有没有天花板由随机地图决定，
高度会在 1.7~5.7 格之间变化，但速度 -22 一定能证明弹跳对随从生效。粘液用逐帧采样（块只有 1 格宽，
随从很快滑出去），只要「站在上面的某一帧」速度受限即通过。

### 27.5 范围边界
版本保持 `0.0005`，未升版、未打 tag、未 push；`project.godot` 未碰。
未实现：战斗、敌人、随从攻击、技能、掉落。怪物系统本身还没做，只预留了 `EnemyGroup` 入口。

---

## 28. 目标框架升 `net9.0`（2026-11-17）

### 28.1 改动

| 文件 | 改动 |
|---|---|
| `ShanhaiSpiritsSurvivors.csproj` | `TargetFramework`：`net8.0` → `net9.0` |
| `ShanhaiSpiritsSurvivors.sln` | **新增**（原来没有）；配置为 `Debug` / `ExportDebug` / `ExportRelease` 三套 |
| `tools/check_project.py` | 断言改 `net9.0`；新增「`.sln` 存在且含 `ExportDebug`/`ExportRelease`」一条 |
| `docs/overview-v0.003.md` | 基线表语言行改 `net9.0`；新增「构建/导出」行 |

### 28.2 为什么必须补 .sln（本次真正的坑）

第一次导出时 Godot 报：

```
ERROR: Export .NET Project: This project contains C# files but no solution file
       was found at the following path: D:/GameProjects/山海幻灵/ShanhaiSpiritsSurvivors.sln
ERROR: System.InvalidOperationException: res://scripts/csharp/core/GameDefs.cs
       is a C# file but no solution file exists.
```

后果是**导出的 exe 里没有程序集**：只有 103MB 模板 + 一张 pck，`dotnet publish` 根本没跑，双击必崩。
工程从回退那轮起就只有 `.csproj` 没有 `.sln`（CLI 建工程留下），平时 `dotnet build` 直接编 csproj 看不出来。

补上 `.sln` 后 `dotnet_publish_project` 才真正执行。注意 `.sln` 的配置段必须是 Godot 期望的
`Debug` / `ExportDebug` / `ExportRelease`——`dotnet new sln` 默认生成的 `Debug|Release` 配置不够用。

### 28.3 运行时口径

- 本机只装 .NET **10** 的 SDK 与运行时（无 8、无 9）。编辑器内运行 net9 程序集走 Godot 的 roll-forward，
  实测正常（导入 0 ERROR、各冒烟全 PASS）。
- 导出是 **self-contained**：产物 `data_ShanhaiSpiritsSurvivors_windows_x86_64/` 里
  `runtimeconfig.json` 写 `includedFrameworks: 9.0.21`，189 个文件含 coreclr。
  **目标机不需要另外装 .NET**，这正是本轮升版想要的效果。

### 28.4 验证（全部实跑）

| # | 命令 | 结果 |
|---|---|---|
| 1 | `dotnet build`（Debug 与 `-c ExportRelease` 各一次） | 均 **0 警告 0 错误** |
| 2 | `tools/check_project.py` | **172/172 PASS** |
| 3 | 编辑器导入 `--editor --quit` | **0 ERROR / 0 SCRIPT ERROR** |
| 4 | `Main --smoke` | **PASS**（`player_moved=true`、`follow_entity_ok=true`） |
| 5 | 整链 `--smoke` / `--pause-smoke` / `--offer-smoke` | 均 **PASS** |
| 6 | `--follow-smoke` 连跑 2 次 | 均 **PASS**：`idle_jumps=0`、`min_pet_distance=0.64 / 0.652`、`stuck_final_dist=1.68 / 1.09` |
| 7 | 真导出（临时预设 `TempNet9Check`，验后删除） | `dotnet_publish_project` **DONE**，产物自带 9.0.21 |
| 8 | 运行导出的 `Shanhai.console.exe --headless --smoke` | **PASS**（`result=PASS`、`player_moved`、`follow_entity_ok`） |

第 7、8 条是这轮唯一能证明「导出真的能用」的证据——单看 `dotnet build` 通过是发现不了缺 `.sln` 的。

### 28.5 范围边界

版本保持 `0.0005`，未升版、未打 tag、未 push；`project.godot` 未碰。
临时 `export_presets.cfg` 与导出产物在验证后已删除，未留在仓库内。

---

## 29. 套用导出模板 + 代码层导出就绪（2026-11-17）

### 29.1 模板来源

参考 `D:/GameProjects/SnakeSchool/export_presets.cfg`（同引擎 4.7.2、同目标框架 net9.0，
且该预设是提交进库的）。逐字段比对后按本项目适配，落地为仓库内的 `export_presets.cfg`。

### 29.2 与模板的差异（只改必要项）

| 字段 | SnakeSchool | 山海幻灵 | 原因 |
|---|---|---|---|
| `export_path` | `./export/SnakeSchool.exe` | `./export/ShanhaiSpiritsSurvivors.exe` | 产物命名 |
| `application/product_name` | 贪吃蛇减肥 | 山海幻灵 | 与 `project.godot` 的 `config/name` 对齐 |
| `application/file_description` | 反向贪吃蛇：催吐减肥 | 山海谷的幻灵师 | 与 `config/description` 对齐 |
| `application/icon` | `uid://...`（其工程图标） | `""`（用引擎默认） | 本项目还没做图标；留空不阻塞导出 |
| Android 预设 | 有 | **不带** | 本项目目前只做桌面；要用时按同一份模板补 `[preset.1]` |

其余保留模板值：`embed_pck=true`（单 exe，无外挂 pck）、`export_console_wrapper=1`、
`export_opengl3=1` 且 vulkan/d3d12/d3d11 全 0（对齐 `gl_compatibility`）、
`dotnet/include_scripts_content=false`、`dotnet/embed_build_outputs=false`。

### 29.3 代码层导出就绪审计（本轮重点）

| 检查项 | 结论 |
|---|---|
| 编辑器专用 API（`EditorInterface` / `IsEditorHint` / `#if TOOLS`） | **0 处**，导出构建不会缺类 |
| 写 `res://` | **0 处**；存档/截图/调参全走 `user://`（`SaveGame`、`GameRoot` 截图、`WhiteboxRoot`） |
| 运行时枚举 `res://` 目录 | `SpiritCatalog` / `UpgradeCatalog` 已 `TrimSuffix(".remap")`——导出后 `.tres` 转 `.res` 并留 `.remap`，这两处原本就会跟 |
| 贴图加载 | `ArtBank` 走 `ResourceLoader.Exists` + `GD.Load`，不读原始文件路径，导出可用 |
| `#if DEBUG` 分支 | 0 处，不存在「编辑器能跑、导出跑另一条代码」的隐患 |

### 29.4 自检新增 5 条（172 → 177）

1. 导出预设入库且平台为 Windows Desktop、产物落 `./export/`
2. pck 内嵌 + 只启 OpenGL3（渲染器与预设不匹配是导出黑屏常见原因）
3. 预设产品名 / 描述与 `project.godot` 一致（防改名漏改一处）
4. `.gitignore` 排除 `.godot/` 与 `export/`
5. 运行期不写 `res://`（正则守卫 `FileAccess.Write` / `SavePng` / `DirAccess` 写模式）

第 5 条做过有效性验证：喂违规样例能被挡、喂合法样例（`user://` 写、`res://` 只读枚举）不误伤。

### 29.5 验证

`tools/check_project.py` → **177/177 PASS**；编辑器加载该预设 `--editor --quit` → **0 ERROR**。

### 29.6 范围边界

用户明确要求本轮**先不导出、只做代码/配置层面的导出就绪**，所以没有跑端到端导出。
导出链路本身已在 §28 用临时预设验证过（含运行导出的 exe 冒烟 PASS），换成本预设后
需要时执行一次即可：

```
godot --headless --path <项目> --export-release "Windows Desktop"
```

release 导出要跑 `dotnet publish` 自包含发布（约数分钟），别用短超时掐断。

版本保持 `0.0005`，未升版、未打 tag、未 push；`project.godot` 未碰。

---

## 30. 版本升 `0.0006` + 升版流程文档化（2026-11-17）

用户开口升版，说明写作「优化代码，改写为 net9」，并要求把升版流程写成文档供以后的 AI 遵循。

### 30.1 同步的口径

| 位置 | 改动 |
|---|---|
| `project.godot` | `config/version` `0.0005` → `0.0006` |
| `tools/check_project.py` | `VERSION` 与文件头 docstring → `0.0006` |
| `README.md` | `Current` / `Updates` → `v0.0006`（说明照用户原话） |
| `docs/overview-v0.003.md` | 标题「现行 0.0006」+ 版本行（升版日期 / tag / 更新内容 / 指向流程文档） |
| `docs/self-check.json` | 脚本重新生成 |
| `docs/versioning.md` | **新增**：升版必须同步的 7 处、标准动作顺序、纪律、自检兜底 |

历史小节（§16–§29）里的 `0.0005` 属当时记录，**未回改**。

### 30.2 顺手修掉的漂移点

`GameRoot.cs` 的冒烟报告里 `["version"] = "0.0005"` 是**写死的字面量**——升版时只要忘了它，
冒烟证据就会继续打印旧版本号，属于典型的「证据骗人」。已改为读
`ProjectSettings.GetSetting("application/config/version")`，以后升版不用再碰它。

### 30.3 自检新增 3 条（177 → 180）

1. 冒烟报告版本号读 `ProjectSettings`，禁止硬编码（正则禁 `["version"] = "0.`）
2. `overview` 的现行版本与脚本 `VERSION` 一致
3. `docs/versioning.md` 存在且列全各处口径

### 30.4 验证

编译 **0 警告 0 错误**；自检 **180/180 PASS**；
`Main --smoke` 与整链 `--smoke` 均 **PASS**，两边输出 `"version": "0.0006"`（旧值已不再出现）。

### 30.5 发布

提交后打附注 tag `v0.0006`（说明「优化代码，改写为 net9」），代码与 tag 一并推送远程。

## 31. 版本升 `0.0007` + 怪物系统 MONSTER-001（2026-11-19）

### 31.1 内容

- **怪物对象池**：`MonsterField` 预实例化 12 只地面追击怪（enemy_shambler.png），激活全量重置、
  回收隐藏屏外，游玩中零 Instantiate / QueueFree；场上上限 8，达到即 PushWarning 拒绝。
- **追击 AI**：复用 `PlatformMover`（粘液减速 / 弹跳块弹射 / 转角修正与主角同口径，EnemyGroup 设计预留）；
  卡住自救跳 + 玩家高于 1 格时追层跳（跳高约 2 行，玩家满跳 3.4 行可垂直甩开）。
- **接触伤害**：`Touch` 感应区（Area2D mask=LayerActor）entered/exited 维护布尔，逐帧零重叠查询；
  感应圈半径 0.8 必须大于双方胶囊半径和 0.7（物理阻挡先于感应）——开发中实测踩过这个坑。
- **玩家生命**：Master.Hp=5 + TakeDamage 唯一入口 + 无敌窗口 1.0s + 受击半透明反馈；
  归零发 Died → GameRoot 定格 0.8 秒回开始页（定时器回调带 IsInstanceValid 防护）。
- **HP HUD**：左上角 ♥/♡ 文本，HealthChanged 信号重绘；HUD 调试按钮「刷一只怪」接入 MonsterField。

### 31.2 自检新增（180 → 197 条）

怪物系统 12 条：GameDefs 常量段 / 怪物场景（贴图+碰撞+感应区）/ 感应圈半径口径 /
IBounceable+EnemyGroup+内核复用 / Area2D 信号零查询 / 对象池三要素 / Main 挂载+调试按钮 /
死亡回开始页接线 / HP HUD / --monster-smoke 十一条断言锚点 / WaitPhysicsFrames 收口 / 主冒烟池就绪；
另「Master 无 TakeDamage」旧约束反转为正向断言（玩法按用户指示接入接触伤害）。

### 31.3 验证

编译 **0 警告 0 错误**；编辑器导入 **0 ERROR**；自检 **197/197 PASS**；
**`--monster-smoke` 11/11 断言 PASS**（池/生成/落地/追踪/池复用/接触扣血/无敌帧/上限/坠落回收/死亡回开始页/接口口径）；
既有 5 条冒烟（--smoke / --follow-smoke / --pause-smoke / --offer-smoke / 整链 --smoke）全部 **PASS**，
断言集与 0.0006 基线逐条 diff 一致；同种子建图输出逐字一致；
主冒烟输出 `"version": "0.0007"`、新增 `monster_field_ready` 断言。

### 31.4 发布

提交后打附注 tag `v0.0007`（说明「加入怪物系统：地面追击怪 + 玩家血量」）；
代码与 tag 按惯例等用户确认后推送远程。

## 32. 版本升 `0.0008` + 波次系统与篝火昼夜循环 NIGHT-001（2026-11-19）

### 32.1 内容

- **昼夜状态机**：`NightDirector` 白天 → 点燃 → 黑夜 20 波（第 k 波 k 只，不断加怪，波间休整 10 秒）
  → 夜深 → 祈祷天亮。波次结构不随夜数变化（用户指定）；全部计时零分配。
- **篝火交互**：新增 `interact` action（E 键）；白天按 E 点燃；黑夜篝火旁长按 E 3 秒且持续
  不受伤（HealthChanged 即重置）→ 天亮。视觉全程序化（木堆 ArtBank 方块 + 火焰 CpuParticles2D）。
- **天亮表现**：`MonsterField.DissolveAll` 全场怪原地掉魂魄消散；背景切换 bg_eclipse/bg_dusk；
  天亮自动弹出随从三选一弹窗（升级 = 复用现成 RUN-ROSTER 系统）。
- **HUD**：顶部昼夜状态（文本缓存去重）+ 祈祷进度条。
- **挂死防御延续**：`--night-smoke` 主体 try/catch，玩家死亡切场景的极端时序带 FAIL 退出。

### 32.2 自检新增（206 → 218 条）

昼夜波次 12 条：GameDefs 常量 / 状态机 / 信号向上裁决 / interact 键（StringName 缓存）/ 不受伤约束 /
篝火程序化零 PNG / 天亮消散 / 三选一衔接 / 背景切换 / HUD / --night-smoke 断言锚点 / 主冒烟白天起步；
输入断言更新（4 action → 5 action，新增 interact=E）。

### 32.3 验证

编译 **0 警告 0 错误**；编辑器导入 **0 ERROR**；自检 **218/218 PASS**；
**`--night-smoke` 11/11 断言 PASS**（白天起步/点燃/波1刷出/清场休整/波2推进/祈祷受伤重置/天亮/
消散/三选一弹窗/再点燃夜 2）并连跑多次稳定；
既有 6 条冒烟（--monster-smoke / --smoke / --follow-smoke / --pause-smoke / --offer-smoke / 整链 --smoke）
全部 **PASS**，断言集与 0.0007 基线逐条 diff 一致；同种子建图输出逐字一致；
主冒烟输出 `"version": "0.0008"`、新增 `night_day_start` 断言。

### 32.4 发布

提交后打附注 tag `v0.0008`（说明「波次系统 + 篝火昼夜循环」）；
代码与 tag 按惯例等用户确认后推送远程。
