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

- **真人 Play Mode 0 次**：以上全为 headless，不证明画面、字体、中文显示或可玩性。
- 键盘焦点交互未经真人验证，仅有 `GrabFocus` / 上下焦点链的静态检查 + 场景独立运行无错。
- `docs/overview-v0.003.md` 仍是旧 v0.003 战斗版全景文档，与回退后代码脱节，本轮按范围未改。
- `assets/art/block_white.png` 已零引用，文件保留未删（等确认）。
- 版本号保持 `0.0003`（用户拍板保留），**未打 tag、未升版**。
