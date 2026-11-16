using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// v0.0001 从零开始的集中数值表：只保留三样东西——
///   ① 世界几何（层高 / 宽度 / 地板口径）
///   ② 《终焉之莉莉》式移动手感（白箱调出后写回这里即生效）
///   ③ 相机与背景表现常量
/// 战斗 / 成长 / 循环数值在本轮对话中逐步设计后追加，不预留占位。
/// </summary>
public static class GameDefs
{
    // ---------------------------------------------------------------- 世界几何
    public const float WorldHalfWidth = 28.0f;
    public const float GroundY = -2.2f;
    public const int LayerCount = 10;
    public const float LayerSpacing = 3.0f;
    public const float PlatformThickness = 0.24f;
    public const float SpawnAboveGround = 1.0f;
    public const float FallKillY = 8.0f;

    public static float LayerY(int layer) =>
        GroundY - Mathf.Clamp(layer, 0, LayerCount - 1) * LayerSpacing;

    // ---------------------------------------------------------------- 碰撞层
    // 1 = 实体地板（GND-001，永不穿透）；2 = 单向平台（可从下方跳穿、可下穿）。角色 mask = 1|2。
    public const uint LayerSolid = 1u;
    public const uint LayerOneWay = 2u;
    // SPIRIT-FOLLOW 角色碰撞层（2026-11-16）：地形之外的「角色」分两层，主角 / 随从 / 未来敌人共用一套规则。
    //   LayerActor  = 主角与敌人：彼此互挡，也会挡住随从。
    //   LayerSpirit = 随从：被主角与敌人挡住；彼此不硬碰（靠散点槽位 + 软分离，避免四只互相卡死）。
    // 主角 layer 保留 LayerSolid：脆弱/弹跳/宝箱的 Area2D 用 mask=1 识别玩家，改掉会全部失联。
    // 主角 mask 含 LayerSpirit，是为了「玩家可以踩在随从头上」——IsOnFloor 需要检测到随从的碰撞体。
    public const uint LayerActor = 4u;
    public const uint LayerSpirit = 8u;
    // LayerSpiritTop = 随从头顶的一小块踩踏平台（StaticBody2D）：
    //   · 主角 mask 含它 → 能踩在随从头上；
    //   · 主角 mask 不含 LayerSpirit → 水平方向永远不被随从顶住（避免"随从挡路"）；
    //   · 随从本体仍然被主角挡住（随从 mask 含 LayerActor），所以它不会主动压到主角身上。
    public const uint LayerSpiritTop = 16u;

