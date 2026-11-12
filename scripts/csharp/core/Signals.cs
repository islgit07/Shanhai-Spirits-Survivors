using System;
using System.Collections.Generic;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace EVT-001 静态 C# 事件总线。全量 C# 后不再需要 Godot autoload 信号桥接。
/// 订阅方必须在 _ExitTree 退订，否则静态事件会持有已销毁节点。
/// </summary>
public static class Signals
{
    public static event Action<RunPhase, int>? PhaseChanged;
    public static event Action<int, int, int>? EssenceChanged;   // current, cost, level
    public static event Action<int, int>? HpChanged;
    public static event Action<int>? CoinsChanged;
    public static event Action? LegionChanged;
    public static event Action<IReadOnlyList<Offer>>? OffersPresented;
    public static event Action<string>? Hint;
    public static event Action<int, int, int>? RunEnded;         // day, kills, embers
    public static event Action? MetaChanged;
    public static event Action? StructuresChanged;
    public static event Action<int>? WildKilled;                 // essence value

    public static void EmitPhase(RunPhase p, int day) => PhaseChanged?.Invoke(p, day);
    public static void EmitEssence(int cur, int cost, int lv) => EssenceChanged?.Invoke(cur, cost, lv);
    public static void EmitHp(int hp, int max) => HpChanged?.Invoke(hp, max);
    public static void EmitCoins(int c) => CoinsChanged?.Invoke(c);
    public static void EmitLegion() => LegionChanged?.Invoke();
    public static void EmitOffers(IReadOnlyList<Offer> o) => OffersPresented?.Invoke(o);
    public static void EmitHint(string s) => Hint?.Invoke(s);
    public static void EmitRunEnded(int d, int k, int e) => RunEnded?.Invoke(d, k, e);
    public static void EmitMeta() => MetaChanged?.Invoke();
    public static void EmitStructures() => StructuresChanged?.Invoke();
    public static void EmitWildKilled(int v) => WildKilled?.Invoke(v);

    /// <summary>跨局只清订阅（由各节点自行退订），总线本身常驻。</summary>
    public static void ClearAllSubscribers()
    {
        PhaseChanged = null; EssenceChanged = null; HpChanged = null; CoinsChanged = null;
        LegionChanged = null; OffersPresented = null; Hint = null; RunEnded = null;
        MetaChanged = null; StructuresChanged = null; WildKilled = null;
    }
}
