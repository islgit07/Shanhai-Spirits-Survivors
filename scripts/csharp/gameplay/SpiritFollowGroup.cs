using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace SPIRIT-FOLLOW 场上的随从实体队伍：本命（槽位 0）+ 局内领取的队友（槽位 1..N）。
///
/// 数据源是 GameRoot.ChosenSpirit 与 GameRoot.Companions（RunRoster，当局临时）。
/// RunRoster 一变（开局清空 / 三选一领取 / 同名合成）就调用 Refresh()：
///   · 新成员 → 从 spirit_pet.tscn 生成实体并接入跟随；
///   · 成员消失（换局）→ 释放实体；
///   · 已有成员 → 只同步等级 / 资质，实体不重建（避免打断跟随手感）；
///   · 槽位按列表顺序重排（个人落点跟着变，队伍自然散开）。
///
/// 队友还没有专属定义卡与美术，用占位色板区分；技能 / 属性效果一律不做。
/// </summary>
public partial class SpiritFollowGroup : Node2D
{
    private const string PetScenePath = "res://scenes/spirits/spirit_pet.tscn";

    // 队友占位色（与三本命的身份色区分开；只用于占位美术）
    private static readonly Color[] PlaceholderTints =
    {
        new(0.98f, 0.72f, 0.35f),
        new(0.55f, 0.85f, 0.70f),
        new(0.78f, 0.62f, 0.95f),
        new(0.95f, 0.55f, 0.55f),
        new(0.55f, 0.78f, 0.95f),
        new(0.92f, 0.90f, 0.55f),
    };

    private readonly List<SpiritPet> _pets = new();
    private Master? _player;
    private IStandableGrid? _grid;   // 方块网格：给随从的 BFS 防卡死寻路用
    private BlockField? _blocks;     // 同一网格的具体类型（冒烟查弹跳 / 粘液块位置用）
    private PackedScene? _petScene;

    /// <summary>当前场上的随从实体（顺序 = 槽位顺序）。</summary>
    public IReadOnlyList<SpiritPet> Pets => _pets;

    /// <summary>本命实体（槽位 0）。</summary>
    public SpiritPet? Primary => _pets.FirstOrDefault(p => p.IsFate);

    /// <summary>局内领取的队友数量（不含本命）。</summary>
    public int Followers => _pets.Count(p => !p.IsFate);

    public override void _Ready()
    {
        _petScene = GD.Load<PackedScene>(PetScenePath);
        if (_petScene == null) GD.PushError($"[SPIRIT] 无法加载随从场景 {PetScenePath}");
        if (IsFollowSmokeRequested()) CallDeferred(new StringName(nameof(RunSmoke)));
    }

    // ---------------------------------------------------------------- 无头冒烟
    private static bool IsFollowSmokeRequested() => SmokeFlags.Has("--follow-smoke");

