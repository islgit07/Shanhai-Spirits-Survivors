using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 养成界面（v0.0005 随从系统第 3 块）：本轮只做基础功能，美术以后美化。
///
/// 三本命页签（数据来自 SpiritCatalog，加随从零代码）→ 左「主角升级树」+ 右「随从局外养成」。
/// 主角树：UpgradeCatalog 按 SpiritId 取定义卡，递归按二叉树层级缩进生成按钮；
///   点节点 = 花魂魄加 1 级（扣魂 + 存档），上限 / 前置由 UpgradeTreeDef 判定。
/// 随从养成：等级 / 血量 / 攻击 / 敏捷四行，每行花 GrowthStepCost 魂魄加一级，写进存档。
/// 魂魄栏常驻顶部：怪物掉落还没做（第五块），本轮靠初始 300 + 加魂按钮演示闭环。
///
/// 所有变动立即 SaveGame.Save()（单槽自动存）。--smoke 自动跑一组确定性操作并输出 JSON 证据。
/// </summary>
public partial class UpgradeScreen : Control
{
    private const string StartScenePath = "res://scenes/StartPage.tscn";

    private Label _soulsLabel = null!;
    private HBoxContainer _tabs = null!;
    private VBoxContainer _heroPanel = null!;
    private VBoxContainer _growthPanel = null!;
    private Label _hint = null!;

    private IReadOnlyList<SpiritDef> _spirits = Array.Empty<SpiritDef>();
    private Button[] _tabButtons = Array.Empty<Button>();
    private int _activeTab;

    public override void _Ready()
    {
        _soulsLabel = GetNode<Label>("Souls");
        _tabs = GetNode<HBoxContainer>("Tabs");
        _heroPanel = GetNode<VBoxContainer>("HeroPanel");
        _growthPanel = GetNode<VBoxContainer>("GrowthPanel");
        _hint = GetNode<Label>("Hint");

        StyleStaticText();
        _spirits = SpiritCatalog.All;
        BuildTabs();
        GetNode<Button>("Back").Pressed += OnBack;

        if (IsSmokeRequested())
        {
            CallDeferred(new StringName(nameof(RunSmoke)));
            return;
        }

        SelectTab(0);
    }

    private static bool IsSmokeRequested() =>
        Array.IndexOf(OS.GetCmdlineArgs(), "--smoke") >= 0 ||
        Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0;

    private void StyleStaticText()
    {
        Style(GetNode<Label>("Title"), 40, UiSkin.Gold);
        Style(_soulsLabel, 26, UiSkin.Gold);
        Style(_hint, 16, UiSkin.Blue);
    }

    private static void Style(Label label, int size, Color color)
    {
        label.AddThemeFontOverride("font", UiSkin.CjkFont());
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
    }

    // ---------------------------------------------------------------- 页签
    private void BuildTabs()
    {
        _tabButtons = new Button[_spirits.Count];
        for (int i = 0; i < _spirits.Count; i++)
        {
            SpiritDef def = _spirits[i];
            int index = i;   // 闭包稳定副本
            var btn = UiSkin.MakeButton(def.DisplayName, 22);
            btn.CustomMinimumSize = new Vector2(180f, 48f);
            btn.FocusMode = Control.FocusModeEnum.All;
            btn.Pressed += () => SelectTab(index);
            _tabs.AddChild(btn);
            _tabButtons[i] = btn;
        }
        for (int i = 0; i < _tabButtons.Length; i++)
            _tabButtons[i].FocusNeighborRight = _tabButtons[(i + 1) % Math.Max(1, _tabButtons.Length)].GetPath();
        for (int i = 0; i < _tabButtons.Length; i++)
            _tabButtons[i].FocusNeighborLeft = _tabButtons[(i + _tabButtons.Length - 1) % Math.Max(1, _tabButtons.Length)].GetPath();
    }

    private void SelectTab(int index)
    {
        if (_spirits.Count == 0) return;
        _activeTab = Mathf.Clamp(index, 0, _spirits.Count - 1);
        for (int i = 0; i < _tabButtons.Length; i++)
            _tabButtons[i].Modulate = i == _activeTab ? Colors.White : new Color(0.62f, 0.64f, 0.72f);
        Refresh();
        if (_tabButtons.Length > 0) _tabButtons[_activeTab].GrabFocus();
    }

    // ---------------------------------------------------------------- 面板刷新
    private void Refresh()
    {
        ClearChildren(_heroPanel);
        ClearChildren(_growthPanel);
        _soulsLabel.Text = $"魂魄：{SaveGame.Data.Souls}";
        if (_spirits.Count == 0)
        {
            _hint.Text = "没有随从定义卡，请检查 resources/spirits/";
            return;
        }

        SpiritDef def = _spirits[_activeTab];
        _hint.Text = $"当前本命：{def.DisplayName} · 每点消耗魂魄，升级后自动存档（W/S/A/D 选择，Enter/A 确认，Esc/B 返回）";
        BuildHeroTree(def);
        BuildGrowthRows(def);
        GD.Print($"[UPGRADE] refresh id={def.Id} souls={SaveGame.Data.Souls}");
    }