    // ---------------------------------------------------------------- 随从跟随（SPIRIT-FOLLOW）
    // 参考 BoneraiserGodot 的随从编队：黄金角散布个人落点 + 闲逛漂移 + 越远越快追赶 + 死区停。
    // 横板差异：本作随从踩平台走（有重力 / 跳跃 / 单向平台），不是俯视悬浮。
    public const float SpiritBodyWidth = 0.62f;          // 碰撞体宽（略小于视觉 1 格，避免蹭墙）
    public const float SpiritBodyHeight = 0.88f;         // 碰撞体高（主角 1.2，站在头上仍有一格空间）
    public const float SpiritFollowBaseSpeed = 6.4f;     // 落点附近的最低速度
    public const float SpiritFollowCatchUp = 3.0f;       // 每 1 格差距追加的速度（越远越快）
    public const float SpiritFollowMaxSpeed = 16.0f;     // 追赶速度上限（不瞬移）
    public const float SpiritFollowAccelGround = 80.0f;  // 地面加速（跟手）
    public const float SpiritFollowAccelAir = 50.0f;     // 空中微调
    public const float SpiritFollowJumpSpeed = 18.4f;    // 与主角满跳同高，保证能跟上层
    public const float SpiritFollowStopRadius = 0.16f;   // 落点死区（小于此值不动，防抖）
    public const float SpiritFollowAnchorMin = 1.05f;    // 个人落点最小半径
    public const float SpiritFollowAnchorStep = 0.30f;   // 半径档距（每人远近不同）
    public const float SpiritFollowAnchorFlatten = 0.38f;// 纵向压扁（横板不需要环绕一圈）
    public const float SpiritFollowAnchorLift = 0.50f;   // 落点整体上浮（灵兽飘在肩上方）
    public const float SpiritFollowWanderRange = 0.22f;  // 闲逛漂移幅度
    public const float SpiritFollowJumpCooldown = 0.40f; // 两次跳跃最小间隔
    public const float SpiritFollowDropTime = 0.18f;     // 主动下穿单向平台窗口
    public const float SpiritFollowTeleportDist = 13.0f; // 掉队超过此距离 → 回到主角身边
    public const float SpiritFollowStuckTime = 0.35f;    // 想走却走不动多久后自救跳
    public const float SpiritAvoidRadius = 0.85f;         // 贴身兜底：与主角小于此距离 → 径向退开（不叠）
    public const float SpiritYieldLaneLength = 1.90f;     // 让路通道：主角前进方向前方的长度（格）
    public const float SpiritYieldLaneHalfHeight = 0.95f; // 让路通道半高（同一水平通道内才算挡路）
    public const float SpiritYieldMinSpeed = 0.6f;        // 主角水平速度超过此值才算「在走」（才启用通道让路）
    public const float SpiritYieldJumpCooldown = 0.90f;   // 让路跳跃最小间隔（防连跳）
    public const float SpiritPushMargin = 0.12f;          // 硬分离额外余量：提前推开，物理上撞不到主角
    public const float SpiritFacingHoldTime = 0.45f;      // 落点镜像到身后的朝向滞回（防左右横跳）
    // 原地跳修复（2026-11-16）：槽位的纵向偏移必须限制在「站在同一地面就够得到」的范围，
    // 否则随从会认为目标永远在头顶 → 落地触发跨层跳 → 跳完落下再触发，形成原地连跳。
    public const float SpiritSlotLiftMax = 1.20f;         // 落点最多高于主角中心多少（格）——只防极端值；
                                                          // 跳跃判定已改用「主角与随从的实际高度差」，不再被落点高度诱发
    public const float SpiritSlotDropMax = 0.10f;         // 落点最多低于主角中心多少（格）
    public const float SpiritJumpUpThreshold = 0.90f;     // 目标高于自身超过此值才判定为「需要跨层」
    public const float SpiritStuckJumpCooldown = 1.20f;   // 卡住自救跳的最小间隔（防原地连跳）
    // 防卡死寻路（SPIRIT-PATH）：离玩家太远且持续一段时间 → 在方块网格上 BFS 找路回去，找不到再瞬移。
    public const float SpiritFindPathDist = 6.0f;         // 超过此距离开始累计「远离时长」
    public const float SpiritFindPathDelay = 2.5f;        // 持续远离多久后进入寻路模式（给正常跟随留出追赶时间）
    public const float SpiritRepathInterval = 0.6f;       // 寻路模式中重新计算路径的间隔
    public const float SpiritSeekTimeout = 5.0f;          // 寻路模式最长持续时间，超时直接瞬移
    public const int SpiritNavMaxRise = 3;                // BFS 允许的最大上跳行数（满跳约 3 格）
    public const int SpiritNavMaxSpan = 5;                // BFS 允许的最大水平跨度（满跳约 5 格）
    public const int SpiritNavMaxDrop = 6;                // BFS 允许的最大下落行数
    public const float SpiritBobHeight = 0.07f;           // 视觉呼吸幅度（只动 Sprite，不动物理体）
    public const string SpiritGroup = "spirit";          // 随从节点组（冒烟 / 查询用）

