# 山海幻灵 · C# 迁移路线（v0.002）

> 用户要求：「我希望能用 C# 来写，因为这个语言优化更加容易」。
> 本文只记录**已验证的事实**与**分批计划**，不把未迁移的脚本算作已完成。

---

## 1. 为什么必须分批，而不是一次重写 21 个文件

1. **跨语言边界是单向的**：GDScript 可以实例化 C# `[GlobalClass]` 并 `call()` / `get()` 它的成员；但 C# **不能**直接调用 GDScript 的 `class_name` 静态函数（如 `GameConfig.layer_y()`），只能走 `GDScript.Call()` 反射，既慢又失去类型安全。所以「先转玩法层、后转数据层」会造出一片反射泥潭，必须**按依赖顺序**从数据层往上转。
2. **4 个 Autoload 是全局契约**：`GameEvents / RunState / MetaProgress / GameManager` 被 20 个脚本直接按全局名引用。改成 C# 后信号命名要从 `snake_case` 切到 PascalCase + `EventHandler`，所有订阅点必须同批改完，否则运行时断连。
3. **验证成本**：全量重写约 1854 行 GDScript → 约 2500+ 行 C#，一次掷出 20+ 文件无法定位是哪一个引入回归。分批后每批都能 `dotnet build` + headless 冒烟独立判定。
4. **版本节奏**：用户明确要求「版本更新不要那么快」，每批 +0.001 正好契合。

---

## 2. 工具链事实（本机实测，非推测）

| 项 | 值 |
|---|---|
| 引擎 | Godot **4.7.2.stable.mono.official**（`D:/Program Files (x86)/Godot_v4.7.2-stable_mono_win64/`） |
| .NET SDK | **10.0.402**（`C:/Program Files/dotnet/sdk`） |
| MSBuild SDK | `Godot.NET.Sdk/4.7.2`，本机 NuGet 缓存已有，**可离线还原** |
| 目标框架 | `net8.0`（SDK 10 向下兼容，实测 0 警告 0 错误） |
| 工程文件 | `ShanHaiHuanLing.csproj`（手写；Godot 命令行不会自动生成） |
| 程序集名 | `ShanHaiHuanLing` —— **ASCII**，与 `project.godot` 的 `dotnet/project/assembly_name` 严格一致；中文程序集名会导致引擎找不到 dll |
| 输出 | `.godot/mono/temp/bin/Debug/ShanHaiHuanLing.dll`（已被 `.gitignore` 排除） |
| 运行时探针 | `godot --headless --path . --script res://tools/CSharpToolchainProbe.cs` → `[CSHARP_PROBE] ok=1 engine=4.7.2-stable` |

**每批改完的标准验证三连**：

```bash
dotnet build ShanHaiHuanLing.csproj -v minimal --nologo     # 期望 0 错误
godot --headless --path . --editor --quit                    # 刷新全局类注册
godot --headless --path . -- --smoke                        # 期望 PASS 12/12
```

---

## 3. 迁移批次

| 批次 | 范围 | 文件数 | 状态 | 版本 |
|---|---|---|---|---|
| **Phase 1** | C# 工具链 + **地形层 `PlatformField.cs`**，同时落地 GND-001 第一层全覆盖硬约束、修正 y 轴方向 | 1 个玩法组件 + csproj + 探针 | ✅ **已完成**（v0.002） | 0.002 |
| **Phase 2** | 数据 / 核心层：`GameConfig` `RunSeed` `RunState` `MetaProgress` `GameEvents` `GameManager` `Bootstrap` | 7 | ⬜ 待做 | 0.003 |
| **Phase 3** | 玩法层：`Player` `Projectile` `WeaponSystem` `Enemy` `EnemySpawner` `CoinPickup` `BuildSystem` `UpgradeSystem` `World` | 9 | ⬜ 待做 | 0.004 |
| **Phase 4** | UI 层：`UiSkin` `MainMenu` `Hud` + 移除最后的 GDScript 外壳 | 3 | ⬜ 待做 | 0.005 |

**Phase 2 是关键闸门**：`GameConfig` 转成 C# 强类型数据表后，`PlatformField.cs` 里那份过渡期常量才能删掉，两套数值收敛为一套。在此之前**必须同步维护两处常量**（已列为 knownLimit）。

**每批验收**：`dotnet build` 0 错误 → headless 导入 0 解析错误 → 冒烟 12/12 PASS → 静态自检全过 → 文档 `design-freeze.json` 更新 file 字段 → +0.001 打 tag。

---

## 4. 过渡期互操作约定（Phase 2 之前有效）

1. GDScript 侧以 `Node2D` 静态类型持有 C# 组件，通过**方法调用**桥接，不用属性反射（实测更稳）：
   ```gdscript
   _platforms = PlatformField.new()          # C# [GlobalClass]
   _platforms.call("Build", seed)            # C# public void Build(int)
   platform_count = int(_platforms.call("GetSegmentCount"))
   ground_continuous = bool(_platforms.call("IsGroundContinuous"))
   ```
2. C# 组件**自包含**：自带过渡期常量与确定性种子，不反向依赖 GDScript 静态函数。
3. **信号命名**：GDScript 保持 `snake_case`；C# 一律 `[Signal] + PascalCase + EventHandler`（本轮 `GroundBuiltEventHandler(int segmentCount, bool groundContinuous)` 已示范）。跨语言暂不直连信号，用方法返回值传递状态，避免两套命名混线。
4. **UID**：`.cs.uid` 与 `.gd.uid` 一样必须纳入 git（本轮 2 个 `.cs.uid` 已入库）。

---

## 5. 本轮（v0.002）已落地 / 未落地

**已落地**
- `ShanHaiHuanLing.csproj`、`project.godot` 程序集名对齐、`tools/CSharpToolchainProbe.cs` 探针。
- `scripts/csharp/PlatformField.cs`：`[GlobalClass]`、只读 `Segment` 结构、强类型 `List<Segment>`、typed signal、`IsGroundContinuous()` 运行时不变量、`QueueFree()` 安全清理。
- 删除 `scripts/gameplay/PlatformField.gd`（被 C# 取代），`World.gd` 改为桥接调用。
- GND-001 第一层全覆盖 + AXIS-001 y 轴修正；冒烟新增 `ground_continuous` / `player_grounded` 两条断言并实测为 true。

**未落地（不冒充完成）**
- 其余 **20 个 GDScript 仍是 GDScript**，包括全部 4 个 Autoload、全部 UI、全部玩法除地形外。
- 没有 C# 版 `GameConfig`，过渡期常量双份。
- 没有 C# 信号总线，跨语言暂用方法调用。
- 没有把 C# 组件做成可拖拽 `.tscn`（当前运行时 `new()`，符合工程「零场景拖拽」既有风格）。
- **真人 Play Mode 仍为 0 次**：C# 编译通过、headless 冒烟通过，都不等于画面/手感被验证过。

---

## 6. 待用户确认

`design-freeze.json` 的 **Q6**：按 Phase 2→3→4 分批推进，还是一次性全量重写？
建议分批——每批都有独立可复核的编译 + 冒烟证据，出问题能立刻定位到批次。
