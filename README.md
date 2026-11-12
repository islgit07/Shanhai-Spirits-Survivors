# 山海幻灵 · v0.001

> **横板昼夜幸存者 2D** ｜ Godot **4.7.2.stable.mono** ｜ GDScript ｜ 纯代码自举、零场景拖拽、零第三方插件
> 工程路径：`D:\GameProjects\山海幻灵` ｜ 迁移自 `D:\UnityProjects\DayNightSurvivorDemo`（Unity 6000.3.26f1 · v0.0003）
> **版本口径**：Godot 新工程独立起线，自 **v0.001** 开始；每次有意义变更 +0.001（v0.001 → v0.002 → …），不再整档大跳，里程碑才跳更大档位。Unity 侧的 v0.0001~v0.0003 属另一条版本线，不并入本仓库历史。

---

## 0. 当前状态（先把边界说清楚）

| 项 | 状态 |
|---|---|
| 版本 | **v0.001** ｜ 设计冻结清单 26 条 ｜ `@trace` 覆盖率 100% ｜ 21 个 GDScript |
| 引擎 | Godot 4.7.2.stable.mono.official（本机 `D:\Program Files (x86)\Godot_v4.7.2-stable_mono_win64`） |
| 真实导入验证 | `--headless --editor --quit` **退出码 0**，16 张 PNG 全部导入，**0 个解析错误** |
| 无头 Play 冒烟 | **PASS 10/10 断言**：3 昼夜 / 39 只怪 / 2 发子弹 / 1 次击杀 / 掉金币 / 选牌 / 结算 / 存档 / 返回主页面（报告 `docs/smoke-report.json`） |
| 静态自检 | 见 `docs/self-check.json` |
| 美术 | 16 张（12 精灵 + 4 背景），从 Unity 工程直接复用 PNG；Godot 不需要 `.meta` |
| 2D 实现 | `CharacterBody2D` 玩家 + `StaticBody2D` 单向平台 + `Camera2D` 跟随；**补上了源工程遗留的「手写距离判定」缺口** |
| **真人 Play Mode** | **仍未做**。冒烟只证明状态机与对象链路在快进下走通，**不等于能玩、更不等于好玩**：跳跃落点、相机跟随是否晕、6 把武器手感、48 宽跑图节奏、HUD 中文显示、布局比例都需真人试玩 |

### 本轮是「可运行 2D 垂直切片」，不是 43 个 C# 的全量移植

源工程 v0.0003 有 **73 条冻结项 / 43 个 C# 文件**。本版把**核心循环**迁移到 Godot 并跑通，其余系统逐条列在 `docs/migration-map.md` 第 4 节「未迁移清单」。**未迁移项没有冒充已做**。

---

## 1. 快速开始

1. 用 Godot **4.7.2** 打开 `D:\GameProjects\山海幻灵`（首次打开会自动导入资源）。
2. 按 **Play**（主场景 `res://scenes/Main.tscn`，只挂一个 `Bootstrap.gd`，其余节点运行时生成）。
3. **主页面**：选出征武器（未解锁的用余烬解锁）、买永久升级、看战绩 → 点「开始新的一局」或按 `Enter`。
4. **白天 20 秒**：`1~6` 或点击买建造物，`Enter` 提前入夜。
5. **夜晚 30 秒**：自动索敌开火、`A/D` 移动、`Space` 跳跃、`F` 换武器、`Esc` 暂停。
6. **天明三选一**：`1/2/3` 选卡，`Q` 重掷（3 金），`E` 跳过（+5 金）。
7. **死亡**：`R` 重开，`M` 回主页面做局外成长。

### 键位表

| 动作 | 键 | 动作 | 键 |
|---|---|---|---|
| 左右移动 | `A/D` `←/→` | 跳跃 | `Space` `W` `↑` |
| 换武器 | `F` | 暂停 | `Esc` `P` |
| 提前入夜 / 菜单快捷开局 | `Enter` | 购买 / 选卡 | `1`~`6` / `1`~`3` |
| 重掷 | `Q` | 跳过 | `E` |
| 重开 | `R` | 回主页面 | `M` |

> **手柄未在本版迁移**（源工程 PAD-001~004）。Godot 的 InputMap 已在 `Bootstrap.gd` 中运行时注册，后续加手柄只需追加 `InputEventJoypadButton/Motion`。

---

## 2. 工程结构

```
山海幻灵/
├── project.godot              # 2D 配置：gl_compatibility / 1280×720 / 4 个 autoload
├── scenes/Main.tscn           # 唯一场景：一个 Node2D + Bootstrap.gd
├── scripts/
│   ├── core/                  # GameConfig / GameEvents / ArtBank / RunSeed
│   │                          # RunState / MetaProgress / GameManager / Bootstrap
│   ├── gameplay/              # PlatformField / Player / WeaponSystem / Projectile
│   │                          # Enemy / EnemySpawner / CoinPickup / BuildSystem
│   │                          # UpgradeSystem / World
│   └── ui/                    # UiSkin / MainMenu / Hud
├── assets/art/                # 16 张 PNG（12 精灵 + 4 背景）
├── docs/                      # 设计冻结 / 迁移映射 / 路线图 / 验证证据
└── tools/check_project.py     # 静态自检（配置 / 文件配对 / @trace 覆盖率 / 版本一致性）
```

