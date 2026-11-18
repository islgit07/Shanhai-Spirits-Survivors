using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// SpiritFollow 的「防卡死寻路」partial 部分（SPIRIT-PATH）：长时间远离玩家 → 在方块网格上
/// BFS 找一条回得去的路，沿路径点走；寻路模式下迟迟回不去（路被堵死 / BFS 太乐观）→
/// 超时兜底直接瞬移回主角身边。能力档案（能跳多高 / 跨多远）参数化在 SpiritNavProfile，
/// 随从变快变敏捷时自动放宽，不写死。
/// 方法体逐字沿用原单文件实现，仅把 _PhysicsProcess 中的寻路段提为方法（行为零改动）。
/// </summary>
public partial class SpiritFollow
{
    private IStandableGrid? _grid;       // 方块网格（BFS 寻路用；null = 不做寻路）
    private float _farTimer;             // 远离玩家的累计时长
    private float _seekTimer;            // 重算路径倒计时
    private float _seekDuration;         // 已处于寻路模式的时长
    private List<Vector2>? _path;        // 当前 BFS 路径（世界坐标）
    private int _pathIndex;              // 当前走向第几个路径点
    // 移动能力档案：BFS 认为这只随从能跳多高 / 跨多远。默认与主角满跳同量级；
    // 以后随从的移速 / 敏捷接入养成后，用 SpiritNavProfile.FromStats 换一份即可。
    private SpiritNavProfile _navProfile = SpiritNavProfile.Default;
    private bool _forceSeek;             // 冒烟专用：模拟「被地形卡死」，强制走寻路模式
    private bool _wasSeeking;

    /// <summary>最近一次 BFS 寻路是否找到路径。</summary>
    public bool LastPathFound { get; private set; }

    /// <summary>本次接入以来是否成功找到过路径（冒烟核对「卡死时确实靠寻路回来」）。</summary>
    public bool EverFoundPath { get; private set; }

    /// <summary>是否已拿到方块网格（冒烟诊断）。</summary>
    public bool HasGrid => _grid != null;

    /// <summary>进入寻路模式的次数（冒烟诊断）。</summary>
    public int SeekStarts { get; private set; }

    /// <summary>
    /// 设置移动能力档案（移速 / 敏捷 → 跳高与跨度）。属性变化后调用一次即可，
    /// 下一次寻路就按新能力算；不调用则一直用基础档案。
    /// </summary>
    public void SetNavProfile(SpiritNavProfile profile) => _navProfile = profile;

    /// <summary>按移速 / 敏捷直接换算并设置（以后接随从养成时的入口）。</summary>
    public void SetMoveStats(float moveSpeed, float agility) =>
        _navProfile = SpiritNavProfile.FromStats(moveSpeed, agility);

    /// <summary>
    /// 冒烟专用：模拟「被地形卡死，直线回不去」——强制进入寻路模式，直到真的回到玩家身边。
    /// 正常游戏不会调用；用来验证 BFS 路径确实被采用并走通。
    /// </summary>
    public void ForceSeekForSmoke()
    {
        _forceSeek = true;
        _farTimer = GameDefs.SpiritFindPathDelay;
    }

    /// <summary>
    /// 寻路段（原 _PhysicsProcess 中连续的一段，逐字搬移）：
    /// 累计远离时长 → 达到阈值进入寻路模式 → 周期性重算 BFS 路径 → 沿路径点改写 wish；
    /// 路径走完交回普通跟随；超时兜底瞬移（teleported = true，调用方本帧直接结束）。
    /// </summary>
    private Vector2 ApplySeek(Vector2 pos, Vector2 playerPos, Vector2 wish, Vector2 dynamicSlot,
        float dt, out bool seeking, out bool teleported)
    {
        seeking = false;
        teleported = false;
        Vector2 relNow = pos - playerPos;
        float distToPlayer = relNow.Length();
        if (_grid != null)
        {
            bool farEnough = distToPlayer > GameDefs.SpiritFindPathDist;
            if (_forceSeek)
            {
                farEnough = true;
                if (distToPlayer < 2.5f) _forceSeek = false;   // 真回到身边才解除（冒烟用）
            }
            if (farEnough) _farTimer += dt;
            else _farTimer = Mathf.Max(0f, _farTimer - dt * 2f);   // 靠近比远离消得快，避免状态残留

            if (_farTimer >= GameDefs.SpiritFindPathDelay)
            {
                if (!_wasSeeking)
                {
                    _wasSeeking = true;
                    SeekStarts++;
                }
                seeking = true;
                _seekDuration += dt;
                _seekTimer -= dt;
                if (_path == null || _seekTimer <= 0f)
                {
                    _path = SpiritPathfinder.FindPath(_grid, pos, playerPos, _navProfile);
                    LastPathFound = _path != null;
                    if (_path != null) EverFoundPath = true;
                    _pathIndex = 0;
                    _seekTimer = GameDefs.SpiritRepathInterval;
                }

                if (_path != null)
                {
                    while (_pathIndex < _path.Count
                           && pos.DistanceTo(_path[_pathIndex]) < 0.45f)
                        _pathIndex++;   // 到达当前路径点 → 走下一个

                    if (_pathIndex < _path.Count)
                    {
                        wish = _path[_pathIndex];
                    }
                    else
                    {
                        // 路径走完：交回普通跟随
                        _farTimer = 0f;
                        _seekDuration = 0f;
                        _path = null;
                    }
                }

                // 兜底：寻路模式下迟迟回不去（路被堵死 / BFS 太乐观）→ 直接瞬移
                if (_seekDuration > GameDefs.SpiritSeekTimeout)
                {
                    _pet.GlobalPosition = playerPos + dynamicSlot + new Vector2(0f, -0.5f);
                    _pet.Velocity = Vector2.Zero;
                    _farTimer = 0f;
                    _seekDuration = 0f;
                    _path = null;
                    _dropTimer = 0f;
                    teleported = true;
                }
            }
            else _wasSeeking = false;
        }

        return wish;
    }
}
