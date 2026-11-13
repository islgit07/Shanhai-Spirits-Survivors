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
/// 结构 = 蛇形主路径骨架 + 随机装饰：先铺一条从地面到顶部的确定性攀爬路线（每隔 2~3 行
/// 一段 3~4 格纯平台，水平步幅严格在跳跃可达内），装饰 run 只在骨架之外随机撒。
/// 大多数 run 是纯跳跃平台：整段至多一块特殊块（PickType 权重，约 83% run 无特殊块）。
/// 弹跳块上方净空 BlockBounceHeadroomRows = 7 格，弹射航道不被上层块遮挡。
/// 单向是普通块的属性（BlockOneWayChance），保留 DROP-001 下穿与 CLIMB-001 台沿上攀。
/// 方块行为：Fragile = 顶面感应 → 碎；Bounce = 整块感应 → IBounceable.Bounce()；
/// Treasure = 整块感应 → 打开（移除碰撞，掉落占位宝物）；Buildable = 实心占位。
/// </summary>
[GlobalClass]
public partial class BlockField : Node2D
{
    private readonly BlockType[,] _type = new BlockType[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _oneWay = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _alive = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _reachable = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly bool[,] _reserved = new bool[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly Node2D?[,] _nodes = new Node2D[GameDefs.BlockRows, GameDefs.BlockCols];
    private readonly RandomNumberGenerator _rng = new();

    public int BlockCount { get; private set; }

    // ---------------------------------------------------------------- 坐标
    // 块 (r,c)：x ∈ [ColX(c), ColX(c)+1]，y ∈ [RowTopY(r)-1, RowTopY(r)]（y-down）
    private static float ColX(int c) => -GameDefs.WorldHalfWidth + c;
    private static float RowTopY(int r) => GameDefs.GroundY - r;
    private Vector2 BlockCenter(int r, int c) => new(ColX(c) + 0.5f, RowTopY(r) - 0.5f);

    // ---------------------------------------------------------------- 生成
    /// <summary>按种子重建整张方块地图，可重复调用（旧节点延迟释放）。</summary>
    public void Build(int seedValue)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        Array.Clear(_type);
        Array.Clear(_oneWay);
        Array.Clear(_alive);
        Array.Clear(_reachable);
        Array.Clear(_nodes);
        _rng.Seed = Rng.DeriveSeed64(seedValue, "blocks");
        BlockCount = 0;

        // GND-001：row 0 全覆盖实体普通块。刻意不走随机分支。
        for (int c = 0; c < GameDefs.BlockCols; c++)
            SetCell(0, c, BlockType.Normal, false);

        BuildBackbone();

        // 随机区从 row BlockHeadroomRows+1 开始（row 1..2 被地面块净空保留，原则①）：
        // run 撒点 + 两遍可达性（允许同行横向借道）
        for (int r = GameDefs.BlockHeadroomRows + 1; r < GameDefs.BlockRows; r++)
        {
            var runs = new List<(int Start, int Len)>();
            int x = _rng.RandiRange(0, 3);
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

        for (int r = 0; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c]) SpawnNode(r, c);

        GD.Print($"[BLOCKS] blocks={BlockCount} ground_continuous={IsGroundContinuous()}");
    }

    /// <summary>
    /// 蛇形主路径骨架（伪随机核心）：每隔 2~3 行一段 3~4 格纯平台，左右交替推进。
    /// 垂直步幅 dr=2/3 对应水平可达 ≤3/≤2 列（与 CellReachable 收紧规则一致），
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
            int dr = _rng.RandiRange(2, 3);
            r += dr;
            if (r >= GameDefs.BlockRows) break;
            int reach = dr == 2 ? 3 : 2;
            int step = _rng.RandiRange(2, reach + len - 2);
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
        // 原则③：大多数 run 是纯跳跃平台——整段至多一块特殊块（约 83% 的 run 掷不到）
        var special = purePlatform ? BlockType.Normal : PickType();
        int specialAt = special == BlockType.Normal ? -1 : _rng.RandiRange(0, len - 1);
        for (int k = 0; k < len; k++)
        {
            int c = start + k;
            if (c >= GameDefs.BlockCols) break;
            if (_reserved[r, c]) continue;     // 下方块的净空区不放块（原则②）
            if (InSpawnZone(r, c)) continue;   // 出生净空区不放块
            var t = k == specialAt ? special : BlockType.Normal;
            bool oneWay = t == BlockType.Normal && _rng.Randf() < GameDefs.BlockOneWayChance;
            SetCell(r, c, t, oneWay);
        }
    }

