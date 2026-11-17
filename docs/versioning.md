# 版本号更新流程（给以后的 AI 看）

> 本项目版本纪律由用户定：**用户不说升版，就一律不动版本号**。
> 说升版时按 **+0.001** 递增（`0.0005` → `0.0006`），同步下面全部位置，再提交、打 tag、推送。
> 本文是唯一权威清单；改流程请连本文一起改，并让 `tools/check_project.py` 的对应断言跟着变。

## 一、必须同步的位置（漏一处就不一致）

| # | 文件 | 改什么 | 谁维护 |
|---|---|---|---|
| 1 | `project.godot` | `[application]` 的 `config/version="0.000x"` | AI 改 |
| 2 | `tools/check_project.py` | 顶部 `VERSION = "0.000x"` 与文件头 docstring 的 `v0.000x` | AI 改 |
| 3 | `docs/overview-v0.003.md` | 标题 `现行 0.000x`；基线表「版本」行的版本号、升级日期、tag、更新内容 | AI 改 |
| 4 | `README.md` | `Current: \`v0.000x\`` 与 `Updates: \`v0.000x\` — 一句话说明` | **正文用户自己写**，AI 只按用户给的说明改这两行 |
| 5 | `docs/verification-2026-11-15.md` | **追加新一节**记录本轮验证；**历史小节里的旧版本号不要改** | AI 改 |
| 6 | `docs/self-check.json` | 由脚本重新生成，不要手改 | 跑 `python tools/check_project.py` 自动写 |
| 7 | `docs/versioning.md` | 本文；若流程变了才动 | AI 改 |

`GameRoot.cs` 的冒烟报告 `version` 字段**已经改成读 `ProjectSettings`**，不用再手改；
自检里有一条断言禁止它再写死字面量。

## 二、标准动作顺序

```bash
# 1) 改上面 1~5、7 的文件
# 2) 自检（会顺带重写 docs/self-check.json）
python tools/check_project.py          # 期望 ALL PASS，条数随功能增长，不要照抄数字

# 3) 编译 + 主冒烟，确认版本号在输出里是新值
dotnet build ShanhaiSpiritsSurvivors.sln -v minimal
godot --headless --path . "res://scenes/Main.tscn" -- --smoke   # 看 "version": "0.000x"

# 4) 提交（版本号四处一起进，别拆成多个提交）
git add project.godot README.md tools/check_project.py docs/
git commit -m "版本 0.000x：<一句话说明>"

# 5) 打附注 tag，说明与 README 的 Updates 一致
git tag -a v0.000x -m "<一句话说明>"

# 6) 推送代码与 tag（只推代码不推 tag 会导致远程缺版本节点）
git push origin master
git push origin v0.000x
```

## 三、纪律（用户原话，最高优先级）

- **不开口不升版**：不管一轮改了多少代码、修了多少 bug，版本号一律不动。
- **git 照常**：改完代码该提交就提交；「不升版」不等于「不提交」。
- **只升 +0.001**：不做大跳（`0.0005` → `0.0006`，不是 `0.001`）。
- **tag 与版本同名**：`v0.0006`，附注写明本次更新内容，且与 README 的 `Updates` 一致。
- **不为了过检查去改 README 正文**：README 的叙事由用户写，AI 只碰上面第 4 行那两行。

## 四、自检兜底

`tools/check_project.py` 里有这几条专门守版本口径，改坏了会直接 FAIL：

- `config/version = 0.000x`（`project.godot` 与脚本 `VERSION` 对齐）
- 冒烟报告版本号读 `ProjectSettings`、不硬编码
- `overview` 的现行版本与脚本一致
- 本文存在且列全了各处位置