    // ---------------------------------------------------------------- 方块地图
    // 1×1 世界单位方块网格：列 = 世界 x（-24..+24）；行从 GroundY 向上逐格（row 0 = 地面层）。
    // row 0 = 全覆盖实体普通块（GND-001）；row 1..2 恒空（地面净空，见 BlockHeadroomRows）；
    // row 3..BlockRows-1 = 自由随机区（run 撒点 + 可达性保证）。
    // 伪随机四原则：
    //   ① 紧贴地面两层不生成（首层顶面高 3.0 < 满跳 3.67，一次跳跃刚好够上）；
    //   ② 每个可站立块上方净空 2 格（身高 1.2 > 单格 1.0，双层才能站人不顶头）；
    //   ③ 大多数 run 是纯跳跃平台（特殊块整段至多一块，权重合计 ≈0.17）；
    //   ④ 同行空档 ≤ 4 格（满跳水平距离 ≈5.4 格 = 6.5 速度 × 0.83s 滞空，留余量）。
    //   ⑤ 层与层之间「隔开两个格子」：骨架层间距 = LayerStepRows = 3 行（中间空 2 行），
    //     装饰块不得挡住骨架跳跃走廊（MAP-004），尽可能少出现挡道的方块。
    //   ⑥ 上下净空（MAP-005）：方块与方块之间要「隔开两个格子」，即行差 ≥ 3
    //     （中间空 2 行）。行差 1~2 且水平列差 ≤ BlockVerticalClearCols 视为贴太近/挡道。
    //     至少 BlockVerticalClearRatio 的方块必须满足；生成期以 BreakChance 概率保留贴太近的块。
    //   ⑦ 尽量少出现单个方块（MAP-006）：BlockRunMin = 2，生成后清理大部分孤立单块，
    //     孤立单块占比控制在 ≤ BlockSingleRatioMax。
    //   ⑧ 完整登顶路径（MAP-007）：保证存在从地面 row 0 完整跳到最高有效层（row ≥ BlockTopReachMinRow）的可达通路。
    //   ⑨ 多条登顶路径（MAP-008）：每次生成 BackbonePathMin~BackbonePathMax 条独立蛇形骨架，
    //     各占一条列带；至少 1 条贯通到顶层，平时 2~3 条全部可达。
    //   ⑩ 粘液块（MAP-009）：极小概率出现在 run 里的实心方块。站上去水平减速、按跳只弹起
    //     极小高度随后被立刻黏回地面（"跳起来被黏住"）。全图数量硬上限 BlockSlimeMaxCount，
    //     保证"出现概率非常小"可验证；另有「每图保底 1 块」（EnsureSlimePresence），
    //     保证任何种子都能看到新方块、也能跑运行时行为断言，二者不冲突：1/200+ 块仍属极小概率。
    public const int BlockCols = 56;
    public const int BlockRows = 27;
    public const int LayerStepRows = 3;    // 骨架主路径层间距（行数）：3 = 中间空 2 行，与 MAP-005 一致
    public const int BlockRunMin = 2;      // run 长度最小为 2（原则⑦：尽量少出现单个方块）
    public const int BlockRunMax = 5;
    public const int BlockGapMin = 3;      // 同行 run 之间的空档（加大 → 装饰更稀，少挡道）
    public const int BlockGapMax = 4;      // ≤ 满跳水平跨距（蔚蓝手感 ≈5.3 格，原则④）
    public const int BlockHeadroomRows = 2;         // 可站立块上方净空行数（原则②）
    public const int BlockBounceHeadroomRows = 7;   // 弹跳块上方净空行数（弹射 ≈5 格 + 身高 1.2）
    // MAP-005：上下隔开两个格子的限制（原则⑥）
    public const int BlockVerticalClearRows = 2;         // 视为「未隔开」的最大行差（行差 ≥3 = 中间空 2 行）
    public const int BlockVerticalClearCols = 2;         // 视为「同一条上下通道」的水平列半径
    public const float BlockVerticalClearRatio = 0.80f;  // 达标线：至少 80% 方块满足上下净空
    public const float BlockVerticalClearBreakChance = 0.20f; // 贴太近的装饰块仍有 20% 概率保留（随机破例）
    // MAP-006：尽量少出现单个方块（原则⑦）
    public const float BlockSingleKeepChance = 0.15f;    // 生成/修剪后残留的孤立单块仅 15% 概率保留
    public const float BlockSingleRatioMax = 0.05f;      // 达标线：孤立单块占总块数 ≤ 5%
    // MAP-007：完整登顶路径（原则⑧）
    public const int BlockTopReachMinRow = 24;           // 从地面起跳可达的最高层下限（层间距 3 时顶层骨架在 row 24）
    // MAP-008：多条登顶路径（原则⑨）
    public const int BackbonePathMin = 2;                // 每次生成的独立骨架通道数下限
    public const int BackbonePathMax = 3;                // 上限（至少 1 条贯通，平时 2~3 条全部可达）
    // 特殊块权重（每个 run 掷一次，整段纯平台概率 = 1 - 总和 ≈0.83，原则③）
    public const float BlockWeightFragile = 0.07f;   // 脆弱木板：踩碎
    public const float BlockWeightBounce = 0.06f;    // 弹跳块：弹射 ≈5 格高
    public const float BlockWeightBuildable = 0.02f; // 可建筑块：占位
    public const float BlockWeightTreasure = 0.02f;  // 宝箱块：占位宝物
    public const float BlockWeightSlime = 0.01f;     // MAP-009 粘液块：每 run 仅 1% 命中（整段至多一块，全图另有限量）
    // FRAGILE-BLINK：脆弱木板踩上后「闪烁 1.5 秒再碎」的视觉参数。
    // 踩中只启动闪烁：碰撞 / 实体格 / 计数全部不动，玩家闪烁期间仍可站立；
    // 闪烁按 Cycles 次 木色↔白色 交替，结束后回调才判碎、摘实体格、释放节点。
    public const float FragileBlinkDuration = 1.5f;  // 闪烁总时长（秒），也是玩家可站立的宽限
    public const int FragileBlinkCycles = 3;         // 完整闪烁次数（6 次变色 / 1.5 秒，单次 0.25s）
    public const float BlockOneWayChance = 0.30f;    // 每个 run 含「单向连排段」的概率（段长分布见 OneWayLen* 权重）
    // 单向平台连排长度分布（同一 run 内横排连续格数）：2~3 常见，1/4 少见，5 极少见，>5 不出现（run 长度上限 5）
    public const float OneWayLen1Weight = 0.10f;
    public const float OneWayLen2Weight = 0.40f;
    public const float OneWayLen3Weight = 0.35f;
    public const float OneWayLen4Weight = 0.12f;
    public const float OneWayLen5Weight = 0.03f;
    public const float BounceSpeed = 22.0f;          // 弹跳块弹射初速（新重力下 ≈4.8 格高，保持旧弹射高度）
    // BOUNCE-JELLY 蹦蹦床视觉参数：只作用于方块 Visual 子节点，碰撞形状 / 弹射速度 / 地图几何都不碰。
    public const float BounceSquashTime = 0.08f;     // ① 下压：踩沉块面
    public const float BounceReleaseTime = 0.10f;   // ② 回弹：纵向过冲拉长
    public const float BounceSettleTime = 0.36f;    // ③ 余震：Elastic 衰减颤悠回原位
    public const float BounceSquashX = 1.45f;       // 下压时横向鼓起倍率
    public const float BounceSquashY = 0.42f;       // 下压时纵向压扁倍率
    public const float BounceStretchX = 0.88f;      // 回弹时横向收窄倍率
    public const float BounceStretchY = 1.22f;      // 回弹时纵向拉伸倍率
    public const float BounceDipY = 0.18f;          // 下压时 Visual 下沉（世界单位）
    public const float BounceLiftY = 0.10f;         // 回弹时 Visual 上冲（世界单位）
    public const float BounceRingStart = 0.35f;     // 冲击环初始缩放
    public const float BounceRingEnd = 1.60f;       // 冲击环扩散终点
    public const float BounceRingTime = 0.32f;      // 冲击环扩散 + 淡出时长


