using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 地形生成配置卡片（MAP-TYPE / SEED-001 分域）：把 BlockField 的「行为参数」从 GameDefs 常量
/// 搬到一个可在 Inspector 里用鼠标编辑的 Resource，双击 resources/maps/*.tres 即可改数值。
///
/// 接线范围：只接影响「生成算法行为」的 28 个参数（run 长度、净空率、单块率、路径数、权重……）。
/// 不接：网格维度 BlockRows/BlockCols、世界坐标基准 GroundY/WorldHalfWidth、粘液组名 SlimeBlockGroup
/// ——前四个是 8 个网格数组的编译期维度与坐标基准，动它们等于重构容器（第 2 阶段容器拆分才开放）；
/// 组名是移动内核与地图之间的代码契约，不该被配置改写。
///
/// 默认值逐字等于 GameDefs 常量，所以「默认配置卡片」生成的地形与接入前完全一致。
/// 本 Resource 是 [GlobalClass]，可在编辑器右键 New Resource → MapGenConfig 创建。
/// </summary>
[GlobalClass]
public partial class MapGenConfig : Resource
{
    [ExportGroup("骨架与层间距")]
    [Export] public int LayerStepRows { get; set; } = GameDefs.LayerStepRows;
    [Export] public int BlockHeadroomRows { get; set; } = GameDefs.BlockHeadroomRows;
    [Export] public int BlockBounceHeadroomRows { get; set; } = GameDefs.BlockBounceHeadroomRows;
    [Export] public int BlockTopReachMinRow { get; set; } = GameDefs.BlockTopReachMinRow;
    [Export] public int BackbonePathMin { get; set; } = GameDefs.BackbonePathMin;
    [Export] public int BackbonePathMax { get; set; } = GameDefs.BackbonePathMax;

    [ExportGroup("run 撒点")]
    [Export] public int BlockRunMin { get; set; } = GameDefs.BlockRunMin;
    [Export] public int BlockRunMax { get; set; } = GameDefs.BlockRunMax;
    [Export] public int BlockGapMin { get; set; } = GameDefs.BlockGapMin;
    [Export] public int BlockGapMax { get; set; } = GameDefs.BlockGapMax;
    [Export] public float BlockOneWayChance { get; set; } = GameDefs.BlockOneWayChance;

    [ExportGroup("单向连排长度权重")]
    [Export] public float OneWayLen1Weight { get; set; } = GameDefs.OneWayLen1Weight;
    [Export] public float OneWayLen2Weight { get; set; } = GameDefs.OneWayLen2Weight;
    [Export] public float OneWayLen3Weight { get; set; } = GameDefs.OneWayLen3Weight;
    [Export] public float OneWayLen4Weight { get; set; } = GameDefs.OneWayLen4Weight;
    [Export] public float OneWayLen5Weight { get; set; } = GameDefs.OneWayLen5Weight;

    [ExportGroup("MAP-005 上下净空")]
    [Export] public int BlockVerticalClearRows { get; set; } = GameDefs.BlockVerticalClearRows;
    [Export] public int BlockVerticalClearCols { get; set; } = GameDefs.BlockVerticalClearCols;
    [Export] public float BlockVerticalClearRatio { get; set; } = GameDefs.BlockVerticalClearRatio;
    [Export] public float BlockVerticalClearBreakChance { get; set; } = GameDefs.BlockVerticalClearBreakChance;

    [ExportGroup("MAP-006 少单块")]
    [Export] public float BlockSingleKeepChance { get; set; } = GameDefs.BlockSingleKeepChance;
    [Export] public float BlockSingleRatioMax { get; set; } = GameDefs.BlockSingleRatioMax;

    [ExportGroup("特殊块权重")]
    [Export] public float BlockWeightFragile { get; set; } = GameDefs.BlockWeightFragile;
    [Export] public float BlockWeightBounce { get; set; } = GameDefs.BlockWeightBounce;
    [Export] public float BlockWeightBuildable { get; set; } = GameDefs.BlockWeightBuildable;
    [Export] public float BlockWeightTreasure { get; set; } = GameDefs.BlockWeightTreasure;
    [Export] public float BlockWeightSlime { get; set; } = GameDefs.BlockWeightSlime;
    [Export] public int BlockSlimeMaxCount { get; set; } = GameDefs.BlockSlimeMaxCount;

    /// <summary>纯代码默认配置（字段初始化器取值，逐字等于 GameDefs）。供未加载 .tres 时兜底。</summary>
    public static MapGenConfig CreateDefault() => new();
}
