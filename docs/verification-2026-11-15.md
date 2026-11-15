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
- `GameDefs` 硬编码手感参数、静态 `Signals` 事件总线为项目既有架构（见 GodotPrompter 路由卡）。
- 跳跃在 `_physics_process` 用 `IsActionJustPressed` 轮询 + `PlatformMover` 跳跃缓冲，
  而非 `_unhandled_input`，为既有移动内核设计。
- 白箱调试热键 R / T / Tab 仍是硬编码 `Key`，未进 Input Map（避免反向增加映射复杂度）。

## 4. 诚实边界与遗留

- 键盘焦点交互仅有 `GrabFocus` / 上下焦点链的静态检查 + 场景独立运行无错。
- `docs/overview-v0.003.md` 仍是旧 v0.003 战斗版全景文档，与回退后代码脱节，本轮按范围未改。
- `assets/art/block_white.png` 已零引用，文件保留未删（等确认）。
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
