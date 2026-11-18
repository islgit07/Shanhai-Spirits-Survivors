using System;
using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// BlockField 的「可达性与统计」partial 部分：MAP-005 净空统计与达标修剪、MAP-006 孤立单块清理、
/// MAP-007 真 BFS 登顶验证、MAP-008 多路径计数、MAP-009 粘液保底与最终统计。
/// 方法体逐字沿用原单文件实现，仅按职责拆出（行为零改动）。
/// </summary>
public partial class BlockField
{
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

    /// <summary>跳跃包络的水平半径：dr 1~3 行高差对应水平 4/3/2 列（生成可达与 BFS 共用同一份口径）。</summary>
    private static int JumpReach(int dr) => dr switch { 1 => 4, 2 => 3, _ => 2 };

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
                int reach = JumpReach(dr);
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
}
