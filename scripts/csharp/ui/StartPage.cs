using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 开始页（主菜单）：键鼠双通道。
/// 鼠标点击按钮；或 ↑/↓ 切换焦点、Enter/Space 确认（走 Godot 内置 ui_* 动作，不新增映射）。
/// 五个按钮：开始 / 养成 / 设置 / 图鉴 / 退出；设置与图鉴尚未实装，先打日志占位。
/// 「开始」先进本命选择页 SpiritSelect.tscn，选完再进 Main.tscn；「养成」进升级界面 UpgradeScreen.tscn。
/// --smoke 时也走这条链（选择页会自动选第一只），整条链路无头可验证。
/// 节点、焦点链与按键说明在 StartPage.tscn 里摆好，本脚本只负责连信号、跳转与版本号。
/// </summary>
public partial class StartPage : Control
{
    /// <summary>「开始」按钮先跳到本命选择页；选完由 SpiritSelect 跳进游戏场景。</summary>
    [Export] private string _gameScenePath = "res://scenes/ui/SpiritSelect.tscn";
    /// <summary>「养成」按钮跳到升级界面（v0.0005 随从系统第 3 块）。</summary>
    [Export] private string _upgradeScenePath = "res://scenes/ui/UpgradeScreen.tscn";

    private Button[] _buttons = null!;

    public override void _Ready()
    {
        _buttons = new[]
        {
            GetNode<Button>("Start"), GetNode<Button>("Upgrade"),
            GetNode<Button>("Setting"), GetNode<Button>("Library"),
            GetNode<Button>("Exit"),
        };

        _buttons[0].Pressed += OnStart;
        _buttons[1].Pressed += OnUpgrade;
        _buttons[2].Pressed += OnSetting;
        _buttons[3].Pressed += OnLibrary;
        _buttons[4].Pressed += OnExit;

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

    private static bool IsSmokeRequested() => SmokeFlags.Has("--smoke");

    private void OnStart()
    {
        GetTree().ChangeSceneToFile(_gameScenePath);
    }

    private void OnUpgrade()
    {
        GD.Print($"[START] 养成 → {_upgradeScenePath}");
        GetTree().ChangeSceneToFile(_upgradeScenePath);
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
