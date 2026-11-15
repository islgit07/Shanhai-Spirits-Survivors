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
    // 特殊块权重（每个 run 掷一次，整段纯平台概率 = 1 - 总和 ≈0.83，原则③）
    public const float BlockWeightFragile = 0.07f;   // 脆弱木板：踩碎
    public const float BlockWeightBounce = 0.06f;    // 弹跳块：弹射 ≈5 格高
    public const float BlockWeightBuildable = 0.02f; // 可建筑块：占位
    public const float BlockWeightTreasure = 0.02f;  // 宝箱块：占位宝物
    public const float BlockOneWayChance = 0.30f;    // 每个 run 含「单向连排段」的概率（段长分布见 OneWayLen* 权重）
    // 单向平台连排长度分布（同一 run 内横排连续格数）：2~3 常见，1/4 少见，5 极少见，>5 不出现（run 长度上限 5）
    public const float OneWayLen1Weight = 0.10f;
    public const float OneWayLen2Weight = 0.40f;
    public const float OneWayLen3Weight = 0.35f;
    public const float OneWayLen4Weight = 0.12f;
    public const float OneWayLen5Weight = 0.03f;
    public const float BounceSpeed = 22.0f;          // 弹跳块弹射初速（新重力下 ≈4.8 格高，保持旧弹射高度）


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

    // ---------------------------------------------------------------- 相机 / 背景
    public const float CameraViewHalfHeight = 6.5f;
    public const float CameraSmooth = 6.0f;
    public const string BackdropArt = "bg_dusk";
    public static readonly Color BackdropTint = new(1.00f, 0.95f, 0.84f);
}
