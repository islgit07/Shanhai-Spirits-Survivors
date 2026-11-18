using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace PAUSE-001 / RUN-ROSTER ESC 双栏面板（v0.0005）：左窄栏继续 / 设置 / 退出，右大栏当前随从队伍。
///
/// 场景驱动：PauseMenu.tscn 摆版式（CanvasLayer + 遮罩 + 近全屏双栏面板，四边留 24px 边距），本脚本只管状态与接线。
/// 输入走 _UnhandledInput 的原生 ESC 键（离散动作）；不新增 Input Map action，
/// 因为 project.godot 的用户改动未提交，手柄 / 手柄 Start 键映射留到后续统一做。
/// 暂停时全树停摆，故 CanvasLayer 的 ProcessMode = Always，菜单仍能收 UI 输入。
/// 右栏队伍读 GameRoot.Companions（当局临时，不写存档），每次打开时重建行；占位文案零技能。
/// 「设置」本轮只有入口与占位页（不擅自加音量/分辨率等功能）；「退出」回开始页，不关窗。
/// --pause-smoke 跑开/关/设置/返回/ESC/队伍刷新/退出目标断言并输出 PAUSE_SMOKE_JSON。
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private const string StartScenePath = "res://scenes/StartPage.tscn";

    private Control _root = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _menu = null!;
    private VBoxContainer _settings = null!;
    private Button _resumeButton = null!;
    private Button _settingsButton = null!;
    private Button _quitButton = null!;
    private Button _settingsBackButton = null!;
    private VBoxContainer _rosterRows = null!;
    private bool _settingsOpen;
    private CompanionOffer? _offer;   // 同场景兄弟节点，_Ready 缓存：_UnhandledInput 对每个未处理输入都会走到

    public bool IsOpen => IsInstanceValid(_root) && _root.Visible;
    public bool SettingsOpen => _settingsOpen;

    public override void _Ready()
    {
        // 暂停期间仍处理输入；子 Control 继承本节点的 ProcessMode。
        ProcessMode = ProcessModeEnum.Always;

        // 逐层下钻，避免一条深层路径因版式调整而整体失效。
        _root = GetNode<Control>("PauseRoot");
        MarginContainer center = _root.GetNode<MarginContainer>("Center");
        _panel = center.GetNode<PanelContainer>("Panel");
        HBoxContainer columns = _panel
                                     .GetNode<VBoxContainer>("VBox")
                                     .GetNode<HBoxContainer>("Columns");
        VBoxContainer left = columns.GetNode<VBoxContainer>("Left");
        _menu = left.GetNode<VBoxContainer>("Menu");
        _settings = left.GetNode<VBoxContainer>("Settings");
        _resumeButton = _menu.GetNode<Button>("ResumeButton");
        _settingsButton = _menu.GetNode<Button>("SettingsButton");
        _quitButton = _menu.GetNode<Button>("QuitButton");
        _settingsBackButton = _settings.GetNode<Button>("BackButton");
        _rosterRows = columns.GetNode<PanelContainer>("Right")
                             .GetNode<VBoxContainer>("VBox")
                             .GetNode<VBoxContainer>("RosterRows");
        _offer = GetParent().GetNodeOrNull<CompanionOffer>("CompanionOffer");

        _resumeButton.Pressed += Resume;
        _settingsButton.Pressed += OpenSettings;
        _quitButton.Pressed += QuitToStart;
        _settingsBackButton.Pressed += CloseSettings;
        WireFocus();

        _root.Visible = false;
        _menu.Visible = true;
        _settings.Visible = false;

        if (IsPauseSmokeRequested())
        {
            CallDeferred(new StringName(nameof(RunSmoke)));
            return;
        }
        GD.Print("[PAUSE] ready esc=on buttons=继续/设置/退出");
    }

    /// <summary>主菜单三按钮的上下焦点环（键盘/手柄通用）。</summary>
    private void WireFocus()
    {
        _resumeButton.FocusNeighborTop = _quitButton.GetPath();
        _resumeButton.FocusNeighborBottom = _settingsButton.GetPath();
        _settingsButton.FocusNeighborTop = _resumeButton.GetPath();
        _settingsButton.FocusNeighborBottom = _quitButton.GetPath();
        _quitButton.FocusNeighborTop = _settingsButton.GetPath();
        _quitButton.FocusNeighborBottom = _resumeButton.GetPath();
        _settingsBackButton.FocusNeighborTop = _settingsBackButton.GetPath();
        _settingsBackButton.FocusNeighborBottom = _settingsBackButton.GetPath();
    }

    /// <summary>右栏队伍：每次打开都从当局 RunRoster 重建，空位显示「空」。</summary>
    private void RefreshRoster()
    {
        foreach (Node child in _rosterRows.GetChildren())
        {
            _rosterRows.RemoveChild(child);
            child.QueueFree();
        }

        RunRoster? roster = GameRoot.Instance?.Companions;
        for (int i = 0; i < RunRoster.MaxSlots; i++)
        {
            bool filled = roster != null && i < roster.Count;
            RunCompanion? c = filled ? roster!.Members[i] : null;
            Color tint = filled ? UiSkin.Text : UiSkin.EmptySlot;
            string text = filled
                ? $"{i + 1}  {c!.DisplayName}　等级 {c.Level}　资质 {c.Quality}"
                : $"{i + 1}  空";
            Label row = UiSkin.MakeLabel(text, 18, tint);
            row.CustomMinimumSize = new Vector2(0f, 40f);
            row.MouseFilter = Control.MouseFilterEnum.Ignore;
            _rosterRows.AddChild(row);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 三选一弹窗开着时吞掉 ESC，避免暂停菜单叠在弹窗上（弹窗要求选完才恢复）。
        CompanionOffer? offer = _offer;
        if (offer != null && offer.IsOpen)
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        // UI 先消费自己的导航键；这里只接漏下来的原生 ESC。
        if (@event is not InputEventKey key || !key.Pressed || key.Echo || key.Keycode != Key.Escape)
            return;
        HandleEscape();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>ESC 的统一语义：未开→打开；设置页→退回主菜单；主菜单→继续。</summary>
    private void HandleEscape()
    {
        if (!IsOpen) Open();
        else if (_settingsOpen) CloseSettings();
        else Resume();
    }

    public void Open()
    {
        if (IsOpen) return;
        _settingsOpen = false;
        _menu.Visible = true;
        _settings.Visible = false;
        RefreshRoster();
        _root.Visible = true;
        GetTree().Paused = true;
        CallDeferred(new StringName(nameof(FocusResume)));
        GD.Print("[PAUSE] open paused=true");
    }

    public void Resume()
    {
        if (!IsOpen) return;
        _root.Visible = false;
        GetTree().Paused = false;
        GD.Print("[PAUSE] resume paused=false");
    }

    private void OpenSettings()
    {
        if (!IsOpen) return;
        _settingsOpen = true;
        _menu.Visible = false;
        _settings.Visible = true;
        CallDeferred(new StringName(nameof(FocusSettingsBack)));
        GD.Print("[PAUSE] settings open（占位页，暂无可配置项）");
    }

    private void CloseSettings()
    {
        if (!_settingsOpen) return;
        _settingsOpen = false;
        _settings.Visible = false;
        _menu.Visible = true;
        CallDeferred(new StringName(nameof(FocusResume)));
    }

    private void QuitToStart()
    {
        GetTree().Paused = false;
        _root.Visible = false;
        GD.Print($"[PAUSE] quit → {StartScenePath}");
        GetTree().ChangeSceneToFile(StartScenePath);
    }

    private void FocusResume() => _resumeButton.GrabFocus();

    private void FocusSettingsBack() => _settingsBackButton.GrabFocus();

    // ---------------------------------------------------------------- 无头冒烟
    private static bool IsPauseSmokeRequested() => SmokeFlags.Has("--pause-smoke");

    private void RunSmoke()
    {
        RunRoster? roster = GameRoot.Instance?.Companions;
        roster?.Clear();   // 冒烟从空队伍开始，跑完也清空（当局数据不跨局）
        bool initialOk = !IsOpen && !GetTree().Paused;
        Open();
        bool openOk = IsOpen && GetTree().Paused && _menu.Visible && !_settings.Visible
                      && _resumeButton.Visible && _settingsButton.Visible && _quitButton.Visible;

        // 右栏随从队列：空位 → 领取 → 同名合成，三态都要反映在 UI 行上。
        bool rosterPanelOk = _rosterRows.Visible && _rosterRows.GetChildCount() == RunRoster.MaxSlots
                             && _rosterRows.GetChild(0) is Label emptyRow && emptyRow.Text.Contains("空");
        roster?.TryApply("A", out _);
        RefreshRoster();
        bool rosterJoinOk = _rosterRows.GetChild(0) is Label joined
                            && joined.Text.Contains("随从 A") && joined.Text.Contains("资质 1");
        roster?.TryApply("A", out _);
        RefreshRoster();
        bool rosterMergeOk = _rosterRows.GetChild(0) is Label merged
                             && merged.Text.Contains("资质 2")
                             && _rosterRows.GetChildCount() == RunRoster.MaxSlots;
        roster?.Clear();
        RefreshRoster();
        OpenSettings();
        bool settingsOk = _settingsOpen && _settings.Visible && !_menu.Visible
                          && _settingsBackButton.Visible;
        CloseSettings();
        bool backOk = !_settingsOpen && _menu.Visible && !_settings.Visible
                      && IsOpen && GetTree().Paused;
        Resume();
        bool resumeOk = !IsOpen && !GetTree().Paused;

        HandleEscape();
        bool escOpenOk = IsOpen && GetTree().Paused;
        HandleEscape();
        bool escCloseOk = !IsOpen && !GetTree().Paused;

        // 真事件过滤：按下开、松开不误触、再按下关（不是只测状态机内部方法）。
        var escDown = new InputEventKey { Keycode = Key.Escape, Pressed = true };
        var escUp = new InputEventKey { Keycode = Key.Escape, Pressed = false };
        _UnhandledInput(escDown);
        bool escEventOpen = IsOpen && GetTree().Paused;
        _UnhandledInput(escUp);
        bool escReleaseIgnored = IsOpen && GetTree().Paused;
        _UnhandledInput(escDown);
        bool escEventClose = !IsOpen && !GetTree().Paused;

        // 三选一弹窗开着时 ESC 必须被吞掉，不能叠出暂停菜单。
        CompanionOffer? offer = _offer;
        offer?.OpenWithIds(new[] { "A", "B", "C" });
        _UnhandledInput(escDown);
        bool escBlockedByOffer = offer != null && offer.IsOpen && !IsOpen;
        offer?.Cancel();

        bool focusOk = _resumeButton.FocusMode == Control.FocusModeEnum.All
                       && _settingsButton.FocusMode == Control.FocusModeEnum.All
                       && _quitButton.FocusMode == Control.FocusModeEnum.All
                       && _settingsBackButton.FocusMode == Control.FocusModeEnum.All;
        bool quitTargetOk = FileAccess.FileExists(StartScenePath);

        // 近全屏走结构性验证：Center 全屏锚点 + 四边 24px，面板与右栏横向纵向 expand-fill。
        // headless 下实测像素不可靠（可见矩形报 1280x1280，且面板刚显示时布局未重排），只记入报告。
        MarginContainer centerNode = _root.GetNode<MarginContainer>("Center");
        Control.SizeFlags expandFill = Control.SizeFlags.ExpandFill;
        PanelContainer rightPanel = _panel.GetNode<PanelContainer>("VBox/Columns/Right");
        bool marginOk = centerNode.Get("theme_override_constants/margin_left").AsInt32() == 24
                        && centerNode.Get("theme_override_constants/margin_top").AsInt32() == 24
                        && centerNode.Get("theme_override_constants/margin_right").AsInt32() == 24
                        && centerNode.Get("theme_override_constants/margin_bottom").AsInt32() == 24;
        bool nearFullscreen = centerNode.AnchorLeft == 0f && centerNode.AnchorTop == 0f
                              && centerNode.AnchorRight == 1f && centerNode.AnchorBottom == 1f
                              && marginOk
                              && _panel.SizeFlagsHorizontal == expandFill
                              && _panel.SizeFlagsVertical == expandFill
                              && rightPanel.SizeFlagsHorizontal == expandFill
                              && rightPanel.SizeFlagsVertical == expandFill;

        var assertions = new Dictionary<string, bool>
        {
            ["initial_hidden"] = initialOk,
            ["esc_open"] = openOk,
            ["roster_panel"] = rosterPanelOk,
            ["roster_join"] = rosterJoinOk,
            ["roster_merge"] = rosterMergeOk,
            ["settings_page"] = settingsOk,
            ["settings_back"] = backOk,
            ["resume"] = resumeOk,
            ["esc_toggle"] = escOpenOk && escCloseOk,
            ["esc_event_filter"] = escEventOpen && escReleaseIgnored && escEventClose,
            ["esc_blocked_by_offer"] = escBlockedByOffer,
            ["focus_ready"] = focusOk,
            ["near_fullscreen"] = nearFullscreen,
            ["quit_to_start_target"] = quitTargetOk,
        };
        bool ok = assertions.Values.All(v => v);
        Vector2 headlessViewport = GetViewport()!.GetVisibleRect().Size;
        var report = new Dictionary<string, object>
        {
            ["project"] = "山海幻灵",
            ["version"] = "0.0005",
            ["scene"] = "PauseMenu",
            ["result"] = ok ? "PASS" : "FAIL",
            ["buttons"] = "继续/设置/退出（左窄栏）",
            ["right_panel"] = "当前队伍（右大栏，expand-fill）",
            ["layout"] = "近全屏：四边 24px 边距，面板随视口拉伸",
            ["headless_viewport"] = $"{headlessViewport.X:0}x{headlessViewport.Y:0}",
            ["headless_panel_size"] = $"{_panel.Size.X:0}x{_panel.Size.Y:0}",
            ["quit_target"] = StartScenePath,
            ["process_mode_always"] = (int)ProcessMode == (int)ProcessModeEnum.Always,
            ["assertions"] = assertions,
        };
        GD.Print("PAUSE_SMOKE_JSON_BEGIN");
        GD.Print(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("PAUSE_SMOKE_JSON_END");
        GD.Print($"[PAUSE] smoke result={(ok ? "PASS" : "FAIL")}");
        GetTree().Paused = false;
        GetTree().Quit(ok ? 0 : 1);
    }
}
