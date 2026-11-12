# 山海幻灵 · v0.003

> **山海谷的幻灵师** ｜ Godot **4.7.2.stable.mono** ｜ **全量 C#** ｜ 2D 横板跳跃 ｜ 纯代码自举、零 autoload、零第三方插件
>
> 工程路径：`D:\GameProjects\山海幻灵`　｜　版本规范：`v0.001` 起每次 `+0.001`，不整档大跳

---

## 1. 这一版是什么

v0.003 是一次**成长内核重构**：学习 `Boneraiser Minions`（本地拆包分析见工作区 `docs/BoneraiserMinions-逆向分析.md`）的召唤设计，把「玩家持枪自动射击 + 天亮三选一」换成「幻灵师不攻击 + 培养契灵 + 灵韵驱动唤灵」。

用户设定（原话）：「主角是个山海谷的幻灵师，不断培养自己的幻灵，去打败野生幻兽（幻灵幻兽 是同一种） 然后游戏的地图是横板跳跃」

| 项 | 结果 |
|---|---|
| 版本 | **v0.003** ｜ 设计冻结清单 31 条 ｜ `@trace` 覆盖率 100% ｜ **19 个 C# / 0 个 GDScript** |
| 引擎 | Godot 4.7.2.stable.mono.official（`D:\Program Files (x86)\Godot_v4.7.2-stable_mono_win64`） |
| 真实编译 | `dotnet build` **0 警告 0 错误**；C# 运行时探针证实引擎加载程序集 |
| 真实导入 | `--headless --editor --quit` **退出码 0**，0 解析错误，19 个 `.cs.uid` 生成 |
| 无头冒烟 | **PASS 24/24 断言**：3 昼夜 / 降伏 7 / 唤灵 5 次 / 融合·培养·召唤三条路径均实际执行 / 全覆盖地板 / 玩家落地 / 存档入账 |
| 静态自检 | `docs/self-check.json` |
| 美术 | 16 张 PNG 从 Unity 工程直接复用（Godot 不需要 `.meta`） |
| 2D | `CharacterBody2D` 幻灵师 + `StaticBody2D` 单向平台 + `Camera2D` 跟随 |
| Git | tag `v0.001` → `v0.002` → `v0.003`；`.godot/` 从不入库 |

---

## 2. 核心循环（学自 Boneraiser Minions）

```
幻灵师走位 / 跳跃 / 拾取（完全不攻击）
        ↓
契灵自动迎战 → 野生幻兽被降伏 → 掉「灵韵」
        ↓
灵韵累积跨过动态阈值 EssenceCost(Lv) = 8 + 6×(Lv-1)   ★助手提案公式
        ↓
立即冻结世界 → 唤灵选卡（默认 3 选 1，Lore 后 4 选 1）
        ├─ 召唤 Raise   ：军团 +1 只 Lv1（铺数量）
        ├─ 培养 Bolster ：Lv1→2→3，能力质变（穿透 / 追踪 / 连锁跳数 / 光环扩域）
        └─ 融合 Fuse    ：献祭 N 只 → 1 只高阶（压缩数量换质变，成本显式画在卡面）
        ↓
选完回到原阶段（白天/夜晚均可触发，不必等天亮）
```

**BM 设计的五个要点都已落地**：① 单一资源驱动唯一成长阀门（升级永远等于一次构筑决策）；② 数量 vs 质量的持续张力；③ **Lv3 是融合材料而非终点**；④ 按物种分类 cap 强制多样性；⑤ 稀有资源「灵魄」把灵魂类融合候选权重 ×20，制造跃迁窗口。

局外 Lore 改的是**候选池的可能性空间**（解锁魁/圣阶分支、4 选 1、军团 cap、开局灵韵、重掷次数），不是给玩家加攻击力。

---

## 3. 「幻灵 / 幻兽是同一种」怎么落地的

这是本轮最关键的约束，不是文案：

- **一张表**：`GameDefs.Species`（10 种，凡/灵/魁/圣四阶）同时是野生幻兽的刷怪池，也是唤灵候选的产出池。
- **一个类**：`Spirit.cs` 同一个 `Node2D`，`Faction` 枚举（`Wild` / `Bound`）只切换 AI 目标、色调和数值缩放来源；不派生子类、不复制属性表。
- **同一套能力**：Lv1~3 的质变（`BoltPierces` / `BoltHomes` / `LevelChainJumps` / `AuraRadiusMul`）敌我共用；投射物 `Projectile.cs` 也只有一个类，按 Faction 切命中组。
- **结构对称**：敌我都可受击、都可死亡。野生 AI 只攻击幻灵师（对应 BM「minion 不朽、meanies 追玩家」），所以契灵实际几乎不死。
- **可视证据**：同一张 PNG，野生偏红、契灵偏青；Lv2/Lv3 用顶点小点标记。
- **冒烟断言**：`shared_species_table` 校验「本局野生刷出过的物种 id」与「军团里的物种 id」确有交集。

---

## 4. 三张表 + 一个入口（BM 拆包结论的最小复用）

| 表 | 位置 | 内容 |
|---|---|---|
| `SpeciesDef[]` | `core/GameDefs.cs` | id / 名称 / 阶 / HP / 伤害 / 速度 / 射程 / 攻击型 / **分类 cap** / 解锁夜 / Lv1~3 质变文案 |
| `FuseRecipe[]` | `core/GameDefs.cs` | 献祭阶与数量 / 最低等级 / 灵魄消耗 / 权重 / **所需 Lore** / 产出物种 |
| `LoreDef[]` | `core/GameDefs.cs` | 局外传承：等级上限 / 价格 / 对候选池的作用 |

