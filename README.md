# 山海幻灵 · v0.0001

> **山海谷的幻灵师** ｜ Godot **4.7.2.stable.mono** ｜ **全量 C#** ｜ 2D 横板跳跃 ｜ 纯代码自举、零 autoload、零第三方插件
>
> 工程路径：`D:\GameProjects\山海幻灵`

---

## 1. 这一版是什么

**v0.0001 是一次「回退重构」**：应用户要求，把 v0.003 的战斗 / 成长 / 昼夜 / UI / 局外存档全部拆除，只保留两样被验证过的东西作为公共底座，之后与助手在这个对话里逐步重新设计游戏。

| 保留 | 内容 |
|---|---|
| **移动手感** | `PlatformMover` + `MoveTuning`：《终焉之莉莉》式跳跃全套（变高跳 / 顶点滞空 / 下落加重 / 下落限速 / 土狼时间 / 跳跃缓冲）+ 类银式下穿（下+跳穿单向平台）。数值 = 白箱实机调出（重力 36 / 跳速 16.2 / 变高跳 0.36 / 下落 2.5 / 顶点 0.45·1.1 / 限速 40） |
| **平台地图生成** | `PlatformField`：第一层全覆盖实体地板硬约束（GND-001）+ 上层 9 层随机分布单向平台（台数 / 位置 / 层高随机、同层保最小间隙、种子可复现） |
| **开发工具** | 跳跃白箱（`scenes/Whitebox.tscn`，与游戏共用移动内核）、`CSharpToolchainProbe`、无头冒烟、`check_project.py` |
| **美术资产** | `assets/art/` 16 张 PNG 全部留在仓库（战斗相关的只是暂时不再被引用） |

| 删除 | 内容 |
|---|---|
| 战斗 | `Spirit` / `Projectile` / `WildSpawner` / `EssencePickup` / `SpiritWorld` |
| 成长 | `Run`（灵韵/资源/军团状态）、`Legion`、`OfferSystem`（唤灵三分支）、`Meta`（局外存档） |
| 表现 | `Hud` / `MainMenu`（全部游戏 UI）、昼夜 / 营地 / 主题轮换 / 物种表 / 融合配方 |
| 事件 | `Signals`（战斗事件总线） |

> **找回旧版**：v0.001 / v0.002 / v0.003 均有 git tag；拆除前的完整状态在提交 `5978a58`（自动上攀快照）。旧文档与验证证据在 `docs/archive/v0.003/`。

## 2. 代码结构（10 个 C#）

```
scripts/csharp/core/       GameDefs · GameRoot · Rng · ArtBank        (4)
scripts/csharp/gameplay/   PlatformMover · PlatformField · Master · WorldRoot  (4)
scripts/csharp/ui/         UiSkin（仅白箱使用）                        (1)
scripts/csharp/tools/      WhiteboxRoot（白箱，非游戏机制）             (1)
```

- `Master.cs` 只有移动 / 跳跃 / 边界 / 坠落重生；`WorldRoot.cs` 只有背景 / 地形 / 相机跟随。
- 启动即进入空世界（无菜单无 UI），A/D + Space 直接跑跳。

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
"$GODOT" --headless --path "$ROOT" -- --smoke                            # ③ 冒烟：建图/落地/会走/会跳
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
