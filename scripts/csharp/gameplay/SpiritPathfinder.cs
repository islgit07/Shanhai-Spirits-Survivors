using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 随从的「移动能力档案」：BFS 认为它能跳多高、跨多远、落多深。
///
/// 和具体属性解耦——现在没有属性系统，用 <see cref="Default"/>（与主角满跳同量级）；
/// 以后随从的移速 / 敏捷接入养成后，只要调用 <see cref="FromStats"/> 换算一份新档案，
/// BFS 与跟随逻辑一行都不用改：
///   · 移速 → 水平跨度（滞空时间 × 移速）；
///   · 敏捷 → 跳跃初速（进而影响跳高与滞空时间）。
/// </summary>
public readonly struct SpiritNavProfile
{
    /// <summary>能跳上几行（1 行 = 1 格高）。</summary>
    public readonly int MaxRise;
    /// <summary>一次跳跃能横跨几列。</summary>
    public readonly int MaxSpan;
    /// <summary>能安全下落几行。</summary>
    public readonly int MaxDrop;

    public SpiritNavProfile(int maxRise, int maxSpan, int maxDrop)
    {
        MaxRise = maxRise;
        MaxSpan = maxSpan;
        MaxDrop = maxDrop;
    }

    /// <summary>基础档案：与主角满跳同量级（3 格高 / 5 格远）。</summary>
    public static SpiritNavProfile Default =>
        new(GameDefs.SpiritNavMaxRise, GameDefs.SpiritNavMaxSpan, GameDefs.SpiritNavMaxDrop);

    /// <summary>
    /// 由移速 / 敏捷换算移动能力。
    /// moveSpeed：格/秒（随从的实际追赶速度）；agility：1.0 = 基础，越大跳得越高。
    /// 公式与移动内核同口径：跳高 = v²/(2g)，滞空 = 2v/g，水平跨度 = 移速 × 滞空。
    /// </summary>
    public static SpiritNavProfile FromStats(float moveSpeed, float agility)
    {
        float jumpSpeed = GameDefs.SpiritFollowJumpSpeed * Mathf.Clamp(agility, 0.5f, 2.0f);
        float rise = jumpSpeed * jumpSpeed / (2f * GameDefs.Gravity);
        float airTime = 2f * jumpSpeed / GameDefs.Gravity;
        float span = Mathf.Max(0.1f, moveSpeed) * airTime;

        int maxRise = Mathf.Clamp(Mathf.CeilToInt(rise), 1, 6);
        int maxSpan = Mathf.Clamp(Mathf.CeilToInt(span), 2, 12);
        return new SpiritNavProfile(maxRise, maxSpan, Mathf.Clamp(maxRise + 3, 3, 12));
    }
}

/// <summary>
/// @trace SPIRIT-PATH 随从防卡死寻路：在方块网格上做 BFS。
///
/// 只解决一件事：随从被地形卡住、离玩家越来越远时，给出一条「站哪块 → 站哪块」的路径。
/// 节点 = 可站立方块（IsStandableCell）；边分三类：
///   · 平地走：同行左右 1 格；
///   · 跳上：向上 1~MaxRise 行、水平 ±MaxSpan 格；
///   · 下落：向下 1~MaxDrop 行、水平 ±MaxSpan 格。
/// 跳跃 / 跨度上限来自 <see cref="SpiritNavProfile"/>，随从变快变敏捷时自动放宽，不写死。
/// 不做物理精算（不检查跳跃弧线是否被挡）——它只是防卡死的找回手段，真走不动还有超时瞬移兜底。
/// 用 BFS 而非 A*：网格只有 27×56，够快且不用调启发式。
/// </summary>
public static class SpiritPathfinder
{
    private const int MaxNodes = 3000;

    /// <summary>用基础能力档案寻路。</summary>
    public static List<Vector2>? FindPath(IStandableGrid grid, Vector2 from, Vector2 to) =>
        FindPath(grid, from, to, SpiritNavProfile.Default);

    /// <summary>
    /// 返回世界坐标路径（不含起点，最后一点接近玩家所在格）；找不到路返回 null。
    /// </summary>
    public static List<Vector2>? FindPath(IStandableGrid grid, Vector2 from, Vector2 to,
                                          SpiritNavProfile profile)
    {
        if (grid == null) return null;
        if (profile.MaxRise <= 0 || profile.MaxSpan <= 0) profile = SpiritNavProfile.Default;

        if (!TryFindStandable(grid, from, out int sr, out int sc)) return null;
        if (!TryFindStandable(grid, to, out int tr, out int tc)) return null;

        int start = Key(sr, sc), goal = Key(tr, tc);
        if (start == goal) return new List<Vector2>();

        var cameFrom = new Dictionary<int, int>();
        var visited = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);

        bool found = false;
        int expanded = 0;
        while (queue.Count > 0 && expanded < MaxNodes)
        {
            int cur = queue.Dequeue();
            expanded++;
            if (cur == goal) { found = true; break; }

            int r = cur / GameDefs.BlockCols;
            int c = cur % GameDefs.BlockCols;

            for (int dr = -profile.MaxDrop; dr <= profile.MaxRise; dr++)
            {
                for (int dc = -profile.MaxSpan; dc <= profile.MaxSpan; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    // 平地只走一格（避免「瞬移跨格」）；跨格必须靠跳跃 / 下落
                    if (dr == 0 && (dc > 1 || dc < -1)) continue;

                    int nr = r + dr, nc = c + dc;
                    if (nr < 0 || nr >= GameDefs.BlockRows) continue;
                    if (nc < 0 || nc >= GameDefs.BlockCols) continue;
                    if (!grid.IsStandableCell(nr, nc)) continue;

                    int nk = Key(nr, nc);
                    if (!visited.Add(nk)) continue;
                    cameFrom[nk] = cur;
                    queue.Enqueue(nk);
                }
            }
        }
        if (!found) return null;

        // 回溯 → 反转
        var cells = new List<int>();
        int node = goal;
        while (node != start)
        {
            cells.Add(node);
            if (!cameFrom.TryGetValue(node, out node)) return null;
        }
        cells.Reverse();

        var path = new List<Vector2>(cells.Count);
        foreach (int k in cells)
            path.Add(grid.StandPoint(k / GameDefs.BlockCols, k % GameDefs.BlockCols));
        return path;
    }

    private static int Key(int row, int col) => row * GameDefs.BlockCols + col;

    /// <summary>从任意世界坐标找最近的可站立方块（角色可能在空中 / 刚起跳）。</summary>
    private static bool TryFindStandable(IStandableGrid grid, Vector2 world, out int row, out int col)
    {
        row = 0;
        col = 0;
        if (!grid.WorldToCell(world, out int r, out int c)) return false;
        if (grid.IsStandableCell(r, c)) { row = r; col = c; return true; }

        // 先向下找（角色通常站在方块上方一点），再向上、左右
        for (int d = 1; d <= 4; d++)
        {
            if (r - d >= 0 && grid.IsStandableCell(r - d, c)) { row = r - d; col = c; return true; }
            if (r + d < GameDefs.BlockRows && grid.IsStandableCell(r + d, c)) { row = r + d; col = c; return true; }
            if (c - d >= 0 && grid.IsStandableCell(r, c - d)) { row = r; col = c - d; return true; }
            if (c + d < GameDefs.BlockCols && grid.IsStandableCell(r, c + d)) { row = r; col = c + d; return true; }
        }
        return false;
    }
}