    private async void RunSmoke()
    {
        GameRoot? root = GameRoot.Instance;
        if (_player == null || root == null)
        {
            GD.PushError("[SPIRIT] follow-smoke 缺少主角或 GameRoot");
            GetTree().Quit(1);
            return;
        }

        // 1) 队伍注入 A/B/C/D 满编 → 场上 = 本命 + 4 队友，共 5 只（验证五人槽位互不重叠）
        root.Companions.Clear();
        root.Companions.TryApply("A", out _);
        root.Companions.TryApply("B", out _);
        root.Companions.TryApply("C", out _);
        root.Companions.TryApply("D", out _);
        Refresh();
        await Frames(4);
        bool countOk = _pets.Count == 5 && Primary != null && Followers == 4;

        // 2) 等落地：三只都应落在主角 3.5 格内，且与主角 AABB 不重叠
        await Frames(40);
        bool convergeOk = _pets.All(p => p.GlobalPosition.DistanceTo(_player.GlobalPosition) < 3.5f);
        bool noOverlapOk = _pets.All(p => !Overlaps(p.GlobalPosition, _player.GlobalPosition));
        bool petsApartOk = PetsApart();   // 随从之间也必须有碰撞体、不重叠

        // 3) 主角瞬移 7 格 → 随从越远越快追上（40 帧内收敛到 4 格内）
        _player.GlobalPosition += new Vector2(7f, 0f);
        _player.Velocity = Vector2.Zero;
        // 阈值 4.5：地图有 3 格层高差，随从偶尔停在下一层台面上仍算「跟上了」。
        await Frames(60);
        bool catchUpOk = _pets.All(p => p.GlobalPosition.DistanceTo(_player.GlobalPosition) < 4.5f);
        bool stillNoOverlap = _pets.All(p => !Overlaps(p.GlobalPosition, _player.GlobalPosition));

        // 4) 挡路实测：主角与随从都放到第一层全覆盖地板正中（x=0，地面层上方两格恒空，
        //    排除地形干扰），随从摆在主角正左方，按住左键 —— 主角应能正常走过去。
        _player.GlobalPosition = new Vector2(0f, GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);
        _player.Velocity = Vector2.Zero;
        await Frames(15);
        SpiritPet blocker = _pets[0];
        blocker.GlobalPosition = _player.GlobalPosition + new Vector2(-0.45f, 0f);
        blocker.Velocity = Vector2.Zero;
        await Frames(3);
        float startX = _player.GlobalPosition.X;
        Input.ActionPress("move_left");
        await Frames(45);
        Input.ActionRelease("move_left");
        float yieldTravel = startX - _player.GlobalPosition.X;
        bool yieldOk = yieldTravel > 2.0f;

        // 5) 踩头：随从站地面，主角从正上方落下 → 应停在随从头顶平台，而不是落到地面。
        //    先把这只随从的跟随暂停，否则它会追着空中的主角自己跑掉，测试不稳定。
        await Frames(10);
        SpiritPet ridePet = _pets[0];
        SpiritFollow? rideFollow = ridePet.GetNodeOrNull<SpiritFollow>("Follow");
        rideFollow?.Disable();
        // 位置贴着主角当前所在地面（地面层全覆盖、上方两格恒空），避免随机方块干扰
        float rideX = Mathf.Clamp(_player.GlobalPosition.X + 1.2f, -20f, 20f);
        ridePet.GlobalPosition = new Vector2(rideX, GameDefs.LayerY(0) - GameDefs.SpiritBodyHeight * 0.5f);
        ridePet.Velocity = Vector2.Zero;
        _player.GlobalPosition = new Vector2(rideX, GameDefs.LayerY(0) - 2.0f);
        _player.Velocity = Vector2.Zero;
        await Frames(50);
        bool rideOk = _player.IsOnFloor()
                      && _player.GlobalPosition.Y < GameDefs.LayerY(0) - 1.0f   // 明显高于地面站姿
                      && Mathf.Abs(_player.GlobalPosition.X - ridePet.GlobalPosition.X) < 0.6f;
        rideFollow?.Enable(_player, 0, _grid);   // 恢复跟随（网格要一起传回去，否则寻路能力被清空）

        // 6) 静止不跳：玩家站地面不动 2.5 秒，随从不应原地连跳
        //    （槽位纵向夹紧 + 跨层阈值 0.9 + 自救跳冷却，三处共同保证）
        _player.GlobalPosition = new Vector2(0f, GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);
        _player.Velocity = Vector2.Zero;
        SpiritFollow[] follows = _pets.Select(p => p.GetNodeOrNull<SpiritFollow>("Follow"))
                                      .Where(f => f != null).ToArray()!;
        await Frames(40);    // 先让它们落地归位
        int jumpsBefore = follows.Sum(f => f.JumpCount);
        await Frames(150);   // 2.5 秒静止
        int idleJumps = follows.Sum(f => f.JumpCount) - jumpsBefore;
        bool idleCalmOk = idleJumps <= 2;
        bool petsApartIdle = PetsApart();   // 静止归位后也不许互相压住

        // 7) BFS 寻路：地面连续时，从 18 格外应能找到回玩家身边的路（非空路径）
        bool pathFound = _grid != null
                         && SpiritPathfinder.FindPath(_grid,
                                _player.GlobalPosition + new Vector2(-18f, 0f),
                                _player.GlobalPosition) is { Count: > 0 };

        // 8) 卡死恢复：把一只随从扔到 10 格外（>6 触发寻路，<13 不会直接瞬移），
        //    7 秒内应靠 BFS 走回来，且期间确实找到过路径。
        SpiritPet stranded = _pets[0];
        SpiritFollow? strandedFollow = stranded.GetNodeOrNull<SpiritFollow>("Follow");
        stranded.GlobalPosition = _player.GlobalPosition + new Vector2(-10f, -1f);
        stranded.Velocity = Vector2.Zero;
        strandedFollow?.ForceSeekForSmoke();   // 模拟「直线回不去」，强制走 BFS
        float stuckStartDist = stranded.GlobalPosition.DistanceTo(_player.GlobalPosition);
        await Frames(300);   // 5 秒足够走完 10 格 + 寻路超时兜底
        float stuckFinalDist = stranded.GlobalPosition.DistanceTo(_player.GlobalPosition);
        bool stuckRecovered = stuckFinalDist < 6f;
        bool stuckByPath = stuckRecovered && strandedFollow?.EverFoundPath == true;

        // 9) 掉队召回：挪到 20 格外（超过瞬移阈值），几帧内应被瞬移回主角身边
        SpiritPet far = _pets[0];
        far.GlobalPosition = _player.GlobalPosition + new Vector2(20f, 0f);
        await Frames(6);
        bool recallOk = far.GlobalPosition.DistanceTo(_player.GlobalPosition) < 4.0f;

        // 10) 弹跳块对随从生效：从弹跳块上方落下，应被弹起 2 格以上
        bool bounceFound = false, bounceOk = false;
        float bounceRise = 0f;
        float bouncePeakVy = 0f;
        if (_blocks != null && _blocks.GetFirstBounce(out float bx, out float bTop))
        {
            bounceFound = true;
            SpiritPet jumper = _pets[0];
            foreach (SpiritPet p in _pets)
                if (p != jumper) p.GetNodeOrNull<SpiritFollow>("Follow")?.Disable();

            _player.GlobalPosition = new Vector2(bx - 2f, bTop - 1f);
            _player.Velocity = Vector2.Zero;
            // 贴得很近才放手：距离短，水平还没加速起来就碰到感应区，不会被带偏到块外
            jumper.GlobalPosition = new Vector2(bx, bTop - 0.55f);
            jumper.Velocity = Vector2.Zero;
            // 放下就立即逐帧采样：弹跳发生在头两帧，晚开始会错过最高点、把弹起高度记小
            float minY = jumper.GlobalPosition.Y;
            float minVy = 0f;
            for (int i = 0; i < 45; i++)
            {
                await Frames(1);
                if (jumper.GlobalPosition.Y < minY) minY = jumper.GlobalPosition.Y;
                if (jumper.Velocity.Y < minVy) minVy = jumper.Velocity.Y;
            }
            bounceRise = bTop - minY;
            bouncePeakVy = minVy;
            // 断言「弹射速度被应用」而不是「弹起多高」：上方有没有天花板由地图随机决定，
            // 高度会因此变化，但 -22 的上抛速度一定说明弹跳块对随从生效了。
            bounceOk = bouncePeakVy <= -GameDefs.BounceSpeed * 0.8f;
        }

        // 11) 粘液块对随从生效：站上去应判定为粘液地面，且水平速度被压低
        bool slimeFound = false, slimeOk = false;
        if (_blocks != null && _blocks.GetFirstSlime(out float sx, out float sTop))
        {
            slimeFound = true;
            SpiritPet slimer = _pets[0];
            SpiritFollow? slimeFollow = slimer.GetNodeOrNull<SpiritFollow>("Follow");
            // 其它随从暂停跟随，避免把测试用的随从从粘液块上挤开
            foreach (SpiritPet p in _pets)
                if (p != slimer) p.GetNodeOrNull<SpiritFollow>("Follow")?.Disable();

            slimer.GlobalPosition = new Vector2(sx, sTop - 0.44f);
            slimer.Velocity = Vector2.Zero;
            _player.GlobalPosition = new Vector2(sx + 6f, sTop - 1f);   // 玩家在 6 格外 → 随从全速追
            _player.Velocity = Vector2.Zero;

            // 逐帧采样：粘液块只有 1 格宽，随从很快会滑出去，只要「站在上面的某帧」速度受限即可
            bool onSlime = false, slimeSlowed = false;
            float slimeCap = GameDefs.SpiritFollowMaxSpeed * GameDefs.SlimeSpeedMul;
            for (int i = 0; i < 12; i++)
            {
                await Frames(1);
                if (slimeFollow?.IsOnSlime != true) continue;
                onSlime = true;
                if (Mathf.Abs(slimer.Velocity.X) <= slimeCap + 1.0f) slimeSlowed = true;
            }
            slimeOk = onSlime && slimeSlowed;
        }

        // 12) 角色碰撞规则：随从 layer=Spirit、mask 含 Actor/Solid；
        //    主角只对 LayerSpiritTop（头顶平台）产生碰撞 —— 头上能踩、水平不被顶住。
        bool layersOk = _pets.All(p => p.CollisionLayer == GameDefs.LayerSpirit
                                       && (p.CollisionMask & GameDefs.LayerActor) != 0u
                                       && (p.CollisionMask & GameDefs.LayerSolid) != 0u)
                        && (_player.CollisionMask & GameDefs.LayerSpiritTop) != 0u
                        && (_player.CollisionMask & GameDefs.LayerSpirit) == 0u
                        && (_player.CollisionMask & GameDefs.LayerActor) != 0u;

        // 13) 随从有真实碰撞体 + 头顶踩踏平台（不是纯贴图）
        bool bodyOk = _pets.All(p => p.GetNodeOrNull<CollisionShape2D>("CollisionShape2D")?.Shape != null
                                     && p.GetNodeOrNull<StaticBody2D>("SpiritTop") != null);

        var assertions = new Dictionary<string, bool>
        {
            ["fate_plus_roster"] = countOk,
            ["converge_near_player"] = convergeOk,
            ["never_overlap"] = noOverlapOk && stillNoOverlap,
            ["catch_up_when_far"] = catchUpOk,
            ["yield_no_block"] = yieldOk,
            ["ride_on_head"] = rideOk,
            ["idle_no_hopping"] = idleCalmOk,
            ["spirit_no_overlap"] = petsApartOk && petsApartIdle,
            ["bfs_path_found"] = pathFound,
            ["stuck_recovery_by_bfs"] = stuckByPath,
            ["recall_when_lost"] = recallOk,
            ["bounce_affects_spirit"] = !bounceFound || bounceOk,
            ["slime_affects_spirit"] = !slimeFound || slimeOk,
            ["actor_layers"] = layersOk,
            ["has_collision_body"] = bodyOk,
        };
        bool ok = assertions.Values.All(v => v);
        var report = new Dictionary<string, object>
        {
            ["project"] = "山海幻灵",
            ["version"] = ProjectSettings.GetSetting("application/config/version", "dev").AsString(),
            ["scene"] = "SpiritFollowGroup",
            ["result"] = ok ? "PASS" : "FAIL",
            ["pets"] = _pets.Count,
            ["followers"] = Followers,
            ["fate_id"] = Primary?.SpiritId ?? "",
            ["max_distance"] = _pets.Count > 0
                ? (float)Math.Round(_pets.Max(p => p.GlobalPosition.DistanceTo(_player.GlobalPosition)), 3) : -1f,
            ["yield_travel"] = (float)Math.Round(yieldTravel, 3),   // 挡路位形下主角按住左键 45 帧走出的距离
            ["idle_jumps"] = idleJumps,                              // 玩家静止 2.5 秒内随从的主动起跳总数
            ["min_pet_distance"] = (float)Math.Round(MinPetDistance(), 3),   // 随从两两最小中心距（≥0.62 即不重叠）
            ["pet_body_width"] = GameDefs.SpiritBodyWidth,
            ["stuck_start_dist"] = (float)Math.Round(stuckStartDist, 2),     // 卡死测试开始时的距离
            ["stuck_final_dist"] = (float)Math.Round(stuckFinalDist, 2),     // 9 秒后的距离
            ["stuck_ever_path"] = strandedFollow?.EverFoundPath ?? false,
            ["stranded_has_grid"] = strandedFollow?.HasGrid ?? false,
            ["stranded_seek_starts"] = strandedFollow?.SeekStarts ?? -1,
            ["bounce_block_found"] = bounceFound,
            ["bounce_rise"] = (float)Math.Round(bounceRise, 2),
            ["bounce_peak_vy"] = (float)Math.Round(bouncePeakVy, 2),
            ["slime_block_found"] = slimeFound,
            ["assertions"] = assertions,
        };
        GD.Print("FOLLOW_SMOKE_JSON_BEGIN");
        GD.Print(JsonSerializer.Serialize(report, SmokeFlags.IndentedJson));
        GD.Print("FOLLOW_SMOKE_JSON_END");
        GD.Print($"[SPIRIT] smoke result={(ok ? "PASS" : "FAIL")}");
        root.Companions.Clear();
        GetTree().Quit(ok ? 0 : 1);
    }

