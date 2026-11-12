using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace WAVE-001 / ECO-001 野生幻兽波次：预算制 B(d) + 自适应间隔。
/// 刷怪池与唤灵池共用 GameDefs.Species —— SPEC-001「敌我同源」的运行时证据。
/// </summary>
public partial class WildSpawner : Node
{
    private int _day = 1;
    private int _remaining;
    private float _timer;
    private RandomNumberGenerator _rng = new();

    public void Begin(int day, int seed)
    {
        _day = day;
        _remaining = GameDefs.Budget(day);
        _timer = GameRoot.SmokeMode ? 0.01f : 0.30f;
        _rng = Rng.Stream(seed, $"wild-{day}");
    }

    public override void _Process(double delta)
    {
        if (Run.Phase != RunPhase.Night || _remaining <= 0) return;
        _timer -= (float)delta;
        if (_timer > 0.0f) return;
        float interval = GameDefs.SpawnInterval(_day);
        _timer = GameRoot.SmokeMode ? Mathf.Min(interval, 0.06f) : interval;

        var pool = new List<SpeciesDef>();
        foreach (SpeciesDef d in GameDefs.Unlocked(_day))
        {
            int weight = d.Tier switch
            {
                SpiritTier.Common => 10, SpiritTier.Spirit => 4,
                SpiritTier.Warden => 2, _ => 1,
            };
            for (int i = 0; i < weight; i++) pool.Add(d);
        }
        if (pool.Count == 0) return;
        SpeciesDef pick = pool[_rng.RandiRange(0, pool.Count - 1)];
        _remaining -= 1 + (int)pick.Tier;
        SpiritWorld.Active?.SpawnWild(pick, _day);
    }
}
