using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 单槽自动存档（v0.0005 随从系统第 3 块）：user://shanhai_save.json，JSON + 版本号。
/// 遵循 save-load 范式：玩家可控数据用 JSON 而非 Resource/.tres（后者加载会执行脚本，不安全）；
/// 路径用 user://（res:// 在导出构建只读）；每个存档带 version 字段，读档时逐版迁移。
///
/// 沿用项目 ArtBank / Rng / SpiritCatalog 的 public static 工具范式，零 autoload。
/// 与纯目录不同，这里持有「一份可变存档缓存」——保存时序列化写盘，读档时反序列化进缓存。
/// 所有变更走 Save() 立即落盘，满足用户「单槽自动存」的决定。
/// </summary>
public static class SaveGame
{
    public const int SaveVersion = 1;
    public const string SavePath = "user://shanhai_save.json";

    private static SaveData? _cache;

    /// <summary>当前存档（首次访问时自动读档或建档）。</summary>
    public static SaveData Data => _cache ??= LoadOrCreate();

    /// <summary>读档：不存在 / 损坏 / 版本过高则回退到全新存档，绝不崩。</summary>
    public static SaveData LoadOrCreate()
    {
        SaveData? loaded = null;
        if (FileAccess.FileExists(SavePath))
        {
            using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            if (file != null)
            {
                string text = file.GetAsText();
                try
                {
                    loaded = JsonSerializer.Deserialize<SaveData>(text);
                }
                catch (JsonException e)
                {
                    GD.PushWarning($"[SAVE] 存档解析失败，改用新档：{e.Message}");
                }
            }
        }

        if (loaded == null || loaded.Version > SaveVersion)
        {
            // 未来版本 / 无法识别：不冒险读，建新档（本作尚无历史版本需迁移）。
            loaded = new SaveData { Version = SaveVersion };
            GD.Print($"[SAVE] 新建存档 path={SavePath} souls={loaded.Souls}");
        }
        else
        {
            loaded.Version = SaveVersion;
            Migrate(loaded);
            GD.Print($"[SAVE] 读档成功 path={SavePath} souls={loaded.Souls}");
        }

        EnsureSpiritEntries(loaded);
        _cache = loaded;
        return loaded;
    }

    /// <summary>版本迁移钩子：目前只有 v1；将来加字段时按旧版本逐版补默认值。</summary>
    private static void Migrate(SaveData data)
    {
        // v1 为首个版本，无迁移步骤；保留钩子便于后续扩展，避免存档格式锁死。
        if (data.HeroNodes == null) data.HeroNodes = new Dictionary<string, Dictionary<string, int>>();
        if (data.Growth == null) data.Growth = new Dictionary<string, SpiritGrowth>();
        if (data.Souls < 0) data.Souls = GameDefs.InitialSouls;
    }

    /// <summary>给目录里每只随从补齐空白成长 / 加点槽；加新随从后老存档也能平滑升级。</summary>
    private static void EnsureSpiritEntries(SaveData data)
    {
        foreach (var def in SpiritCatalog.All)
        {
            if (!data.Growth.ContainsKey(def.Id))
                data.Growth[def.Id] = new SpiritGrowth();
            if (!data.HeroNodes.ContainsKey(def.Id))
                data.HeroNodes[def.Id] = new Dictionary<string, int>();
        }
    }

    /// <summary>立即写盘（单槽自动存：任何加点 / 升级 / 加魂后都调用）。</summary>
    public static void Save()
    {
        SaveData data = Data;
        data.Version = SaveVersion;
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError($"[SAVE] 无法写入存档 {SavePath}");
            return;
        }
        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        file.StoreString(json);
        GD.Print($"[SAVE] 写盘 path={SavePath} souls={data.Souls}");
    }

    /// <summary>仅测试/探针用：丢弃缓存并强制下次重新读档（验证落盘往返）。</summary>
    public static void ResetCache() => _cache = null;

    /// <summary>
    /// 冒烟用：写入一份全新默认档（魂魄回到初值、所有加点清零）并立即落盘。
    /// 比起删档，写新档能让每次冒烟从确定状态起步，且验证「写盘 → 重读」往返。
    /// </summary>
    public static void ResetToDefault()
    {
        _cache = new SaveData { Version = SaveVersion, Souls = GameDefs.InitialSouls };
        EnsureSpiritEntries(_cache);
        Save();
    }

    /// <summary>删除存档文件并清空缓存（需要彻底清空时用）。</summary>
    public static void DeleteSave()
    {
        if (FileAccess.FileExists(SavePath)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
        _cache = null;
    }

    public static bool HasSave => FileAccess.FileExists(SavePath);

    // ---------------------------------------------------------------- 魂魄
    public static void AddSouls(int amount)
    {
        if (amount <= 0) return;
        Data.Souls += amount;
        Save();
    }

    public static bool TrySpendSouls(int cost) => TrySpendSouls(cost, true);

    /// <summary>扣魂；saveNow=false 供「扣魂 + 改状态」合并成一次写盘的复合操作使用。</summary>
    private static bool TrySpendSouls(int cost, bool saveNow)
    {
        if (cost <= 0 || Data.Souls < cost) return false;
        Data.Souls -= cost;
        if (saveNow) Save();
        return true;
    }

    // ---------------------------------------------------------------- 主角升级树
    public static int NodeLevel(string spiritId, string nodeId) =>
        Data.HeroNodes.TryGetValue(spiritId, out var nodes)
        && nodes.TryGetValue(nodeId, out int level) ? level : 0;

    /// <summary>给某本命的某个树节点加 level 点；成功才扣魂并写盘。</summary>
    public static bool TryUpgradeNode(string spiritId, string nodeId, int cost)
    {
        if (!TrySpendSouls(cost, saveNow: false)) return false;
        if (!Data.HeroNodes.TryGetValue(spiritId, out var nodes))
        {
            nodes = new Dictionary<string, int>();
            Data.HeroNodes[spiritId] = nodes;
        }
        nodes[nodeId] = NodeLevel(spiritId, nodeId) + 1;
        Save();   // 「扣魂 + 加点」一次落盘，避免中间态被读到
        return true;
    }

    // ---------------------------------------------------------------- 随从局外养成
    public static SpiritGrowth Growth(string spiritId)
    {
        if (!Data.Growth.TryGetValue(spiritId, out var growth))
        {
            growth = new SpiritGrowth();
            Data.Growth[spiritId] = growth;
        }
        return growth;
    }

    public static bool TryUpgradeGrowth(string spiritId, GrowthStat stat)
    {
        var growth = Growth(spiritId);
        // 先确认「未满级 + 魂魄够」再变更，避免扣费失败却白加属性。
        if (growth.StepCount(stat) >= GameDefs.GrowthMaxStep) return false;
        if (Data.Souls < GameDefs.GrowthStepCost) return false;
        if (!TrySpendSouls(GameDefs.GrowthStepCost, saveNow: false)) return false;
        growth.TryUpgrade(stat);
        Save();
        return true;
    }
}