    private System.Threading.Tasks.Task Frames(int count) => SmokeFlags.WaitPhysicsFrames(this, count);

    /// <summary>批量开关场上随从的近战攻击组件（--monster-smoke 确定性用；平时随 Refresh 常开）。</summary>
    public void SetMeleeEnabled(bool on)
    {
        foreach (SpiritPet pet in _pets)
            pet.GetNodeOrNull<SpiritMeleeAttack>("MeleeAttack")?.SetEnabled(on);
    }

    /// <summary>
    /// 随从两两之间是否互相重叠。按胶囊真实形状算中心距（不是 AABB）：
    /// 胶囊 = 竖直线段（半长 (H-W)/2）+ 半径 W/2，两胶囊不重叠 ⇔ 线段最短距离 ≥ W。
    /// </summary>
    private bool PetsApart() => MinPetDistance() >= GameDefs.SpiritBodyWidth;

    /// <summary>所有随从对之间的最小中心距（重叠诊断用）。</summary>
    private float MinPetDistance()
    {
        float min = float.MaxValue;
        for (int i = 0; i < _pets.Count; i++)
            for (int j = i + 1; j < _pets.Count; j++)
            {
                float dist = Capsule(_pets[i].GlobalPosition, _pets[j].GlobalPosition).Dist;
                if (dist < min) min = dist;
            }
        return min;
    }

