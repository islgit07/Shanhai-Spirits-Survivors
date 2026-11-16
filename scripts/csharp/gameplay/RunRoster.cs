using System;
using System.Collections.Generic;
using System.Linq;

namespace ShanhaiSpiritsSurvivors;

/// <summary>@trace RUN-ROSTER 单条当局随从记录（占位文案，零技能 / 零属性）。</summary>
public sealed class RunCompanion
{
    public string Id { get; }
    public string DisplayName { get; }
    public int Level { get; set; } = 1;
    public int Quality { get; set; } = 1;

    public RunCompanion(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}

/// <summary>
/// @trace RUN-ROSTER 局内随从队伍（第四块 UI 的可跑逻辑，v0.0005）。
/// 纯 C# 对象，由 GameRoot 持有：当局临时、退出即清、绝不写存档。
/// 选新随从 → 加入队伍；选已在队的同名随从 → 合成、资质 +1（沿用已记录的第四块目标）。
/// 所有名字都是占位「随从 A」形式；技能由用户自己设计，本类不创造任何技能或属性效果。
/// </summary>
public sealed class RunRoster
{
    public const int MaxSlots = 4;
    public static readonly string[] CandidateIds = { "A", "B", "C", "D", "E", "F" };

    private readonly List<RunCompanion> _members = new();

    public IReadOnlyList<RunCompanion> Members => _members;
    public int Count => _members.Count;

    public static string NameOf(string id) => $"随从 {id}";

    public void Clear() => _members.Clear();

    public bool Has(string id) => _members.Any(m => m.Id == id);

    public int QualityOf(string id) => _members.FirstOrDefault(m => m.Id == id)?.Quality ?? 0;

    /// <summary>领取或合成；返回是否成功，message 给 UI / 日志用。</summary>
    public bool TryApply(string id, out string message)
    {
        if (Array.IndexOf(CandidateIds, id) < 0)
        {
            message = "未知随从（占位池外）";
            return false;
        }

        RunCompanion? existing = _members.FirstOrDefault(m => m.Id == id);
        if (existing != null)
        {
            existing.Quality++;
            message = $"{NameOf(id)} 合成成功，资质 {existing.Quality}";
            return true;
        }

        if (_members.Count >= MaxSlots)
        {
            message = "队伍已满，选已在队的随从可合成";
            return false;
        }

        _members.Add(new RunCompanion(id, NameOf(id)));
        message = $"{NameOf(id)} 加入队伍";
        return true;
    }

    /// <summary>从占位池随机抽 3 个不同 Id；rng 由调用方给，冒烟可注入确定性序列。</summary>
    public static string[] PickOffer(Random rng)
    {
        return CandidateIds.OrderBy(_ => rng.Next()).Take(3).ToArray();
    }
}
