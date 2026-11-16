using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 本命升级树目录（v0.0005 随从系统第 3 块）：主角升级树的只读注册表。
/// 扫描 res://resources/upgrades/*.tres，按 SpiritId 索引；加新随从 = 丢一张树定义卡进目录，
/// 与 SpiritCatalog / 界面零耦合。沿用 ArtBank / SpiritCatalog 的静态工具范式，零 autoload。
/// 每只随从缺卡时用最小合法树兜底并告警，保证界面不会因漏配资源崩掉。
/// </summary>
public static class UpgradeCatalog
{
    public const string TreesDir = "res://resources/upgrades";

    private static IReadOnlyDictionary<string, UpgradeTreeDef>? _cache;

    public static IReadOnlyDictionary<string, UpgradeTreeDef> All => _cache ??= LoadAll();

    /// <summary>按随从 Id 取树；缺卡返回代码兜底树（配错要告警，但不能让界面崩）。</summary>
    public static UpgradeTreeDef Tree(string spiritId)
    {
        if (All.TryGetValue(spiritId, out var tree)) return tree;
        var def = SpiritCatalog.Find(spiritId);
        return UpgradeTreeDef.CreateFallback(spiritId, def?.DisplayName ?? spiritId);
    }

    public static void ClearCache() => _cache = null;

    private static IReadOnlyDictionary<string, UpgradeTreeDef> LoadAll()
    {
        var map = new Dictionary<string, UpgradeTreeDef>(StringComparer.Ordinal);
        using var dir = DirAccess.Open(TreesDir);
        if (dir == null)
        {
            GD.PushWarning($"[UPGRADE] 无法打开升级树目录 {TreesDir}，将用代码兜底树");
            return map;
        }

        dir.ListDirBegin();
        string file = dir.GetNext();
        while (file != string.Empty)
        {
            string name = file.TrimSuffix(".remap");
            if (!dir.CurrentIsDir() && (name.EndsWith(".tres") || name.EndsWith(".res")))
            {
                Resource? res = ResourceLoader.Load(TreesDir.PathJoin(name));
                if (res is UpgradeTreeDef tree && !string.IsNullOrWhiteSpace(tree.SpiritId))
                {
                    if (map.ContainsKey(tree.SpiritId))
                        GD.PushWarning($"[UPGRADE] 重复 SpiritId「{tree.SpiritId}」：升级树卡应保证唯一");
                    else
                        map[tree.SpiritId] = tree;
                }
            }
            file = dir.GetNext();
        }
        GD.Print($"[UPGRADE] catalog loaded count={map.Count} ids=[{string.Join(",", map.Keys.OrderBy(k => k, StringComparer.Ordinal))}]");
        return map;
    }
}
