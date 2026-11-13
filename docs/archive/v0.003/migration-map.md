# Unity → Godot 迁移映射表（v0.001）

> 源：`D:\UnityProjects\DayNightSurvivorDemo` · Unity **6000.3.26f1** · **v0.0003** · 43 个 C# · 73 条冻结项
> 目标：`D:\GameProjects\山海幻灵` · Godot **4.7.2.stable.mono** · **v0.001** · 21 个 GDScript · 26 条冻结项
> 口径：**迁移 = 需求与数值照搬、实现按 Godot 重写**。不是逐行翻译，也不是 43 个文件的全量移植。未迁移项逐条列在第 4 节，不冒充已做。

---

## 1. 为什么这样迁

Unity 的 `GameObject + Component + C# + IMGUI + Resources + PlayerPrefs` 与 Godot 的 `Node + GDScript + Control + load() + user://` 是两套模型，**逐行翻译不可行也没有意义**。本轮策略：

1. **先让核心循环在 Godot 里真跑通**（主页面 → 白天 → 夜晚 → 三选一 → 结算 → 局外成长），并用 headless 冒烟证明。
2. **数值与公式照搬** `GameConfig.cs`，不擅自调平衡；可复算表格见 README 第 3 节。
3. **借迁移补一个历史欠债**：源工程 L3 硬缺口「敌人/平台用手写距离判定」，本版玩家与平台改用 `CharacterBody2D` + `StaticBody2D` 单向平台，是真正的 2D 物理。
4. **所有节点运行时程序化生成**，不手写复杂 `.tscn`/`.tres`；主场景只有一个 Node2D + `Bootstrap.gd`。

---

## 2. 技术栈差异对照

| 维度 | Unity（源） | Godot（本版） | 说明 |
|---|---|---|---|
| 脚本语言 | C# | **GDScript** | 本机 Godot 为 mono 版，但本工程零 C#；启动日志 `.NET: Failed to load project assembly` 属正常 |
| 场景构成 | 运行时 `new GameObject` | 运行时 `Node.new()` + `add_child()` | 两者都零场景拖拽 |
| 自举入口 | `RuntimeInitializeOnLoadMethod` | `project.godot` 的 `run/main_scene` → `Bootstrap.gd._ready()` | 主场景仅 1 个节点 |
| 2D 物理 | 手写距离判定 + 手写单向平台 | **`CharacterBody2D.move_and_slide()` + `CollisionShape2D.one_way_collision`** | 本版实打实的 2D 物理 |
| 输入 | 旧 Input Manager + Joystick API | 运行时 `InputMap.add_action()` 注册 16 个动作 | 手柄事件未迁，扩展位已留 |
| UI | IMGUI（`OnGUI` / `GUI.matrix`） | **`CanvasLayer` + `Control`（Label/Button/ProgressBar）** | 不再手写布局矩阵 |
| 事件 | C# `event` + 委托表 | **autoload `signal`** | 订阅方仍需自行断开（autoload 跨局不销毁） |
| 资源 | `Resources.Load` + `Sprite.Create` | `load()` + `Sprite2D.scale` 规格化 | 16 张 PNG 直接复用 |
| 元数据 | 每个资源一个 `.meta`（GUID） | 每个脚本一个 **`.gd.uid`**（21 个，必须提交） | 两种引擎的引用稳定机制 |
| 存档 | `PlayerPrefs`（明文） | `user://shanhai_meta.json`（JSON 明文） | 同为 demo 级取舍 |
| 相机 | 正交 orthoSize 6.5 + SmoothDamp | `Camera2D.zoom ≈ 55.38` + `lerp` 跟随 | `zoom = 720/(2×6.5)`，视野 23.1×13 世界单位 |
| 对象池 | 手写池（子弹/投掷物/特效） | 短命对象 + `queue_free()` | 对象池列为未迁移 |
| 字体 | Unity 内置字体 | `SystemFont` 请求系统 CJK 字体 | 内置字体不含中文 |
| 编译验证 | `Unity.exe -batchmode -quit -nographics` | `godot --headless --editor --quit` | 本轮两者都真实跑过 |
| 运行验证 | 无头 Play 冒烟 26 项 | 无头冒烟 **10 项断言** | 都是 headless，均非真人试玩 |

