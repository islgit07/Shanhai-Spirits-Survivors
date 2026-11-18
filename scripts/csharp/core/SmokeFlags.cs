using System;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 无头自检的公共落点：命令行冒烟 / 截图开关的统一查询（--smoke 与各子冒烟 --follow-smoke /
/// --pause-smoke / --offer-smoke 以及 --shot；Godot 把引擎参数与用户参数分开，「--」之后为
/// user args，两类都要查）、--shot 的存图退出流程、冒烟 JSON 报告的共享序列化配置。
/// 无状态纯函数，沿用 ArtBank / Rng 的静态工具范式，零 autoload。
/// </summary>
public static class SmokeFlags
{
    /// <summary>冒烟 / 白箱 JSON 报告共用序列化配置（官方建议复用 options 实例）。</summary>
    public static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>仅 --shot 自检用：等 1.5 秒首屏渲染稳定，截屏存 user:// 后退出进程。</summary>
    public static async void Capture(Node caller, string path, string logTag)
    {
        await caller.ToSignal(caller.GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        await caller.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = caller.GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[{logTag}] shot err={err}");
        caller.GetTree().Quit(0);
    }
    public static bool Has(string flag) =>
        Array.IndexOf(OS.GetCmdlineArgs(), flag) >= 0
        || Array.IndexOf(OS.GetCmdlineUserArgs(), flag) >= 0;

    /// <summary>无头冒烟等 N 个物理帧（跟随 / 怪物冒烟共用；原 SpiritFollowGroup 私有实现收口）。</summary>
    public static async Task WaitPhysicsFrames(Node caller, int count)
    {
        for (int i = 0; i < count; i++)
        {
            // 场景可能已被切走（如冒烟中玩家死亡回开始页）：节点不在树上立即中止，
            // 否则 ToSignal 恢复时打已释放节点，冒烟永不退出（无头进程挂死）。
            if (!GodotObject.IsInstanceValid(caller) || !caller.IsInsideTree()) return;
            await caller.ToSignal(caller.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
