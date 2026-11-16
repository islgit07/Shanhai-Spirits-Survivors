using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace PAUSE-001 局内暂停菜单小插曲（v0.0005）：ESC 呼出 / 收起，三个按钮：继续 / 设置 / 退出。
///
/// 场景驱动：PauseMenu.tscn 摆版式（CanvasLayer + 遮罩 + 居中面板），本脚本只管状态与接线。
/// 输入走 _UnhandledInput 的原生 ESC 键（离散动作）；不新增 Input Map action，
/// 因为 project.godot 的用户改动未提交，手柄 / 手柄 Start 键映射留到后续统一做。
/// 暂停时全树停摆，故 CanvasLayer 的 ProcessMode = Always，菜单仍能收 UI 输入。
/// 「设置」本轮只有入口与占位页（不擅自加音量/分辨率等功能）；「退出」回开始页，不关窗。
/// --pause-smoke 自动跑开/关/设置/返回/ESC/退出目标一组断言并输出 PAUSE_SMOKE_JSON。
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    private const string StartScenePath = "res://scenes/StartPage.tscn";

    private Control _root = null!;
    private VBoxContainer _menu = null!;
    private VBoxContainer _settings = null!;
    private Button _resumeButton = null!;
    private Button _settingsButton = null!;
    private Button _quitButton = null!;
    private Button _settingsBackButton = null!;
    private bool _settingsOpen;

    public bool IsOpen => IsInstanceValid(_root) && _root.Visible;
    public bool SettingsOpen => _settingsOpen;

    public override void _Ready()
    {
        // 暂停期间仍处理输入；子 Control 继承本节点的 ProcessMode。
        ProcessMode = ProcessModeEnum.Always;

        // 逐层下钻，避免一条深层路径因版式调整而整体失效。
        _root = GetNode<Control>("PauseRoot");
        VBoxContainer vbox = _root.GetNode<CenterContainer>("Center")
                                  .GetNode<PanelContainer>("Panel")
                                  .GetNode<VBoxContainer>("VBox");
        _menu = vbox.GetNode<VBoxContainer>("Menu");
        _settings = vbox.GetNode<VBoxContainer>("Settings");
        _resumeButton = _menu.GetNode<Button>("ResumeButton");
        _settingsButton = _menu.GetNode<Button>("SettingsButton");
        _quitButton = _menu.GetNode<Button>("QuitButton");
        _settingsBackButton = _settings.GetNode<Button>("BackButton");

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

    public override void _UnhandledInput(InputEvent @event)
    {
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
    private static bool IsPauseSmokeRequested() =>
        Array.IndexOf(OS.GetCmdlineArgs(), "--pause-smoke") >= 0 ||
        Array.IndexOf(OS.GetCmdlineUserArgs(), "--pause-smoke") >= 0;

    private void RunSmoke()
    {
        bool initialOk = !IsOpen && !GetTree().Paused;
        Open();
        bool openOk = IsOpen && GetTree().Paused && _menu.Visible && !_settings.Visible
                      && _resumeButton.Visible && _settingsButton.Visible && _quitButton.Visible;
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

        bool focusOk = _resumeButton.FocusMode == Control.FocusModeEnum.All
                       && _settingsButton.FocusMode == Control.FocusModeEnum.All
                       && _quitButton.FocusMode == Control.FocusModeEnum.All
                       && _settingsBackButton.FocusMode == Control.FocusModeEnum.All;
        bool quitTargetOk = FileAccess.FileExists(StartScenePath);

        var assertions = new Dictionary<string, bool>
        {
            ["initial_hidden"] = initialOk,
            ["esc_open"] = openOk,
            ["settings_page"] = settingsOk,
            ["settings_back"] = backOk,
            ["resume"] = resumeOk,
            ["esc_toggle"] = escOpenOk && escCloseOk,
            ["esc_event_filter"] = escEventOpen && escReleaseIgnored && escEventClose,
            ["focus_ready"] = focusOk,
            ["quit_to_start_target"] = quitTargetOk,
        };
        bool ok = assertions.Values.All(v => v);
        var report = new Dictionary<string, object>
        {
            ["project"] = "山海幻灵",
            ["version"] = "0.0005",
            ["scene"] = "PauseMenu",
            ["result"] = ok ? "PASS" : "FAIL",
            ["buttons"] = "继续/设置/退出",
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
