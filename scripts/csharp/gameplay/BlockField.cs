using System;
using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 方块类型（方块地图）。
/// Normal = 实心，最多；Fragile = 踩碎（本局不恢复）；Bounce = 触碰弹射 ≈5 格高；
/// Buildable = 占位（行为后续版本实现）；Treasure = 触碰打开，掉落占位宝物。
/// </summary>
public enum BlockType
{
    Normal,
    Fragile,
    Bounce,
    Buildable,
    Treasure,
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
/// 方块行为：Fragile = 顶面感应 → 碎；Bounce = 整块感应 → IBounceable.Bounce()；
/// Treasure = 整块感应 → 打开（移除碰撞，掉落占位宝物）；Buildable = 实心占位。
/// </summary>
[GlobalClass]
public partial class BlockField : Node2D, IBlockGeometry
{
    private readonly BlockType[,] _type = new BlockType[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _oneWay = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _alive = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _reachable = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _reserved = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _backboneCell = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly Node2D?[,] _nodes = new Node2D[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly Sprite2D?[,] _visuals = new Sprite2D[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _jellyBusy = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly RandomNumberGenerator _rng = new();
    private readonly List<(int Row, int C0, int C1)> _backbone = new();

    // ---------------------------------------------------------------- 场景预制体（编辑器可拖拽换贴图）
    [Export] public PackedScene? SceneSolid;
    [Export] public PackedScene? SceneOneWay;
    [Export] public PackedScene? SceneFragile;
    [Export] public PackedScene? SceneBounce;
    [Export] public PackedScene? SceneBuildable;
    [Export] public PackedScene? SceneTreasure;
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

    // ---------------------------------------------------------------- 坐标
    // 块 (r,c)：x ∈ [ColX(c), ColX(c)+1]，y ∈ [RowTopY(r)-1, RowTopY(r)]（y-down）
    private static float ColX(int c) => -GameDefs.WorldHalfWidth + c;
    private static float RowTopY(int r) => GameDefs.GroundY - r;
    private Vector2 BlockCenter(int r, int c) => new(ColX(c) + 0.5f, RowTopY(r) - 0.5f);

    // ---------------------------------------------------------------- 生成
    /// <summary>按种子重建整张方块地图，可重复调用（旧节点延迟释放）。</summary>
    public void Build(int seedValue)
    {
        EnsureScenes();
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        Array.Clear(_type);
        Array.Clear(_oneWay);
        Array.Clear(_alive);
        Array.Clear(_reachable);
        Array.Clear(_reserved);   // 可重复 Build：净空标记也必须清，否则换种子后地图越来越稀
        Array.Clear(_backboneCell);
        Array.Clear(_nodes);
        Array.Clear(_visuals);
        Array.Clear(_jellyBusy);
        _backbone.Clear();
        _rng.Seed = Rng.DeriveSeed64(seedValue, "blocks");
        BlockCount = 0;
        VerticalClearCount = 0;
        VerticalClearTotal = 0;
        VerticalClearRatio = 1f;
        VerticalClearPruned = 0;

        // GND-001：row 0 全覆盖实体普通块。刻意不走随机分支。
        for (int c = 0; c < GameDefs.BlockCols; c++)
            SetCell(0, c, BlockType.Normal, false);

        BuildBackbone();

        // 随机区从 row BlockHeadroomRows+1 开始（row 1..2 被地面块净空保留，原则①）：
        // run 撒点 + 两遍可达性（允许同行横向借道）
        for (int r = GameDefs.BlockHeadroomRows + 1; r < GameDefs.BlockRows; r++)
        {
            var runs = new List<(int Start, int Len)>();
            int x = _rng.RandiRange(0, 6);
            while (x < GameDefs.BlockCols)
            {
                int len = _rng.RandiRange(GameDefs.BlockRunMin, GameDefs.BlockRunMax);
                runs.Add((x, len));
                x += len + _rng.RandiRange(GameDefs.BlockGapMin, GameDefs.BlockGapMax);
            }
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < runs.Count; i++)
                {
                    int idx = pass == 0 ? i : runs.Count - 1 - i;
                    PlaceRun(r, runs[idx].Start, runs[idx].Len);
                }
        }

        EnforceVerticalClearance();   // 统计 MAP-005 合规率，不达标则修剪装饰块补齐，再实例化节点

        for (int r = 0; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c]) SpawnNode(r, c);

        GD.Print($"[BLOCKS] blocks={BlockCount} ground_continuous={IsGroundContinuous()} " +
                 $"vertical_clear={VerticalClearCount}/{VerticalClearTotal} ({VerticalClearRatio:P1}) " +
                 $"pruned={VerticalClearPruned} oneway={OneWayCount}");

    }

    /// <summary>
    /// 蛇形主路径骨架（伪随机核心）：每隔 3 行（中间空 2 行 = MAP-005）一段 3~4 格纯平台，左右交替推进。
    /// 层间距 dr=3 对应水平可达 ≤2 列（与 CellReachable 收紧规则一致），故步幅 1~2 列；
    /// 加上平台自身长度重叠，保证相邻两段之间一定跳得过去；撞墙折返。
    /// </summary>
    private void BuildBackbone()
    {
        int r = GameDefs.BlockHeadroomRows + 1;
        bool right = _rng.Randf() < 0.5f;
        int c = right ? _rng.RandiRange(2, 8)
                      : _rng.RandiRange(GameDefs.BlockCols - 9, GameDefs.BlockCols - 3);
        while (r < GameDefs.BlockRows)
        {
            int len = _rng.RandiRange(3, 4);
            c = Mathf.Clamp(c, 0, GameDefs.BlockCols - len);
            PlaceRun(r, c, len, purePlatform: true);
            for (int k = 0; k < len && c + k < GameDefs.BlockCols; k++)
                _backboneCell[r, c + k] = true;   // 兜底修剪时不删主路径骨架
            _backbone.Add((r, c, c + len - 1));
            int dr = GameDefs.LayerStepRows;   // 原则⑤：层间空 2 行
            r += dr;
            if (r >= GameDefs.BlockRows) break;
            int step = _rng.RandiRange(1, 2);   // dr=3 的水平可达半径 ≤2 列
            int next = c + (right ? step : -step);
            if (next < 1 || next + len > GameDefs.BlockCols - 1)
            {
                right = !right;
                next = c + (right ? step : -step);
            }
            c = next;
        }
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
        if (_rng.Randf() < GameDefs.BlockOneWayChance)
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
                && _rng.Randf() >= GameDefs.BlockVerticalClearBreakChance) continue;
            var t = k == specialAt ? special : BlockType.Normal;
            bool oneWay = t == BlockType.Normal && k >= oneWayStart && k < oneWayStart + oneWayLen;
            SetCell(r, c, t, oneWay);
        }
    }