    /// <summary>
    /// 两只随从（竖直胶囊碰撞体：半长 (H-W)/2 的线段 + 半径 W/2）中心间的最短距离，
    /// 同时带回中心差向量（调用方可能还要用它算推离方向）。
    /// P2 收口：SpiritFollow.SeparateFromPets 与本组的重叠诊断共用同一份公式。
    /// </summary>
    public static (float Dist, Vector2 Delta) Capsule(Vector2 a, Vector2 b)
    {
        float segSpan = GameDefs.SpiritBodyHeight - GameDefs.SpiritBodyWidth;   // 两段端点的抵扣长度
        Vector2 delta = a - b;
        float vertical = Mathf.Max(0f, Mathf.Abs(delta.Y) - segSpan);
        return (Mathf.Sqrt(delta.X * delta.X + vertical * vertical), delta);
    }

    /// <summary>随从与某角色的 AABB 重叠判定（P2 收口：GameRoot 冒烟断言复用同一份公式）。</summary>
    public static bool Overlaps(Vector2 a, Vector2 b)
    {
        float halfW = (GameDefs.SpiritBodyWidth + GameDefs.MasterSize.X) * 0.5f;
        float halfH = (GameDefs.SpiritBodyHeight + GameDefs.MasterSize.Y) * 0.5f;
        return Mathf.Abs(a.X - b.X) < halfW && Mathf.Abs(a.Y - b.Y) < halfH;
    }