    // ---------------------------------------------------------------- 主角移动手感
    // 目标 = 蔚蓝（Celeste）式响应：起停跟手、跳跃弧线干脆、零顶点浮空。
    // 核心 = 加速度曲线（地面/空中独立加减速 + 转向加成）替代旧的「空中控制系数」；
    // 顶点滞空默认关闭（1.0 = 无浮空），白箱保留滑条可做对照。
    public const float MasterMoveSpeed = 8.0f;      // 顶速（格/秒）
    public const float Gravity = 50.0f;             // 上升重力：起跳干脆（满跳 ≈3.4 格）
    public const float MasterJumpSpeed = 18.4f;     // 满跳高度 = 18.4²/(2×50) ≈ 3.4 ≥ 层高差 3.0+0.3 抖动
    public const float MasterGroundAccel = 90.0f;   // 地面加速：0→满速 ≈0.09s（蔚蓝式起手）
    public const float MasterGroundDecel = 110.0f;  // 地面刹车：满速→0 ≈0.07s（松键即停）
    public const float MasterAirAccel = 60.0f;      // 空中加速：0→满速 ≈0.13s（跟手但保留一点惯性）
    public const float MasterAirDecel = 55.0f;      // 空中减速基准（实际 × EndTraction）
    public const float MasterTurnAccelMul = 2.0f;   // 反向输入时加速 ×2（急转身）
    public const float MasterEndTraction = 0.12f;   // 空中松开方向键的减速倍率：保留跳跃动量
    public const float MasterCoyoteTime = 0.10f;    // 土狼时间（离台沿后仍可起跳的宽限）
    public const float MasterJumpBuffer = 0.12f;    // 跳跃缓冲（落地前预按自动起跳）
    // 蔚蓝式跳跃细节
    public const float MasterJumpCutMul = 0.42f;    // 变高跳：上升松键截断系数（短按 ≈0.6 格矮跳）
    public const float MasterFallGravityMul = 1.6f; // 下落比上升重 1.6 倍 → 干脆的下坠弧线
    public const float MasterApexGravityMul = 1.0f; // 顶点滞空：默认关闭（1.0 = 无浮空）；白箱可调回 <1 对照
    public const float MasterApexThreshold = 1.1f;  // 顶点区速度阈值（单位/秒）
    public const float MasterMaxFallSpeed = 24.0f;  // 终端下落速度封顶
    // 类银式下穿单向平台：站上台沿按住「下」+「跳」穿层落下（HK / Ori / Dead Cells 通例）
    public const float MasterDropThroughTime = 0.18f; // 下穿窗口（秒）：窗口内下落 ≈1.7，穿过本层（≥0.9）且够不到最小层高差的下一层（2.6）
    // 终焉之莉莉式「自动上攀（台沿翻越）」：撞到上层单向平台边缘附近时，几何探测台沿并自动拉上台面。
    // 采用几何台沿探测（非射线/碰撞探测）：上层平台为单向碰撞、侧面不挡人，靠矩形命中更稳。
    // 总开关（探测手感待重调，暂时关闭；代码与参数全部保留，改回 true 即恢复）。
    public static readonly bool MasterAutoClimbEnabled = false;
    public const float MasterClimbReachUp = 0.6f;     // 台顶可高于脚底的最大距离（单位）：够得到就拉上去
    public const float MasterClimbReachDown = 0.35f;  // 台顶可低于脚底的最大距离（单位）
    public const float MasterClimbEdgeBand = 0.55f;   // 平台边缘外探测带宽（单位）：玩家中心在边缘 ± 此值内触发
    public const float MasterClimbStep = 0.35f;       // 翻上后向台内推进的距离（单位），防止立即掉下边缘
    public const float MasterClimbCooldown = 0.25f;   // 两次翻越的最小间隔（秒），防抖