**autoload**：`GameEvents`（信号总线）→ `RunState`（本局构筑）→ `MetaProgress`（局外存档）→ `GameManager`（昼夜状态机）。

---

## 3. 可复算数值表（与源工程 v0.0003 一致，未擅自调平衡）

| 项 | 公式 / 取值 | 极值验算 |
|---|---|---|
| 昼夜时长 | 白天 20s / 夜晚 30s | 单昼夜 50s |
| 世界 | 半宽 24（宽 48）、10 层、层高 3.2 | 顶层 Y = -2.2 + 9×3.2 = 26.6 |
| 平台段数 | `clamp(round(48/8), 5, 8) = 6` 段/层 | 共 1 + 9×6 = **55** 段（冒烟实测 55 ✓） |
| 跳跃可达 | `v²/(2g) = 14.2²/(2×26) ≈ 3.88` | 3.88 > 3.2 → 单跳可跨一层 ✓ |
| 波次预算 | `B(d)=min(130, 14+7(d-1))` | B(1)=14，B(17)=130 封顶 |
| 刷怪间隔 | `clamp(30×0.86/B(d), 0.22, 0.85)` | d=1 → 0.85（被上限截断）；d=17 → 0.22 |
| 永久升级价 | `20×(lv+1)` | Lv0→20，Lv4→100，5 级共 300 余烬 |
| 余烬结算 | `3×天数 + floor(击杀/10) + 8×Boss击杀` | 第 3 天 0 杀 = 9 余烬 |
| 相机 | `zoom = 720/(2×6.5) ≈ 55.38` | 视野 1280/55.38 ≈ 23.1 × 13 世界单位 |

---

## 4. 验证方式（可复算）

```bash
GODOT="/d/Program Files (x86)/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
ROOT="D:/GameProjects/山海幻灵"

# ① 资源导入 + 全脚本解析
"$GODOT" --headless --path "$ROOT" --editor --quit

# ② 无头 Play 冒烟（自动 3 昼夜，打印 SMOKE_JSON，PASS 退出 0 / FAIL 退出 1）
"$GODOT" --headless --path "$ROOT" -- --smoke

# ③ 静态自检（文件配对 / @trace 覆盖 / 版本一致 / 证据齐备）
python "$ROOT/tools/check_project.py"
```

证据归档：`docs/compile-evidence.txt`、`docs/smoke-evidence.txt`、`docs/smoke-report.json`、`docs/self-check.json`。

> **关于 mono 版**：本机只有 mono 版 Godot，启动时打印 `.NET: Failed to load project assembly` 属正常（本工程纯 GDScript，无 C# 程序集），不影响运行，已在日志中确认。

---

## 5. 已知限制（不隐藏）

1. **中文字体**：Godot 内置字体不含 CJK，`UiSkin.gd` 用 `SystemFont` 请求系统「Microsoft YaHei / Noto Sans CJK SC」等。目标机器无中文字体会显示方块；未分发字体文件（授权问题）。
2. **真人 Play 未验证**：所有验证均为 headless，画面、手感、布局未经人眼确认。
3. **敌人仍用距离判定**：玩家/平台已换 Godot 原生物理，敌人与子弹仍是 `Node2D` 手算，未换成 `Area2D`/`Rigidbody2D`；对象池也未迁移（改用短命对象 + `queue_free`）。
4. **金币落地固定为底层**：源工程按平台层做 `TryLanding`，本版简化。
5. **炮台只有视觉**：白天可购买并生成色块，但自动开火未迁移。
6. **存档 JSON 明文**（`user://shanhai_meta.json`）：与源工程 `PlayerPrefs` 同为 demo 级取舍，正式项目需加密 + 账号体系。
7. **无音频 / 无 Boss / 无手柄 / 无夜晚事件 / 无精英词缀**：详见迁移清单。
8. **导出未配置**：没有 `export_presets.cfg`，尚未产出任何平台构建。

---

## 6. 本轮产出 / 未完成 / 建议下一步

- **产出**：Godot 4 2D 工程骨架、21 个 GDScript、16 张复用美术、v0.001 设计冻结清单、迁移映射、自检脚本、真实导入 + 无头冒烟双验证证据、git 初始提交与 `v0.001` 标签。
- **未完成**：上面第 5 节全部；源工程 73 条冻结项中未迁移的部分逐条列在迁移清单。
- **建议下一步**（按性价比）：① **先真人按一次 Play**，确认能玩、中文正常、跳跃落点合理；② 再决定补哪一块（手柄 / 音频 / Boss / 对象池 / 导出预设）。**不建议在没试玩前继续堆系统**。
