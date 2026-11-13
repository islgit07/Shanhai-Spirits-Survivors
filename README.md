# 山海幻灵
类吸血幸存者游戏 个人开发 用于游戏引擎的熟悉和美术的练习

> **v0.0002** ｜ Godot 4.7.2.stable.mono ｜ 全量 C# ｜ 找回旧版：拆除前完整状态在提交 `5978a58`，旧文档与验证证据在 `docs/archive/v0.003/`

## 操作与验证

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

dotnet build "$ROOT/ShanhaiSpiritsSurvivors.csproj" -v minimal --nologo          # ① 编译
"$GODOT" --headless --path "$ROOT" --editor --quit                       # ② 资源导入
"$GODOT" --headless --path "$ROOT" "res://scenes/Main.tscn" -- --smoke   # ③ 冒烟：建图/落地/会走/会跳/会下穿
python "$ROOT/tools/check_project.py"                                    # ④ 静态自检
```

白箱调参：Godot 打开 `scenes/Whitebox.tscn` 按 F6；调好把 `user://whitebox_tuning.json` 的数值写回 `GameDefs.cs` 即游戏内生效。

## 版本规范

- 每次改动 `+0.001`。
- 每次提交同步 `project.godot` 与 `README.md` 的版本口径并打同名轻量 tag。