    public static readonly Vector2 MasterSize = new(0.8f, 1.2f);
    // 验算：满跳高度 = 18.4²/(2×50) ≈ 3.39；上升 0.37s + 下落(×1.6) 0.29s ≈ 0.66s 滞空
    //        短按高度 ≈ 3.39 × 0.42² ≈ 0.6（矮跳）
    //        ✓ 满跳 ≈3.39 ≥ 最大层高差 ≈3.3（基准 3.0 ± 0.3）→ 一次满跳刚好够上相邻层
    //        ✓ 2 格高差落点横移 ≈4.8 格 ≥ 地图 dr2 可达半径 3；3 格高差 ≈3.9 格 ≥ dr3 半径 2
    //        ✓ 满跳水平跨距 ≈ 8.0 × 0.66 ≈ 5.3 格 ≥ 地图同行空档上限 4

    // 蔚蓝式转角修正：上升撞到方块角时自动横移滑过，而不是撞头掉下来。
    // 只在身体小部分探入方块边缘时触发（Celeste ≈ 半格以内），绝不长距离瞬移。
    public const float MasterCornerCorrectionMax = 0.45f;  // 允许修正的最大交叠深度（单位）
    public const float MasterCornerCorrectionSkin = 0.08f; // 修正后与方块的保留间隙

