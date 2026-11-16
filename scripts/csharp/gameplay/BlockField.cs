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

    /// <summary>
    /// MAP-008 多条蛇形主路径骨架：每次生成 BackbonePathMin~BackbonePathMax 条，
    /// 每条独占一条列带（BlockCols 均分），带内左右交替推进、撞带边界折返，互不交叉。
    /// 每条都是「每隔 3 行（中间空 2 行 = MAP-005）一段 3~4 格纯平台」；
    /// 层间距 dr=3 对应水平可达 ≤2 列，故步幅 1~2 列，平台长度重叠保证相邻段一定跳得过去。
    /// </summary>
    private void BuildBackbone()
    {
        int pathCount = _rng.RandiRange(_cfg.BackbonePathMin, _cfg.BackbonePathMax);
        int band = GameDefs.BlockCols / pathCount;
        for (int p = 0; p < pathCount; p++)
        {
            int bandLeft = p * band;
            int bandRight = p == pathCount - 1 ? GameDefs.BlockCols - 1 : (p + 1) * band - 1;
            var segments = new List<(int Row, int C0, int C1)>();

            int r = _cfg.BlockHeadroomRows + 1;
            bool right = _rng.Randf() < 0.5f;
            int c = right
                ? _rng.RandiRange(bandLeft + 1, bandLeft + 4)
                : _rng.RandiRange(Mathf.Max(bandLeft + 1, bandRight - 7),
                                  Mathf.Max(bandLeft + 1, bandRight - 4));
            while (r < GameDefs.BlockRows)
            {
                int len = _rng.RandiRange(3, 4);
                c = Mathf.Clamp(c, bandLeft, Mathf.Max(bandLeft, bandRight - len + 1));
                PlaceRun(r, c, len, purePlatform: true);
                for (int k = 0; k < len && c + k < GameDefs.BlockCols; k++)
                    if (_alive[r, c + k]) _backboneCell[r, c + k] = true;   // 兜底修剪时不删主路径骨架
                _backbone.Add((r, c, c + len - 1));
                segments.Add((r, c, c + len - 1));

                r += _cfg.LayerStepRows;   // 原则⑤：层间空 2 行
                if (r >= GameDefs.BlockRows) break;
                int step = _rng.RandiRange(1, 2);   // dr=3 的水平可达半径 ≤2 列
                int next = c + (right ? step : -step);
                if (next < bandLeft + 1 || next + len - 1 > bandRight - 1)
                {
                    right = !right;
                    next = c + (right ? step : -step);
                }
                c = Mathf.Clamp(next, bandLeft + 1, Mathf.Max(bandLeft + 1, bandRight - len));
            }
            if (segments.Count > 0) _paths.Add(segments);
        }
        PathCount = _paths.Count;
    }

    private void PlaceRun(int r, int start, int len, bool purePlatform = false)
    {
        if (start >= GameDefs.BlockCols || !RunReachable(r, start, len)) return;
        // 原则⑤：装饰 run 不得挡住骨架跳跃走廊（MAP-004）；骨架段本身豁免。
        if (!purePlatform)
        {
            int blocked = 0;
            for (int k = 0; k < len; k++)
                if (InJumpCorridor(r, start + k)) blocked++;
            if (blocked > len / 2) return;   // 过半落在走廊 → 整段放弃
        }
        // 原则③：大多数 run 是纯跳跃平台——整段至多一块特殊块（约 83% 的 run 掷不到）
        var special = purePlatform ? BlockType.Normal : PickType();
        // 单向连排段：按 run 掷一次，段长 2~3 常见 / 1 或 4 少见 / 5 极少见 / >5 不出现
        int oneWayLen = 0, oneWayStart = 0;
        if (_rng.Randf() < _cfg.BlockOneWayChance)
        {
            int want = SampleOneWayLength();
            if (want <= len)
            {
                for (int attempt = 0; attempt < 4 && oneWayLen == 0; attempt++)
                {
                    int s = _rng.RandiRange(0, len - want);
                    bool fits = true;
                    for (int k = 0; k < want; k++)
                    {
                        int cc = start + s + k;
                        if (cc >= GameDefs.BlockCols || _reserved[r, cc] || _alive[r, cc] || InSpawnZone(r, cc))
                        {
                            fits = false;
                            break;
                        }
                    }
                    if (fits)
                    {
                        oneWayLen = want;
                        oneWayStart = s;
                    }
                }
            }
        }
        int specialAt = -1;
        if (special != BlockType.Normal)
        {
            var candidates = new List<int>();
            for (int k = 0; k < len; k++)
                if (k < oneWayStart || k >= oneWayStart + oneWayLen) candidates.Add(k);
            if (candidates.Count > 0)
                specialAt = candidates[_rng.RandiRange(0, candidates.Count - 1)];
        }
        for (int k = 0; k < len; k++)
        {
            int c = start + k;
            if (c >= GameDefs.BlockCols) break;
            if (_reserved[r, c]) continue;     // 下方块的净空区不放块（原则②）
            if (InSpawnZone(r, c)) continue;   // 出生净空区不放块
            // MAP-005：上下没隔开 2 格的装饰块按概率拒绝，避免挡道；骨架段豁免（必要通路）。
            if (!purePlatform && HasNearVerticalNeighbor(r, c)
                && _rng.Randf() >= _cfg.BlockVerticalClearBreakChance) continue;
            var t = k == specialAt ? special : BlockType.Normal;
            bool oneWay = t == BlockType.Normal && k >= oneWayStart && k < oneWayStart + oneWayLen;
            SetCell(r, c, t, oneWay);
        }
    }

    /// <summary>单向连排段长度抽样：2~3 常见，1/4 少见，5 极少见（权重见 GameDefs.OneWayLen*）。</summary>
    private int SampleOneWayLength()
    {
        float roll = _rng.Randf();
        float acc = _cfg.OneWayLen1Weight;
        if (roll < acc) return 1;
        acc += _cfg.OneWayLen2Weight;
        if (roll < acc) return 2;
        acc += _cfg.OneWayLen3Weight;
        if (roll < acc) return 3;
        acc += _cfg.OneWayLen4Weight;
        if (roll < acc) return 4;
        return 5;   // 剩余概率 = OneWayLen5Weight
    }

    /// <summary>
    /// 出生净空区：row 1..3、col 22..27（世界 x -2..+4，出生点 x=0 一带）。
    /// row 1..2 已由地面净空恒空；row 3 再保留出生点正上方的几列，
    /// 避免在出生点起跳时头顶撞上首层平台底面。
    /// </summary>
    private static bool InSpawnZone(int r, int c) => r >= 1 && r <= 3 && c >= 22 && c <= 27;

    /// <summary>
    /// 原则⑤（MAP-004）：骨架跳跃走廊。玩家从下层骨架平台跳到上层时经过的格子：
    /// 每段骨架平台上方 1~2 行（层间距 3 → 中间 2 行；落点行 dr=3 不算走廊）、
    /// 水平方向平台两端各外扩 3 列（保守值，dr=3 的 CellReachable 半径是 2）。
    /// 装饰块落入走廊会挡跳跃路线，直接拒绝。
    /// </summary>
    private bool InJumpCorridor(int r, int c)
    {
        foreach (var (row, c0, c1) in _backbone)
        {
            int dr = row - r;
            if (dr < 1 || dr >= _cfg.LayerStepRows) continue;   // 只看层间 1..2 行（config 变量，故用普通比较）
            int reach = 3;   // 保守外扩（CellReachable 的 dr=3 半径是 2）
            if (c >= c0 - reach && c <= c1 + reach) return true;
        }
        return false;
    }

    /// <summary>run 内任一块有可达来源，则整段可放（run 内行走保证全段可达）。</summary>
    private bool RunReachable(int r, int start, int len)
    {
        for (int k = 0; k < len; k++)
        {
            int c = start + k;
            if (c < 0 || c >= GameDefs.BlockCols || _alive[r, c]) continue;
            if (CellReachable(r, c)) return true;
        }
        return false;
    }

    /// <summary>
    /// 从已放块起跳可达：下方 1~3 行（满跳 3.67 ≥ 3 格，水平距离随行差收紧：
    /// dr1≤4 列 / dr2≤3 列 / dr3≤2 列），或同行 ±4 列内有已放块（行走 / 平跳）。
    /// 高层跌落是自由的（无需校验），所以构造方向恒为自下而上。
    /// </summary>
    private bool CellReachable(int r, int c)
    {
        for (int dr = 1; dr <= 3; dr++)
        {
            int sr = r - dr;
            if (sr < 0) break;
            int reach = dr switch { 1 => 4, 2 => 3, _ => 2 };
            for (int dc = -reach; dc <= reach; dc++)
            {
                int sc = c + dc;
                if (sc < 0 || sc >= GameDefs.BlockCols) continue;
                if (_alive[sr, sc] && _reachable[sr, sc]) return true;
            }
        }
        const int SameRowReach = 4;
        for (int dc = -SameRowReach; dc <= SameRowReach; dc++)
        {
            if (dc == 0) continue;
            int sc = c + dc;
            if (sc < 0 || sc >= GameDefs.BlockCols) continue;
            if (_alive[r, sc] && _reachable[r, sc]) return true;
        }
        return false;
    }

    /// <summary>特殊块类型按权重掷（普通 = 其余）。每个 run 掷一次；数值见 GameDefs。
    /// MAP-009：粘液块权重极小，且整图数量受 BlockSlimeMaxCount 硬上限约束。</summary>
    private BlockType PickType()
    {
        float roll = _rng.Randf();
        float acc = _cfg.BlockWeightFragile;
        if (roll < acc) return BlockType.Fragile;
        acc += _cfg.BlockWeightBounce;
        if (roll < acc) return BlockType.Bounce;
        acc += _cfg.BlockWeightBuildable;
        if (roll < acc) return BlockType.Buildable;
        acc += _cfg.BlockWeightTreasure;
        if (roll < acc) return BlockType.Treasure;
        acc += _cfg.BlockWeightSlime;
        if (roll < acc && _slimeBudget < _cfg.BlockSlimeMaxCount) return BlockType.Slime;
        return BlockType.Normal;
    }

    private void SetCell(int r, int c, BlockType t, bool oneWay)
    {
        if (_alive[r, c]) return;
        _type[r, c] = t;
        _oneWay[r, c] = oneWay;
        _alive[r, c] = true;
        _reachable[r, c] = true;
        ReserveHeadroom(r, c, t);
        BlockCount++;
        if (t == BlockType.Slime) _slimeBudget++;
    }

    /// <summary>原则②：可站立块上方保留净空（普通 2 格 / 弹跳块 7 格），生成自下而上，后续行不得占用。</summary>
    private void ReserveHeadroom(int r, int c, BlockType t)
    {
        int rows = t == BlockType.Bounce ? _cfg.BlockBounceHeadroomRows : _cfg.BlockHeadroomRows;
        for (int i = 1; i <= rows && r + i < GameDefs.BlockRows; i++)
            _reserved[r + i, c] = true;
    }

    /// <summary>
    /// MAP-005：该格上下 BlockVerticalClearRows 行内、水平 ±BlockVerticalClearCols 列内
    /// 是否已有存活方块。行差 1~2 = 中间没空出 2 格 → 贴太近、会挡道；行差 ≥3 才合规。
    /// 生成期只看已放块（自下而上），Build 末尾统计时看整张图（双向对称）。
    /// </summary>
    private bool HasNearVerticalNeighbor(int r, int c)
    {
        for (int dr = -_cfg.BlockVerticalClearRows; dr <= _cfg.BlockVerticalClearRows; dr++)
        {
            if (dr == 0) continue;   // 同行横排不算上下相邻
            int rr = r + dr;
            if (rr < 0 || rr >= GameDefs.BlockRows) continue;
            int c0 = Math.Max(0, c - _cfg.BlockVerticalClearCols);
            int c1 = Math.Min(GameDefs.BlockCols - 1, c + _cfg.BlockVerticalClearCols);
            for (int cc = c0; cc <= c1; cc++)
                if (_alive[rr, cc]) return true;
        }
        return false;
    }

    /// <summary>判断 (r,c) 是否为孤立单个方块（同行左右两侧均无相邻存活方块）。</summary>
    private bool IsIsolatedSingle(int r, int c)
    {
        if (!_alive[r, c]) return false;
        bool left = c > 0 && _alive[r, c - 1];
        bool right = c + 1 < GameDefs.BlockCols && _alive[r, c + 1];
        return !left && !right;
    }

    /// <summary>
    /// MAP-009：最终统计粘液块数量（在修剪 / 骨架补齐 / 保底之后调用）。
    /// </summary>
    private void CountSlimeBlocks()
    {
        int n = 0;
        for (int r = 0; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _type[r, c] == BlockType.Slime) n++;
        SlimeCount = n;
    }

    /// <summary>
    /// MAP-009 保底：出现概率非常小，但每局至少留 1 块，避免整图看不到新方块、也无法验证。
    /// 只把一块「已存在的普通装饰块」改类型，不改 _alive / _oneWay / _reachable，
    /// 因此 MAP-005 净空、MAP-006 单块率、MAP-007/008 可达路径的统计全部不受影响。
    /// 严格避开：地面 row 0（GND-001 硬约束）、单向平台、骨架主路径（避免把唯一通路变成黏住跳不上去）。
    /// 用 _rng 挑，保证同种子可复现。
    /// </summary>
    private void EnsureSlimePresence()
    {
        CountSlimeBlocks();
        if (SlimeCount > 0) return;
        var candidates = new List<(int R, int C)>();
        for (int r = _cfg.BlockHeadroomRows + 1; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _type[r, c] == BlockType.Normal
                    && !_oneWay[r, c] && !_backboneCell[r, c])
                    candidates.Add((r, c));
        if (candidates.Count == 0) return;
        var (rr, cc) = candidates[_rng.RandiRange(0, candidates.Count - 1)];
        _type[rr, cc] = BlockType.Slime;
        _slimeBudget++;
        GD.Print($"[BLOCKS] slime fallback placed at ({cc},{rr})");
    }

    /// <summary>MAP-005 / MAP-006：统计最终地图里「上下隔开 2 格」与「孤立单块」的方块占比，供日志与冒烟断言使用。</summary>
    private void MeasureVerticalClearance()
    {
        int total = 0, clear = 0, oneWay = 0, singles = 0;
        for (int r = 0; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
            {
                if (!_alive[r, c]) continue;
                total++;
                if (_oneWay[r, c]) oneWay++;
                if (!HasNearVerticalNeighbor(r, c)) clear++;
                if (IsIsolatedSingle(r, c)) singles++;
            }
        VerticalClearTotal = total;
        VerticalClearCount = clear;
        VerticalClearRatio = total == 0 ? 1f : (float)clear / total;
        OneWayCount = oneWay;
        SingleBlockCount = singles;
        SingleBlockRatio = total == 0 ? 0f : (float)singles / total;
    }

    /// <summary>
    /// MAP-006：尽量少出现单个方块。生成/修剪后残留的孤立单块（非骨架）仅以极低概率保留，
    /// 若整体单块率仍高于 BlockSingleRatioMax 则继续清理，确保地图以连排平台为主。
    /// </summary>
    private void PruneIsolatedSingles()
    {
        for (int r = 1; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
            {
                if (!_alive[r, c] || _backboneCell[r, c]) continue;
                if (IsIsolatedSingle(r, c) && _rng.Randf() >= _cfg.BlockSingleKeepChance)
                {
                    _alive[r, c] = false;
                    _reachable[r, c] = false;
                    BlockCount--;
                }
            }
        MeasureVerticalClearance();
        if (SingleBlockRatio <= _cfg.BlockSingleRatioMax) return;
        for (int r = GameDefs.BlockRows - 1; r >= 1; r--)
            for (int c = 0; c < GameDefs.BlockCols; c++)
            {
                if (SingleBlockRatio <= _cfg.BlockSingleRatioMax) return;
                if (!_alive[r, c] || _backboneCell[r, c] || !IsIsolatedSingle(r, c)) continue;
                _alive[r, c] = false;
                _reachable[r, c] = false;
                BlockCount--;
                MeasureVerticalClearance();
            }
    }

    /// <summary>
    /// MAP-007：真实验证并兜底「从最下面 row 0 跳到最上面」的完整路径。
    /// 规则：要求方块上方至少 2 格非实心净空（玩家可站立），按跳跃包络（dr 1..3 对应水平 4/3/2、
    /// 同行水平 ≤4、下落 dr -1..-6 对应水平 ≤4）从 row 0 做 BFS，刷新 _reachable 并验证可达最高层。
    /// </summary>
    private void EnsureFullPathToTop()
    {
        ComputeTrueReachability();
        // 至少 1 条贯通是硬保证；平时要求全部 BackbonePathMin~Max 条都可达
        if (HasPathToTop && ReachablePathCount >= _cfg.BackbonePathMin) return;

        // 若因出生区豁免或极端修剪导致断路，把全部骨架段补齐（连排 3~4 格，绝不产生单块）
        foreach (var (row, c0, c1) in _backbone)
        {
            for (int c = c0; c <= c1; c++)
            {
                if (!_alive[row, c])
                {
                    SetCell(row, c, BlockType.Normal, false);
                    _backboneCell[row, c] = true;
                }
                // 确保骨架块上方 2 格净空不被实心装饰块封死
                for (int hr = 1; hr <= _cfg.BlockHeadroomRows && row + hr < GameDefs.BlockRows; hr++)
                    if (_alive[row + hr, c] && !_backboneCell[row + hr, c] && !_oneWay[row + hr, c])
                    {
                        _alive[row + hr, c] = false;
                        BlockCount--;
                    }
            }
        }
        MeasureVerticalClearance();
        ComputeTrueReachability();
    }

    private bool CanStandOn(int r, int c)
    {
        if (r < 0 || r >= GameDefs.BlockRows || c < 0 || c >= GameDefs.BlockCols) return false;
        if (!_alive[r, c]) return false;
        for (int hr = 1; hr <= _cfg.BlockHeadroomRows && r + hr < GameDefs.BlockRows; hr++)
            if (_alive[r + hr, c] && !_oneWay[r + hr, c]) return false;
        return true;
    }

    private void ComputeTrueReachability()
    {
        Array.Clear(_reachable);
        var q = new Queue<(int R, int C)>();
        MaxReachableRow = 0;
        for (int c = 0; c < GameDefs.BlockCols; c++)
            if (CanStandOn(0, c))
            {
                _reachable[0, c] = true;
                q.Enqueue((0, c));
            }

        while (q.Count > 0)
        {
            var (r, c) = q.Dequeue();
            if (r > MaxReachableRow) MaxReachableRow = r;

            // 1. 向上跳跃 dr = 1..3
            for (int dr = 1; dr <= 3; dr++)
            {
                int nr = r + dr;
                if (nr >= GameDefs.BlockRows) break;
                int reach = dr switch { 1 => 4, 2 => 3, _ => 2 };
                for (int dc = -reach; dc <= reach; dc++)
                {
                    int nc = c + dc;
                    if (nc < 0 || nc >= GameDefs.BlockCols || _reachable[nr, nc]) continue;
                    if (CanStandOn(nr, nc))
                    {
                        _reachable[nr, nc] = true;
                        q.Enqueue((nr, nc));
                    }
                }
            }
            // 2. 同行行走 / 平跳 dc = -4..+4
            for (int dc = -4; dc <= 4; dc++)
            {
                if (dc == 0) continue;
                int nc = c + dc;
                if (nc < 0 || nc >= GameDefs.BlockCols || _reachable[r, nc]) continue;
                if (CanStandOn(r, nc))
                {
                    _reachable[r, nc] = true;
                    q.Enqueue((r, nc));
                }
            }
            // 3. 向下跳落 dr = 1..6
            for (int dr = 1; dr <= 6; dr++)
            {
                int nr = r - dr;
                if (nr < 0) break;
                for (int dc = -4; dc <= 4; dc++)
                {
                    int nc = c + dc;
                    if (nc < 0 || nc >= GameDefs.BlockCols || _reachable[nr, nc]) continue;
                    if (CanStandOn(nr, nc))
                    {
                        _reachable[nr, nc] = true;
                        q.Enqueue((nr, nc));
                    }
                }
            }
        }
        // MAP-008：每条独立骨架通道只要顶端段有可达块，即算一条贯通的登顶路径
        ReachablePathCount = 0;
        foreach (var segments in _paths)
        {
            if (segments.Count == 0) continue;
            var top = segments[^1];
            for (int c = top.C0; c <= top.C1 && c < GameDefs.BlockCols; c++)
            {
                if (!_reachable[top.Row, c]) continue;
                ReachablePathCount++;
                break;
            }
        }
        HasPathToTop = MaxReachableRow >= _cfg.BlockTopReachMinRow && ReachablePathCount >= 1;
    }

    /// <summary>
    /// MAP-005 达标兜底：合规率低于 BlockVerticalClearRatio 时，从高层往低层移除
    /// 「违规且非骨架」的装饰块；每移除一块重新统计（它周围的方块也会跟着恢复合规），达标即停。
    /// 地面层与蛇形骨架受保护，主路径可达性不受影响；20% 随机破例只在预算内保留。
    /// </summary>
    private void EnforceVerticalClearance()
    {
        MeasureVerticalClearance();
        if (VerticalClearRatio >= _cfg.BlockVerticalClearRatio) return;

        var candidates = new List<(int R, int C)>();
        for (int r = GameDefs.BlockRows - 1; r > 0; r--)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && !_backboneCell[r, c] && HasNearVerticalNeighbor(r, c))
                    candidates.Add((r, c));

        foreach (var (r, c) in candidates)
        {
            if (VerticalClearRatio >= _cfg.BlockVerticalClearRatio) break;
            if (!_alive[r, c] || !HasNearVerticalNeighbor(r, c)) continue;
            _alive[r, c] = false;
            _reachable[r, c] = false;
            BlockCount--;
            VerticalClearPruned++;
            MeasureVerticalClearance();
        }
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

    /// <summary>配色：实心普通 = 绿 / 单向普通 = 紫（沿用旧平台配色），其余块一眼可辨。白箱图例共用。</summary>
    public static Color Tint(BlockType t, bool oneWay = false) => t switch
    {
        BlockType.Normal => oneWay ? new Color(0.42f, 0.40f, 0.52f) : new Color(0.30f, 0.36f, 0.30f),
        BlockType.Fragile => new Color(0.72f, 0.55f, 0.33f),
        BlockType.Bounce => new Color(0.95f, 0.55f, 0.35f),
        BlockType.Buildable => new Color(0.55f, 0.62f, 0.72f),
        BlockType.Treasure => new Color(0.95f, 0.80f, 0.35f),
        BlockType.Slime => new Color(0.36f, 0.78f, 0.42f),   // 黏液：荧光青绿，一眼可辨
        _ => Colors.White,
    };

    /// <summary>
    /// 触碰感应链路（BUGFIX-FRAGILE）：感应区只由方块预制体自带，SpawnNode 取 "Touch" 接线。
    /// 曾经这里还有一份代码动态创建感应区的死实现，位置完全埋在方块内部（踩不到），已删除，
    /// 避免「两份几何、其中一份是错的」再次误导维护者。
    /// </summary>
    private void OnTouched(int r, int c, Node body)
    {
        if (!_alive[r, c]) return;

        // 「角色」= 玩家 / 随从 / 未来怪物。弹跳块对三者都有效；
        // 脆弱木板与宝箱仍只给玩家（随从踩碎木板会让 AI 掉坑，宝箱属于玩家收益）。
        bool isMaster = body.IsInGroup(GameDefs.MasterGroup);
        bool isActor = isMaster
                       || body.IsInGroup(GameDefs.SpiritGroup)
                       || body.IsInGroup(GameDefs.EnemyGroup);

        switch (_type[r, c])
        {
            case BlockType.Fragile:
                if (!isMaster) break;
                StartFragileBlink(r, c);
                break;
            case BlockType.Bounce:
                if (!isActor) break;
                Jelly(r, c);
                (body as IBounceable)?.Bounce();
                break;
            case BlockType.Treasure:
                if (!isMaster) break;
                OpenTreasure(r, c);
                break;
        }
    }

    /// <summary>
    /// BUGFIX-FRAGILE：Area2D.BodyEntered 属于物理回调，里面同步 RemoveChild 一个 CollisionObject
    /// 会触发 Godot 的 "Removing a CollisionObject node during a physics callback is not allowed"。
    /// QueueFree 是帧末延迟释放（自动从父节点摘除），这里只置空引用，不做同步移除。
    /// </summary>
    private static void DeferredRemove(Node2D? n)
    {
        if (n != null && IsInstanceValid(n)) n.QueueFree();
    }

    /// <summary>
    /// FRAGILE-BLINK：踩中只启动闪烁 —— 碰撞、感应、_alive 全部不动，玩家仍可站在上面；
    /// _blinking 标记防止闪烁期间重复触碰重启动画。真正的判碎交给闪烁结束回调。
    /// </summary>
    private void StartFragileBlink(int r, int c)
    {
        if (!_alive[r, c] || _blinking[r, c]) return;
        _blinking[r, c] = true;
        FragileBlinkStartedCount++;
        BlinkThenFree(r, c, _nodes[r, c], _visuals[r, c]);
        GD.Print($"[BLOCKS] fragile blink start ({c},{r}) duration={GameDefs.FragileBlinkDuration}s");
    }

    /// <summary>
    /// 闪烁结束才判碎：此时才把格子从实体/可达集合摘掉、统计 +1、释放节点。
    /// 由 Tween 在 idle 帧回调（不是物理回调），QueueFree 仍是帧末延迟释放。
    /// </summary>
    private void FinishFragileBreak(int r, int c, Node2D? body)
    {
        if (!_blinking[r, c]) return;
        _blinking[r, c] = false;
        if (!_alive[r, c]) return;
        _alive[r, c] = false;
        _reachable[r, c] = false;
        _nodes[r, c] = null;
        _visuals[r, c] = null;
        BlockCount--;
        FragileBrokenCount++;
        FragileBlinkFinishedCount++;
        DeferredRemove(body);
        GD.Print($"[BLOCKS] fragile broken ({c},{r}) after blink");
    }

    /// <summary>
    /// FRAGILE-BLINK：绑定在 Sprite2D 上的一次性 Tween，做 2 次「木色 ↔ 白色」闪烁
    /// 只改颜色、绝不动 scale，满 1.5 秒后回调 FinishFragileBreak 才真正碎掉。
    /// Tween 宿主是 visual：换种子 Build 释放整张地图时 tween 自动失效，
    /// 回调不会打到已释放节点；回调内部再做 _blinking 状态校验。
    /// </summary>
    private void BlinkThenFree(int r, int c, Node2D? body, Sprite2D? visual)
    {
        if (body == null || !IsInstanceValid(body) || visual == null || !IsInstanceValid(visual))
        {
            FinishFragileBreak(r, c, body);
            return;
        }

        Color baseTint = Tint(BlockType.Fragile);
        Color flashTint = Colors.White;
        float half = GameDefs.FragileBlinkDuration / (GameDefs.FragileBlinkCycles * 2f);
        var tw = visual.CreateTween();
        for (int i = 0; i < GameDefs.FragileBlinkCycles; i++)
        {
            // 只改颜色，绝不动 scale —— 闪完必须原尺寸消失，不要放大/回弹。
            tw.TweenProperty(visual, "modulate", flashTint, half)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            tw.TweenProperty(visual, "modulate", baseTint, half)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        }
        tw.TweenCallback(Callable.From(() => FinishFragileBreak(r, c, body)));
    }

    /// <summary>
    /// BOUNCE-JELLY 蹦蹦床动画（三段式，只动 Visual 子节点）：
    /// ① 踩沉压扁 + 亮黄高亮 → ② 纵向过冲拉长、块面上抬 → ③ Elastic 余震颤回原样。
    /// 碰撞形状、弹射速度、地图几何一概不动；动画期间重复触碰不重播，弹射不受影响。
    /// </summary>
    private void Jelly(int r, int c)
    {
        var visual = _visuals[r, c];
        if (visual == null || _jellyBusy[r, c]) return;
        _jellyBusy[r, c] = true;
        BounceJellyStartedCount++;
        GD.Print($"[BLOCKS] bounce jelly start ({c},{r})");

        Vector2 baseScale = visual.Scale;
        Vector2 basePos = visual.Position;
        Color baseTint = Tint(BlockType.Bounce);
        Color flashTint = new(1.0f, 0.95f, 0.55f, 1.0f);
        // Tween 宿主是 visual：换种子 Build 释放节点时动画自动停止，不会打到已释放节点。
        var tw = visual.CreateTween();

        // ① 下压：网面被踩沉，横向鼓起、纵向压扁，同时闪一下亮黄
        tw.TweenProperty(visual, "scale",
                new Vector2(baseScale.X * GameDefs.BounceSquashX, baseScale.Y * GameDefs.BounceSquashY),
                GameDefs.BounceSquashTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(visual, "position:y", basePos.Y + GameDefs.BounceDipY,
                GameDefs.BounceSquashTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(visual, "modulate", flashTint, GameDefs.BounceSquashTime);

        // ② 回弹：纵向拉长过冲、块面上抬、高亮回落
        tw.TweenProperty(visual, "scale",
                new Vector2(baseScale.X * GameDefs.BounceStretchX, baseScale.Y * GameDefs.BounceStretchY),
                GameDefs.BounceReleaseTime)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(visual, "position:y", basePos.Y - GameDefs.BounceLiftY,
                GameDefs.BounceReleaseTime)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(visual, "modulate", baseTint, GameDefs.BounceReleaseTime);

        // ③ 余震：Elastic 自带数次衰减抖动，像蹦床网面颤悠着停住
        tw.TweenProperty(visual, "scale", baseScale, GameDefs.BounceSettleTime)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(visual, "position:y", basePos.Y, GameDefs.BounceSettleTime)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        tw.TweenCallback(Callable.From(() => _jellyBusy[r, c] = false));

        SpawnBounceRing(r, c);
    }

    /// <summary>
    /// BOUNCE-JELLY 冲击环：Line2D 程序化扁椭圆，从块面扩散并淡出。
    /// 零 PNG / 零新资源 / 零碰撞；Tween 绑定环自身，播完 QueueFree。
    /// </summary>
    private void SpawnBounceRing(int r, int c)
    {
        var body = _nodes[r, c];
        if (body == null || !IsInstanceValid(body)) return;

        const int segments = 24;
        var ring = new Line2D
        {
            Name = "BounceRing",
            Position = new Vector2(0.0f, -0.35f),   // 块顶面上方一点
            ZIndex = 5,
            Width = 0.055f,
            DefaultColor = new Color(1.0f, 0.90f, 0.55f, 0.9f),
            Scale = Vector2.One * GameDefs.BounceRingStart,
        };
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.45f);   // 扁椭圆，像地面冲击波
        }
        ring.Points = points;
        body.AddChild(ring);

        var tw = ring.CreateTween().SetParallel(true);
        tw.TweenProperty(ring, "scale", Vector2.One * GameDefs.BounceRingEnd, GameDefs.BounceRingTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(ring, "modulate:a", 0.0f, GameDefs.BounceRingTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.Chain().TweenCallback(Callable.From(ring.QueueFree));
    }

    /// <summary>宝箱块：触碰打开，移除碰撞，掉落占位宝物（内容后续版本设计）。</summary>
    private void OpenTreasure(int r, int c)
    {
        if (!_alive[r, c]) return;
        _alive[r, c] = false;
        _reachable[r, c] = false;
        var n = _nodes[r, c];
        _nodes[r, c] = null;
        DeferredRemove(n);   // 触碰回调里不能同步移除碰撞体（统一帧末延迟释放）
        BlockCount--;

        if (SceneLootDrop != null)
        {
            var loot = SceneLootDrop.Instantiate<Node2D>();
            loot.Name = $"Loot_{c}_{r}";
            loot.Position = BlockCenter(r, c);
            // 零 PNG：掉落物与方块共用运行时白纹理；0.15 × 2px = 0.3 世界单位。
            var lootVisual = loot.GetNodeOrNull<Sprite2D>("Visual");
            if (lootVisual != null)
            {
                lootVisual.Texture = ArtBank.PlainTexture();
                lootVisual.Scale = Vector2.One * 0.15f;
            }
            AddChild(loot);
        }
        GD.Print($"[LOOT] 宝箱打开 ({c},{r}) —— 占位宝物，内容待设计");
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

    /// <summary>
    /// MAP-009 冒烟测试用：取第一块「可站立」的粘液块（上方 2 格净空、非单向），
    /// 返回中心 x 与可行走台面 y（块上缘），供「站在粘液上按跳 → 立刻被黏回」验证。
    /// 只用于 --smoke 传送，不参与正常游戏流程。
    /// </summary>
    public bool GetFirstSlime(out float centerX, out float topY)
    {
        for (int r = 1; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _type[r, c] == BlockType.Slime
                    && !_oneWay[r, c] && CanStandOn(r, c))
                {
                    centerX = ColX(c) + 0.5f;
                    topY = RowTopY(r) - 1f;
                    return true;
                }
        centerX = 0f;
        topY = 0f;
        return false;
    }

    /// <summary>
    /// BUGFIX-FRAGILE 冒烟测试用：取第一块「能从上方踩到」的脆弱木板，返回中心 x 与台面 y。
    /// 要求正上方一格无任何方块且满足 CanStandOn，保证角色自由落下时不会先踩到别的块。
    /// 只用于 --smoke 传送，不参与正常游戏流程。
    /// </summary>
    public bool GetFirstFragile(out float centerX, out float topY)
    {
        for (int r = 1; r < GameDefs.BlockRows - 1; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _type[r, c] == BlockType.Fragile
                    && !_oneWay[r, c] && !_alive[r + 1, c] && CanStandOn(r, c))
                {
                    centerX = ColX(c) + 0.5f;
                    topY = RowTopY(r) - 1f;
                    return true;
                }
        centerX = 0f;
        topY = 0f;
        return false;
    }

    /// <summary>
    /// BOUNCE-JELLY 冒烟测试用：取第一块「能从上方踩到」的弹跳块，返回中心 x 与台面 y。
    /// 与 GetFirstFragile 同口径：正上方一格空且可站，保证自由落下触发真实感应与弹射。
    /// </summary>
    public bool GetFirstBounce(out float centerX, out float topY)
    {
        for (int r = 1; r < GameDefs.BlockRows - 1; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _type[r, c] == BlockType.Bounce
                    && !_oneWay[r, c] && !_alive[r + 1, c] && CanStandOn(r, c))
                {
                    centerX = ColX(c) + 0.5f;
                    topY = RowTopY(r) - 1f;
                    return true;
                }
        centerX = 0f;
        topY = 0f;
        return false;
    }

    /// <summary>冒烟测试用：取第一块单向块（row ≥1）的中心 x 与可行走台面 y（块上缘），供「下 + 跳」下穿验证。</summary>
    public bool GetFirstOneWay(out float centerX, out float topY)
    {
        for (int r = 1; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _oneWay[r, c])
                {
                    centerX = ColX(c) + 0.5f;
                    topY = RowTopY(r) - 1f;
                    var b = _nodes[r, c] as StaticBody2D;
                    GD.Print($"[BLOCKDBG] first oneway r={r} c={c} type={_type[r, c]} oneWay={_oneWay[r, c]} bodyLayer={(b != null ? b.CollisionLayer : 99u)}");
                    for (int rr = 1; rr <= 2; rr++)
                        for (int cc = Math.Max(0, c - 2); cc <= Math.Min(GameDefs.BlockCols - 1, c + 2); cc++)
                            if (_alive[rr, cc])
                            {
                                var bb = _nodes[rr, cc] as StaticBody2D;
                                GD.Print($"[BLOCKDBG]   ({cc},{rr}) type={_type[rr, cc]} oneWay={_oneWay[rr, cc]} layer={(bb != null ? bb.CollisionLayer : 99u)}");
                            }
                    return true;
                }
        centerX = 0f;
        topY = 0f;
        return false;
    }
}
