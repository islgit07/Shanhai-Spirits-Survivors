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
    private static bool IsFollowSmokeRequested() =>
        Array.IndexOf(OS.GetCmdlineArgs(), "--follow-smoke") >= 0
        || Array.IndexOf(OS.GetCmdlineUserArgs(), "--follow-smoke") >= 0;

    private async void RunSmoke()
    {
        GameRoot? root = GameRoot.Instance;
        if (_player == null || root == null)
        {
            GD.PushError("[SPIRIT] follow-smoke 缺少主角或 GameRoot");
            GetTree().Quit(1);
            return;
        }

        // 1) 队伍注入 A / B → 场上 = 本命 + 2 队友，共 3 只
        root.Companions.Clear();
        root.Companions.TryApply("A", out _);
        root.Companions.TryApply("B", out _);
        Refresh();
        await Frames(4);
        bool countOk = _pets.Count == 3 && Primary != null && Followers == 2;

        // 2) 等落地：三只都应落在主角 3.5 格内，且与主角 AABB 不重叠
        await Frames(40);
        bool convergeOk = _pets.All(p => p.GlobalPosition.DistanceTo(_player.GlobalPosition) < 3.5f);
        bool noOverlapOk = _pets.All(p => !Overlaps(p.GlobalPosition, _player.GlobalPosition));

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
        rideFollow?.Enable(_player, 0);   // 恢复跟随

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

        // 7) 掉队召回：挪到 20 格外，几帧内应被瞬移回主角身边
        SpiritPet far = _pets[0];
        far.GlobalPosition = _player.GlobalPosition + new Vector2(20f, 0f);
        await Frames(6);
        bool recallOk = far.GlobalPosition.DistanceTo(_player.GlobalPosition) < 4.0f;

        // 6) 角色碰撞规则：随从 layer=Spirit、mask 含 Actor/Solid；
        //    主角只对 LayerSpiritTop（头顶平台）产生碰撞 —— 头上能踩、水平不被顶住。
        bool layersOk = _pets.All(p => p.CollisionLayer == GameDefs.LayerSpirit
                                       && (p.CollisionMask & GameDefs.LayerActor) != 0u
                                       && (p.CollisionMask & GameDefs.LayerSolid) != 0u)
                        && (_player.CollisionMask & GameDefs.LayerSpiritTop) != 0u
                        && (_player.CollisionMask & GameDefs.LayerSpirit) == 0u
                        && (_player.CollisionMask & GameDefs.LayerActor) != 0u;

        // 7) 随从有真实碰撞体 + 头顶踩踏平台（不是纯贴图）
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
            ["recall_when_lost"] = recallOk,
            ["actor_layers"] = layersOk,
            ["has_collision_body"] = bodyOk,
        };
        bool ok = assertions.Values.All(v => v);
        var report = new Dictionary<string, object>
        {
            ["project"] = "山海幻灵",
            ["version"] = "0.0005",
            ["scene"] = "SpiritFollowGroup",
            ["result"] = ok ? "PASS" : "FAIL",
            ["pets"] = _pets.Count,
            ["followers"] = Followers,
            ["fate_id"] = Primary?.SpiritId ?? "",
            ["max_distance"] = _pets.Count > 0
                ? (float)Math.Round(_pets.Max(p => p.GlobalPosition.DistanceTo(_player.GlobalPosition)), 3) : -1f,
            ["yield_travel"] = (float)Math.Round(yieldTravel, 3),   // 挡路位形下主角按住左键 45 帧走出的距离
            ["idle_jumps"] = idleJumps,                              // 玩家静止 2.5 秒内随从的主动起跳总数
            ["assertions"] = assertions,
        };
        GD.Print("FOLLOW_SMOKE_JSON_BEGIN");
        GD.Print(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print("FOLLOW_SMOKE_JSON_END");
        GD.Print($"[SPIRIT] smoke result={(ok ? "PASS" : "FAIL")}");
        root.Companions.Clear();
        GetTree().Quit(ok ? 0 : 1);
    }

    private async System.Threading.Tasks.Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static bool Overlaps(Vector2 a, Vector2 b)
    {
        float halfW = (GameDefs.SpiritBodyWidth + GameDefs.MasterSize.X) * 0.5f;
        float halfH = (GameDefs.SpiritBodyHeight + GameDefs.MasterSize.Y) * 0.5f;
        return Mathf.Abs(a.X - b.X) < halfW && Mathf.Abs(a.Y - b.Y) < halfH;
    }

    /// <summary>由 GameRoot 在主角就绪后调用：绑定跟随对象。</summary>
    public void Bind(Master player) => _player = player;

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
            follow.Enable(_player, i);
            if (firstBind) follow.SnapToTarget();   // 新实体直接落位，不从远处跑过来
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