    // ---------------------------------------------------------------- MAP-009 粘液块手感
    // 「走上去会减速」= 水平顶速 × SlimeSpeedMul；「无法跳跃」= 按跳只给 SlimeHopSpeed 的极小上跳，
    // 上升段用 SlimeHopGravity 弹起一点点，一旦到顶点就用 SlimeSnapGravity 立刻压回地面
    // （视觉上像刚起跳就被黏住拽回去）。全部走 MoveTuning，白箱与游戏共用同一份内核。
    public const string SlimeBlockGroup = "slime_block";  // 粘液块碰撞体组别：移动内核靠地板碰撞体识别脚下块
    public const int BlockSlimeMaxCount = 3;             // 全图粘液块数量上限（"出现概率非常小"的可验证口径）
    public const float SlimeSpeedMul = 0.35f;             // 粘液上水平顶速倍率（8 → 2.8 格/秒）
    public const float SlimeAccelMul = 0.40f;             // 粘液上加/减速倍率（起步黏滞、松键也更滑）
    public const float SlimeHopSpeed = 6.0f;              // 粘液上的起跳初速（正常 18.4，只弹起 ≈0.25 格）
    public const float SlimeHopGravity = 60.0f;          // 粘液起跳上升段重力（略大于正常 50，压低最高点）
    public const float SlimeSnapGravity = 900.0f;        // 顶点向下的黏回重力（正常 50 的 18 倍），瞬间压回
    public const float SlimeSnapMaxFall = 16.0f;         // 黏回下压速度上限，防止过冲穿块
    public const float SlimeSnapTime = 0.20f;            // 黏回状态最长持续（秒），兜底防卡在空中
    // 验算：起跳帧 vy=-6 当帧位移 -0.1；随后半隐式积分 dt=1/60 → -5,-4,-3,-2,-1,0，
    //       累计最高离地 = 0.1 + (5+4+3+2+1)/60 = 0.35 格；下一帧 g=900 立刻翻成 +15 拍回台面。
    //       运行时因物理帧 delta 抖动实测约 0.37~0.38 格，故上限留到 0.45（仍 < 1 格 = 连一格都上不去）。
    public const float SlimeHopApexMax = 0.45f;          // 粘液上允许的最大离地高度（冒烟 / 探针断言上限）

    // ---------------------------------------------------------------- 相机 / 背景
    public const float CameraViewHalfHeight = 6.5f;
    public const float CameraSmooth = 6.0f;
    public const string BackdropArt = "bg_dusk";
    public static readonly Color BackdropTint = new(1.00f, 0.95f, 0.84f);

    // ---------------------------------------------------------------- v0.0005 成长 / 存档（第 3 块）
    // 魂魄 = 怪物死亡掉落物（用户定义：「金币换了个名字」）。本块无战斗，
    // 存档预置一笔初始魂魄供开发期加点；真实掉落在第五块接入。
    public const int InitialSouls = 300;
    // 随从局外成长：本命各自继承存档的初始数值，只在局外界面升级；局内玩法留后续块。
    public const int GrowthBaseLevel = 1;
    public const int GrowthBaseHp = 100;
    public const int GrowthBaseAttack = 10;
    public const int GrowthBaseAgility = 5;
    public const int GrowthStepLevel = 1;     // 初始等级每次 +1
    public const int GrowthStepHp = 10;       // 初始血量每次 +10
    public const int GrowthStepAttack = 2;    // 初始攻击每次 +2
    public const int GrowthStepAgility = 2;   // 初始敏捷每次 +2
    public const int GrowthMaxStep = 20;      // 单项最多加 20 级（防无限点）
    public const int GrowthStepCost = 50;     // 每加一级消耗魂魄
}
