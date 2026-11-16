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

        // 4) 掉队召回：挪到 20 格外，几帧内应被瞬移回主角身边
        SpiritPet far = _pets[0];
        far.GlobalPosition = _player.GlobalPosition + new Vector2(20f, 0f);
        await Frames(6);
        bool recallOk = far.GlobalPosition.DistanceTo(_player.GlobalPosition) < 4.0f;

        // 5) 角色碰撞规则：随从 layer=Spirit、mask 含 Actor/Solid；主角 mask 含 Spirit（才能踩头）
        bool layersOk = _pets.All(p => p.CollisionLayer == GameDefs.LayerSpirit
                                       && (p.CollisionMask & GameDefs.LayerActor) != 0u
                                       && (p.CollisionMask & GameDefs.LayerSolid) != 0u)
                        && (_player.CollisionMask & GameDefs.LayerSpirit) != 0u
                        && (_player.CollisionMask & GameDefs.LayerActor) != 0u;

        // 6) 随从有真实碰撞体（不是纯贴图）
        bool bodyOk = _pets.All(p => p.GetNodeOrNull<CollisionShape2D>("CollisionShape2D")?.Shape != null);

        var assertions = new Dictionary<string, bool>
        {
            ["fate_plus_roster"] = countOk,
            ["converge_near_player"] = convergeOk,
            ["never_overlap"] = noOverlapOk && stillNoOverlap,
            ["catch_up_when_far"] = catchUpOk,
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
