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
    public const float WorldHalfWidth = 24.0f;
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
    public const int BlockCols = 48;
    public const int BlockRows = 27;
    public const int BlockRunMin = 1;      // run 长度（同行连续块数）
    public const int BlockRunMax = 5;
    public const int BlockGapMin = 2;      // 同行 run 之间的空档
    public const int BlockGapMax = 4;      // ≤ 满跳水平距离（原则④）
    public const int BlockHeadroomRows = 2;         // 可站立块上方净空行数（原则②）
    public const int BlockBounceHeadroomRows = 7;   // 弹跳块上方净空行数（弹射 ≈5 格 + 身高 1.2）
    // 特殊块权重（每个 run 掷一次，整段纯平台概率 = 1 - 总和 ≈0.83，原则③）
    public const float BlockWeightFragile = 0.07f;   // 脆弱木板：踩碎
    public const float BlockWeightBounce = 0.06f;    // 弹跳块：弹射 ≈5 格高
    public const float BlockWeightBuildable = 0.02f; // 可建筑块：占位
    public const float BlockWeightTreasure = 0.02f;  // 宝箱块：占位宝物
    public const float BlockOneWayChance = 0.30f;    // 非地面普通块成为单向的概率（保留 DROP-001 下穿）
    public const float BounceSpeed = 19.0f;          // 弹跳块弹射初速（≈5 格高，实机可调）


    // ---------------------------------------------------------------- 主角移动手感
    // 目标 = 复刻《终焉之莉莉》：轻盈可控、顶点悬停、短按矮跳、长按照高。
    // 以下数值 = 跳跃白箱（scenes/Whitebox.tscn）按《终焉之莉莉》手感实机调出后写回（terrain_seed 20261112）。
    public const float MasterMoveSpeed = 6.5f;     // 空中更易修正落点
    public const float Gravity = 36.0f;            // 偏重 → 整体更沉稳（原默认 22）
    public const float MasterJumpSpeed = 16.2f;    // 满跳高度 ≈ 3.67 ≥ 最大层高差 3.4 → 一次跳跃刚好够上相邻层（见下注）
    public const float MasterAirControl = 0.6f;    // 空中水平控制（明显低于地面，保留惯性）
    public const float MasterCoyoteTime = 0.13f;   // 土狼时间（离台沿后仍可起跳的宽限；关闭自动上攀后攀爬全靠真跳，容错调宽）
    public const float MasterJumpBuffer = 0.13f;   // 跳跃缓冲（落地前预按自动起跳；同上调宽）
    // 《终焉之莉莉》式跳跃细节
    public const float MasterJumpCutMul = 0.36f;   // 变高跳：上升松键截断系数（短按≈0.39 单位矮跳）
    public const float MasterFallGravityMul = 2.5f;// 下落比上升重 2.5 倍 → 落得利落
    public const float MasterApexGravityMul = 0.45f;// 顶点滞空：穿过顶点附近重力降到 0.45 倍（保留飘感但更利落，压总滞空）
    public const float MasterApexThreshold = 1.1f; // 顶点区速度阈值（单位/秒）
    public const float MasterMaxFallSpeed = 40.0f; // 终端下落速度封顶
    // 类银式下穿单向平台：站上台沿按住「下」+「跳」穿层落下（HK / Ori / Dead Cells 通例）
    public const float MasterDropThroughTime = 0.20f; // 下穿窗口（秒）：窗口内下落 ≈2.2，穿过本层（≥0.9）且够不到最小层高差的下一层（2.6）
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
    // 验算：满跳高度 = (16.2²-1.1²)/(2×36) + 1.1²/(2×36×0.45) ≈ 3.67，滞空 ≈ 0.83s
    //        短按高度 ≈ 3.67 × 0.36² ≈ 0.48（矮跳）
    //        ✓ 3.67 ≥ 最大层高差 3.4（基准 3.0 ± 层高抖动 0.4）→ 一次满跳刚好够上任意相邻层，跳过头 ≤ 0.3。
    //        ✓ 滞空 0.83s < 旧版 0.92s（跳速 14.6 时代）——更短更利落。

    // ---------------------------------------------------------------- 相机 / 背景
    public const float CameraViewHalfHeight = 6.5f;
    public const float CameraSmooth = 6.0f;
    public const string BackdropArt = "bg_dusk";
    public static readonly Color BackdropTint = new(1.00f, 0.95f, 0.84f);
}
