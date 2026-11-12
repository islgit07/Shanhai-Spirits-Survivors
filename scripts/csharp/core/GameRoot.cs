using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace BOOT-001 / GAME-001 / TEST-001 程序化自举 + 状态机 + 无头冒烟。
///
/// v0.003 重构要点：全量 C#（零 autoload）、Main.tscn 只挂本脚本，其余节点运行时生成。
/// 成长阀门从「天亮固定三选一」改为「灵韵跨阈值即时唤灵」——学习 BM 的 vBones_NextLevel 模型。
/// 昼夜仍作为节奏容器保留（不删除既有功能），但不再是升级的唯一入口。
/// </summary>
public partial class GameRoot : Node2D
{
    public static GameRoot? Instance;
    public static bool SmokeMode;

    private const float SmokeStep = 0.80f;
    private const int SmokeTargetDay = 3;
    private const int SmokeFixedSeed = 20261112;

    private MainMenu _menu = null!;
    private SpiritWorld _world = null!;
    private Hud _hud = null!;

    private float _timer;
    private RunPhase _resumePhase = RunPhase.Night;
    private IReadOnlyList<Offer> _offers = Array.Empty<Offer>();
    private int _offerSalt;

    private readonly List<string> _smokeEvents = new();
    private int _smokeOfferCount;
    private bool _smokeFused, _smokeBolstered, _smokeRaised;
    private bool _smokeBranchesComplete;

    private static readonly Dictionary<string, int[]> ActionKeys = new()
    {
        { "move_left",   new[] { (int)Key.A, (int)Key.Left } },
        { "move_right",  new[] { (int)Key.D, (int)Key.Right } },
        { "jump",        new[] { (int)Key.Space, (int)Key.W, (int)Key.Up } },
        { "pause",       new[] { (int)Key.Escape, (int)Key.P } },
        { "start_night", new[] { (int)Key.Enter, (int)Key.KpEnter } },
        { "reroll",      new[] { (int)Key.Q } },
        { "skip",        new[] { (int)Key.E } },
        { "restart",     new[] { (int)Key.R } },
        { "menu",        new[] { (int)Key.M } },
        { "slot1", new[] { 49 } }, { "slot2", new[] { 50 } }, { "slot3", new[] { 51 } },
        { "slot4", new[] { 52 } }, { "slot5", new[] { 53 } }, { "slot6", new[] { 54 } },
    };

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;   // 唤灵暂停时状态机仍需推进
        Meta.Load();
        RegisterActions();

        _menu = new MainMenu { Name = "MenuRoot", ProcessMode = ProcessModeEnum.Always };
        _world = new SpiritWorld { Name = "WorldRoot" };
        _hud = new Hud { Name = "HudRoot", ProcessMode = ProcessModeEnum.Always };
        AddChild(_menu);
        AddChild(_world);
        AddChild(_hud);

        _world.Visible = false;
        _hud.Visible = false;
        _menu.Visible = true;
        Run.Phase = RunPhase.Menu;

