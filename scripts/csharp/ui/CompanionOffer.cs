using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace RUN-ROSTER 局内「模拟升级」三选一弹窗（第四块 UI，v0.0005）。
/// 由 CompanionHud 的调试按钮打开：暂停游戏 → 抽 3 张占位卡 → 点一张 → 写 RunRoster → 恢复。
/// 卡片全是占位文案（随从 A / 等级 1），不创造技能名或属性效果。
/// 同一随从再次被选中时走合成分支（资质 +1），逻辑在 RunRoster.TryApply。
/// --offer-smoke 用固定 A/B/C 跑开启 / 领取 / 合成 / 占位文案断言并输出 OFFER_SMOKE_JSON。
/// </summary>
public partial class CompanionOffer : CanvasLayer
{
    private Control _root = null!;
    private VBoxContainer _vbox = null!;
    private HBoxContainer _cards = null!;
    private Button[] _cardButtons = Array.Empty<Button>();
    private string[] _cardIds = Array.Empty<string>();
    private bool _isOpen;

    public bool IsOpen => IsInstanceValid(_root) && _root.Visible;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;   // 暂停期间仍能点卡片

        _root = GetNode<Control>("OfferRoot");
        _vbox = _root.GetNode<CenterContainer>("Center")
                     .GetNode<PanelContainer>("Panel")
                     .GetNode<VBoxContainer>("VBox");
        _cards = _vbox.GetNode<HBoxContainer>("Cards");
        _cardButtons = new Button[3];
        for (int i = 0; i < _cardButtons.Length; i++)
        {
            int index = i;   // 闭包稳定副本
            _cardButtons[i] = _cards.GetNode<Button>($"Card{i}");
            _cardButtons[i].FocusMode = Control.FocusModeEnum.All;
            _cardButtons[i].Pressed += () => OnCardPressed(index);
        }
        WireFocus();

