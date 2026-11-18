using System;
using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 方块类型（方块地图）。
/// Normal = 实心，最多；Fragile = 踩后闪烁再碎（闪烁期间仍可站立，本局不恢复）；Bounce = 触碰弹射 ≈5 格高；
/// Buildable = 占位（行为后续版本实现）；Treasure = 触碰打开，掉落占位宝物；
/// Slime = 粘液（MAP-009，出现概率非常小）：站上去减速 + 跳起被立刻黏回地面。
/// </summary>
public enum BlockType
{
    Normal,
    Fragile,
    Bounce,
    Buildable,
    Treasure,
    Slime,
}

/// <summary>
/// 方块几何查询接口：供移动内核做转角修正等几何探测。
/// IsBlocked = 该格有存活且实体（非单向）的方块；单向块上升撞头不成立，不计入。
/// </summary>
public interface IBlockGeometry
{
    bool WorldToCell(Vector2 worldPos, out int row, out int col);
    bool IsBlocked(int row, int col);
}

/// <summary>
/// 可站立网格接口（IBlockGeometry 的扩展）：随从防卡死 BFS 寻路用。
/// 节点 = 一块「上方有净空、可以站人」的方块，边 = 平地走 / 跳上 / 下落。
/// </summary>
public interface IStandableGrid : IBlockGeometry
{
    /// <summary>该格是否是可站立方块（有实体方块，且上方 BlockHeadroomRows 格净空）。</summary>
    bool IsStandableCell(int row, int col);
    /// <summary>站在该方块上时角色中心的近似世界坐标（寻路路径点）。</summary>
    Vector2 StandPoint(int row, int col);
}

/// <summary>
/// 方块弹跳接口：Master / WhiteboxPlayer 实现。BlockField 触碰感应置标志，
/// 角色在 Step 之后应用弹射速度（先应用会被贴地逻辑当帧把 Y 清零）。
/// </summary>
public interface IBounceable
{
    void Bounce();
}