    private static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren()) child.QueueFree();
    }

    private void BuildHeroTree(SpiritDef def)
    {
        AddSectionTitle(_heroPanel, $"主角升级树 · {def.DisplayName}");
        UpgradeTreeDef tree = UpgradeCatalog.Tree(def.Id);
        if (!tree.IsValid(out string reason))
        {
            AddNote(_heroPanel, $"升级树配置异常：{reason}（已用默认树展示）");
            tree = UpgradeTreeDef.CreateFallback(def.Id, def.DisplayName);
        }

        var depth = new int[tree.NodeCount];
        for (int i = 0; i < tree.NodeCount; i++) depth[i] = DepthOf(tree, i);

        // 按层级排序，同层按数组下标：视觉上呈现二叉树的「从上到下、同层并列」。
        foreach (int i in Enumerable.Range(0, tree.NodeCount).OrderBy(k => depth[k]).ThenBy(k => k))
        {
            string id = tree.NodeIds[i];
            int level = SaveGame.NodeLevel(def.Id, id);
            bool unlocked = tree.IsUnlocked(i);
            bool maxed = tree.IsMaxed(i);
            int cost = tree.NodeCost[i];
            bool affordable = SaveGame.Data.Souls >= cost;

            string prefix = depth[i] == 0 ? "◆ " : new string('　', Math.Min(depth[i], 5)) + "├ ";
            string state = maxed ? "（已满）" : !unlocked ? "（需前置）" : !affordable ? "（魂魄不足）" : "";
            string text = $"{prefix}{tree.NodeNames[i]}　Lv {level}/{tree.MaxLevelPerNode}　+{tree.NodeHpBonus[i]}血　{cost}魂 {state}";

            var btn = UiSkin.MakeButton(text, 17);
            btn.FocusMode = Control.FocusModeEnum.All;
            btn.Disabled = maxed || !unlocked || !affordable;
            btn.Modulate = def.Tint;
            int nodeIndex = i;
            btn.Pressed += () => OnHeroNodePressed(def, tree, nodeIndex, cost);
            _heroPanel.AddChild(btn);
        }
    }

    private static int DepthOf(UpgradeTreeDef tree, int index)
    {
        int depth = 0;
        int parent = tree.ParentOf(index);
        while (parent >= 0 && depth < 16)
        {
            depth++;
            parent = tree.ParentOf(parent);
        }
        return depth;
    }

    private void BuildGrowthRows(SpiritDef def)
    {
        AddSectionTitle(_growthPanel, $"随从局外养成 · {def.DisplayName}");
        SpiritGrowth growth = SaveGame.Growth(def.Id);
        foreach (GrowthStat stat in Enum.GetValues<GrowthStat>())
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);

            int stepCount = growth.StepCount(stat);
            bool maxed = stepCount >= GameDefs.GrowthMaxStep;
            var label = UiSkin.MakeLabel(
                $"{growth.Label(stat)}：{growth.Value(stat)}（已加 {stepCount}/{GameDefs.GrowthMaxStep}）", 18, UiSkin.Text);
            label.CustomMinimumSize = new Vector2(300f, 44f);
            row.AddChild(label);

            var btn = UiSkin.MakeButton(maxed ? "已满" : $"+1（{GameDefs.GrowthStepCost}魂）", 17);
            btn.CustomMinimumSize = new Vector2(150f, 44f);
            btn.FocusMode = Control.FocusModeEnum.All;
            btn.Disabled = maxed || SaveGame.Data.Souls < GameDefs.GrowthStepCost;
            btn.Pressed += () => OnGrowthPressed(def, stat);
            row.AddChild(btn);
            _growthPanel.AddChild(row);
        }

        AddNote(_growthPanel, "局外成长继承存档，本轮只在局外生效；局内应用与吸血鬼幸存者式领取/合成留后续块。");
    }

    private void AddSectionTitle(VBoxContainer panel, string text)
    {
        var label = UiSkin.MakeLabel(text, 22, UiSkin.Gold);
        label.CustomMinimumSize = new Vector2(0f, 40f);
        panel.AddChild(label);
    }

    private void AddNote(VBoxContainer panel, string text)
    {
        var label = UiSkin.MakeLabel(text, 14, UiSkin.Blue);
        label.AutowrapMode = TextServer.AutowrapMode.Word;
        label.CustomMinimumSize = new Vector2(0f, 52f);
        panel.AddChild(label);
    }

    // ---------------------------------------------------------------- 交互
    private void OnHeroNodePressed(SpiritDef def, UpgradeTreeDef tree, int nodeIndex, int cost)
    {
        if (nodeIndex < 0 || nodeIndex >= tree.NodeCount) return;
        string nodeId = tree.NodeIds[nodeIndex];
        if (tree.IsMaxed(nodeIndex) || !tree.IsUnlocked(nodeIndex)) return;
        if (SaveGame.TryUpgradeNode(def.Id, nodeId, cost))
            GD.Print($"[UPGRADE] hero node id={def.Id} node={nodeId} level={SaveGame.NodeLevel(def.Id, nodeId)}");
        Refresh();
    }

    private void OnGrowthPressed(SpiritDef def, GrowthStat stat)
    {
        if (SaveGame.TryUpgradeGrowth(def.Id, stat))
            GD.Print($"[UPGRADE] growth id={def.Id} stat={stat} value={SaveGame.Growth(def.Id).Value(stat)}");
        Refresh();
    }

    private void OnBack() => GetTree().ChangeSceneToFile(StartScenePath);

    // ---------------------------------------------------------------- 无头冒烟
    private void RunSmoke()
    {
        SaveGame.ResetToDefault();          // 确定起点：默认魂魄 + 全零加点
        int soulsBefore = SaveGame.Data.Souls;
        int treeCount = UpgradeCatalog.All.Count;
        bool treesValid = treeCount >= 3 && _spirits.Count >= 3
            && _spirits.All(s => UpgradeCatalog.Tree(s.Id).IsValid(out _));

        SelectTab(0);
        SpiritDef def = _spirits[0];
        UpgradeTreeDef tree = UpgradeCatalog.Tree(def.Id);
        int cost = tree.NodeCost[0];
        bool heroUp = SaveGame.TryUpgradeNode(def.Id, tree.NodeIds[0], cost);
        int heroLevel = SaveGame.NodeLevel(def.Id, tree.NodeIds[0]);
        int afterHero = SaveGame.Data.Souls;

        bool growthUp = SaveGame.TryUpgradeGrowth(def.Id, GrowthStat.Level);
        int growthLevel = SaveGame.Growth(def.Id).Level;
        int afterGrowth = SaveGame.Data.Souls;

        // 写盘 → 丢缓存 → 重读：验证「单槽自动存」真的落盘，不是只在内存里加。
        SaveGame.Save();
        SaveGame.ResetCache();
        var reloaded = SaveGame.LoadOrCreate();
        int reloadedHero = SaveGame.NodeLevel(def.Id, tree.NodeIds[0]);
        int reloadedGrowth = SaveGame.Growth(def.Id).Level;

        var assertions = new Dictionary<string, bool>
        {
            ["upgrade_trees_ok"] = treesValid,
            ["souls_initial_ok"] = soulsBefore == GameDefs.InitialSouls,
            ["hero_node_upgrade_ok"] = heroUp && heroLevel == 1
                                      && afterHero == soulsBefore - cost,
            ["growth_upgrade_ok"] = growthUp
                                    && growthLevel == GameDefs.GrowthBaseLevel + GameDefs.GrowthStepLevel
                                    && afterGrowth == afterHero - GameDefs.GrowthStepCost,
            ["save_roundtrip_ok"] = reloaded.Souls == afterGrowth
                                    && reloadedHero == heroLevel
                                    && reloadedGrowth == growthLevel,
            ["ui_built_ok"] = _tabButtons.Length == _spirits.Count
                              && _heroPanel.GetChildCount() > 1
                              && _growthPanel.GetChildCount() >= 2,
        };
        bool ok = assertions.Values.All(v => v);
        var report = new Dictionary<string, object>
        {
            ["project"] = "山海幻灵",
            ["version"] = "0.0005",
            ["scene"] = "UpgradeScreen",
            ["result"] = ok ? "PASS" : "FAIL",
            ["souls_before"] = soulsBefore,
            ["souls_after"] = afterGrowth,
            ["tree_count"] = treeCount,
            ["tree_ids"] = string.Join(",", UpgradeCatalog.All.Keys.OrderBy(k => k, StringComparer.Ordinal)),
            ["upgrade_spirit"] = def.Id,
            ["hero_node"] = tree.NodeIds[0],
            ["hero_level"] = heroLevel,
            ["growth_level"] = growthLevel,
            ["reloaded_hero"] = reloadedHero,
            ["reloaded_growth"] = reloadedGrowth,
            ["assertions"] = assertions,
        };
        GD.Print("UPGRADE_SMOKE_JSON_BEGIN");
        GD.Print(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("UPGRADE_SMOKE_JSON_END");
        GD.Print($"[UPGRADE] smoke result={(ok ? "PASS" : "FAIL")}");
        // 冒烟不该污染玩家存档：验证完往返后复位到干净初始档（300 魂、零加点）。
        SaveGame.ResetToDefault();
        GD.Print($"[UPGRADE] smoke save reset → souls={SaveGame.Data.Souls}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
