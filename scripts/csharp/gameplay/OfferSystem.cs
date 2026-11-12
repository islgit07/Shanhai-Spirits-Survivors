using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>一次唤灵候选。献祭成本显式保存在 SacrificeUids，UI 直接展示给玩家。</summary>
public sealed class Offer
{
    public OfferKind Kind;
    public string Title = "";
    public string Desc = "";
    public int Weight = 10;
    public string SpeciesId = "";
    public int UnitUid;
    public FuseRecipe? Recipe;
    public List<int> SacrificeUids = new();

    public string CostText
    {
        get
        {
            if (SacrificeUids.Count == 0) return "无消耗";
            var names = Run.Legion.Where(u => SacrificeUids.Contains(u.Uid))
                .GroupBy(u => $"{u.Def.Name} Lv.{u.Level}")
                .Select(g => $"{g.Key} ×{g.Count()}");
            string soul = Recipe is { SoulCost: > 0 } ? " + 灵魄 ×" + Recipe.SoulCost : "";
            return "献祭：" + string.Join("、", names) + soul;
        }
    }

    /// <summary>应用后回调 Legion.Add / LevelUp / Fuse —— 对应 BM 的 sLegion_* 落地。</summary>
    public void Apply()
    {
        switch (Kind)
        {
            case OfferKind.Raise:
                Legion.Add(SpeciesId, 1);
                break;
            case OfferKind.Bolster:
                Legion.LevelUp(UnitUid);
                break;
            case OfferKind.Fuse:
                if (Recipe != null) Legion.Fuse(Recipe, SacrificeUids);
                break;
            case OfferKind.Rest:
                Run.Heal(20);
                Run.AddCoins(3);
                Signals.EmitHint("凝神调息：回复 20 HP，拾得贝币 ×3");
                break;
        }
        Run.CardsTaken++;
    }
}

/// <summary>
/// @trace OFFR-001 / SOUL-001 唤灵选卡：分支生成 → 前置过滤 → 加权抽样。
/// 学习 BM 的 sChooseNewItem → sCNI_Offer_* → sCNI_GRM_Add 管线：
/// 「解锁」= 往候选池加入一条分支，而不是在各处写 if。
/// </summary>
public static class OfferSystem
{
    private static bool TierUnlocked(SpiritTier tier) => tier switch
    {
        SpiritTier.Warden => Meta.HasLore("lore_warden"),
        SpiritTier.Sacred => Meta.HasLore("lore_sacred"),
        _ => true,
    };

    private static Offer RaiseOffer(SpeciesDef d) => new()
    {
        Kind = OfferKind.Raise,
        Title = $"唤灵 · {d.Name}",
        Desc = $"{GameDefs.TierName(d.Tier)}　{d.Traits[0]}\n" +
               $"加入 1 只 Lv.1 {d.Name}（当前 {Legion.CountOf(d.Id)}/{d.Cap}）",
        SpeciesId = d.Id,
        Weight = d.Tier switch
        {
            SpiritTier.Common => 30, SpiritTier.Spirit => 12,
            SpiritTier.Warden => 5, _ => 2,
        },
    };

    private static Offer BolsterOffer(LegionUnit u) => new()
    {
        Kind = OfferKind.Bolster,
        Title = $"培养 · {u.Def.Name}",
        Desc = $"Lv.{u.Level} → Lv.{u.Level + 1}\n{u.Def.Traits[Math.Min(u.Level, 2)]}",
        SpeciesId = u.SpeciesId,
        UnitUid = u.Uid,
        Weight = u.Level == 1 ? 26 : 18,
    };

    private static Offer FuseOffer(FuseRecipe r, List<LegionUnit> victims, SpeciesDef result)
    {
        int w = r.Weight;
        // ★助手提案：对应 BM「Hero Soul 把 Meldus 概率 ×20」的跃迁窗口。
        if (r.SoulCost > 0 && Run.Souls > 0) w *= 20;
        return new Offer
        {
            Kind = OfferKind.Fuse,
            Title = r.Title,
            Desc = $"{r.Desc}\n契得 {result.Name}（{GameDefs.TierName(result.Tier)}，Lv.{r.ResultLevel}）",
            SpeciesId = r.ResultId,
            Recipe = r,
            SacrificeUids = victims.Select(v => v.Uid).ToList(),
            Weight = w,
        };
    }

    private static Offer RestOffer() => new()
    {
        Kind = OfferKind.Rest,
        Title = "凝神 · 调息",
        Desc = "当前无可唤之灵（军团已满 / 前置未达）\n回复 20 HP，拾得贝币 ×3",
        Weight = 1,
    };

    /// <summary>构建完整候选池（分支生成 + 前置过滤）。公开给冒烟做确定性分支可达性校验。</summary>
    public static List<Offer> BuildCandidates()
    {
        var pool = new List<Offer>();

        // 分支 1：Raise（铺量）
        foreach (SpeciesDef d in GameDefs.Unlocked(Run.Day))
            if (TierUnlocked(d.Tier) && Legion.CanAdd(d))
                pool.Add(RaiseOffer(d));

        // 分支 2：Bolster（养单体，Lv1→2→3）
        foreach (LegionUnit u in Run.Legion)
            if (u.Level < GameDefs.MaxUnitLevel)
                pool.Add(BolsterOffer(u));

        // 分支 3：Fuse（压缩数量换质变；Lv3 是材料）
        foreach (FuseRecipe r in GameDefs.Fuses)
        {
            if (!Legion.CanFuse(r)) continue;
            var victims = Legion.FindSacrifice(r);
            pool.Add(FuseOffer(r, victims, GameDefs.SpeciesById(r.ResultId)));
        }
        return pool;
    }

    /// <summary>生成 count 个不重复候选；按权重无放回抽样，池不足时用凝神补位防软锁。</summary>
    public static List<Offer> ChooseNewItem(int seed, int level)
    {
        int count = Meta.OptionCount;
        var pool = BuildCandidates();
        var rng = Rng.Stream(seed, $"summon-{level}-{Run.CardsTaken}");
        var work = new List<Offer>(pool);
        var result = new List<Offer>();
        while (result.Count < count && work.Count > 0)
        {
            int total = work.Sum(o => Math.Max(1, o.Weight));
            int roll = rng.RandiRange(1, total);
            int acc = 0, pick = 0;
            for (int i = 0; i < work.Count; i++)
            {
                acc += Math.Max(1, work[i].Weight);
                if (roll <= acc) { pick = i; break; }
            }
            result.Add(work[pick]);
            work.RemoveAt(pick);
        }
        while (result.Count < Math.Min(count, 3)) result.Add(RestOffer());
        return result;
    }
}
