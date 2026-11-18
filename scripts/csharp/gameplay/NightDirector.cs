using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace NIGHT-001 昼夜波次状态机（v0.0008）。
/// 节奏循环（用户设计）：白天安静 → 玩家在篝火旁按 E 点燃（Ignite）→ 黑夜：第 k 波刷 k 只怪
/// （波内每 WaveSpawnInterval 秒一只，不断加怪；受 MonsterMaxActive 节流）→ 场上清空后
/// 进入波间长休整（WaveBreatherTime，玩家可趁机去篝火祈祷天亮）→ 下一波 → 20 波全部清完
/// = 夜深（不再刷怪，等祈祷）→ 祈祷完成（Dawn）→ 天亮：怪物全部消散成魂魄。
/// 波次结构不随夜数变化（用户指定）；夜数只做计数与升级机会。
/// 所有计时在 _PhysicsProcess 累计（零分配）；刷怪走 MonsterField 对象池。
/// 玩家死亡回开始页 = 自然重置（BeginRun 调 ResetToDay）。
/// </summary>
public partial class NightDirector : Node
{
    /// <summary>昼夜阶段。Night 细分为刷怪中 / 等清场 / 波间休整 / 夜深。</summary>
    public enum Phase { Day, Spawning, Clearing, Breather, NightDeep }

    private MonsterField? _monsters;
    private Master? _player;

    /// <summary>当前阶段。</summary>
    public Phase Current { get; private set; } = Phase.Day;

    /// <summary>当前夜数（点燃一次 +1；0 = 尚未点燃过）。</summary>
    public int Night { get; private set; }

    /// <summary>当前波数（1..NightWaveTotal）。</summary>
    public int Wave { get; private set; }

    /// <summary>本波已刷出的怪数。</summary>
    public int SpawnedInWave { get; private set; }

    /// <summary>夜开始（点燃成功后）。</summary>
    [Signal] public delegate void NightStartedEventHandler(int night);

    /// <summary>一波开始刷怪。</summary>
    [Signal] public delegate void WaveStartedEventHandler(int wave, int total);

    /// <summary>一波全部清空。</summary>
    [Signal] public delegate void WaveClearedEventHandler(int wave);

    /// <summary>祈祷完成天亮（怪物已全部消散成魂魄）。</summary>
    [Signal] public delegate void DawnCameEventHandler();

    private float _timer;
    private SoulField? _souls;

    public override void _Ready()
    {
        SetPhysicsProcess(false);
        if (IsNightSmokeRequested()) CallDeferred(new StringName(nameof(RunSmoke)));
    }

    /// <summary>绑定主角、怪物池与魂魄场（GameRoot 在地形建好后调用）。</summary>
    public void Bind(Master player, MonsterField monsters, SoulField souls)
    {
        _player = player;
        _monsters = monsters;
        _souls = souls;
    }

    /// <summary>重置为白天（每局开始 / 死亡重开时调用）。</summary>
    public void ResetToDay()
    {
        Current = Phase.Day;
        Night = 0;
        Wave = 0;
        SpawnedInWave = 0;
        _timer = 0f;
        SetPhysicsProcess(false);
    }

    /// <summary>点燃篝火：白天 → 黑夜第 1 波（非白天调用忽略）。由 Campfire 的点燃请求触发。</summary>
    public void Ignite()
    {
        if (Current != Phase.Day || _monsters == null) return;
        Night++;
        StartNextWave();
        SetPhysicsProcess(true);
        EmitSignal(SignalName.NightStarted, Night);
    }

    /// <summary>祈祷完成 → 天亮：停波次、全场怪物消散成魂魄。非黑夜调用忽略。</summary>
    public void Dawn()
    {
        if (Current == Phase.Day) return;
        Current = Phase.Day;
        Wave = 0;
        _monsters?.DissolveAll();
        SetPhysicsProcess(false);
        EmitSignal(SignalName.DawnCame);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_monsters == null || _player == null || !IsInstanceValid(_player)) return;
        float dt = (float)delta;

