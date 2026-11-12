using System;
using System.Collections.Generic;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>军团里的一个契约单位：只存数据，可视节点由 World 按 LegionChanged 重建。</summary>
public sealed class LegionUnit
{
    public int Uid;
    public string SpeciesId = "";
    public int Level = 1;
    public LegionUnit(int uid, string speciesId, int level = 1)
    {
        Uid = uid; SpeciesId = speciesId; Level = level;
    }
    public SpeciesDef Def => GameDefs.SpeciesById(SpeciesId);
}

/// <summary>
/// @trace ESSN-001 / GAME-001 本局状态单点。
/// 学习 BM：灵韵既是经验又是召唤货币——累积跨阈值即触发一次「唤灵」构筑决策，
/// 不存在「升级只加数值」的空转。PendingOffers 允许多级连升排队，一次只弹一张卡。
/// </summary>
public static class Run
{
    public static RunPhase Phase = RunPhase.Menu;
    public static int Seed;
    public static int Day = 1;
    public static int Level = 1;
    public static int Essence;
    public static int EssenceTotal;
    public static int PendingOffers;
    public static int Kills;
    public static int Coins;
    public static int Souls;               // ★助手提案：对应 BM 的 Hero Soul
    public static int Hp = GameDefs.MasterMaxHp;
    public static int MaxHp = GameDefs.MasterMaxHp;
    public static int CardsTaken;
    public static int Rerolls;
    public static string StartSpecies = "qingxiao";

    public static readonly List<LegionUnit> Legion = new();
    public static readonly Dictionary<string, int> Structures = new();
    private static int _nextUid = 1;

    public static int NextCost => GameDefs.EssenceCost(Level);
    public static int LegionCap => GameDefs.BaseLegionCap
        + Meta.LoreLevel("lore_cap") * 2
        + Count("altar");
    public static int Count(string kind) => Structures.TryGetValue(kind, out int n) ? n : 0;
    public static float WildSlow => 1.0f - Mathf.Min(0.45f, Count("stele") * 0.15f);
    public static float AllyIntervalMul => 1.0f / (1.0f + Count("lamp") * 0.08f);
    public static int WellEssence => Count("well") * 6;
    public static int HutHeal => Count("hut") * 12;

    public static void Reset(int seed, string startSpecies)
    {
        Seed = seed;
        Day = 1;
        Level = 1;
        Essence = 0;
        EssenceTotal = 0;
        PendingOffers = 0;
        Kills = 0;
        Coins = 0;
        Souls = 0;
        CardsTaken = 0;
        Rerolls = 2 + Meta.LoreLevel("lore_reroll") * 2;
        MaxHp = GameDefs.MasterMaxHp;
        Hp = MaxHp;
        StartSpecies = string.IsNullOrEmpty(startSpecies) ? "qingxiao" : startSpecies;
        Legion.Clear();
        Legion.Add(new LegionUnit(_nextUid++, StartSpecies, 1));  // 开局自带 1 只契灵
        Structures.Clear();
        _nextUid = 2;

        int bonus = Meta.LoreLevel("lore_essence") * 10;
        if (bonus > 0) AddEssence(bonus);

        Signals.EmitEssence(Essence, NextCost, Level);
        Signals.EmitHp(Hp, MaxHp);
        Signals.EmitCoins(Coins);
        Signals.EmitLegion();
        Signals.EmitStructures();
    }

    public static int NewUid() => _nextUid++;

    /// <summary>灵韵入账：跨阈值即排队一次唤灵；此函数只改状态，不切阶段。</summary>
    public static void AddEssence(int amount)
    {
        if (amount <= 0) return;
        Essence += amount;
        EssenceTotal += amount;
        while (Essence >= GameDefs.EssenceCost(Level))
        {
            Essence -= GameDefs.EssenceCost(Level);
            Level++;
            PendingOffers++;
        }
        Signals.EmitEssence(Essence, NextCost, Level);
    }

    public static void AddCoins(int n)
    {
        Coins = Mathf.Max(0, Coins + n);
        Signals.EmitCoins(Coins);
    }

    public static void AddSoul(int n)
    {
        Souls += n;
        if (n > 0) Signals.EmitHint("拾得灵魄 · 唤灵池中圣阶候选概率大幅跃迁");
    }

    public static void Damage(int amount)
    {
        Hp = Mathf.Max(0, Hp - Mathf.Max(1, amount));
        Signals.EmitHp(Hp, MaxHp);
    }

    public static void Heal(int amount)
    {
        Hp = Mathf.Min(MaxHp, Hp + amount);
        Signals.EmitHp(Hp, MaxHp);
    }

    public static void BuyStructure(CampItem item)
    {
        if (item.Kind == "instant") { Heal(30); return; }
        Structures[item.Kind] = Count(item.Kind) + 1;
        Signals.EmitStructures();
    }
}
