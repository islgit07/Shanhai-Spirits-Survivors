using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace LORE-001 / META-001 局外成长。
/// 学习 BM：Lore 改变的是「唤灵候选池的可能性空间」（解锁分支 / 改 cap / 改候选数 / 改资源），
/// 不是给玩家直接加攻击力。存档用 System.Text.Json（纯 C#，无 Godot Variant 转换）。
/// </summary>
public static class Meta
{
    public const string SavePath = "user://shanhai_meta.json";

    public static int Embers;
    public static int BestDay;
    public static int TotalRuns;
    public static int TotalKills;
    public static readonly Dictionary<string, int> Lores = new();
    public static readonly HashSet<string> UnlockedSpecies = new() { "qingxiao" };

    private sealed class SaveDto
    {
        public int Embers { get; set; }
        public int BestDay { get; set; }
        public int TotalRuns { get; set; }
        public int TotalKills { get; set; }
        public Dictionary<string, int> Lores { get; set; } = new();
        public List<string> Unlocked { get; set; } = new();
    }

    public static void Load()
    {
        Lores.Clear();
        foreach (LoreDef l in GameDefs.Lores) Lores[l.Id] = 0;
        UnlockedSpecies.Clear();
        UnlockedSpecies.Add("qingxiao");

        if (!FileAccess.FileExists(SavePath)) return;
        var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (f == null) return;
        string text = f.GetAsText();
        f.Close();
        try
        {
            SaveDto? dto = JsonSerializer.Deserialize<SaveDto>(text);
            if (dto == null) return;
            Embers = dto.Embers; BestDay = dto.BestDay;
            TotalRuns = dto.TotalRuns; TotalKills = dto.TotalKills;
            foreach (var kv in dto.Lores)
                if (Lores.ContainsKey(kv.Key)) Lores[kv.Key] = kv.Value;
            foreach (string s in dto.Unlocked)
                if (GameDefs.Species.Any(x => x.Id == s)) UnlockedSpecies.Add(s);
        }
        catch (Exception e)
        {
            GD.PushWarning("存档解析失败，使用默认值: " + e.Message);
        }
    }

    public static void Save()
    {
        var dto = new SaveDto
        {
            Embers = Embers, BestDay = BestDay, TotalRuns = TotalRuns, TotalKills = TotalKills,
            Lores = new Dictionary<string, int>(Lores), Unlocked = UnlockedSpecies.ToList(),
        };
        var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (f == null) { GD.PushWarning("存档写入失败: " + SavePath); return; }
        f.StoreString(JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        f.Close();
        Signals.EmitMeta();
    }

    public static int LoreLevel(string id) => Lores.TryGetValue(id, out int lv) ? lv : 0;
    public static bool HasLore(string id) => LoreLevel(id) > 0;
    public static int OptionCount => GameDefs.PickCount + (HasLore("lore_options") ? 1 : 0);

    public static bool BuyLore(string id)
    {
        LoreDef def = GameDefs.LoreById(id);
        int lv = LoreLevel(id);
        if (lv >= def.MaxLevel || Embers < def.Price(lv)) return false;
        Embers -= def.Price(lv);
        Lores[id] = lv + 1;
        Save();
        return true;
    }

    /// <summary>起始契灵解锁价（★助手提案：按凡阶顺序定价，非 BM 数值）。</summary>
    public static int StartPrice(string id) => id switch
    {
        "qingxiao" => 0,
        "chiwu" => 25,
        "xuanjia" => 45,
        _ => 60,
    };

    public static bool UnlockSpecies(string id)
    {
        if (UnlockedSpecies.Contains(id)) return false;
        int price = StartPrice(id);
        if (Embers < price) return false;
        Embers -= price;
        UnlockedSpecies.Add(id);
        Save();
        return true;
    }

    public static int RecordRun(int day, int kills)
    {
        int gain = day * 3 + kills / 10;
        Embers += gain;
        TotalRuns++;
        TotalKills += kills;
        BestDay = Mathf.Max(BestDay, day);
        Save();
        return gain;
    }

    public static void ClearAll()
    {
        Embers = 0; BestDay = 0; TotalRuns = 0; TotalKills = 0;
        Lores.Clear();
        foreach (LoreDef l in GameDefs.Lores) Lores[l.Id] = 0;
        UnlockedSpecies.Clear();
        UnlockedSpecies.Add("qingxiao");
        Save();
    }
}
