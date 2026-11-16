using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 主角升级树定义卡（v0.0005 随从系统第 3 块）：每本命一棵树，用户说「每一个本命怪的加点都不一样」。
///
/// 结构 = 二叉树，用平行数组存节点（Godot Inspector 可直接编辑，避免嵌套 sub-resource 手写易错）：
///   NodeIds[i] / NodeNames[i] / NodeHpBonus[i] / NodeCost[i] / NodeLeft[i] / NodeRight[i]
///   NodeLeft / NodeRight 存子节点在数组里的下标，-1 表示无。下标 0 为根。
/// 开发初期所有节点的收益字段统一是 NodeHpBonus（加血量）——用户明确「先用加血量设计，以后再丰富加点树」。
/// 以后要加攻击/敏捷节点，加一个新字段 + 界面读它即可，树结构与存档键不变。
///
/// Resource 只读共享（定义卡），运行期不改。
/// </summary>
[GlobalClass]
public partial class UpgradeTreeDef : Resource
{
    [ExportGroup("身份")]
    [Export] public string SpiritId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";

    [ExportGroup("二叉树节点（平行数组，下标 0 = 根，-1 = 无子节点）")]
    [Export] public string[] NodeIds { get; set; } = Array.Empty<string>();
    [Export] public string[] NodeNames { get; set; } = Array.Empty<string>();
    [Export] public int[] NodeHpBonus { get; set; } = Array.Empty<int>();
    [Export] public int[] NodeCost { get; set; } = Array.Empty<int>();
    [Export] public int[] NodeLeft { get; set; } = Array.Empty<int>();
    [Export] public int[] NodeRight { get; set; } = Array.Empty<int>();

    [ExportGroup("规则")]
    [Export] public int MaxLevelPerNode { get; set; } = 3;

    public int NodeCount => NodeIds?.Length ?? 0;

    /// <summary>某节点是否已加点到上限（存档 + 定义卡联合判断）。</summary>
    public bool IsMaxed(int index) => index < 0 || index >= NodeCount
        || SaveGame.NodeLevel(SpiritId, NodeIds[index]) >= MaxLevelPerNode;

    /// <summary>解锁规则：根恒可用；子节点需父节点至少 1 级（经典的「先点前置」加点树）。</summary>
    public int ParentOf(int index)
    {
        if (index <= 0 || index >= NodeCount) return -1;
        for (int i = 0; i < NodeCount; i++)
        {
            if (NodeLeft != null && i < NodeLeft.Length && NodeLeft[i] == index) return i;
            if (NodeRight != null && i < NodeRight.Length && NodeRight[i] == index) return i;
        }
        return -1;
    }

    public bool IsUnlocked(int index)
    {
        if (index < 0 || index >= NodeCount) return false;
        if (index == 0) return true;
        int parent = ParentOf(index);
        return parent >= 0 && SaveGame.NodeLevel(SpiritId, NodeIds[parent]) >= 1;
    }

    /// <summary>
    /// 树结构自检：节点数一致、id 唯一非空、左右子下标合法且不成环、根可达全部节点。
    /// 用于冒烟断言与加卡排错——数据卡配错要能立刻发现，而不是界面上静默缺节点。
    /// </summary>
    public bool IsValid(out string reason)
    {
        reason = "";
        int n = NodeCount;
        if (n == 0) { reason = "无节点"; return false; }
        if (NodeIds.Any(string.IsNullOrWhiteSpace)) { reason = "存在空 NodeId"; return false; }
        if (NodeIds.Distinct(StringComparer.Ordinal).Count() != n) { reason = "NodeId 重复"; return false; }
        if (NodeNames.Length != n || NodeHpBonus.Length != n || NodeCost.Length != n
            || NodeLeft.Length != n || NodeRight.Length != n) { reason = "平行数组长度不一致"; return false; }
        if (NodeHpBonus.Any(v => v <= 0) || NodeCost.Any(v => v <= 0)) { reason = "血量/耗费必须为正"; return false; }

        var seen = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            foreach (int child in new[] { NodeLeft[i], NodeRight[i] })
            {
                if (child < -1 || child >= n) { reason = $"节点 {i} 子下标越界 {child}"; return false; }
                if (child == i) { reason = $"节点 {i} 自引用"; return false; }
                if (child == 0) { reason = "根节点不能被当作子节点"; return false; }
                if (child >= 0 && !seen.Add(child)) { reason = $"节点 {child} 有多个父节点"; return false; }
            }
        }
        // 从根 BFS：必须覆盖全部节点（防断链 / 环）。
        var visited = new HashSet<int> { 0 };
        var queue = new Queue<int>();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            foreach (int child in new[] { NodeLeft[i], NodeRight[i] })
                if (child >= 0 && visited.Add(child)) queue.Enqueue(child);
        }
        if (visited.Count != n) { reason = $"仅根可达 {visited.Count}/{n} 个节点"; return false; }
        return true;
    }

    /// <summary>根目录缺卡时的代码兜底：一棵最小合法二叉树，保证界面永不因缺资源崩。</summary>
    public static UpgradeTreeDef CreateFallback(string spiritId, string displayName) => new()
    {
        SpiritId = spiritId,
        DisplayName = displayName,
        NodeIds = new[] { "root" },
        NodeNames = new[] { "体魄" },
        NodeHpBonus = new[] { 10 },
        NodeCost = new[] { 30 },
        NodeLeft = new[] { -1 },
        NodeRight = new[] { -1 },
        MaxLevelPerNode = 3,
    };
}
