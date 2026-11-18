using System;
using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// BlockField 的「地图生成」partial 部分：蛇形骨架主路径 / 装饰 run 撒点与可达性构造 /
/// 类型权重与单向段抽样 / 放置与净空保留。方法体逐字沿用原单文件实现，仅按职责拆出（行为零改动）。
/// 类声明、状态字段、Build 编排与实例化在 BlockField.cs；可达性 BFS 与修剪在 BlockField.Reachability.cs。
/// </summary>
public partial class BlockField
{
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
            int reach = JumpReach(dr);
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
}