    /// <summary>单向连排段长度抽样：2~3 常见，1/4 少见，5 极少见（权重见 GameDefs.OneWayLen*）。</summary>
    private int SampleOneWayLength()
    {
        float roll = _rng.Randf();
        float acc = GameDefs.OneWayLen1Weight;
        if (roll < acc) return 1;
        acc += GameDefs.OneWayLen2Weight;
        if (roll < acc) return 2;
        acc += GameDefs.OneWayLen3Weight;
        if (roll < acc) return 3;
        acc += GameDefs.OneWayLen4Weight;
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
            if (dr is < 1 or >= GameDefs.LayerStepRows) continue;   // 只看层间 1..2 行
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

    /// <summary>特殊块类型按权重掷（普通 = 其余）。每个 run 掷一次；数值见 GameDefs。</summary>
    private BlockType PickType()
    {
        float roll = _rng.Randf();
        float acc = GameDefs.BlockWeightFragile;
        if (roll < acc) return BlockType.Fragile;
        acc += GameDefs.BlockWeightBounce;
        if (roll < acc) return BlockType.Bounce;
        acc += GameDefs.BlockWeightBuildable;
        if (roll < acc) return BlockType.Buildable;
        acc += GameDefs.BlockWeightTreasure;
        if (roll < acc) return BlockType.Treasure;
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
    }

    /// <summary>原则②：可站立块上方保留净空（普通 2 格 / 弹跳块 7 格），生成自下而上，后续行不得占用。</summary>
    private void ReserveHeadroom(int r, int c, BlockType t)
    {
        int rows = t == BlockType.Bounce ? GameDefs.BlockBounceHeadroomRows : GameDefs.BlockHeadroomRows;
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
        for (int dr = -GameDefs.BlockVerticalClearRows; dr <= GameDefs.BlockVerticalClearRows; dr++)
        {
            if (dr == 0) continue;   // 同行横排不算上下相邻
            int rr = r + dr;
            if (rr < 0 || rr >= GameDefs.BlockRows) continue;
            int c0 = Math.Max(0, c - GameDefs.BlockVerticalClearCols);
            int c1 = Math.Min(GameDefs.BlockCols - 1, c + GameDefs.BlockVerticalClearCols);
            for (int cc = c0; cc <= c1; cc++)
                if (_alive[rr, cc]) return true;
        }
        return false;
    }

    /// <summary>MAP-005：统计最终地图里「上下隔开 2 格」的方块占比，供日志与冒烟断言使用。</summary>
    private void MeasureVerticalClearance()
    {
        int total = 0, clear = 0, oneWay = 0;
        for (int r = 0; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
            {
                if (!_alive[r, c]) continue;
                total++;
                if (_oneWay[r, c]) oneWay++;
                if (!HasNearVerticalNeighbor(r, c)) clear++;
            }
        VerticalClearTotal = total;
        VerticalClearCount = clear;
        VerticalClearRatio = total == 0 ? 1f : (float)clear / total;
        OneWayCount = oneWay;
    }

    /// <summary>
    /// MAP-005 达标兜底：合规率低于 BlockVerticalClearRatio 时，从高层往低层移除
    /// 「违规且非骨架」的装饰块；每移除一块重新统计（它周围的方块也会跟着恢复合规），达标即停。
    /// 地面层与蛇形骨架受保护，主路径可达性不受影响；20% 随机破例只在预算内保留。
    /// </summary>
    private void EnforceVerticalClearance()
    {
        MeasureVerticalClearance();
        if (VerticalClearRatio >= GameDefs.BlockVerticalClearRatio) return;

        var candidates = new List<(int R, int C)>();
        for (int r = GameDefs.BlockRows - 1; r > 0; r--)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && !_backboneCell[r, c] && HasNearVerticalNeighbor(r, c))
                    candidates.Add((r, c));

        foreach (var (r, c) in candidates)
        {
            if (VerticalClearRatio >= GameDefs.BlockVerticalClearRatio) break;
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
            _ => SceneSolid,
        };
        if (scene == null) return;
        var body = scene.Instantiate<StaticBody2D>();
        body.Name = $"{t}_{c}_{r}";
        body.Position = BlockCenter(r, c);
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
                touch.BodyEntered += b => OnTouched(r, c, b);
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
        _ => Colors.White,
    };

    /// <summary>行为感应区：Fragile 只感应顶面 0.4（踩碎），Bounce / Treasure 感应整块。</summary>
    private void AddTouchArea(Node2D body, int r, int c)
    {
        var area = new Area2D();
        bool topOnly = _type[r, c] == BlockType.Fragile;
        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = topOnly ? new Vector2(1f, 0.4f) : new Vector2(1f, 1f) },
            Position = topOnly ? new Vector2(0f, -0.3f) : Vector2.Zero,
        };
        area.AddChild(shape);
        area.BodyEntered += b => OnTouched(r, c, b);
        body.AddChild(area);
    }

    private void OnTouched(int r, int c, Node body)
    {
        if (!_alive[r, c] || !body.IsInGroup("master")) return;
        switch (_type[r, c])
        {
            case BlockType.Fragile:
                BreakBlock(r, c);
                break;
            case BlockType.Bounce:
                Jelly(r, c);
                (body as IBounceable)?.Bounce();
                break;
            case BlockType.Treasure:
                OpenTreasure(r, c);
                break;
        }
    }

    /// <summary>脆弱块：踩碎消失，本局内不恢复（换种子 / 重开新局恢复）。</summary>
    private void BreakBlock(int r, int c)
    {
        if (!_alive[r, c]) return;
        _alive[r, c] = false;
        _reachable[r, c] = false;
        var n = _nodes[r, c];
        _nodes[r, c] = null;
        if (n != null && IsInstanceValid(n)) { RemoveChild(n); n.QueueFree(); }
        BlockCount--;
        GD.Print($"[BLOCKS] fragile broken ({c},{r})");
    }

    /// <summary>
    /// 弹跳块果冻动画：被压扁（横向膨胀 ×1.35 / 纵向压扁 ×0.55）后弹性回弹，
    /// 弹性缓动自带 1~2 次过冲抖动。动画期间重复触碰不重播，弹射不受影响。
    /// </summary>
    private void Jelly(int r, int c)
    {
        var visual = _visuals[r, c];
        if (visual == null || _jellyBusy[r, c]) return;
        _jellyBusy[r, c] = true;
        Vector2 baseScale = visual.Scale;
        var tw = CreateTween();
        tw.TweenProperty(visual, "scale", new Vector2(baseScale.X * 1.35f, baseScale.Y * 0.55f), 0.07f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(visual, "scale", baseScale, 0.5f)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        tw.TweenCallback(Callable.From(() => _jellyBusy[r, c] = false));
    }

    /// <summary>宝箱块：触碰打开，移除碰撞，掉落占位宝物（内容后续版本设计）。</summary>
    private void OpenTreasure(int r, int c)
    {
        if (!_alive[r, c]) return;
        _alive[r, c] = false;
        _reachable[r, c] = false;
        var n = _nodes[r, c];
        _nodes[r, c] = null;
        if (n != null && IsInstanceValid(n)) { RemoveChild(n); n.QueueFree(); }
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
