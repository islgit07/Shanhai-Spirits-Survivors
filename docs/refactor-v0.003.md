# v0.003 重构对照：学习 Boneraiser Minions 的召唤设计

> 日期 2026-11-12　｜　源版本 v0.002 → 本版 v0.003
> 设计参考：工作区 `docs/BoneraiserMinions-逆向分析.md`（基于本地 `data.win` 拆包，证据分级 A/B/C/D）
> 本文只记录**本次真实发生的改动**，不把计划写成已完成。

---

## 1. 用户原话与解读边界

> 「现在我要对山海幻灵进行一次重构，请学习Boneraiser Minions 的游戏设计，这个游戏的设定是主角是个山海谷的幻灵师，不断培养自己的幻灵，去打败野生幻兽（幻灵幻兽 是同一种） 然后游戏的地图是横板跳跃」

| 明确给出 | 落地 |
|---|---|
| 主角是山海谷的幻灵师 | `Master.cs`：只走位/跳跃/拾取，**完全不攻击** |
| 不断培养自己的幻灵 | `Legion.cs` + `Bolster` 选项：Lv1→2→3 质变，Lv3 成为融合材料 |
| 打败野生幻兽 | `WildSpawner.cs` 波次 + 契灵自动迎战 + 降伏掉灵韵 |
| **幻灵幻兽是同一种** | `SpeciesDef` 单表 + `Spirit` 单类 + `Faction` 枚举；刷怪池与唤灵池同源 |
| 地图是横板跳跃 | `PlatformField.cs` + `Master.cs`，10 层，第一层全覆盖硬约束不变 |

「学习 BM 的游戏设计」被解读为移植**机制结构**（单一资源阀门 / 三类构筑出口 / Lv3 是材料 / 分类 cap / 局外改可能性空间），而非移植 BM 的数值、美术或世界观。所有自定内容集中在第 4 节标为 ★助手提案。

---

## 2. 逐文件对照

### 2.1 删除（21 个 GDScript + 1 个过渡期 C# 地形层）

| 原文件 | 原因 |
|---|---|
| `core/GameConfig.gd` | 数值表并入 `core/GameDefs.cs`（C# 单点，消除双份常量） |
| `core/GameEvents.gd` | → `core/Signals.cs`（静态 C# event） |
| `core/RunSeed.gd` | → `core/Rng.cs` |
| `core/RunState.gd` | → `core/Run.cs`（静态状态类） |
| `core/MetaProgress.gd` | → `core/Meta.cs`（`System.Text.Json`） |
| `core/GameManager.gd` | → `core/GameRoot.cs`（状态机合并自举入口） |
| `core/Bootstrap.gd`、`core/ArtBank.gd` | → `GameRoot.cs` / `ArtBank.cs` |
| `gameplay/WeaponSystem.gd` | **整系统删除**：玩家不再开火 |
| `gameplay/UpgradeSystem.gd` | → `gameplay/OfferSystem.cs`（BM 式唤灵） |
| `gameplay/Enemy.gd` / `EnemySpawner.gd` | → `Spirit.cs` / `WildSpawner.cs`（同源物种） |
| `gameplay/Player.gd` | → `Master.cs`（幻灵师不攻击） |
| `gameplay/Projectile.gd` | → `Projectile.cs`（敌我共用） |
| `gameplay/CoinPickup.gd` | → `EssencePickup.cs`（一次入账三种资源） |
| `gameplay/BuildSystem.gd` | → `Run.cs` 内联聚合 + `CampItem` 表 |
| `gameplay/World.gd` | → `SpiritWorld.cs` |
| `ui/UiSkin.gd` / `MainMenu.gd` / `Hud.gd` | → 同名 `.cs` |
| `scripts/csharp/PlatformField.cs`（旧平铺路径） | → `scripts/csharp/gameplay/PlatformField.cs`，逻辑保留、常量改从 `GameDefs` 读 |