    /// <summary>由 GameRoot 在主角与地形就绪后调用：绑定跟随对象与方块网格（网格用于 BFS 寻路）。</summary>
    public void Bind(Master player, IStandableGrid? grid = null)
    {
        _player = player;
        _grid = grid;
        _blocks = grid as BlockField;
    }

    /// <summary>按「本命 + 当局队伍」同步实体；幂等，可随时调用。</summary>
    public void Refresh()
    {
        GameRoot? root = GameRoot.Instance;
        if (_player == null || !IsInstanceValid(_player) || root == null) return;
        _petScene ??= GD.Load<PackedScene>(PetScenePath);
        if (_petScene == null) return;

        SpiritDef? fate = root.ChosenSpirit;
        RunRoster roster = root.Companions;

        // 期望序列：本命固定 0 号位，队友按领取顺序排在后面。
        var wanted = new List<(string Key, SpiritDef? Def, RunCompanion? Member)>();
        if (fate != null) wanted.Add(($"fate:{fate.Id}", fate, null));
        foreach (RunCompanion m in roster.Members) wanted.Add(($"member:{m.Id}", null, m));

        // 1) 移除不在期望里的实体
        for (int i = _pets.Count - 1; i >= 0; i--)
        {
            if (wanted.Exists(w => w.Key == KeyOf(_pets[i]))) continue;
            SpiritPet gone = _pets[i];
            _pets.RemoveAt(i);
            gone.QueueFree();
        }

        // 2) 按期望顺序补齐 / 同步，并重排槽位
        for (int i = 0; i < wanted.Count; i++)
        {
            (string Key, SpiritDef? Def, RunCompanion? Member) w = wanted[i];
            SpiritPet? pet = _pets.Find(p => KeyOf(p) == w.Key);
            if (pet == null)
            {
                pet = _petScene.Instantiate<SpiritPet>();
                if (w.Def != null) pet.Setup(w.Def);
                else pet.SetupCompanion(w.Member!.Id, w.Member.DisplayName,
                                        w.Member.Level, w.Member.Quality, TintFor(w.Member.Id));
                pet.Name = $"Spirit_{w.Member?.Id ?? w.Def!.Id}";
                AddChild(pet);
                _pets.Add(pet);
            }
            else if (w.Member != null)
            {
                pet.UpdateCompanionStats(w.Member.Level, w.Member.Quality);
            }

            SpiritFollow? follow = pet.GetNodeOrNull<SpiritFollow>("Follow");
            if (follow == null) continue;
            bool firstBind = !follow.Enabled;
            follow.Enable(_player, i, _grid);
            if (firstBind) follow.SnapToTarget();   // 新实体直接落位，不从远处跑过来

            // MONSTER-001：随从近战攻击（独立组件，移动/跟随行为不受影响；伤害数值在 GameDefs）
            pet.GetNodeOrNull<SpiritMeleeAttack>("MeleeAttack")?.Enable(root.MonsterField);
        }

        // 3) 列表顺序与槽位保持一致（后续冒烟 / 界面按顺序读）
        _pets.Sort((a, b) =>
            wanted.FindIndex(w => w.Key == KeyOf(a)).CompareTo(wanted.FindIndex(w => w.Key == KeyOf(b))));
    }

    private static string KeyOf(SpiritPet p) =>
        p.IsFate ? $"fate:{p.SpiritId}" : $"member:{p.SpiritId}";

    private static Color TintFor(string id)
    {
        int idx = System.Array.IndexOf(RunRoster.CandidateIds, id);
        return PlaceholderTints[(idx >= 0 ? idx : 0) % PlaceholderTints.Length];
    }
}