        GD.Print("[BOOT] 山海幻灵 v0.003 · 山海谷幻灵师 · Godot 4 2D / C# 全量重构");
        string[] args = OS.GetCmdlineArgs();
        string[] userArgs = OS.GetCmdlineUserArgs();
        if (args.Contains("--smoke") || userArgs.Contains("--smoke")) BeginSmoke();
    }

    private static void RegisterActions()
    {
        foreach (var kv in ActionKeys)
        {
            var name = new StringName(kv.Key);
            if (!InputMap.HasAction(name)) InputMap.AddAction(name, 0.2f);
            foreach (int code in kv.Value)
                InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = (Key)code });
        }
    }

    public override void _Process(double delta)
    {
        if (GetTree().Paused && Run.Phase != RunPhase.Summon) return;
        float dt = (float)delta;
        if (Run.Phase is RunPhase.Day or RunPhase.Night)
        {
            _timer -= dt;
            if (_timer <= 0.0f) Advance();
        }
        if (Run.PendingOffers > 0 && Run.Phase is RunPhase.Day or RunPhase.Night)
            EnterSummon();
    }

    private float PhaseDuration(RunPhase p)
    {
        if (SmokeMode) return SmokeStep;
        return p == RunPhase.Day ? GameDefs.DayDuration : GameDefs.NightDuration;
    }

    private void Advance()
    {
        if (Run.Phase == RunPhase.Day) EnterNight();
        else if (Run.Phase == RunPhase.Night) OnDawn();
    }

    // ---------------------------------------------------------------- 开局 / 阶段
    public void StartRun(string startSpecies, int? fixedSeed = null)
    {
        GetTree().Paused = false;
        int seed = fixedSeed ?? ((int)Time.GetUnixTimeFromSystem() & 0x7FFFFFFF);
        Run.Reset(seed, startSpecies);
        _menu.Visible = false;
        _world.Visible = true;
        _hud.Visible = true;
        _world.BeginRun();
        _offerSalt = 0;
        SmokeLog("start_run");
        EnterDay();
    }

    private void EnterDay()
    {
        Run.Phase = RunPhase.Day;
        _timer = PhaseDuration(Run.Phase);
        _world.SetPhase(Run.Phase, Run.Day);
        Signals.EmitPhase(Run.Phase, Run.Day);
        Signals.EmitHint($"第 {Run.Day} 天 · 营地整备");
        SmokeLog("day_started");
    }

    private void EnterNight()
    {
        Run.Phase = RunPhase.Night;
        _timer = PhaseDuration(Run.Phase);
        _world.SetPhase(Run.Phase, Run.Day);
        Signals.EmitPhase(Run.Phase, Run.Day);
        Signals.EmitHint("入夜 · 野生幻兽来袭，幻灵自动迎战");
        SmokeLog("night_started");
    }

    private void OnDawn()
    {
        _world.OnDawn();
        Run.Day++;
        SmokeLog("dawn");
        if (SmokeMode && Run.Day > SmokeTargetDay) { PlayerDied(); return; }
        EnterDay();
    }

    // ---------------------------------------------------------------- 唤灵（BM 核心）
    private void EnterSummon()
    {
        _resumePhase = Run.Phase;
        PresentOffers();
    }

    private void PresentOffers()
    {
        Run.Phase = RunPhase.Summon;
        GetTree().Paused = true;   // 世界冻结，UI 与状态机为 Always
        _offers = OfferSystem.ChooseNewItem(Run.Seed + _offerSalt * 7919, Run.Level);
        // 冒烟注入：换签直到当前候选里出现「尚未被验证过的唤灵出口」，
        // 保证召唤 / 培养 / 融合三条路径都被真实 Apply() 执行，而不只是出现在候选池。
        if (SmokeMode)
        {
            int guard = 0;
            while (guard < 40 && !_offers.Any(o => MissingKind(o.Kind)))
            {
                _offerSalt++;
                _offers = OfferSystem.ChooseNewItem(Run.Seed + _offerSalt * 7919, Run.Level);
                guard++;
            }
        }
        _smokeOfferCount++;
        Signals.EmitPhase(Run.Phase, Run.Day);
        Signals.EmitOffers(_offers);
        Signals.EmitHint($"唤灵 · 第 {Run.Level} 次（灵韵达到阈值，随时可触发）");
        SmokeLog("offers_presented");
        if (SmokeMode) SmokeResolve();
    }

    public void ChooseOffer(int idx)
    {
        if (Run.Phase != RunPhase.Summon || idx < 0 || idx >= _offers.Count) return;
        Offer o = _offers[idx];
        if (o.Kind == OfferKind.Fuse) _smokeFused = true;
        else if (o.Kind == OfferKind.Bolster) _smokeBolstered = true;
        else if (o.Kind == OfferKind.Raise) _smokeRaised = true;
        o.Apply();
        Run.PendingOffers = Math.Max(0, Run.PendingOffers - 1);
        SmokeLog("card_chosen");
        if (Run.PendingOffers > 0) PresentOffers();
        else Resume();
    }

    private void Resume()
    {
        GetTree().Paused = false;
        Run.Phase = _resumePhase;
        Signals.EmitPhase(Run.Phase, Run.Day);
    }

    public void Reroll()
    {
        if (Run.Phase != RunPhase.Summon || Run.Rerolls <= 0) return;
        Run.Rerolls--;
        _offerSalt++;
        _offers = OfferSystem.ChooseNewItem(Run.Seed + _offerSalt * 7919, Run.Level);
        Signals.EmitOffers(_offers);
        Signals.EmitHint("已重掷唤灵候选");
    }

    public void Skip()
    {
        if (Run.Phase != RunPhase.Summon) return;
        Run.AddCoins(3);
        Run.PendingOffers = Math.Max(0, Run.PendingOffers - 1);
        SmokeLog("skipped");
        if (Run.PendingOffers > 0) PresentOffers();
        else Resume();
    }

    // ---------------------------------------------------------------- 营地 / 交互
    public void BuyCamp(int index)
    {
        if (Run.Phase != RunPhase.Day || index < 0 || index >= GameDefs.CampItems.Length) return;
        CampItem item = GameDefs.CampItems[index];
        if (Run.Coins < item.Price) { Signals.EmitHint("贝币不足"); return; }
        Run.AddCoins(-item.Price);
        Run.BuyStructure(item);
        if (item.Kind != "instant") _world.AddStructureVisual(item.Kind);
        Signals.EmitHint("已添置 " + item.Name);
        SmokeLog("buy");
    }

    public void StartNightEarly()
    {
        if (Run.Phase == RunPhase.Day) EnterNight();
    }

    public void TogglePause()
    {
        if (Run.Phase is RunPhase.Day or RunPhase.Night)
        {
            GetTree().Paused = !GetTree().Paused;
            Signals.EmitHint(GetTree().Paused ? "已暂停" : "继续");
        }
    }

    public void PlayerDied()
    {
        if (Run.Phase is RunPhase.Dead or RunPhase.Menu) return;
        GetTree().Paused = false;
        Run.Phase = RunPhase.Dead;
        int embers = Meta.RecordRun(Run.Day, Run.Kills);
        _world.SetPhase(Run.Phase, Run.Day);
        Signals.EmitPhase(Run.Phase, Run.Day);
        Signals.EmitRunEnded(Run.Day, Run.Kills, embers);
        SmokeLog("run_ended");
        if (SmokeMode) CallDeferred(new StringName(nameof(FinishSmoke)));
    }

    public void ReturnToMenu()
    {
        GetTree().Paused = false;
        Run.Phase = RunPhase.Menu;
        _world.EndRun();
        _world.Visible = false;
        _hud.Visible = false;
        _menu.Visible = true;
        _menu.Refresh();
        SmokeLog("return_to_menu");
    }

    public void RestartRun()
    {
        GetTree().Paused = false;
        StartRun(Run.StartSpecies, SmokeMode ? SmokeFixedSeed : null);
    }

    // ---------------------------------------------------------------- 无头冒烟
    /// <summary>
    /// @trace TEST-001 自动跑完 3 昼夜并校验新内核断言。
    /// 注入项只在 --smoke 生效：固定种子、额外 2 只凡阶契灵（让融合分支可达）、
    /// 1 枚灵魄、42 点灵韵（触发多次唤灵）。正常游戏数值完全不受影响。
    /// </summary>
    private void BeginSmoke()
    {
        SmokeMode = true;
        _smokeEvents.Clear();
        StartRun("qingxiao", SmokeFixedSeed);
        Legion.Add("qingxiao", 1);
        Legion.Add("chiwu", 1);
        Run.AddSoul(1);
        // 在军团尚未被融合压缩前，校验完整候选池三类出口都可达（可复现的初始事实）
        List<Offer> initialPool = OfferSystem.BuildCandidates();
        _smokeBranchesComplete = initialPool.Any(o => o.Kind == OfferKind.Raise)
            && initialPool.Any(o => o.Kind == OfferKind.Bolster)
            && initialPool.Any(o => o.Kind == OfferKind.Fuse);
        Run.AddEssence(100);   // 8+14+20+26+32 = 5 次唤灵，覆盖三类出口
    }

    private bool MissingKind(OfferKind k) => k switch
    {
        OfferKind.Fuse => !_smokeFused,
        OfferKind.Bolster => !_smokeBolstered,
        OfferKind.Raise => !_smokeRaised,
        _ => false,
    };

    private void SmokeLog(string e)
    {
        if (!SmokeMode) return;
        _smokeEvents.Add(e);
        GD.Print("[SMOKE] " + e + " day=" + Run.Day);
    }

    private void SmokeResolve()
    {
        if (!SmokeMode || Run.Phase != RunPhase.Summon || _offers.Count == 0) return;
        int idx = -1;
        // 优先挑「还没验证过」的出口：融合 → 培养 → 召唤
        if (!_smokeFused) idx = FindOffer(OfferKind.Fuse);
        if (idx < 0 && !_smokeBolstered) idx = FindOffer(OfferKind.Bolster);
        if (idx < 0 && !_smokeRaised) idx = FindOffer(OfferKind.Raise);
        if (idx < 0) idx = FindOffer(OfferKind.Fuse);
        if (idx < 0) idx = FindOffer(OfferKind.Bolster);
        if (idx < 0) idx = FindOffer(OfferKind.Raise);
        if (idx < 0) idx = 0;
        ChooseOffer(idx);
    }

    private int FindOffer(OfferKind kind)
    {
        for (int i = 0; i < _offers.Count; i++)
            if (_offers[i].Kind == kind) return i;
        return -1;
    }

    private void FinishSmoke()
    {
        bool sharedSpecies = _world.WildSpeciesSpawned
            .Overlaps(Run.Legion.Select(u => u.SpeciesId));
        bool capRespected = Run.Legion.Count <= Run.LegionCap
            && Run.Legion.GroupBy(u => u.SpeciesId)
                .All(g => g.Count() <= GameDefs.SpeciesById(g.Key).Cap);
        bool loreChangesPossibility = GameDefs.Fuses.Any(r => !string.IsNullOrEmpty(r.RequiredLore))
            && GameDefs.Lores.Any(l => l.Id == "lore_options");
        bool soulJumpConfigured = GameDefs.Fuses.Any(r => r.SoulCost > 0);

        int day = Run.Day;
        int kills = Run.Kills;
        int cards = Run.CardsTaken;
        bool ended = Run.Phase == RunPhase.Dead;
        ReturnToMenu();

        var assertions = new Dictionary<string, bool>
        {
            ["reached_target_day"] = day >= SmokeTargetDay,
            ["run_ended"] = ended,
            ["platforms_built"] = _world.PlatformCount > 0,
            ["ground_continuous"] = _world.GroundContinuous,
            ["player_grounded"] = _world.PlayerGrounded,
            ["wilds_spawned"] = _world.WildSpawned > 0,
            ["projectiles_fired"] = _world.ProjectilesFired > 0,
            ["kills_recorded"] = kills > 0,
            ["pickups_collected"] = _world.PickupsCollected > 0,
            ["essence_gained"] = Run.EssenceTotal > 0,
            ["level_advanced"] = Run.Level > 1,
            ["offers_presented"] = _smokeOfferCount > 0,
            ["cards_taken"] = cards > 0,
            ["branches_complete"] = _smokeBranchesComplete,
            ["fuse_applied"] = _smokeFused,
            ["bolster_applied"] = _smokeBolstered,
            ["raise_applied"] = _smokeRaised,
            ["shared_species_table"] = sharedSpecies,
            ["cap_respected"] = capRespected,
            ["lore_changes_pool"] = loreChangesPossibility,
            ["soul_jump_configured"] = soulJumpConfigured,
            ["save_file_written"] = FileAccess.FileExists(Meta.SavePath),
            ["returned_to_menu"] = Run.Phase == RunPhase.Menu,
            ["events_ordered"] = _smokeEvents.Contains("day_started")
                && _smokeEvents.Contains("night_started")
                && _smokeEvents.Contains("offers_presented")
                && _smokeEvents.Contains("run_ended"),
        };
        bool ok = assertions.Values.All(v => v);
        var report = new Dictionary<string, object>
        {
            ["project"] = "山海幻灵",
            ["version"] = "0.003",
            ["setting"] = "山海谷幻灵师 · 培养幻灵 · 降伏野生幻兽（敌我同源）· 横板跳跃",
            ["engine"] = Engine.GetVersionInfo()["string"].AsString(),
            ["language"] = "C# (full)",
            ["mode"] = "headless-smoke",
            ["result"] = ok ? "PASS" : "FAIL",
            ["days_reached"] = day,
            ["kills"] = kills,
            ["cards_taken"] = cards,
            ["legion_size"] = Run.Legion.Count,
            ["platforms"] = _world.PlatformCount,
            ["ground_continuous"] = _world.GroundContinuous,
            ["player_grounded"] = _world.PlayerGrounded,
            ["wilds_spawned"] = _world.WildSpawned,
            ["projectiles_fired"] = _world.ProjectilesFired,
            ["pickups_spawned"] = _world.PickupsSpawned,
            ["pickups_collected"] = _world.PickupsCollected,
            ["essence_total"] = Run.EssenceTotal,
            ["level"] = Run.Level,
            ["smoke_fused"] = _smokeFused,
            ["smoke_bolstered"] = _smokeBolstered,
            ["smoke_raised"] = _smokeRaised,
            ["smoke_injection"] = "固定种子 20261112；额外 2 只凡阶契灵；1 枚灵魄；100 灵韵（5 次唤灵）；换签直到三类出口均被实际执行；敌人近场 0.9~1.6；幻灵伤害 +200；阶段 0.8 秒（全部仅 --smoke）",
            ["events"] = _smokeEvents.ToArray(),
            ["assertions"] = assertions,
        };
        GD.Print("SMOKE_JSON_BEGIN");
        GD.Print(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("SMOKE_JSON_END");
        GetTree().Quit(ok ? 0 : 1);
    }
}