`project.godot` 的 `[autoload]` 四行整段删除。全工程 **0 个 autoload**。

### 2.2 新增（19 个 C#）

```
scripts/csharp/core/       GameDefs · Rng · Signals · Run · Meta · ArtBank · GameRoot        (7)
scripts/csharp/gameplay/   Spirit · Legion · OfferSystem · Projectile · EssencePickup
                           Master · WildSpawner · PlatformField · SpiritWorld                (9)
scripts/csharp/ui/         UiSkin · MainMenu · Hud                                           (3)
```

### 2.3 保留但被替换语义

| v0.002 | v0.003 | 说明 |
|---|---|---|
| 金币（击杀掉落） | 灵韵 / 贝币 / 灵魄 | 灵韵 = 经验 + 召唤阀门；贝币仅用于营地；灵魄稀有跃迁 |
| 6 把武器 | 10 种同源物种 | 攻击型从「武器 kind」移到「物种 AttackKind」 |
| 14 张加数值卡 | Raise / Bolster / Fuse 三分支 | 升级 = 构筑决策，不再加攻击力 |
| 5 项数值 Meta | 6 项 Lore | 改候选池，不改玩家数值 |
| 6 项建造物 | 6 项营地器具 | 数量不变，语义重定位（灵契坛 / 聚灵井 / 引灵灯 / 镇兽碑 / 疗愈庐 / 草木膏） |
| F 切武器 | 移除 | 起始契灵在主页面选，局内不可切 |
| 天亮固定三选一 | 灵韵即时唤灵 | 昼夜仍作节奏容器，但不再是唯一升级入口 |

---

## 3. BM 机制 → 本工程映射

| BM 拆包结论 | 本工程实现 |
|---|---|
| `vBones` / `vBones_NextLevel`：骨头既是经验又是召唤货币 | `Run.Essence` / `GameDefs.EssenceCost(Lv)`；`AddEssence` 跨阈值即 `PendingOffers++` |
| `sChooseNewItem → sCNI_Offer_* → sCNI_GRM_Add` | `OfferSystem.BuildCandidates()` 三分支 → `ChooseNewItem()` 加权抽样 |
| `BI_Raise` / `BI_Bolst` / `BI_Fuse` | `OfferKind.Raise / Bolster / Fuse` |
| `Blst1` / `Blst2` 质变文案（穿透 / 追踪 / 连锁） | `SpeciesDef.Traits[3]` + `GameDefs.BoltPierces/BoltHomes/LevelChainJumps/AuraRadiusMul` |
| Lv3 + Hero Soul → Demon；Lv3 是材料 | `FuseRecipe fuser_lv3`（Lv3 + 灵魄 → 梼杌）；`CostMinLevel=3` |
| `sGetMinionCap` / 分类独立 cap | `Legion.CanAdd()`：`Run.LegionCap` 全局 + `SpeciesDef.Cap` 按物种 |
| `sCNI_DrawSacrificeMinions` 显式献祭成本 | `Offer.SacrificeUids` + `Offer.CostText` 直接印在卡面 |
| Hero Soul 把 Meldus 概率 ×20 | 持有灵魄时 `SoulCost>0` 配方权重 ×20 |
| `MtPlayer_*` Lore 解锁候选分支 | `FuseRecipe.RequiredLore` / `OfferSystem.TierUnlocked()` / `Meta.OptionCount` |
| `vMaxOptions_Bone` 默认 3 → Meta 4 | `GameDefs.PickCount=3` + `lore_options` → 4 |
| Re-roll / Seal / Banish 各 3 次 | 只实现 Re-roll（`Run.Rerolls`）与 Skip；**Seal / Banish 未做** |
| `sLegion_Add/Remove/LevelUp` | `Legion.Add/Remove/LevelUp/Fuse` |
| 玩家不攻击、minion 自动战斗 | `Master.cs` 无攻击逻辑；`Spirit.AttackWilds()` 自动索敌 |

---

## 4. ★助手提案（用户未逐项确认，可随时推翻）

