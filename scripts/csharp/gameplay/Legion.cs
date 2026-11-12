using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace LEGN-001 幻灵军团：Add / LevelUp / Fuse 三个出口 + 分类 cap。
/// 学习 BM 的 sLegion_* 与 sGetMinionCap：高阶单位各有独立上限，堆单一最强在数学上不成立。
/// </summary>
public static class Legion
{
    public static int CountOf(string speciesId) => Run.Legion.Count(u => u.SpeciesId == speciesId);

    public static int CountTier(SpiritTier tier) =>
        Run.Legion.Count(u => u.Def.Tier == tier);

    public static bool CanAdd(SpeciesDef def) =>
        Run.Legion.Count < Run.LegionCap && CountOf(def.Id) < def.Cap;

    public static void Add(string speciesId, int level = 1)
    {
        var def = GameDefs.SpeciesById(speciesId);
        if (!CanAdd(def))
        {
            Signals.EmitHint("军团已满或该物种已达上限");
            return;
        }
        Run.Legion.Add(new LegionUnit(Run.NewUid(), speciesId, Mathf.Clamp(level, 1, GameDefs.MaxUnitLevel)));
        Signals.EmitLegion();
        Signals.EmitHint($"契得 {def.Name} Lv.{level}");
    }

    public static void Remove(int uid)
    {
        int n = Run.Legion.RemoveAll(u => u.Uid == uid);
        if (n > 0) Signals.EmitLegion();
    }

    /// <summary>Lv1 → Lv2 → Lv3。Lv3 不是终点，是融合 / 凶化的入口材料。</summary>
    public static bool LevelUp(int uid)
    {
        LegionUnit? u = Run.Legion.FirstOrDefault(x => x.Uid == uid);
        if (u == null || u.Level >= GameDefs.MaxUnitLevel) return false;
        u.Level++;
        Signals.EmitLegion();
        Signals.EmitHint($"{u.Def.Name} 培养至 Lv.{u.Level}：{u.Def.Traits[Mathf.Min(u.Level - 1, 2)]}");
        return true;
    }

    /// <summary>按配方挑出将被献祭的单位；排序确定性，选卡界面据此显示明确成本。</summary>
    public static List<LegionUnit> FindSacrifice(FuseRecipe r)
    {
        return Run.Legion
            .Where(u => u.Def.Tier == r.CostTier && u.Level >= r.CostMinLevel)
            .OrderBy(u => u.Level)
            .ThenBy(u => u.Uid)
            .Take(r.CostCount)
            .ToList();
    }

    public static bool CanFuse(FuseRecipe r)
    {
        if (!string.IsNullOrEmpty(r.RequiredLore) && !Meta.HasLore(r.RequiredLore)) return false;
        if (Run.Souls < r.SoulCost) return false;
        var result = GameDefs.SpeciesById(r.ResultId);
        if (CountOf(result.Id) >= result.Cap) return false;
        return FindSacrifice(r).Count >= r.CostCount;
    }

    public static void Fuse(FuseRecipe r, IReadOnlyList<int> victimUids)
    {
        if (!CanFuse(r)) return;
        var victims = Run.Legion.Where(u => victimUids.Contains(u.Uid)).ToList();
        if (victims.Count < r.CostCount) return;
        var set = new HashSet<int>(victimUids);
        Run.Legion.RemoveAll(u => set.Contains(u.Uid));
        Run.Souls -= r.SoulCost;
        var result = GameDefs.SpeciesById(r.ResultId);
        Run.Legion.Add(new LegionUnit(Run.NewUid(), r.ResultId, r.ResultLevel));
        Signals.EmitLegion();
        Signals.EmitHint($"献祭 {victims.Count} 只 → 契得 {result.Name} Lv.{r.ResultLevel}");
    }

    public static string Summary() =>
        $"{Run.Legion.Count}/{Run.LegionCap}　" +
        string.Join(" ", Run.Legion.GroupBy(u => u.Def.Name)
            .Select(g => $"{g.Key}×{g.Count()}"));
}
