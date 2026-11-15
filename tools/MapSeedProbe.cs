using Godot;
using ShanhaiSpiritsSurvivors;

/// <summary>
/// 多种子回归探针（临时验证用，不进游戏场景）。
/// Run: godot --headless --path . --script res://tools/MapSeedProbe.cs
/// 按多个种子 Build 地图，检查 MAP-005~008 的达标线不是只对固定种子成立；
/// MAP-009 追加「粘液块数量 / 分组计数」以及移动内核黏跳纯逻辑的确定性断言；
/// BUGFIX-FRAGILE 追加「脆弱木板感应区接线数 == 生成数、Build 后无残留碎块」。
/// </summary>
public partial class MapSeedProbe : SceneTree
{
    public override void _Initialize()
    {
        int fail = 0;

        // MAP-009 纯逻辑：与 Step / 冒烟共用 PlatformMover.SimulateSlimeHop 单份黏跳模型。
        MoveTuning tuning = MoveTuning.GameDefaults();
        PlatformMover.SimulateSlimeHop(tuning, 1.0f / 60.0f, out float apex, out bool snapped);
        float slimeTarget = PlatformMover.ComputeSlimeTargetSpeed(1.0f, tuning);
        bool slowOk = Mathf.IsEqualApprox(slimeTarget, tuning.MoveSpeed * tuning.SlimeSpeedMul)
                      && slimeTarget < tuning.MoveSpeed * 0.5f;
        bool hopOk = apex > 0.0f && apex <= GameDefs.SlimeHopApexMax && snapped;
        if (!slowOk || !hopOk) fail++;
        GD.Print($"[SLIME_PROBE] target_speed={slimeTarget:F2} (normal={tuning.MoveSpeed:F2}) " +
                 $"apex={apex:F3} (max={GameDefs.SlimeHopApexMax:F2}) snapped={snapped} " +
                 $"slow_ok={(slowOk ? 1 : 0)} hop_ok={(hopOk ? 1 : 0)}");

        int[] seeds = { 20261113, 1, 7, 42, 1234, 99991, 20261115, 777, 31337, 88888 };
        foreach (int seed in seeds)
        {
            var field = new BlockField();
            field.Build(seed);
            float ratio = field.VerticalClearRatio;
            bool slimeOk = field.SlimeCount >= 1
                           && field.SlimeCount <= GameDefs.BlockSlimeMaxCount
                           && field.SlimeGroupedCount == field.SlimeCount;   // 分组数 == 类型数才算内核认得出
            // BUGFIX-FRAGILE：每块脆弱木板都必须成功接上 Touch.BodyEntered，否则踩上去不会碎。
            bool fragileOk = field.FragileTouchWiredCount == field.FragileCount
                             && field.FragileBrokenCount == 0;
            bool ok = ratio >= GameDefs.BlockVerticalClearRatio
                      && field.SingleBlockRatio <= GameDefs.BlockSingleRatioMax
                      && field.HasPathToTop
                      && field.ReachablePathCount >= GameDefs.BackbonePathMin
                      && field.PathCount >= GameDefs.BackbonePathMin
                      && field.PathCount <= GameDefs.BackbonePathMax
                      && field.IsGroundContinuous()
                      && field.OneWayCount > 0
                      && slimeOk
                      && fragileOk;
            if (!ok) fail++;
            GD.Print($"[MAP_PROBE] seed={seed} blocks={field.BlockCount} " +
                     $"vertical_clear={field.VerticalClearCount}/{field.VerticalClearTotal} ({ratio:P1}) " +
                     $"singles={field.SingleBlockCount}/{field.VerticalClearTotal} ({field.SingleBlockRatio:P1}) " +
                     $"top={field.MaxReachableRow} path={field.HasPathToTop} " +
                     $"paths={field.ReachablePathCount}/{field.PathCount} " +
                     $"pruned={field.VerticalClearPruned} oneway={field.OneWayCount} " +
                     $"slime={field.SlimeCount}/{field.SlimeGroupedCount} slime_ok={(slimeOk ? 1 : 0)} " +
                     $"fragile={field.FragileCount}/{field.FragileTouchWiredCount} fragile_ok={(fragileOk ? 1 : 0)} " +
                     $"ok={(ok ? 1 : 0)}");
            field.Free();
        }
        GD.Print($"[MAP_PROBE] total={seeds.Length} fail={fail}");
        Quit(fail == 0 ? 0 : 1);
    }
}
