# C# 迁移状态：v0.003 已全量完成

> 本文取代 v0.002 时的「分批迁移 Phase 2/3/4 计划」。用户上轮提出「我希望能用 C# 来写 因为这个语言优化更加容易」，v0.003 重构以行动选择**全量 C#**，`design-freeze.json` 的 Q6 由此关闭。

---

## 1. 工具链事实（本机实测，非推测）

| 项 | 值 |
|---|---|
| Godot | `4.7.2.stable.mono.official`，`D:/Program Files (x86)/Godot_v4.7.2-stable_mono_win64/` |
| .NET SDK | 10.0.402（本机 `dotnet --list-sdks` 实测） |
| NuGet 缓存 | `Godot.NET.Sdk/4.7.2` 已在 `C:/Users/Administrator/.nuget/packages/`，可离线还原 |
| 工程文件 | `ShanhaiSpiritsSurvivors.csproj`：`Godot.NET.Sdk/4.7.2` + `net8.0` + `EnableDynamicLoading` + `Nullable=enable` |
| 程序集名 | ASCII `ShanhaiSpiritsSurvivors`，与 `project.godot [dotnet] project/assembly_name` 对齐（中文名会让 mono 加载失败） |
| 验证 | `dotnet build` 0 警告 0 错误；`--script res://tools/CSharpToolchainProbe.cs` 证实引擎加载并执行 C#；headless 导入 0 解析错误 |

> Godot CLI **不会自动创建 `.csproj`**（需编辑器菜单或手写）；本仓库的 csproj 是手写的。`.cs.uid` 由 `--headless --editor --quit` 自动生成，等同 Unity 的 `.meta`，**必须提交**以保住脚本引用稳定。

---

## 2. v0.002 → v0.003 的实际变化

| v0.002（Phase 1 过渡态） | v0.003（收口） |
|---|---|
| 21 个 GDScript + 1 个 C# 地形层 | **19 个 C#，0 个 GDScript** |
| 4 个 GDScript autoload（GameEvents / RunState / MetaProgress / GameManager） | **0 个 autoload**；静态 C# 状态类 `Run` / `Meta` + 静态事件总线 `Signals` |
| GDScript `GameConfig.gd` 与 C# `PlatformField.cs` 各存一份数值常量 | 数值统一收敛到 C# `GameDefs.cs`，**消除双份常量** |
| 跨语言用 `Node.call("MethodName")` 桥接 | 全部同语言直接调用，桥接代码删除 |
| `World.gd` 用 `_platforms.call("Build")` | `SpiritWorld.cs` 直接 `_platforms.Build(Run.Seed)` |
| 信号用 Godot `signal`（snake_case） | 静态 C# `event`（PascalCase），UI 在 `_Ready` 订阅、`_ExitTree` 退订 |
| 存档 `JSON.parse_string` + Godot Variant | `System.Text.Json` + 强类型 DTO |
| 中文 UI 用 GDScript `SystemFont` | C# `new SystemFont().Set("font_names", Godot.Collections.Array<string>)` |

### 为什么选「静态状态类 + 零 autoload」

全量 C# 后不再需要 Godot autoload 做单例：静态类天然是进程内单点，`Run.Reset()` 显式重置跨局状态，`Signals` 静态事件在 `_ExitTree` 退订避免持有已销毁节点。这比让 autoload 挂 C# 脚本再跨语言取单例更干净，也让 headless 冒烟不依赖场景树里的固定节点名。

**代价**：静态字段在编辑器热重载 / 域重载时需手动重置；本工程用显式 `Reset` 处理，未做热重载兼容（列入已知限制）。

---

## 3. 已作废的过渡期约定

以下 v0.002 约定在 v0.003 **全部作废**，不要照旧文档执行：

- ~~「C# 不能直接调 GDScript `class_name` 静态函数，先转玩法层会造反射泥潭」~~ → 已无 GDScript。
- ~~「4 个 Autoload 信号契约要从 snake_case 切到 PascalCase，需分批」~~ → autoload 已删除。
- ~~「过渡期 GameConfig 常量在两边各存一份，需同步」~~ → 已收敛到 `GameDefs.cs`。
- ~~「Phase 2 数据/核心 7 个 → Phase 3 玩法 9 个 → Phase 4 UI 3 个」~~ → 一次性全量完成并真编译验证。
- ~~「跨平台用方法 `call()` 桥接」~~ → 删除。

---

## 4. 目录与文件

```
scripts/csharp/core/       GameDefs.cs  Rng.cs  Signals.cs  Run.cs  Meta.cs  ArtBank.cs  GameRoot.cs
scripts/csharp/gameplay/   Spirit.cs  Legion.cs  OfferSystem.cs  Projectile.cs  EssencePickup.cs
                           Master.cs  WildSpawner.cs  PlatformField.cs  SpiritWorld.cs
scripts/csharp/ui/         UiSkin.cs  MainMenu.cs  Hud.cs
tools/                     CSharpToolchainProbe.cs（运行时探针）
```

`Main.tscn` 只有一个 `Node2D` 挂 `GameRoot.cs`；其余节点全部运行时程序化构建。

---

## 5. 诚实边界

- 全量 C# 是**本次重构顺带完成的**，不是用户在这一轮单独下单的需求；此前 Q6 未得到文字回答，此处以行动关闭，若用户希望改回混合方案需明确说。
- 编译通过 + 引擎加载 + 冒烟 24/24 **不等于真人 Play 验证**（仍 0 次）。
- mono 版 Godot 在未先 `dotnet build` 时会打印 `.NET: Failed to load project assembly`；本仓库已构建，正常启动不报。
- `Nullable=enable` 下，延迟初始化的 Godot 节点字段用 `= null!;` 标注，属刻意的引擎生命周期取舍，不是忽略空安全。