`OfferSystem.ChooseNewItem()` 是唯一入口：`BuildCandidates()` 按分支生成 → 前置过滤 → 加权无放回抽样 → `Offer.Apply()` 回调 `Legion.Add / LevelUp / Fuse`。**「解锁」= 往候选池加一条分支，而不是在各处写 if**——这正是拆包里 `sCNI_Offer_*` 按类别拆函数的用意。

---

## 5. 重构边界（逐条列清，不冒充你的原话）

| 类别 | 内容 |
|---|---|
| **保留** | 横板跳跃 10 层地图、**第一层全覆盖硬约束 GND-001**、y-down 修正、相机跟随、四套区域主题、16 张美术、分域种子随机、昼夜节奏容器、主页面/HUD、局外存档、无头冒烟、git 版本规范 |
| **替换** | 6 把武器→幻灵军团；14 张数值卡→三类唤灵选项；6 敌人原型→同源物种表；金币→灵韵/贝币/灵魄；5 项数值局外→6 项 Lore；6 项建造物→山海谷营地器具；21 GDScript + 4 autoload→19 C# + 静态状态类 |
| **删除** | `WeaponSystem`、`UpgradeSystem`、起始武器选择与 F 切换、全部 21 个 `.gd` 及其 `.gd.uid`、4 个 autoload |
| **★助手提案** | 灵魄稀有资源、灵韵阈值公式 `8+6×(Lv-1)`、10 种山海物种命名与数值、5 条融合配方、6 项 Lore / 6 项营地器具、「凝神」防软锁补位、营地语义重定位 |

★ 项全部是「学习 BM 时自定」的内容，**不是 BM 原作数值**（BM 的 `vBones_NextLevel` 精确公式未从字节码取得），也**不是你指定的值**，可随时推翻。完整对照见 `docs/refactor-v0.003.md`。

上一轮你提出「希望能用 C# 来写」，这次重构一并收口：**全量 C#，Q6 以行动回答**（不再分批迁移）。`docs/csharp-migration.md` 记录了工具链事实与过渡期约定的作废情况。

---

## 6. 操作

| 键 | 作用 |
|---|---|
| `A / D` `← / →` | 走位 |
| `Space` `W` `↑` | 跳跃（真实重力，单向平台） |
| `1~6` | 白天购置营地器具 ／ 唤灵时选卡 |
| `Q` | 重掷唤灵候选 |
| `E` | 凝神跳过（+3 贝币） |
| `Enter` | 白天提前入夜 ／ 主页面直接开局 |
| `Esc` `P` | 暂停 |
| `R` / `M` | 重开一局 ／ 返回山海谷 |

> 手柄适配、音频未在本版迁移（见第 8 节）。

---

## 7. 复现验证

```bash
GODOT="/d/Program Files (x86)/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
ROOT="D:/GameProjects/山海幻灵"

# ① C# 真编译
dotnet build "$ROOT/ShanHaiHuanLing.csproj" -v minimal --nologo

# ② 资源导入 + 全脚本解析（生成 .cs.uid）
"$GODOT" --headless --path "$ROOT" --editor --quit

# ③ C# 运行时探针
"$GODOT" --headless --path "$ROOT" --script res://tools/CSharpToolchainProbe.cs

# ④ 无头 Play 冒烟（自动 3 昼夜，打印 SMOKE_JSON，PASS 退出 0 / FAIL 退出 1）
"$GODOT" --headless --path "$ROOT" -- --smoke

# ⑤ 静态自检
python "$ROOT/tools/check_project.py"
```

证据归档：`docs/compile-evidence.txt`、`docs/smoke-evidence.txt`、`docs/smoke-report.json`、`docs/self-check.json`。

> **mono 版说明**：启动时若打印 `.NET: Failed to load project assembly` 只有在**未先 `dotnet build`** 时才会出现；本仓库已构建程序集，正常启动不报此错。

---

## 8. 诚实边界

- **真人 Play Mode 仍是 0 次**。headless 只证明不报错、状态机走得通、数值链路闭合，**不证明画面、手感、中文字体或好玩**。
- 未做：Seal / Banish / Giga Banish、18 职业 Heritage、Heroic 难度旋钮、190+ 召唤项与图鉴、Boss 专属 AI、音效、手柄、粒子特效、对象池、敌人物理碰撞、导出预设、CI、存档加密。完整清单见 `design-freeze.json.notImplemented`。
- 中文靠 `SystemFont` 请求系统中文字体，无中文字体的机器会显示方块；未内嵌字体文件。
- 野生幻兽与契灵是飞行体（y 插值），不参与平台碰撞；只有幻灵师受重力与平台约束。
- 冒烟含 6 项**仅 `--smoke` 生效**的注入（固定种子、额外契灵、灵魄、100 灵韵、近场刷怪、伤害 +200、阶段 0.8 秒），正常游戏数值完全不受影响。
- **本轮未修改 Boneraiser Minions 的任何文件，也未修改 Unity 源工程。**

---

## 9. 文档与下一步

| 文档 | 作用 |
|---|---|
| `docs/refactor-v0.003.md` | 逐条重构对照 + ★助手提案清单 + 待确认问题 |
| `docs/design-freeze.json` | 31 条设计冻结项、版本规范、验证证据、未做清单、Q1~Q8 |
| `docs/migration-map.md` | Unity → Godot 初版迁移映射（历史，部分口径已被 v0.003 覆盖） |
| `docs/csharp-migration.md` | C# 工具链事实与「全量 C# 已完成」说明 |
| `docs/roadmap.md` | 商业完成度路线图 |

**下一步最该做的不是继续加系统，是打开 Godot 按一次 Play**：确认中文字体、跳跃落点、相机跟随、唤灵面板可读性。然后请回 `design-freeze.json` 的 Q2（★助手提案是否保留）与 Q3（昼夜是否保留）——这两条决定后续所有数值与结构。