        switch (Current)
        {
            case Phase.Spawning:
                _timer -= dt;
                if (_timer <= 0f && _monsters.Active.Count < GameDefs.MonsterMaxActive)
                {
                    SpawnOneNearPlayer();
                    if (SpawnedInWave >= Wave)
                        Current = Phase.Clearing;   // 本波刷完 → 等场上清空
                    else
                        _timer = GameDefs.WaveSpawnInterval;
                }
                break;

            case Phase.Clearing:
                if (_monsters.Active.Count == 0)
                {
                    EmitSignal(SignalName.WaveCleared, Wave);
                    if (Wave >= GameDefs.NightWaveTotal)
                    {
                        Current = Phase.NightDeep;   // 20 波清完 = 夜深，不再刷怪，等祈祷
                    }
                    else
                    {
                        Current = Phase.Breather;   // 波间长休整：玩家可去篝火祈祷天亮
                        _timer = GameDefs.WaveBreatherTime;
                    }
                }
                break;

            case Phase.Breather:
                _timer -= dt;
                if (_timer <= 0f) StartNextWave();
                break;
        }
    }

    private void StartNextWave()
    {
        Wave++;
        SpawnedInWave = 0;
        Current = Phase.Spawning;
        _timer = 0f;   // 第一只立即刷出
        EmitSignal(SignalName.WaveStarted, Wave, GameDefs.NightWaveTotal);
    }

    private void SpawnOneNearPlayer()
    {
        if (_player == null || !IsInstanceValid(_player) || _monsters == null) return;
        float side = GD.Randf() < 0.5f ? -1f : 1f;
        float dist = (float)GD.RandRange(GameDefs.MonsterSpawnMinDist, GameDefs.MonsterSpawnMaxDist);
        Vector2 pos = new(_player.GlobalPosition.X + side * dist, GameDefs.LayerY(0) - 2f);
        _monsters.SpawnAt(pos);
        SpawnedInWave++;
    }

    // ---------------------------------------------------------------- 无头冒烟
    private static bool IsNightSmokeRequested() => SmokeFlags.Has("--night-smoke");

    private async void RunSmoke()
    {
        // 玩家死亡 / 场景切换的极端时序：任何一步炸掉都带 FAIL 退出，绝不挂死无头进程
        var tree = GetTree();
        try
        {
            await RunSmokeSteps(tree);
        }
        catch (Exception e)
        {
            GD.PushError($"[NIGHT] smoke aborted: {e.Message}");
            tree.Quit(1);
        }
    }

    private async Task RunSmokeSteps(SceneTree tree)
    {
        GameRoot? root = GameRoot.Instance;
        if (_monsters == null || _player == null || root == null)
        {
            GD.PushError("[NIGHT] night-smoke 缺少主角 / 怪物池 / GameRoot");
            tree.Quit(1);
            return;
        }

        // 1) 开局白天：无怪、篝火未点燃
        bool dayStartOk = Current == Phase.Day && Night == 0 && Wave == 0 && _monsters.Active.Count == 0;

        // 2) 点燃：玩家传送篝火旁 + 按 E（输入注入，与 GameRoot 冒烟同模式）
        _player.GlobalPosition = GameDefs.CampfirePos + new Vector2(0.4f, -0.4f);
        _player.Velocity = Vector2.Zero;
        _player.ResetMover();
        await SmokeFlags.WaitPhysicsFrames(this, 5);   // 感应区入场
        Input.ActionPress("interact");
        await SmokeFlags.WaitPhysicsFrames(this, 3);
        Input.ActionRelease("interact");
        // 波 1 只有 1 只怪：刷完立即进入 Clearing，故不锁定 Spawning 阶段
        bool igniteOk = Night == 1 && Wave == 1 && root.Campfire.Lit;

        // 3) 波 1 刷出（第一只立即刷）
        int wait1 = 0;
        while (_monsters.Active.Count == 0 && wait1 < 120) { await SmokeFlags.WaitPhysicsFrames(this, 1); wait1++; }
        bool wave1SpawnOk = _monsters.Active.Count >= 1 && Wave == 1;

        // 4) 清场（直接打空血；随从也可能补刀，ToArray 防遍历中修改）→ 波间休整 → 波 2 自动开始
        int killWait = 0;
        while (_monsters.Active.Count > 0 && killWait < 60)
        {
            foreach (Monster m in _monsters.Active.ToArray()) m.TakeDamage(m.Hp);
            await SmokeFlags.WaitPhysicsFrames(this, 1);
            killWait++;
        }
        bool wave1ClearOk = _monsters.Active.Count == 0 && Current == Phase.Breather && Wave == 1;
        int wait2 = 0;
        while (Wave < 2 && wait2 < 750) { await SmokeFlags.WaitPhysicsFrames(this, 1); wait2++; }
        bool wave2Ok = Wave == 2 && Current == Phase.Spawning;

        // 5) 清空波 2 → 进入休整长窗口（10 秒无刷怪）→ 在窗口内做祈祷测试，全程无怪干扰
        int clearWait = 0;
        while (Current != Phase.Breather && clearWait < 900)
        {
            foreach (Monster m in _monsters.Active.ToArray()) m.TakeDamage(m.Hp);
            await SmokeFlags.WaitPhysicsFrames(this, 1);
            clearWait++;
        }
        bool breatherOk = Current == Phase.Breather && Wave == 2 && _monsters.Active.Count == 0;

        // 6) 祈祷 + 受伤重置证明：按住 E 2 秒时模拟受伤（SetHpForSmoke 触发 HealthChanged → 重置），
        //    再按 1.33 秒——若无重置 2+1.33 > 3.0 早已天亮；仍黑夜 = 「不受伤长按」约束真实生效。
        _player.SetHpForSmoke(GameDefs.MasterMaxHp);
        Input.ActionPress("interact");
        await SmokeFlags.WaitPhysicsFrames(this, 120);   // 2.0s 持续按住
        _player.SetHpForSmoke(GameDefs.MasterMaxHp - 1);  // 受伤 → 祈祷进度重置
        await SmokeFlags.WaitPhysicsFrames(this, 80);     // 1.33s：无重置应已天亮
        bool prayerResetOk = Current != Phase.Day;
        int waitDawn = 0;
        while (Current != Phase.Day && waitDawn < 240) { await SmokeFlags.WaitPhysicsFrames(this, 1); waitDawn++; }
        Input.ActionRelease("interact");
        bool dawnOk = Current == Phase.Day;

        // 7) 天亮断言：怪物全消散（魂魄已掉）+ 三选一弹窗自动弹出 + 篝火熄灭
        bool dawnDissolveOk = _monsters.Active.Count == 0 && (_souls?.SpawnedCount ?? -1) >= 3;
        bool offerOk = root.CompanionOfferNode != null && root.CompanionOfferNode.IsOpen;
        root.CompanionOfferNode?.Cancel();   // 关弹窗恢复时间流（选卡收益不在本冒烟范围）

        // 8) 再点燃 = 夜 2，波次梯子从头开始（波次结构不随夜数变化）
        Input.ActionPress("interact");
        await SmokeFlags.WaitPhysicsFrames(this, 3);
        Input.ActionRelease("interact");
        bool relightOk = Night == 2 && Wave == 1 && root.Campfire.Lit;

        var assertions = new Dictionary<string, bool>
        {
            ["night_day_start"] = dayStartOk,
            ["night_ignite_ok"] = igniteOk,
            ["night_wave1_spawn_ok"] = wave1SpawnOk,
            ["night_wave1_clear_ok"] = wave1ClearOk,
            ["night_wave2_ok"] = wave2Ok,
            ["night_breather_ok"] = breatherOk,
            ["night_prayer_reset_ok"] = prayerResetOk,
            ["night_dawn_ok"] = dawnOk,
            ["night_dawn_dissolve_ok"] = dawnDissolveOk,
            ["night_offer_ok"] = offerOk,
            ["night_relight_ok"] = relightOk,
        };
        bool ok = assertions.Values.All(v => v);
        var report = new Dictionary<string, object>
        {
            ["project"] = "山海幻灵",
            ["version"] = ProjectSettings.GetSetting("application/config/version", "dev").AsString(),
            ["scene"] = "NightDirector",
            ["result"] = ok ? "PASS" : "FAIL",
            ["night"] = Night,
            ["wave"] = Wave,
            ["souls_spawned"] = _souls?.SpawnedCount ?? -1,
            ["offer_opened"] = offerOk,
            ["assertions"] = assertions,
        };
        GD.Print("NIGHT_SMOKE_JSON_BEGIN");
        GD.Print(JsonSerializer.Serialize(report, SmokeFlags.IndentedJson));
        GD.Print("NIGHT_SMOKE_JSON_END");
        GD.Print($"[NIGHT] smoke result={(ok ? "PASS" : "FAIL")}");
        tree.Quit(ok ? 0 : 1);
    }

    /// <summary>SoulField 引用（冒烟核对魂魄掉落用；GameRoot.Bind 时注入）。</summary>
    public SoulField? Souls => _souls;
}
