using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 本命随从目录（v0.0005 随从系统第 1 块）：随从身份数据的只读注册表。
/// 启动时扫描 res://resources/spirits/*.tres，把其中挂 SpiritDef 脚本的资源收集成目录。
/// 加新随从 = 往该目录丢一张定义卡，零代码改动——这是本块的核心价值。
///
/// 沿用项目 ArtBank / Rng 的 public static class 工具范式：不持有可变游戏状态，
/// 只在首次访问时构建一次缓存；同一 Id 重复出现时保留第一张并打印警告（数据卡配置错误要能发现）。
/// </summary>
public static class SpiritCatalog
{
    public const string SpiritsDir = "res://resources/spirits";

    /// <summary>
    /// 本命选择跨场景传递键：选择界面写入 SceneTree meta，GameRoot 读取后实例化对应灵兽。
    /// 用 SceneTree meta 而非静态字段/autoload——项目铁律零 autoload、零静态状态类，
    /// 且 meta 随场景树存活、换场景不丢，正好覆盖「选完本命进入游戏」这一段。
    /// </summary>
    public const string ChosenIdMeta = "chosen_spirit_id";

    private static IReadOnlyList<SpiritDef>? _cache;

    /// <summary>全部随从定义，按 Id 升序（确定性顺序，冒烟与 UI 都依赖它）。</summary>
    public static IReadOnlyList<SpiritDef> All => _cache ??= LoadAll();

    /// <summary>按 Id 查定义；未找到返回 null（Id 是代码契约，大小写敏感）。</summary>
    public static SpiritDef? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>仅用于测试/热重载：清空缓存，下次访问重新扫描目录。</summary>
    public static void ClearCache() => _cache = null;

    private static IReadOnlyList<SpiritDef> LoadAll()
    {
        var found = new List<SpiritDef>();
        using var dir = DirAccess.Open(SpiritsDir);
        if (dir == null)
        {
            GD.PushError($"[SPIRITS] 无法打开随从目录 {SpiritsDir}");
            return found;
        }

        dir.ListDirBegin();
        string file = dir.GetNext();
        while (file != string.Empty)
        {
            // 导出构建里文本资源会变成 name.tres.remap；Load 会跟随 remap，这里按技能范式去掉后缀。
            string name = file.TrimSuffix(".remap");
            if (!dir.CurrentIsDir() && (name.EndsWith(".tres") || name.EndsWith(".res")))
            {
                Resource? res = ResourceLoader.Load(SpiritsDir.PathJoin(name));
                if (res is SpiritDef def && !string.IsNullOrWhiteSpace(def.Id))
                    found.Add(def);
            }
            file = dir.GetNext();
        }

        var ordered = found.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
        for (int i = 1; i < ordered.Count; i++)
            if (ordered[i].Id == ordered[i - 1].Id)
                GD.PushWarning($"[SPIRITS] 重复 Id「{ordered[i].Id}」：定义卡应保证 Id 唯一");

        GD.Print($"[SPIRITS] catalog loaded count={ordered.Count} ids=[{string.Join(",", ordered.Select(s => s.Id))}]");
        return ordered;
    }
}
