using System;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 开始页（主菜单）：键鼠双通道。
/// 鼠标点击按钮；或 ↑/↓ 切换焦点、Enter/Space 确认（走 Godot 内置 ui_* 动作，不新增映射）。
/// 四个按钮：开始 / 设置 / 图鉴 / 退出；设置与图鉴尚未实装，先打日志占位。
/// --smoke 时直接进 Main.tscn，避免无头冒烟停在开始页挂死。
/// 节点、焦点链与按键说明在 StartPage.tscn 里摆好，本脚本只负责连信号、跳转与版本号。
/// </summary>
public partial class StartPage : Control
{
    /// <summary>「开始」按钮跳转到的游戏场景。</summary>
    [Export] private string _gameScenePath = "res://scenes/Main.tscn";

    private Button[] _buttons = null!;

    public override void _Ready()
    {
        _buttons = new[]
        {
            GetNode<Button>("Start"), GetNode<Button>("Setting"),
            GetNode<Button>("Library"), GetNode<Button>("Exit"),
        };

        _buttons[0].Pressed += OnStart;
        _buttons[1].Pressed += OnSetting;
        _buttons[2].Pressed += OnLibrary;
        _buttons[3].Pressed += OnExit;

        // 上下焦点链（首尾循环），让键盘导航不依赖几何推断。
        int n = _buttons.Length;
        for (int i = 0; i < n; i++)
        {
            _buttons[i].FocusMode = Control.FocusModeEnum.All;
            _buttons[i].FocusNeighborTop = _buttons[(i + n - 1) % n].GetPath();
            _buttons[i].FocusNeighborBottom = _buttons[(i + 1) % n].GetPath();
        }

        // 版本号 Label：拼上 project.godot 里的 application/config/version
        var version = ProjectSettings.GetSetting("application/config/version", "dev").AsString();
        if (string.IsNullOrEmpty(version)) version = "dev";
        GetNode<Label>("Label").Text = $"版本号：{version}";

        // 仅 --smoke 生效：开始页自动进游戏，让 GameRoot 接管无头冒烟并自行退出。
        // 延迟一帧：_Ready 期间场景树正在 add/remove children，直接切场景会让新场景的
        // BlockField.Build 撞上 "Parent node is busy adding/removing children"。
        if (IsSmokeRequested())
        {
            CallDeferred(new StringName(nameof(OnStart)));
            return;
        }

        _buttons[0].GrabFocus();
    }

    private static bool IsSmokeRequested() =>
        Array.IndexOf(OS.GetCmdlineArgs(), "--smoke") >= 0 ||
        Array.IndexOf(OS.GetCmdlineUserArgs(), "--smoke") >= 0;

    private void OnStart()
    {
        GetTree().ChangeSceneToFile(_gameScenePath);
    }

    private void OnSetting()
    {
        // TODO: 设置页实装后改为 GetTree().ChangeSceneToFile("res://scenes/Setting.tscn");
        GD.Print("[START] 设置页尚未实装");
    }

    private void OnLibrary()
    {
        // TODO: 图鉴页实装后改为 GetTree().ChangeSceneToFile("res://scenes/Library.tscn");
        GD.Print("[START] 图鉴页尚未实装");
    }

    private void OnExit()
    {
        GetTree().Quit();
    }
}