    /// <summary>
    /// 出生净空区：row 1..3、col 22..27（世界 x -2..+4，出生点 x=0 一带）。
    /// row 1..2 已由地面净空恒空；row 3 再保留出生点正上方的几列，
    /// 避免在出生点起跳时头顶撞上首层平台底面。
    /// </summary>
    private static bool InSpawnZone(int r, int c) => r >= 1 && r <= 3 && c >= 22 && c <= 27;

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

    // ---------------------------------------------------------------- 节点
    private void SpawnNode(int r, int c)
    {
        var t = _type[r, c];
        bool oneWay = _oneWay[r, c];
        var body = new StaticBody2D
        {
            Name = $"{t}_{c}_{r}",
            Position = BlockCenter(r, c),
            // 碰撞分层：实体 = LayerSolid；单向普通块 = LayerOneWay（DROP-001 可下穿）
            CollisionLayer = (t == BlockType.Normal && oneWay) ? GameDefs.LayerOneWay : GameDefs.LayerSolid,
        };
        body.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(1f, 1f) },
            OneWayCollision = oneWay,
            OneWayCollisionMargin = 0.2f,
        });
        body.AddChild(new ColorRect
        {
            Color = Tint(t, oneWay),
            Size = new Vector2(1f, 1f),
            Position = new Vector2(-0.5f, -0.5f),
        });
        if (t is BlockType.Fragile or BlockType.Bounce or BlockType.Treasure)
            AddTouchArea(body, r, c);
        AddChild(body);
        _nodes[r, c] = body;
    }

    /// <summary>配色：实心普通 = 绿 / 单向普通 = 紫（沿用旧平台配色），其余块一眼可辨。</summary>
    private static Color Tint(BlockType t, bool oneWay) => t switch
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

        var holder = new Node2D
        {
            Name = $"Loot_{c}_{r}",
            Position = BlockCenter(r, c),
        };
        holder.AddChild(new ColorRect
        {
            Color = new Color(1.0f, 0.85f, 0.4f, 0.95f),
            Size = new Vector2(0.3f, 0.3f),
            Position = new Vector2(-0.15f, -0.15f),
        });
        AddChild(holder);
        GD.Print($"[LOOT] 宝箱打开 ({c},{r}) —— 占位宝物，内容待设计");
    }

    // ---------------------------------------------------------------- 查询
    /// <summary>
    /// CLIMB-001：返回所有「可攀」台沿（仅单向块，实体块不计入）。
    /// 每个条目是该块台面的水平区间与顶面 y，供 PlatformMover 几何台沿探测使用。
    /// 单向块不会被踩碎，列表整局稳定，Build 后取一次即可。
    /// </summary>
    public List<PlatformMover.Ledge> GetLedges()
    {
        var outList = new List<PlatformMover.Ledge>();
        for (int r = 0; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _oneWay[r, c])
                    outList.Add(new PlatformMover.Ledge(ColX(c), ColX(c) + 1f, RowTopY(r)));
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

    /// <summary>冒烟测试用：取第一块单向块（row ≥1）的中心 x 与台面 y，供「下 + 跳」下穿验证。</summary>
    public bool GetFirstOneWay(out float centerX, out float topY)
    {
        for (int r = 1; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _oneWay[r, c])
                {
                    centerX = ColX(c) + 0.5f;
                    topY = RowTopY(r);
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