---

## 3. 文件级映射

### 3.1 核心层

| 源（Unity C#） | 目标（Godot GDScript） | 状态 |
|---|---|---|
| `GameConfig.cs` | `core/GameConfig.gd` | 迁移，常量与数据表照搬 |
| `GameEvents.cs` | `core/GameEvents.gd` | 重写为 signal 总线 |
| `RunSeed.cs` | `core/RunSeed.gd` | 迁移，FNV-1a 加 32 位截断（GDScript int 为 64 位） |
| `RunState.cs` | `core/RunState.gd` | 迁移 |
| `MetaProgress.cs` | `core/MetaProgress.gd` | 重写，`PlayerPrefs` → `FileAccess` + JSON |
| `GameManager.cs` | `core/GameManager.gd` | 重写，三态 → 五态状态机 |
| `DemoBootstrap.cs` | `core/Bootstrap.gd` | 重写 |
| `DemoInput.cs` / `PadInput.cs` / `PadFocus.cs` | `core/Bootstrap.gd`（InputMap 注册） | 部分：仅键鼠；手柄 3 个文件未迁 |
| `ArtBank.cs` | `core/ArtBank.gd` | 重写，`Sprite.Create` → `Sprite2D.scale` |
| `LayerWorld.cs` / `PlatformField.cs` | `gameplay/PlatformField.gd` | 合并重写为物理平台 |
| `CameraController.cs` / `FxSystem.cs` / `VisualFactory.cs` | `gameplay/World.gd` | 合并简化 |
| `Backdrop.cs` | `gameplay/World.gd::apply_theme()` | 合并 |
| `SmokeTestRunner.cs` | `core/GameManager.gd::begin_smoke()` | 重写为 `--smoke` 模式 |

### 3.2 玩法层

| 源（Unity C#） | 目标（Godot GDScript） | 状态 |
|---|---|---|
| `PlayerController.cs` | `gameplay/Player.gd` | 重写为 `CharacterBody2D` |
| `WeaponSystem.cs` / `AutoShooter.cs` | `gameplay/WeaponSystem.gd` | 合并，仅玩家开火 |
| `Projectile.cs` / `ThrownBlade.cs` / `WeaponFx.cs` | `gameplay/Projectile.gd` | 合并简化，池 → 短命对象 |
| `Enemy.cs` / `EnemyCatalog.cs` | `gameplay/Enemy.gd` + `core/GameConfig.gd` | 数据表 + 行为拆分 |
| `EnemySpawner.cs` | `gameplay/EnemySpawner.gd` | 迁移，公式一致 |
| `CoinPickup.cs` / `PickupField.cs` | `gameplay/CoinPickup.gd` + `World.on_dawn()` | 合并简化 |
| `BuildSystem.cs` / `StructureField.cs` | `gameplay/BuildSystem.gd` + `GameManager.try_buy()` | 合并 |
| `UpgradeSystem.cs` | `gameplay/UpgradeSystem.gd` | 迁移，41 张卡 → 14 张 |
| `HudController.cs` / `MainMenuController.cs` / `UiSkin.cs` | `ui/Hud.gd` / `ui/MainMenu.gd` / `ui/UiSkin.gd` | 重写为 Control |

### 3.3 完全未迁移的源文件

`AllyUnit.cs`（独立宠物）、`EliteAffix.cs`（精英词缀）、`NightEventSystem.cs`（夜晚事件）、`EnemyProjectile.cs`（敌方弹丸）、`Health.cs`（通用血条组件）、`AudioSystem.cs`、`ArtImportPostprocessor.cs` + `DemoSceneBuilder.cs`（Unity Editor 脚本，Godot 无对应）、`EnemyCatalog.cs` 的 Boss 部分。

---

## 4. 未迁移清单（机器可查：`design-freeze.json` 的 `notMigrated`）