/// <summary>
/// 方块地图：1×1 世界单位方块网格（48 列 × 27 行），取代旧的分段平台。
/// 列 = 世界 x（-24..+24）；row 0 = 地面层（GND-001：全覆盖实体普通块，永不单向）；
/// row 1..2 恒空（每个可站立块上方净空 BlockHeadroomRows = 2 格，地面块自动保留）；
/// row 3..26 = 自由随机区：run 撒点（长度 1~5 / 空档 2~4）+ 构造式可达性保证
/// （每个放下的块都有从地面起的跳跃通路：下方 1~3 行且水平距离随行差收紧 dr1≤4 / dr2≤3 / dr3≤2 列；
/// 同行 ±4 列内有已放块可借道；两遍 左→右 / 右→左）。
/// 结构 = 蛇形主路径骨架 + 随机装饰：先铺一条从地面到顶部的确定性攀爬路线（每隔 3 行
/// = 中间空 2 行，一段 3~4 格纯平台，水平步幅 ≤2 列严格在 dr3 跳跃可达内），装饰 run 在骨架之外随机撒。
/// 大多数 run 是纯跳跃平台：整段至多一块特殊块（PickType 权重，约 83% run 无特殊块）。
/// 弹跳块上方净空 BlockBounceHeadroomRows = 7 格，弹射航道不被上层块遮挡。
/// MAP-005 上下净空：任意两块行差 1~2 且列差 ≤ BlockVerticalClearCols 视为贴太近（没隔开 2 格、
/// 会挡道）。骨架层间距改为 3 行（中间空 2 行）；装饰块生成期按 (1 - BreakChance) 概率拒绝
/// 贴太近的格子，20% 破例保留随机性；Build 末尾统计全局合规率，不足 BlockVerticalClearRatio
/// 时修剪违规装饰块补齐（骨架 / 地面受保护）。冒烟断言合规率 ≥ BlockVerticalClearRatio。
/// 单向平台按 run 成段出现（BlockOneWayChance 为 run 命中率），段长连排分布见 GameDefs.OneWayLen*：
/// 2~3 格连排常见，1/4 少见，5 极少见，>5 不出现（run 长度上限 5）。保留 DROP-001 下穿与 CLIMB-001 台沿上攀。
/// 方块行为：Fragile = 顶面感应 → 闪烁 1.5 秒后碎（期间仍可站立）；Bounce = 整块感应 → IBounceable.Bounce()；
/// Treasure = 整块感应 → 打开（移除碰撞，掉落占位宝物）；Buildable = 实心占位。
/// MAP-009 粘液块：不靠触碰感应，而是把碰撞体加入 SlimeBlockGroup；PlatformMover 每帧读
/// 地板碰撞体（与 DROP-001 下穿同一处判定），站上去就减速、起跳只弹极小高度后立刻黏回。
/// 生成权重 BlockWeightSlime 极小，全图 SlimeCount 硬上限 BlockSlimeMaxCount。
/// </summary>
[GlobalClass]
public partial class BlockField : Node2D, IStandableGrid
{
    private readonly BlockType[,] _type = new BlockType[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _oneWay = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _alive = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _blinking = new bool[GameDefs.BlockRows, GameDefs.BlockCols]; // 闪烁中：仍实体，防重复启动
    private readonly bool[,] _reachable = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _reserved = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _backboneCell = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly Node2D?[,] _nodes = new Node2D[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly Sprite2D?[,] _visuals = new Sprite2D[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _jellyBusy = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly RandomNumberGenerator _rng = new();
    private readonly List<(int Row, int C0, int C1)> _backbone = new();
    private readonly List<List<(int Row, int C0, int C1)>> _paths = new();
    private static readonly StringName SlimeGroup = new(GameDefs.SlimeBlockGroup);
    private int _slimeBudget;   // 生成期已落地的粘液块数（硬上限，保证"出现概率非常小"）
    // MAP-TYPE：当前地形配置。默认走 GameDefs 常量逐字等值，故默认地形行为与接线前完全一致。
    private MapGenConfig _cfg = MapGenConfig.CreateDefault();

    // ---------------------------------------------------------------- 场景预制体（编辑器可拖拽换贴图）
    [Export] public PackedScene? SceneSolid;
    [Export] public PackedScene? SceneOneWay;
    [Export] public PackedScene? SceneFragile;
    [Export] public PackedScene? SceneBounce;
    [Export] public PackedScene? SceneBuildable;
    [Export] public PackedScene? SceneTreasure;
    [Export] public PackedScene? SceneSlime;
    [Export] public PackedScene? SceneLootDrop;

    /// <summary>未在编辑器里拖场景时按路径加载（白箱在代码里 new BlockField() 也能用）。</summary>
    private void EnsureScenes()
    {
        SceneSolid ??= GD.Load<PackedScene>("res://scenes/blocks/block_solid.tscn");
        SceneOneWay ??= GD.Load<PackedScene>("res://scenes/blocks/block_oneway.tscn");
        SceneFragile ??= GD.Load<PackedScene>("res://scenes/blocks/block_fragile.tscn");
        SceneBounce ??= GD.Load<PackedScene>("res://scenes/blocks/block_bounce.tscn");
        SceneBuildable ??= GD.Load<PackedScene>("res://scenes/blocks/block_buildable.tscn");
        SceneTreasure ??= GD.Load<PackedScene>("res://scenes/blocks/block_treasure.tscn");
        SceneSlime ??= GD.Load<PackedScene>("res://scenes/blocks/block_slime.tscn");
        SceneLootDrop ??= GD.Load<PackedScene>("res://scenes/fx/loot_drop.tscn");
    }

    public int BlockCount { get; private set; }
    /// <summary>MAP-005：Build 后统计的上下净空合规块数 / 总块数 / 合规率。</summary>
    public int VerticalClearCount { get; private set; }
    public int VerticalClearTotal { get; private set; }
    public float VerticalClearRatio { get; private set; }
    /// <summary>单向平台数量（冒烟下穿验证需要至少一块，纯验证用）。</summary>
    public int OneWayCount { get; private set; }
    /// <summary>MAP-005 兜底修剪掉的装饰块数（达标即停）。</summary>
    public int VerticalClearPruned { get; private set; }
    /// <summary>MAP-006：最终地图中的孤立单块数量与占比。</summary>
    public int SingleBlockCount { get; private set; }
    public float SingleBlockRatio { get; private set; }
    /// <summary>MAP-007：从地面层 BFS 跳跃可达的最高行号，以及是否具备完整登顶路径。</summary>
    public int MaxReachableRow { get; private set; }
    public bool HasPathToTop { get; private set; }
    /// <summary>MAP-008：生成的独立骨架通道数 / 顶端可达的通道数。</summary>
    public int PathCount { get; private set; }
    public int ReachablePathCount { get; private set; }
    /// <summary>MAP-009：最终地图里的粘液块数量（0 ~ BlockSlimeMaxCount，冒烟/探针读取）。</summary>
    public int SlimeCount { get; private set; }
    /// <summary>MAP-009：实例化后确实挂上 SlimeBlockGroup 的碰撞体数（== SlimeCount 才说明移动内核能识别）。</summary>
    public int SlimeGroupedCount { get; private set; }
    /// <summary>BUGFIX-FRAGILE：最终地图中的脆弱木板数量（生成后统计，冒烟/探针读取）。</summary>
    public int FragileCount { get; private set; }
    /// <summary>BUGFIX-FRAGILE：成功接上 Touch.BodyEntered 的脆弱块数（== FragileCount 才说明感应链路接线完整）。</summary>
    public int FragileTouchWiredCount { get; private set; }
    /// <summary>BUGFIX-FRAGILE：本局已碎的脆弱块数（闪烁结束时 FinishFragileBreak 递增，Build 复位）。</summary>
    public int FragileBrokenCount { get; private set; }
    /// <summary>FRAGILE-BLINK：已启动闪烁的碎块数（踩碎当帧 +1）。</summary>
    public int FragileBlinkStartedCount { get; private set; }
    /// <summary>FRAGILE-BLINK：闪烁结束、视觉节点已释放的碎块数。Started &gt; Finished 即「还没立刻消失」。</summary>
    public int FragileBlinkFinishedCount { get; private set; }
    /// <summary>BOUNCE-JELLY：蹦蹦床视觉动画实际触发次数（Jelly 递增，Build 复位）。</summary>
    public int BounceJellyStartedCount { get; private set; }

    // ---------------------------------------------------------------- 坐标
    // 块 (r,c)：x ∈ [ColX(c), ColX(c)+1]，y ∈ [RowTopY(r)-1, RowTopY(r)]（y-down）
    private static float ColX(int c) => -GameDefs.WorldHalfWidth + c;
    private static float RowTopY(int r) => GameDefs.GroundY - r;
    private Vector2 BlockCenter(int r, int c) => new(ColX(c) + 0.5f, RowTopY(r) - 0.5f);

    // ---------------------------------------------------------------- 生成
    /// <summary>按种子 + 默认配置重建地图（旧入口，供探针与既有调用方零改动使用）。</summary>
    public void Build(int seedValue) => Build(seedValue, MapGenConfig.CreateDefault());

    /// <summary>
    /// MAP-TYPE：按种子 + 指定地形配置重建整张方块地图，可重复调用（旧节点延迟释放）。
    /// 算法与数值逐字沿用原实现，仅把 28 个行为参数来源从 GameDefs 常量改为 config。
    /// </summary>
    public void Build(int seedValue, MapGenConfig config)
    {
        _cfg = config ?? MapGenConfig.CreateDefault();
        EnsureScenes();
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        Array.Clear(_type);
        Array.Clear(_oneWay);
        Array.Clear(_alive);
        Array.Clear(_blinking);
        Array.Clear(_reachable);
        Array.Clear(_reserved);   // 可重复 Build：净空标记也必须清，否则换种子后地图越来越稀
        Array.Clear(_backboneCell);
        Array.Clear(_nodes);
        Array.Clear(_visuals);
        Array.Clear(_jellyBusy);
        _backbone.Clear();
        _paths.Clear();
        _rng.Seed = Rng.DeriveSeed64(seedValue, "blocks");
        BlockCount = 0;
        VerticalClearCount = 0;
        VerticalClearTotal = 0;
        VerticalClearRatio = 1f;
        VerticalClearPruned = 0;
        SingleBlockCount = 0;
        SingleBlockRatio = 0f;
        MaxReachableRow = 0;
        HasPathToTop = false;
        PathCount = 0;
        ReachablePathCount = 0;
        SlimeCount = 0;
        SlimeGroupedCount = 0;
        _slimeBudget = 0;
        FragileCount = 0;
        FragileTouchWiredCount = 0;
        FragileBrokenCount = 0;
        FragileBlinkStartedCount = 0;
        FragileBlinkFinishedCount = 0;
        BounceJellyStartedCount = 0;

        // GND-001：row 0 全覆盖实体普通块。刻意不走随机分支。
        for (int c = 0; c < GameDefs.BlockCols; c++)
            SetCell(0, c, BlockType.Normal, false);

        BuildBackbone();

        // 随机区从 row BlockHeadroomRows+1 开始（row 1..2 被地面块净空保留，原则①）：
        // run 撒点 + 两遍可达性（允许同行横向借道）
        for (int r = _cfg.BlockHeadroomRows + 1; r < GameDefs.BlockRows; r++)
        {
            var runs = new List<(int Start, int Len)>();
            int x = _rng.RandiRange(0, 6);
            while (x < GameDefs.BlockCols)
            {
                int len = _rng.RandiRange(_cfg.BlockRunMin, _cfg.BlockRunMax);
                runs.Add((x, len));
                x += len + _rng.RandiRange(_cfg.BlockGapMin, _cfg.BlockGapMax);
            }
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < runs.Count; i++)
                {
                    int idx = pass == 0 ? i : runs.Count - 1 - i;
                    PlaceRun(r, runs[idx].Start, runs[idx].Len);
                }
        }

        EnforceVerticalClearance();   // 统计 MAP-005 合规率，不达标则修剪装饰块补齐
        PruneIsolatedSingles();       // MAP-006：尽量少出现孤立单个方块（移除后只会让上下净空更宽松）
        MeasureVerticalClearance();   // 重新同步最终方块统计与单块率
        EnsureFullPathToTop();        // MAP-007：真实验证从地面到顶层的跳跃可达性
        EnsureSlimePresence();        // MAP-009：保底 1 块粘液（只改类型，不动任何几何/净空/可达统计）
        CountSlimeBlocks();           // 最终统计（修剪/补齐/保底之后），供冒烟与探针读取

        for (int r = 0; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c]) SpawnNode(r, c);

        GD.Print($"[BLOCKS] blocks={BlockCount} ground_continuous={IsGroundContinuous()} " +
                 $"vertical_clear={VerticalClearCount}/{VerticalClearTotal} ({VerticalClearRatio:P1}) " +
                 $"singles={SingleBlockCount}/{VerticalClearTotal} ({SingleBlockRatio:P1}) " +
                 $"top_row={MaxReachableRow} path_to_top={HasPathToTop} " +
                 $"paths={ReachablePathCount}/{PathCount} " +
                 $"pruned={VerticalClearPruned} oneway={OneWayCount} " +
                 $"slime={SlimeCount} slime_grouped={SlimeGroupedCount} " +
                 $"fragile={FragileCount} fragile_wired={FragileTouchWiredCount}");

    }

    // ---------------------------------------------------------------- 节点
    /// <summary>实例化方块预制体场景：碰撞层 / 单向属性 / 贴图都由场景自带，代码只管摆位置和连触摸信号。</summary>
    private void SpawnNode(int r, int c)
    {
        var t = _type[r, c];
        bool oneWay = _oneWay[r, c];
        PackedScene? scene = t switch
        {
            BlockType.Normal => oneWay ? SceneOneWay : SceneSolid,
            BlockType.Fragile => SceneFragile,
            BlockType.Bounce => SceneBounce,
            BlockType.Buildable => SceneBuildable,
            BlockType.Treasure => SceneTreasure,
            BlockType.Slime => SceneSlime,
            _ => SceneSolid,
        };
        if (scene == null) return;
        var body = scene.Instantiate<StaticBody2D>();
        body.Name = $"{t}_{c}_{r}";
        body.Position = BlockCenter(r, c);
        // BUGFIX-FRAGILE：SpawnNode 只在存活格上调用，这里统计最贴近最终地图。
        if (t == BlockType.Fragile) FragileCount++;
        // MAP-009：粘液块靠「地板碰撞体所在组」被移动内核识别，不挂触碰信号。
        // 计数与 SlimeCount 一一对应；不等说明分组没挂上，移动内核会认不出脚下是粘液。
        if (t == BlockType.Slime)
        {
            body.AddToGroup(SlimeGroup);
            SlimeGroupedCount++;
        }
        var visual = body.GetNodeOrNull<Sprite2D>("Visual");
        if (visual != null)
        {
            // 零 PNG：预制体只带碰撞与配色，纹理用运行时 2×2 白图 + modulate 上色。
            // 2 px 纹理铺满 1×1 世界单位 → scale = 0.5（果冻动画以本 scale 为基准）。
            visual.Texture = ArtBank.PlainTexture();
            visual.Scale = Vector2.One * 0.5f;
            _visuals[r, c] = visual;
        }
        if (t is BlockType.Fragile or BlockType.Bounce or BlockType.Treasure)
        {
            var touch = body.GetNodeOrNull<Area2D>("Touch");
            if (touch != null)
            {
                touch.BodyEntered += b => OnTouched(r, c, b);
                // BUGFIX-FRAGILE：接线数单独可观测，把「没接上信号」与「感应区碰不到」区分开。
                if (t == BlockType.Fragile) FragileTouchWiredCount++;
            }
        }
        AddChild(body);
        _nodes[r, c] = body;
    }

    // ---------------------------------------------------------------- 查询
    /// <summary>
    /// CLIMB-001：返回所有「可攀」台沿（仅单向块，实体块不计入）。
    /// 每个条目是该块的台面水平区间与可行走顶面 y（块上缘 = RowTopY(r)-1），
    /// 供 PlatformMover 几何台沿探测使用。
    /// 单向块不会被踩碎，列表整局稳定，Build 后取一次即可。
    /// </summary>
    public List<PlatformMover.Ledge> GetLedges()
    {
        var outList = new List<PlatformMover.Ledge>();
        for (int r = 0; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _oneWay[r, c])
                    outList.Add(new PlatformMover.Ledge(ColX(c), ColX(c) + 1f, RowTopY(r) - 1f));
        return outList;
    }

    /// <summary>GND-001 运行时不变量：row 0 恰好全覆盖、全部普通块、非单向。</summary>
    public bool IsGroundContinuous()
    {
        for (int c = 0; c < GameDefs.BlockCols; c++)
            if (!_alive[0, c] || _type[0, c] != BlockType.Normal || _oneWay[0, c])
                return false;
        return true;
    }

    // ---------------------------------------------------------------- 几何查询（IBlockGeometry）
    /// <summary>世界坐标 → 方块格（row/col），出界返回 false。</summary>
    public bool WorldToCell(Vector2 worldPos, out int row, out int col)
    {
        int c = (int)MathF.Floor(worldPos.X - ColX(0));
        float yFromGround = GameDefs.GroundY - worldPos.Y;   // y-down：高于地面的距离
        int r = (int)MathF.Floor(yFromGround);
        row = r;
        col = c;
        return r >= 0 && r < GameDefs.BlockRows && c >= 0 && c < GameDefs.BlockCols;
    }

    /// <summary>该格是否有存活且实体（非单向）的方块（转角修正只把实体块当障碍）。</summary>
    public bool IsBlocked(int row, int col)
    {
        return _alive[row, col] && !_oneWay[row, col];
    }

    /// <summary>该格是否是可站立方块（有实体方块 + 上方净空）—— 随从 BFS 寻路用。</summary>
    public bool IsStandableCell(int row, int col) => CanStandOn(row, col);

    /// <summary>站在该方块上时角色中心的近似世界坐标（随从 BFS 路径点）。</summary>
    public Vector2 StandPoint(int row, int col) => new(ColX(col) + 0.5f, RowTopY(row) - 0.5f);

}
