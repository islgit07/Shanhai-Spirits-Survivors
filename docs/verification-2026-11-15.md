# 2026-11-15 验证记录（交接整改）

范围：统一英文名、键鼠映射精简、开始页适配、白箱零 PNG、版本口径补齐。**玩法未改动**。
引擎：Godot 4.7.2.stable.mono / C# net8.0 / 程序集 `ShanhaiSpiritsSurvivors`。

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
- 版本保持 `0.0003`，未升版、未打 tag、未 push。