| 源冻结项 | 内容 | 原因 |
|---|---|---|
| `EVNT-001` | 7 种夜晚随机事件 | 内容系统，优先保核心循环 |
| `AFFX-001` | 6 种精英词缀 + 分裂/护盾 | 同上 |
| `MAST-001` | 5 大流派奥义 | 卡池 41→14，标签体系未迁 |
| `BOS-001` | 每 5 夜 Boss、狂暴、扇形弹幕、追加强化 | 大型系统 |
| `AUD-001` | 10 种程序化合成音效 | 本版无音频 |
| `PAD-001~004` | 手柄输入门面、焦点导航、全界面接入、设备切换提示 | 本版键鼠；InputMap 已留摇杆扩展位 |
| `UIS-002/003` | 圆角皮肤、渐变进度条、辉光、残影血条 | 用原生 Control 默认样式 |
| `SEED-002` | 随机商店（9 抽 4 / 折扣 / 刷新）、随机平台宝箱 | 商品表固定 6 项、无宝箱 |
| `CRFT-001` | 武器熟练度、图鉴、营地篝火地标 | 助手提案类内容 |
| `ALLY-001` / `TURRET-001` | 独立宠物、炮台自动开火 | 炮台仅生成视觉色块 |
| `ENEMY-PROJ` | 敌人远程弹丸、浮空无视地形 | 敌人统一近身接触伤害 |
| `POOL-001` | 子弹/投掷物/特效对象池 | 改用 `queue_free()` |
| `PHYS-2D` | 敌人/子弹的 Rigidbody2D/Area2D 碰撞 | 玩家与平台已换物理，敌人仍距离判定 |
| `SAVE-ENC` | 存档加密与账号体系 | JSON 明文 |
| `BUILD-PIPE` | CI / 自动化测试 / 导出预设 / 平台构建 | 无 `export_presets.cfg` |

---

## 5. 数值一致性验算

| 公式 | 源工程 | 本版 | 验算 |
|---|---|---|---|
| 波次预算 | `min(130, 14+7(d-1))` | 同 | B(1)=14；B(17)=130 封顶 |
| 刷怪间隔 | `clamp(30×0.86/B(d), 0.22, 0.85)` | 同 | d=1: 1.843→0.85（上限）；d=17: 0.198→0.22（下限） |
| 平台段数 | 按宽度自适应 5~8 | `clamp(round(48/8),5,8)=6` | 共 1+9×6 = **55** 段，冒烟实测 55 ✓ |
| 跳跃高度 | `14.2²/(2×26)≈3.88` | 同 | 3.88 > 层高 3.2 → 可跨一层 ✓ |
| 永久升级价 | `20×(lv+1)` | 同 | 5 级合计 20+40+60+80+100 = 300 余烬 |
| 余烬结算 | `3×天 + ⌊击杀/10⌋ + 8×Boss击杀` | 同 | 第 3 天 0 杀 = 9 |
| 相机视野 | orthoSize 6.5 | `zoom=720/13≈55.38` | 1280/55.38≈23.1 宽 × 13 高 |

**未擅自调平衡**：以上数值全部与源工程 v0.0003 一致。

---

## 6. 已知限制

1. **真人 Play 未验证**：本轮所有验证均为 headless；画面、手感、布局未经人眼确认。
2. **中文字体**：`UiSkin.gd` 用 `SystemFont` 请求「Microsoft YaHei / Noto Sans CJK SC / SimHei」，无中文字体的机器会显示方块；未分发字体文件（授权原因）。
3. **金币落地固定底层**：源工程按层做 `TryLanding`，本版简化。
4. **炮台只有视觉**：可购买、生成色块，但不开火（HUD 里已明写）。
5. **敌人跨层追击**用 Y 向插值，对应源工程 Q16 未确认项，未做真人手感验证。
6. **存档明文** `user://shanhai_meta.json`，demo 级。
7. **mono 版启动提示** `.NET: Failed to load project assembly` 不影响纯 GDScript 工程。

---

## 7. 复现验证

```bash
GODOT="/d/Program Files (x86)/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
ROOT="D:/GameProjects/山海幻灵"
"$GODOT" --headless --path "$ROOT" --editor --quit          # 导入 + 脚本解析，退出 0
"$GODOT" --headless --path "$ROOT" -- --smoke               # 无头冒烟，PASS 退出 0
python "$ROOT/tools/check_project.py"                        # 静态自检
```
