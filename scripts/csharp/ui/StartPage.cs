using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// 开始页（主菜单）。
/// 四个按钮：开始 / 设置 / 图鉴 / 退出。
/// 开始 → 进入游戏（Main.tscn）；退出 → 关闭游戏；设置 / 图鉴 → 尚未实装，先打日志占位。
/// 节点在场景里手动摆好，本脚本只负责连信号 + 跳转，不改样式。
/// </summary>
public partial class StartPage : Control
{
    /// <summary>「开始」按钮跳转到的游戏场景。</summary>
    [Export] private string _gameScenePath = "res://scenes/Main.tscn";

    public override void _Ready()
    {
        GetNode<Button>("Start").Pressed += OnStart;
        GetNode<Button>("Setting").Pressed += OnSetting;
        GetNode<Button>("Library").Pressed += OnLibrary;
        GetNode<Button>("Exit").Pressed += OnExit;

        // 版本号 Label：拼上 project.godot 里的 application/config/version
        var version = ProjectSettings.GetSetting("application/config/version", "dev").AsString();
        if (string.IsNullOrEmpty(version)) version = "dev";
        GetNode<Label>("Label").Text = $"版本号：{version}";
    }

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
