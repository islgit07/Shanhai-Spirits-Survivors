using System.Collections.Generic;

namespace ShanhaiSpiritsSurvivors;

/// <summary>局外养成可升级的四项（用户指定：初始等级 / 血量 / 攻击力 / 敏捷）。</summary>
public enum GrowthStat
{
    Level,
    Hp,
    Attack,
    Agility,
}

/// <summary>
/// 单只本命随从的局外成长数值（v0.0005 随从系统第 3 块）。
/// 这些是「继承存档的初始属性」：只在局外界面加点，局内生效留后续块。
/// 数值直接存最终值（而非存加点次数），以后改 GameDefs 基值不会篡改老存档。
/// </summary>
public sealed class SpiritGrowth
{
    public int Level { get; set; } = GameDefs.GrowthBaseLevel;
    public int Hp { get; set; } = GameDefs.GrowthBaseHp;
    public int Attack { get; set; } = GameDefs.GrowthBaseAttack;
    public int Agility { get; set; } = GameDefs.GrowthBaseAgility;

    public int Value(GrowthStat stat) => stat switch
    {
        GrowthStat.Level => Level,
        GrowthStat.Hp => Hp,
        GrowthStat.Attack => Attack,
        GrowthStat.Agility => Agility,
        _ => 0,
    };

    public string Label(GrowthStat stat) => stat switch
    {
        GrowthStat.Level => "初始等级",
        GrowthStat.Hp => "初始血量",
        GrowthStat.Attack => "初始攻击",
        GrowthStat.Agility => "初始敏捷",
        _ => "",
    };

    public static int Step(GrowthStat stat) => stat switch
    {
        GrowthStat.Level => GameDefs.GrowthStepLevel,
        GrowthStat.Hp => GameDefs.GrowthStepHp,
        GrowthStat.Attack => GameDefs.GrowthStepAttack,
        GrowthStat.Agility => GameDefs.GrowthStepAgility,
        _ => 0,
    };

    public static int Base(GrowthStat stat) => stat switch
    {
        GrowthStat.Level => GameDefs.GrowthBaseLevel,
        GrowthStat.Hp => GameDefs.GrowthBaseHp,
        GrowthStat.Attack => GameDefs.GrowthBaseAttack,
        GrowthStat.Agility => GameDefs.GrowthBaseAgility,
        _ => 0,
    };

    /// <summary>该项已加的级数（用于上限判断与界面显示）。</summary>
    public int StepCount(GrowthStat stat)
    {
        int step = Step(stat);
        return step <= 0 ? 0 : (Value(stat) - Base(stat)) / step;
    }

    /// <summary>加一级；已满 GrowthMaxStep 返回 false 且不改动。</summary>
    public bool TryUpgrade(GrowthStat stat)
    {
        if (StepCount(stat) >= GameDefs.GrowthMaxStep) return false;
        switch (stat)
        {
            case GrowthStat.Level: Level += Step(stat); break;
            case GrowthStat.Hp: Hp += Step(stat); break;
            case GrowthStat.Attack: Attack += Step(stat); break;
            case GrowthStat.Agility: Agility += Step(stat); break;
            default: return false;
        }
        return true;
    }
}

/// <summary>
/// 单槽存档的序列化载体（v0.0005 随从系统第 3 块）。
/// 纯 POCO + System.Text.Json：玩家可控数据不用 Resource/.tres（加载 Resource 会执行脚本，不安全）。
/// HeroNodes：spiritId → (nodeId → 已加点数)，每本命一棵树、互不串档。
/// Growth：spiritId → 局外成长数值，继承当前存档。
/// </summary>
public sealed class SaveData
{
    public int Version { get; set; } = SaveGame.SaveVersion;
    public int Souls { get; set; } = GameDefs.InitialSouls;
    public Dictionary<string, Dictionary<string, int>> HeroNodes { get; set; } = new();
    public Dictionary<string, SpiritGrowth> Growth { get; set; } = new();
}
