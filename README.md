# 山海幻灵 · v0.0002

> **山海谷的幻灵师** ｜ Godot **4.7.2.stable.mono** ｜ **全量 C#** ｜ 2D 横板跳跃 ｜ 零 autoload、零第三方插件
>
> 工程路径：`D:\GameProjects\山海幻灵`

---

## 1. 这一版是什么

**v0.0002：开始页 + 场景编辑器化**。在 v0.0001 底座（移动手感 + 平台地图生成）上做了三件事：

| 变更 | 内容 |
|---|---|
| **开始页** | `StartPage.tscn`（开始 / 设置 / 图鉴 / 退出：开始 → 进世界、退出 → 关游戏、设置 / 图鉴留 TODO 未实装）；`run/main_scene` 改为开始页 |
| **场景结构编辑器化** | `Main.tscn` 摆节点：根 WorldRoot 挂 GameRoot，子节点 Camera2D / PlatformField / Player；碰撞体在编辑器摆（胶囊体，包围盒 = GameDefs.MasterSize）；代码只取节点 + 程序化生成（平台 / 背景） |
| **主角独立场景** | `Player.tscn`（Node2D → CharacterBody2D[Master] → CollisionShape2D），供后续战斗 / 装备扩展复用；`WorldRoot.cs` 合并进 `GameRoot.cs` |

**背景（v0.0001）**：v0.0001 是「回退重构」——应用户要求把 v0.003 的战斗 / 成长 / 昼夜 / UI / 局外存档全部拆除，只保留「移动手感」（PlatformMover：《终焉之莉莉》式跳跃全套 + 类银式下穿）与「平台地图生成」（PlatformField：GND-001 全覆盖地板 + 9 层随机单向平台）两样被验证过的东西作为公共底座，之后与助手在这个对话里逐步重新设计游戏。

> **找回旧版**：v0.001 / v0.002 / v0.003 均有 git tag；拆除前的完整状态在提交 `5978a58`（自动上攀快照）。旧文档与验证证据在 `docs/archive/v0.003/`。

## 2. 代码结构（10 个 C#）

```
scripts/csharp/core/       GameDefs · GameRoot · Rng · ArtBank        (4)
scripts/csharp/gameplay/   PlatformMover · PlatformField · Master    (3)
scripts/csharp/ui/         UiSkin（仅白箱使用） · StartPage（开始页）  (2)
scripts/csharp/tools/      WhiteboxRoot（白箱，非游戏机制）             (1)
```

- 场景结构在 Godot 编辑器里摆：`Main.tscn`（根 WorldRoot 挂 GameRoot，子节点 Camera2D / PlatformField / Player）+ `Player.tscn`（主角独立场景：Node2D → CharacterBody2D[Master.cs] → CollisionShape2D）。
- `Master.cs` 只有移动 / 跳跃 / 边界 / 坠落重生；碰撞体在 `Player.tscn` 里（胶囊体，包围盒 = GameDefs.MasterSize）。
- 启动进入开始页（`StartPage.tscn`：开始 / 设置 / 图鉴 / 退出），点「开始」进空世界；A/D + Space 跑跳。

## 3. 操作与验证

| 键 | 作用 |
|---|---|
| `A / D` `← / →` | 走位 |
| `Space` `W` `↑` | 跳跃（短按矮跳 / 长按照高） |
| `S / ↓` + `Space` | 下穿单向平台（类银式：站上台沿按住下键 + 跳，穿层落下；实体地板不受影响） |
| 手柄 左摇杆 / 十字键 | 走位 / 下穿（对应 A/D/S；摇杆支持渐进走速） |
| 手柄 A 键（Xbox A / PS 交叉） | 跳跃 |
| `Tab`（白箱内） | 隐藏调参面板 |

```bash
GODOT="/d/Program Files (x86)/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
ROOT="D:/GameProjects/山海幻灵"

dotnet build "$ROOT/ShanHaiHuanLing.csproj" -v minimal --nologo          # ① 编译
"$GODOT" --headless --path "$ROOT" --editor --quit                       # ② 资源导入
"$GODOT" --headless --path "$ROOT" res://scenes/Main.tscn -- --smoke     # ③ 冒烟：建图/落地/走/跳/下穿（主场景是开始页，需显式指定 Main.tscn）
python "$ROOT/tools/check_project.py"                                    # ④ 静态自检
```

白箱调参：Godot 打开 `scenes/Whitebox.tscn` 按 F6；调好把 `user://whitebox_tuning.json` 的数值写回 `GameDefs.cs` 即游戏内生效。

## 4. 版本规范

- v0.0001 = **新起点版本线**（应用户指定回退），后续每次改动 `+0.001`，里程碑跳 `0.010 / 0.100`。
- 每次提交同步 `project.godot` 与 `README.md` 的版本口径并打同名轻量 tag。

## 5. 参考与下一步

- 逆向分析：`C:\Users\Administrator\WorkBuddy\2026-11-10-13-01-12\docs\BoneraiserMinions-逆向分析.md`（BM 拆包，证据分级 A/B/C/D）
- v0.003 状态全景（战斗短板清单 / BM 差距表 / 改造候选方向）：`docs/overview-v0.003.md`
- **下一步不是写代码**：先与助手把「战斗形式 / 循环结构 / 成长路径」聊清楚，再逐项落进空世界。
