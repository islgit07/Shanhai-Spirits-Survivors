# 随从跟随（SPIRIT-FOLLOW）决策记录 — 2026-11-16（v0.0005）

## 1. 需求原话
- 「接下来做随从跟随，你可以看看 D:\GameProjects\BoneraiserGodot 项目的随从跟随逻辑，
  虽然这个是俯视平面的，但是我觉得里面的逻辑你是可以参考的」
- 「切记 是有碰撞体的 主角和随从不能叠在一起 以后加入敌人系统也是这一段逻辑」
- 「而且玩家可以踩在随从头上」

## 2. 拍板结果（AskUserQuestion 四问）
| 决策 | 选择 |
|---|---|
| 跟随范围 | 本命 + 局内领取的队友（最多 5 只同场） |
| 移动方式 | **踩平台走**（有重力、跳跃、单向平台），不是悬浮飞行 |
| 队形 | 散点编队（参考项目黄金角散布） |
| 避障 | 轻避障，不钻进实心块 |

补充硬约束（用户追加）：随从必须有真实碰撞体；主角与随从不能重叠；玩家可以踩在随从头上；
这一套角色碰撞逻辑以后敌人系统直接复用。

## 3. 参考项目（BoneraiserGodot）可移植的部分
读了 `scripts/Actors.cs` 的 `Minion._Process`、`scripts/Defs.cs` 的 `Minion*` 常量：

| 机制 | 参考实现 | 出处 |
|---|---|---|
| 个人落点 | 黄金角 `uid * 2.39996` + 半径按 `uid % 5` 分档 | `Minion.Setup` |
| 闲逛漂移 | `_wander` 每 1.2~2.8 秒换 ±(14,11) 目标，`Lerp` 跟随 | `Minion._Process` |
| 呼吸浮动 | `_phase += dt * 2.1`，`sin/cos` 小幅偏移 | 同上 |
| 越远越快 | `Clamp(Base + dist * CatchUp, Base, MaxSpeed)` | `MinionBaseSpeed=70 / CatchUp=3.4 / Max=430` |
| 死区停 | `dist > MinionStopRadius(7)` 才移动 | `sMinion_GetMoveSpeed` |
| 不出主角身 | `ClearPlayer(dt)` 强制分离 | 同上 |
| 敌人扑击 / 战斗 | `MinionAggroRange` 内扑敌 | **不移植**（本作还没有战斗） |

## 4. 移植到横板的改动
参考项目是俯视无重力，本作是横板平台跳跃，所以：
- **不做**悬浮直飞：随从是 `CharacterBody2D`，有重力、能站方块、能跳。
- 落点纵向压扁并整体上浮：`sin(ang) * rad * 0.38 - 0.50`——横板不需要环绕一圈，
  只要在主角肩上方散开；压扁同时减少「为了够到高落点而疯狂起跳」。
- 补了四个平台专用行为：跨层自动起跳（目标高于自身 0.7 格且水平 2.4 格内）、
  卡住自救跳（想走超过 0.35 秒没动）、单向平台主动下穿（目标在下方 0.8 格）、
  掉队/坠落瞬移（> 13 格或掉出地图）。
- 呼吸浮动只作用 `Sprite2D.Position`，不动物理体——否则会和落点互相拉扯、导致持续抖动。

## 5. 角色碰撞体系（这段以后敌人复用）
在 `GameDefs` 新增两层：
```
LayerSolid  = 1  (地形，已有)
LayerOneWay = 2  (单向平台，已有)
LayerActor  = 4  (主角 / 未来敌人)
LayerSpirit = 8  (随从)
```
- **主角**：`layer = LayerSolid | LayerActor`，`mask = Solid | OneWay | Actor | Spirit`。
  - 保留 `LayerSolid`：脆弱 / 弹跳 / 宝箱方块的 Area2D 用 `mask = 1` 识别玩家，改掉会全部失联。
  - `mask` 含 `LayerSpirit`：**玩家才能踩在随从头上**（`IsOnFloor` 需要检测到随从碰撞体）。
  - `mask` 含 `LayerActor`：敌人接入后主角与敌人互相阻挡。
- **随从**：`layer = LayerSpirit`，`mask = Solid | OneWay | Actor`。
  - 被地形与主角挡住；不含 `LayerSpirit` → 随从彼此不硬碰（避免 4~5 只互相卡死），
    靠散点槽位 + 每帧硬分离保持间距。
- **防卡死三件套**：
  1. 随从 `ProcessPhysicsPriority = -10`，先于主角处理，让路先生效；
  2. 与主角小于 `SpiritAvoidRadius(0.85)` 时让路，且**直接给满速**；
  3. `SeparateFrom`：AABB 重叠时沿水平轴硬推出去（垂直推会塞进地板或顶到头顶）。

## 6. 被踩的处理
随从每帧在头顶放一个 `0.70 × 0.30` 薄矩形做形状查询，只查 `LayerActor`：
- 命中 → `IsStoodOn = true`，水平速度归零、只保留贴地/重力，**停止跟随**，玩家能站稳；
- 玩家离开 → 自动恢复跟随。
用几何查询而不是反查主角碰撞列表，是因为主角物理步进晚于随从（优先级差 10）。

## 7. 实体队伍同步
`SpiritFollowGroup`（`Main.tscn` 子节点）持有一份实体列表：
- 数据源 = `GameRoot.ChosenSpirit`（槽位 0）+ `GameRoot.Companions`（`RunRoster`，槽位 1..N）。
- `Refresh()` 幂等：新成员从 `spirit_pet.tscn` 生成并 `SnapToTarget`；成员消失则 `QueueFree`；
  已有成员只同步等级/资质，不重建（不打断跟随手感）；槽位按顺序重排。
- 触发点：开局 `BeginRun`、三选一选卡后 `GameRoot.RefreshCompanions()`。
- 队友还没有定义卡与美术，用 6 色占位色板区分；**零技能、零属性效果**（用户自己设计）。

## 8. 否决项
1. 悬浮飞行跟随 —— 用户明确选「踩平台走」。
2. 主角 `mask` 不含随从层、靠主线分离 —— 那样踩不上头，不满足追加要求。
3. 随从之间也硬碰撞 —— 4~5 只容易互相卡死；改用槽位 + 硬分离。
4. 用 `Area2D` 当随从碰撞体 —— 站不住，不能满足「踩头」。
5. 修改 `project.godot` / 新增 `Input Map` —— 用户文件未提交，不碰。
6. 给队友编技能 / 攻击 / 伤害 —— 越界，用户以后自己设计。

## 9. 范围边界
版本保持 `0.0005`，未升版、未打 tag、未 push。
未实现：战斗、敌人、随从攻击、技能、真实掉落、随从之间的软推挤可视化。