        _root.Visible = false;
        if (IsOfferSmokeRequested())
        {
            CallDeferred(new StringName(nameof(RunSmoke)));
            return;
        }
        GD.Print("[OFFER] ready simulated-upgrade button → 3 张占位卡");
    }

    private void WireFocus()
    {
        for (int i = 0; i < _cardButtons.Length; i++)
        {
            _cardButtons[i].FocusNeighborLeft =
                _cardButtons[(i + _cardButtons.Length - 1) % _cardButtons.Length].GetPath();
            _cardButtons[i].FocusNeighborRight =
                _cardButtons[(i + 1) % _cardButtons.Length].GetPath();
        }
    }

    /// <summary>正式入口：随机抽三张不重复的占位随从。</summary>
    public void Open()
    {
        OpenWithIds(RunRoster.PickOffer(new Random(System.Environment.TickCount)));
    }

    /// <summary>确定性入口：冒烟注入固定卡片。</summary>
    public void OpenWithIds(IReadOnlyList<string> ids)
    {
        if (_isOpen) return;
        if (GameRoot.Instance == null)
        {
            GD.PushError("[OFFER] GameRoot 不存在，无法写局内队伍");
            return;
        }

        _cardIds = ids.Take(3).ToArray();
        while (_cardIds.Length < 3)
            _cardIds = _cardIds.Concat(new[] { RunRoster.CandidateIds[0] }).ToArray();

        RunRoster roster = GameRoot.Instance.Companions;
        for (int i = 0; i < _cardButtons.Length; i++)
        {
            string id = _cardIds[i];
            string status = roster.Has(id)
                ? $"已拥有 · 资质 {roster.QualityOf(id)}（合成）"
                : "新随从";
            _cardButtons[i].Text = $"{RunRoster.NameOf(id)}\n等级 1\n{status}";
        }

        _isOpen = true;
        _root.Visible = true;
        GetTree().Paused = true;
        CallDeferred(new StringName(nameof(FocusFirstCard)));
        GD.Print($"[OFFER] open ids=[{string.Join(",", _cardIds)}] paused=true");
    }

    private void FocusFirstCard()
    {
        if (_cardButtons.Length > 0) _cardButtons[0].GrabFocus();
    }

    private void OnCardPressed(int index)
    {
        if (!_isOpen || index < 0 || index >= _cardIds.Length) return;
        string id = _cardIds[index];
        RunRoster? roster = GameRoot.Instance?.Companions;
        string message = "";
        bool ok = roster != null && roster.TryApply(id, out message);
        GD.Print($"[OFFER] choose id={id} ok={ok} msg={message} roster={roster?.Count ?? -1}");
        if (ok) GameRoot.Instance?.RefreshCompanions();   // 场上实体立刻跟上（加人 / 合成重排槽位）
        Close();
    }

    /// <summary>外部取消（例如冒烟复位）；不写队伍，只恢复游戏。</summary>
    public void Cancel() => Close();

    private void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _root.Visible = false;
        GetTree().Paused = false;
        GD.Print("[OFFER] close paused=false");
    }

    // ---------------------------------------------------------------- 无头冒烟
    private static bool IsOfferSmokeRequested() =>
        Array.IndexOf(OS.GetCmdlineArgs(), "--offer-smoke") >= 0 ||
        Array.IndexOf(OS.GetCmdlineUserArgs(), "--offer-smoke") >= 0;

    private void RunSmoke()
    {
        RunRoster? roster = GameRoot.Instance?.Companions;
        bool initialOk = !IsOpen && !GetTree().Paused;

        string[] ids = { "A", "B", "C" };
        OpenWithIds(ids);
        bool openOk = IsOpen && GetTree().Paused && _cardButtons.Length == 3
                      && _cardButtons.All(b => b.Visible && !string.IsNullOrEmpty(b.Text));
        bool textsOk = _cardButtons.Select(b => b.Text).SequenceEqual(new[]
        {
            "随从 A\n等级 1\n新随从", "随从 B\n等级 1\n新随从", "随从 C\n等级 1\n新随从",
        });
        bool placeholderOk = _cardButtons.All(b =>
            !b.Text.Contains("技能") && !b.Text.Contains("攻击") && !b.Text.Contains("伤害"));

        OnCardPressed(0);   // A 加入队伍
        bool pickNewOk = roster != null && roster.Count == 1 && roster.Has("A")
                         && !IsOpen && !GetTree().Paused;

        OpenWithIds(ids);   // 再来一次：A 应走合成
        bool mergeCardText = _cardButtons[0].Text.Contains("合成") && _cardButtons[0].Text.Contains("资质");
        OnCardPressed(0);
        bool mergeOk = roster != null && roster.Count == 1
                       && roster.Has("A") && roster.QualityOf("A") == 2
                       && !IsOpen && !GetTree().Paused;

        var assertions = new Dictionary<string, bool>
        {
            ["initial_hidden"] = initialOk,
            ["offer_open_pauses"] = openOk,
            ["three_placeholder_cards"] = textsOk && placeholderOk,
            ["pick_new_joins"] = pickNewOk,
            ["merge_card_hint"] = mergeCardText,
            ["pick_same_merges"] = mergeOk,
        };
        bool ok = assertions.Values.All(v => v);
        var report = new Dictionary<string, object>
        {
            ["project"] = "山海幻灵",
            ["version"] = "0.0005",
            ["scene"] = "CompanionOffer",
            ["result"] = ok ? "PASS" : "FAIL",
            ["roster_count"] = roster?.Count ?? -1,
            ["quality_A"] = roster?.QualityOf("A") ?? -1,
            ["assertions"] = assertions,
        };
        GD.Print("OFFER_SMOKE_JSON_BEGIN");
        GD.Print(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("OFFER_SMOKE_JSON_END");
        GD.Print($"[OFFER] smoke result={(ok ? "PASS" : "FAIL")}");
        GetTree().Paused = false;
        roster?.Clear();
        GetTree().Quit(ok ? 0 : 1);
    }
}
