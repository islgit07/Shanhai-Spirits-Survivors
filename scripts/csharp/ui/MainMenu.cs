using System;
using System.Collections.Generic;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace MENU-001 主页面：山海谷设定 / 出征准备（起始契灵）/ 灵契藏阁（局外 Lore）/ 战绩。
/// Lore 只改候选池的可能性空间，不加玩家攻击力——学习 BM 的 Boneraise Lore。
/// </summary>
public partial class MainMenu : CanvasLayer
{
    private static readonly string[] StartCandidates = { "qingxiao", "chiwu", "xuanjia" };
    private static readonly StringName ConfirmKey = new("start_night");

    private Control _root = null!;
    private string _startSpecies = "qingxiao";
    private readonly List<Button> _speciesButtons = new();
    private readonly List<Button> _loreButtons = new();
    private Label _stats = null!, _hint = null!;
    private int _clearConfirm;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Build();
        Refresh();
        Signals.MetaChanged += Refresh;
    }

    public override void _ExitTree() => Signals.MetaChanged -= Refresh;

    private void Build()
    {
        _root = new Control();
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var bg = new ColorRect { Color = UiSkin.Ink };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(bg);

        var title = UiSkin.MakeLabel("山 海 幻 灵", 54, UiSkin.Gold);
        title.Position = new Vector2(0, 40);
        title.Size = new Vector2(1280, 80);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _root.AddChild(title);

        var sub = UiSkin.MakeLabel(
            "山海谷 · 幻灵师　｜　培养契灵，降伏野生幻兽　｜　幻灵与幻兽本是同一种　｜　Godot 4 · 2D · C#",
            17, UiSkin.Text);
        sub.Position = new Vector2(0, 120);
        sub.Size = new Vector2(1280, 30);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        _root.AddChild(sub);

        _root.AddChild(UiSkin.MakePanel(new Rect2(60, 170, 540, 420)));
        _root.AddChild(UiSkin.MakePanel(new Rect2(680, 170, 540, 420)));
        var h1 = UiSkin.MakeLabel("出征准备 · 选起始契灵", 22, UiSkin.Gold);
        h1.Position = new Vector2(84, 182);
        _root.AddChild(h1);
        var h2 = UiSkin.MakeLabel("灵契藏阁 · 局外传承", 22, UiSkin.Blue);
        h2.Position = new Vector2(704, 182);
        _root.AddChild(h2);

        var note = UiSkin.MakeLabel(
            "幻灵师不直接出手：走位、跳跃、拾取灵韵。\n" +
            "灵韵累积即触发「唤灵」——召唤 / 培养 / 融合，三选一构筑。\n" +
            "同一张物种表既刷出野生幻兽，也产出你的契灵。", 16, UiSkin.Jade);
        note.Position = new Vector2(84, 226);
        note.Size = new Vector2(492, 110);
        _root.AddChild(note);

        for (int i = 0; i < StartCandidates.Length; i++)
        {
            var b = UiSkin.MakeButton("", 17);
            b.Position = new Vector2(84, 344 + i * 48);
            b.Size = new Vector2(492, 40);
            int idx = i;
            b.Pressed += () => OnSpecies(idx);
            _root.AddChild(b);
            _speciesButtons.Add(b);
        }

        for (int i = 0; i < GameDefs.Lores.Length; i++)
        {
            var b = UiSkin.MakeButton("", 16);
            b.Position = new Vector2(704, 222 + i * 44);
            b.Size = new Vector2(492, 38);
            int idx = i;
            b.Pressed += () => OnLore(idx);
            _root.AddChild(b);
            _loreButtons.Add(b);
        }

        _stats = UiSkin.MakeLabel("", 16, UiSkin.Text);
        _stats.Position = new Vector2(704, 496);
        _stats.Size = new Vector2(492, 56);
        _root.AddChild(_stats);

        var clear = UiSkin.MakeButton("清空存档（需点两次确认）", 15);
        clear.Position = new Vector2(704, 548);
        clear.Size = new Vector2(492, 30);
        clear.Pressed += OnClear;
        _root.AddChild(clear);

        var start = UiSkin.MakeButton("入 山 海 谷", 24);
        start.Position = new Vector2(440, 610);
        start.Size = new Vector2(400, 56);
        start.Pressed += OnStart;
        _root.AddChild(start);

        _hint = UiSkin.MakeLabel("", 16, UiSkin.Jade);
        _hint.Position = new Vector2(0, 674);
        _hint.Size = new Vector2(1280, 28);
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _root.AddChild(_hint);
    }

    public void Refresh()
    {
        if (_speciesButtons.Count == 0) return;
        if (!Meta.UnlockedSpecies.Contains(_startSpecies)) _startSpecies = "qingxiao";
        for (int i = 0; i < StartCandidates.Length; i++)
        {
            string id = StartCandidates[i];
            SpeciesDef d = GameDefs.SpeciesById(id);
            bool owned = Meta.UnlockedSpecies.Contains(id);
            string mark = _startSpecies == id ? "◆ " : "   ";
            string tail = owned ? "已契" : $"{Meta.StartPrice(id)} 余烬解锁";
            _speciesButtons[i].Text = $"{mark}{d.Name}（{GameDefs.TierName(d.Tier)}·{d.Traits[0]}）    {tail}";
        }
        for (int i = 0; i < GameDefs.Lores.Length; i++)
        {
            LoreDef l = GameDefs.Lores[i];
            int lv = Meta.LoreLevel(l.Id);
            string price = lv >= l.MaxLevel ? "已满" : $"{l.Price(lv)} 余烬";
            _loreButtons[i].Text = $"{l.Name}  Lv.{lv}/{l.MaxLevel}    {l.Desc}    {price}";
        }
        _stats.Text = $"余烬 {Meta.Embers}　｜　最远第 {Meta.BestDay} 天　｜　累计 {Meta.TotalRuns} 局　｜　累计降伏 {Meta.TotalKills}";
        _hint.Text = "按 Enter 直接入谷　｜　鼠标点选契灵 / 传承　｜　手柄操作未在本版迁移";
    }

    private void OnSpecies(int i)
    {
        string id = StartCandidates[i];
        if (!Meta.UnlockedSpecies.Contains(id))
        {
            if (!Meta.UnlockSpecies(id))
            {
                _hint.Text = $"余烬不足，无法契得 {GameDefs.SpeciesById(id).Name}";
                return;
            }
        }
        _startSpecies = id;
        _hint.Text = "起始契灵：" + GameDefs.SpeciesById(id).Name;
        Refresh();
    }

    private void OnLore(int i)
    {
        LoreDef l = GameDefs.Lores[i];
        _hint.Text = Meta.BuyLore(l.Id) ? $"已习得 {l.Name}" : "余烬不足或已满级";
        Refresh();
    }

    private void OnClear()
    {
        _clearConfirm++;
        if (_clearConfirm >= 2)
        {
            Meta.ClearAll();
            _clearConfirm = 0;
            _startSpecies = "qingxiao";
            _hint.Text = "存档已清空";
        }
        else _hint.Text = "再点一次确认清空存档（不可撤销）";
        Refresh();
    }

    private void OnStart() => GameRoot.Instance?.StartRun(_startSpecies);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event.IsActionPressed(ConfirmKey)) OnStart();
    }
}
