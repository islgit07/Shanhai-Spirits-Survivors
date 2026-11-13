using Godot;

namespace ShanHaiHuanLing;

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


    // ---------------------------------------------------------------- 主角移动手感
    // 目标 = 复刻《终焉之莉莉》：轻盈可控、顶点悬停、短按矮跳、长按照高。
    // 以下数值 = 跳跃白箱（scenes/Whitebox.tscn）按《终焉之莉莉》手感实机调出后写回（terrain_seed 20261112）。
    public const float MasterMoveSpeed = 6.5f;     // 空中更易修正落点
    public const float Gravity = 36.0f;            // 偏重 → 整体更沉稳（原默认 22）
    public const float MasterJumpSpeed = 16.2f;    // 满跳高度 ≈ 3.67 ≥ 最大层高差 3.4 → 一次跳跃刚好够上相邻层（见下注）
    public const float MasterAirControl = 0.6f;    // 空中水平控制（明显低于地面，保留惯性）
    public const float MasterCoyoteTime = 0.11f;   // 土狼时间（离台沿后仍可起跳的宽限）
    public const float MasterJumpBuffer = 0.11f;   // 跳跃缓冲（落地前预按自动起跳）
    // 《终焉之莉莉》式跳跃细节
    public const float MasterJumpCutMul = 0.36f;   // 变高跳：上升松键截断系数（短按≈0.39 单位矮跳）
    public const float MasterFallGravityMul = 2.5f;// 下落比上升重 2.5 倍 → 落得利落
    public const float MasterApexGravityMul = 0.45f;// 顶点滞空：穿过顶点附近重力降到 0.45 倍（保留飘感但更利落，压总滞空）
    public const float MasterApexThreshold = 1.1f; // 顶点区速度阈值（单位/秒）
    public const float MasterMaxFallSpeed = 40.0f; // 终端下落速度封顶
    // 类银式下穿单向平台：站上台沿按住「下」+「跳」穿层落下（HK / Ori / Dead Cells 通例）
    public const float MasterDropThroughTime = 0.20f; // 下穿窗口（秒）：窗口内下落 ≈2.2，穿过本层（≥0.9）且够不到最小层高差的下一层（2.6）

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