| # | 内容 | 为什么自定 |
|---|---|---|
| 1 | **灵魄**稀有资源 + 灵魂类融合权重 ×20 | BM 有 Hero Soul，但掉率、消耗、倍率未从字节码读出（C 级） |
| 2 | **灵韵阈值 `8+6×(Lv-1)`** | BM `vBones_NextLevel` 精确递推公式**未取得**，不能照抄；「每 100 骨头」出自 2022 EA 评测（D 级） |
| 3 | **10 种山海物种**命名 / 数值 / Lv1~3 文案 | 世界观是用户新设定，BM 是哥特骷髅，不可能沿用 |
| 4 | **5 条融合配方**（2 凡→山魈、3 凡→灵猿、3 灵→赑屃、Lv3+灵魄→梼杌、2 魁+灵魄→白泽） | 结构学 BM（2/3/5 换高阶、Lv3+英魂），具体输入输出为自定 |
| 5 | **6 项 Lore / 6 项营地器具**具体条目 | BM 有 27 项 Lore、604 条职业天赋，本版只做最小可验证集 |
| 6 | **「凝神」补位选项** | 候选池被 cap / 前置全卡死时防软锁；BM 用别的机制保证不空池 |
| 7 | 白天建造物 → 营地器具的语义重定位 | 「木炮台自动开火」与「幻灵师不攻击」冲突，必须改语义 |

> 这些项**不是 BM 原作数值，也不是用户指定值**。若 Q2 回答「推翻」，改 `GameDefs.cs` 的表即可，不动架构。

---

## 5. 冒烟测试的真实调试过程（不粉饰）

| 轮次 | 结果 | 发现与修复 |
|---|---|---|
| 1 | PASS 21/21 | 但 `smoke_fused=false`——融合只「候选池可达」，**没被真实 Apply() 执行**。判为验证缺口，不是通过 |
| 2 | **FAIL** `branches_complete=false` | 把三类出口升级成硬断言后失败。根因：断言在终局检查，而军团已被融合从 3 压到 2，凑不齐融合前置。**测试断言时机错误，非游戏缺陷** |
| 3 | **PASS 24/24** | 断言移到开局注入后；冒烟换签直到三类出口都真实执行。终局 `legion_size=2` 正是「压缩数量换质变」的实证 |

调试期间未改动任何正常游戏数值；所有注入都只在 `--smoke` 分支生效。

---

## 6. 已知未做（诚实清单）

真人 Play（0 次）／ Seal·Banish·Giga Banish ／ 18 职业 Heritage ／ Heroic Force 难度旋钮 ／ 190+ 召唤项与图鉴 ／ Boss 专属 AI 与精英词缀 ／ 夜晚事件与流派奥义 ／ 音效音乐 ／ 手柄 ／ 粒子与命中特效（现为纯色块）／ 对象池 ／ 敌人物理碰撞 ／ `export_presets.cfg` ／ CI ／ 存档加密 ／ 中文离线内嵌字体。

---

## 7. 待确认（对应 `design-freeze.json` Q1~Q8 的新增部分）

- **Q2（最优先）**：★助手提案 7 项是否保留？灵韵阈值 `8+6×(Lv-1)` 节奏合适吗？
- **Q3**：昼夜是否继续保留为节奏容器？BM 是连续波次，若去掉，白天营地放哪里？
- **Q4**：「同一种」是否要进一步做成「击败野生幻兽可当场收服」？目前只做到共用定义表（BM 本身也没有捕捉机制）。
- **Q5**：契灵是否应当真的会阵亡？本版结构对称但野生 AI 只打幻灵师。
- **Q7**：先扩物种数量，还是先把现有 10 种的动作/技能做深？
- **Q8**：Lore 是否要像 BM 一样可出售 respec？当前买入不可退。

**Q6 已由本轮行动回答**：全量 C#（不再分批迁移），19 个 `.cs` / 0 个 `.gd`。
