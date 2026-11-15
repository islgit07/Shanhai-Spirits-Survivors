using Godot;
using ShanhaiSpiritsSurvivors;

/// <summary>
/// MAP-005 多种子回归探针（临时验证用，不进游戏场景）。
/// Run: godot --headless --path . --script res://tools/MapSeedProbe.cs
/// 直接按多个种子 Build 地图，检查 80% 上下净空达标线不是只对固定种子成立。
/// </summary>
public partial class MapSeedProbe : SceneTree
{
    public override void _Initialize()
    {
        int[] seeds = { 20261113, 1, 7, 42, 1234, 99991, 20261115, 777, 31337, 88888 };
        int fail = 0;
        foreach (int seed in seeds)
        {
            var field = new BlockField();
            field.Build(seed);
            float ratio = field.VerticalClearRatio;
            bool ok = ratio >= GameDefs.BlockVerticalClearRatio
                      && field.IsGroundContinuous()
                      && field.OneWayCount > 0;
            if (!ok) fail++;
            GD.Print($"[MAP_PROBE] seed={seed} blocks={field.BlockCount} " +
                     $"vertical_clear={field.VerticalClearCount}/{field.VerticalClearTotal} " +
                     $"({ratio:P1}) pruned={field.VerticalClearPruned} " +
                     $"oneway={field.OneWayCount} ok={(ok ? 1 : 0)}");
            field.Free();
        }
        GD.Print($"[MAP_PROBE] total={seeds.Length} fail={fail}");
        Quit(fail == 0 ? 0 : 1);
    }
}
