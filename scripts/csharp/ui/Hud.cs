using System;
using System.Collections.Generic;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace HUD-001 局内 HUD：灵韵进度 / HP / 军团 / 唤灵三选一（含献祭成本）/ 营地 / 结算。
/// 唤灵面板是 BM 式设计的核心界面：每次升级都是一次构筑决策，必须把献祭成本显式画出来。
/// </summary>
public partial class Hud : CanvasLayer
{
    private Control _root = null!;
    private Label _top = null!, _legion = null!, _coins = null!, _kills = null!, _hint = null!;
    private Label _deadText = null!, _summonTitle = null!;
    private ProgressBar _hpBar = null!, _essenceBar = null!;
    private Control _dayPanel = null!, _nightPanel = null!, _summonPanel = null!, _deadPanel = null!;
    private readonly List<Button> _offerButtons = new();
    private readonly List<Button> _campButtons = new();
    private IReadOnlyList<Offer> _offers = Array.Empty<Offer>();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Build();
        Signals.OffersPresented += OnOffers;
        Signals.RunEnded += OnRunEnded;
        Signals.Hint += OnHint;
        Signals.StructuresChanged += OnStructures;
    }

    public override void _ExitTree()
    {
        Signals.OffersPresented -= OnOffers;
        Signals.RunEnded -= OnRunEnded;
        Signals.Hint -= OnHint;
        Signals.StructuresChanged -= OnStructures;
    }

    private void Build()
    {
        _root = new Control();
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var bar = new ColorRect
        {
            Color = new Color(0.06f, 0.07f, 0.12f, 0.86f),
            Position = Vector2.Zero,
            Size = new Vector2(1280, 96),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(bar);

        _top = UiSkin.MakeLabel("", 22, UiSkin.Gold);
        _top.Position = new Vector2(24, 6);
        _top.Size = new Vector2(700, 38);
        _root.AddChild(_top);

        _hpBar = UiSkin.MakeBar(new Vector2(24, 52), new Vector2(240, 18), UiSkin.Crimson);
        _root.AddChild(_hpBar);
        _essenceBar = UiSkin.MakeBar(new Vector2(284, 52), new Vector2(240, 18), UiSkin.Jade);
        _root.AddChild(_essenceBar);

        _legion = UiSkin.MakeLabel("", 16, UiSkin.Jade);
        _legion.Position = new Vector2(540, 40);
        _legion.Size = new Vector2(380, 44);
        _root.AddChild(_legion);
        _coins = UiSkin.MakeLabel("", 17, UiSkin.Gold);
        _coins.Position = new Vector2(930, 8);
        _root.AddChild(_coins);
        _kills = UiSkin.MakeLabel("", 16, UiSkin.Text);
        _kills.Position = new Vector2(930, 44);
        _root.AddChild(_kills);

        _hint = UiSkin.MakeLabel("", 15, UiSkin.Jade);
        _hint.Position = new Vector2(24, 100);
        _hint.Size = new Vector2(1230, 28);
        _root.AddChild(_hint);

        BuildDay();
        BuildNight();
        BuildSummon();
        BuildDead();
    }

    private Control MakePanel() => new()
    {
        Position = Vector2.Zero,
        Size = new Vector2(1280, 720),
    };

    private void BuildDay()
    {
        _dayPanel = MakePanel();
        _root.AddChild(_dayPanel);
        _dayPanel.AddChild(UiSkin.MakePanel(new Rect2(120, 170, 1040, 400),
            new Color(0.10f, 0.12f, 0.18f, 0.90f)));
        var t = UiSkin.MakeLabel("山海谷 · 营地（白天 20 秒）　按 1~6 或点击购置　Enter 提前入夜", 21, UiSkin.Gold);
        t.Position = new Vector2(150, 184);
        _dayPanel.AddChild(t);
        for (int i = 0; i < GameDefs.CampItems.Length; i++)
        {
            CampItem it = GameDefs.CampItems[i];
            var b = UiSkin.MakeButton($"{i + 1}  {it.Name}   {it.Price} 贝币   {it.Desc}", 16);
            b.Position = new Vector2(150 + (i % 2) * 500, 232 + (i / 2) * 62);
            b.Size = new Vector2(470, 48);
            int idx = i;
            b.Pressed += () => GameRoot.Instance?.BuyCamp(idx);
            _dayPanel.AddChild(b);
            _campButtons.Add(b);
        }
        var s = UiSkin.MakeLabel(
            "灵韵既是经验也是唤灵阀门：降伏野生幻兽掉落灵韵，累积跨阈值立即触发唤灵，不必等天亮。",
            15, UiSkin.Text);
        s.Position = new Vector2(150, 480);
        s.Size = new Vector2(1000, 44);
        _dayPanel.AddChild(s);
    }

    private void BuildNight()
    {
        _nightPanel = MakePanel();
        _root.AddChild(_nightPanel);
        var t = UiSkin.MakeLabel(
            "入夜 · 野生幻兽来袭　　A/D 走位　Space 跳跃　幻灵自动迎战　Esc 暂停　M 回谷",
            20, UiSkin.Blue);
        t.Position = new Vector2(0, 640);
        t.Size = new Vector2(1280, 40);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        _nightPanel.AddChild(t);
    }

    private void BuildSummon()
    {
        _summonPanel = MakePanel();
        _root.AddChild(_summonPanel);
        _summonPanel.AddChild(UiSkin.MakePanel(new Rect2(90, 150, 1100, 450),
            new Color(0.10f, 0.12f, 0.20f, 0.95f)));
        _summonTitle = UiSkin.MakeLabel("", 21, UiSkin.Gold);
        _summonTitle.Position = new Vector2(120, 164);
        _summonTitle.Size = new Vector2(1040, 36);
        _summonPanel.AddChild(_summonTitle);
        for (int i = 0; i < 4; i++)
        {
            var b = UiSkin.MakeButton("", 15);
            b.Position = new Vector2(120 + i * 262, 220);
            b.Size = new Vector2(244, 260);
            int idx = i;
            b.Pressed += () => GameRoot.Instance?.ChooseOffer(idx);
            _summonPanel.AddChild(b);
            _offerButtons.Add(b);
        }
        var tip = UiSkin.MakeLabel(
            "召唤 = 铺数量　｜　培养 = Lv1→2→3 质变　｜　融合 = 献祭换高阶（成本已在卡面列出）　｜　Q 重掷　E 凝神跳过",
            15, UiSkin.Text);
        tip.Position = new Vector2(120, 510);
        tip.Size = new Vector2(1040, 40);
        _summonPanel.AddChild(tip);
    }

    private void BuildDead()
    {
        _deadPanel = MakePanel();
        _root.AddChild(_deadPanel);
        _deadPanel.AddChild(UiSkin.MakePanel(new Rect2(300, 220, 680, 280),
            new Color(0.16f, 0.08f, 0.10f, 0.95f)));
        _deadText = UiSkin.MakeLabel("", 23, UiSkin.Crimson);
        _deadText.Position = new Vector2(320, 256);
        _deadText.Size = new Vector2(640, 200);
        _deadText.HorizontalAlignment = HorizontalAlignment.Center;
        _deadText.VerticalAlignment = VerticalAlignment.Center;
        _deadPanel.AddChild(_deadText);
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _top.Text = $"第 {Run.Day} 天　｜　{PhaseLabel()}　｜　灵韵 Lv.{Run.Level}";
        _hpBar.MaxValue = Math.Max(1, Run.MaxHp);
        _hpBar.Value = Run.Hp;
        _essenceBar.MaxValue = Math.Max(1, Run.NextCost);
        _essenceBar.Value = Run.Essence;
        _legion.Text = "契灵 " + Legion.Summary();
        _coins.Text = $"贝币 {Run.Coins}　灵魄 {Run.Souls}　重掷 {Run.Rerolls}";
        _kills.Text = $"降伏 {Run.Kills}　｜　{GameDefs.Themes[GameDefs.ThemeIndex(Run.Day)].Name}";
        _dayPanel.Visible = Run.Phase == RunPhase.Day;
        _nightPanel.Visible = Run.Phase == RunPhase.Night;
        _summonPanel.Visible = Run.Phase == RunPhase.Summon;
        _deadPanel.Visible = Run.Phase == RunPhase.Dead;
    }

    private static string PhaseLabel() => Run.Phase switch
    {
        RunPhase.Day => "白天 · 营地",
        RunPhase.Night => "夜晚 · 野生幻兽",
        RunPhase.Summon => "唤灵 · 构筑抉择",
        RunPhase.Dead => "本局结束",
        _ => "主页面",
    };

    private void OnOffers(IReadOnlyList<Offer> offers)
    {
        _offers = offers;
        _summonTitle.Text = $"唤 灵 · 第 {Run.Level} 次　（{offers.Count} 选 1；同类高阶各有上限，堆单一最强不可行）";
        for (int i = 0; i < _offerButtons.Count; i++)
        {
            Button b = _offerButtons[i];
            if (i < offers.Count)
            {
                Offer o = offers[i];
                string kind = o.Kind switch
                {
                    OfferKind.Raise => "召唤", OfferKind.Bolster => "培养",
                    OfferKind.Fuse => "融合", _ => "凝神",
                };
                b.Text = $"{i + 1}　【{kind}】{o.Title}\n\n{o.Desc}\n\n{o.CostText}";
                b.Disabled = false;
                b.Visible = true;
            }
            else
            {
                b.Text = "—";
                b.Disabled = true;
                b.Visible = false;
            }
        }
    }

    private void OnRunEnded(int day, int kills, int embers)
    {
        _deadText.Text = $"本 局 结 束\n\n第 {day} 天　｜　降伏 {kills}　｜　唤灵 {Run.CardsTaken} 次\n\n获得余烬 {embers}\n\nR 重开一局　　M 返回山海谷";
    }

    private void OnHint(string text) => _hint.Text = text;
    private void OnStructures() => _hint.Text = "营地已添置：" + string.Join("、", Run.Structures.Keys);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        for (int i = 0; i < 6; i++)
        {
            if (!@event.IsActionPressed(new StringName($"slot{i + 1}"))) continue;
            if (Run.Phase == RunPhase.Day) GameRoot.Instance?.BuyCamp(i);
            else if (Run.Phase == RunPhase.Summon) GameRoot.Instance?.ChooseOffer(i);
        }
        if (@event.IsActionPressed(new StringName("reroll"))) GameRoot.Instance?.Reroll();
        if (@event.IsActionPressed(new StringName("skip"))) GameRoot.Instance?.Skip();
        if (@event.IsActionPressed(new StringName("pause"))) GameRoot.Instance?.TogglePause();
        if (@event.IsActionPressed(new StringName("start_night"))) GameRoot.Instance?.StartNightEarly();
        if (@event.IsActionPressed(new StringName("restart"))) GameRoot.Instance?.RestartRun();
        if (@event.IsActionPressed(new StringName("menu"))) GameRoot.Instance?.ReturnToMenu();
    }
}
