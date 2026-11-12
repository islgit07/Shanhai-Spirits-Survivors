using System;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace WORLD-001 / AREA-001 世界组装：程序化构建全部 2D 节点、相机跟随、区域主题。
/// 所有节点运行时生成，不手写复杂 .tscn。
/// </summary>
public partial class SpiritWorld : Node2D
{
    public static SpiritWorld? Active;

    public int PlatformCount;
    public bool GroundContinuous;
    public bool PlayerGrounded;
    public int WildSpawned;
    public int ProjectilesFired;
    public int PickupsSpawned;
    public int PickupsCollected;
    public int BoundCount;
    // @trace FOLLOW-001 / WPN-001 幻灵输出统计（冒烟据此判定「不是均分圆环」「攻击真的落地」）
    public int AllyHits;
    public int AllyMeleeHits;
    public int AllyBoltHits;
    public int AllyAuraHits;
    public int AllyChainHits;
    public int AllyDamageDealt;
    public readonly System.Collections.Generic.HashSet<string> WildSpeciesSpawned = new();
    public Master? Player;

    private PlatformField _platforms = null!;
    private Camera2D _camera = null!;
    private Sprite2D _backdrop = null!;
    private Node2D _wild = null!, _bound = null!, _projectiles = null!, _pickups = null!, _structures = null!;
    private WildSpawner _spawner = null!;
    private RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        Active = this;
        AddToGroup("world");
        Signals.LegionChanged += SyncLegion;
    }

    public override void _ExitTree()
    {
        Signals.LegionChanged -= SyncLegion;
        if (Active == this) Active = null;
    }

    public float ZoomValue()
    {
        float h = GetViewportRect().Size.Y;
        if (h <= 0.0f) h = 720.0f;
        return h / (2.0f * GameDefs.CameraViewHalfHeight);
    }

    public Vector2 ViewSizeWorld() => GetViewportRect().Size / ZoomValue();

    public void BeginRun()
    {
        ClearAll();
        Active = this;
        PlatformCount = 0; GroundContinuous = false; PlayerGrounded = false;
        WildSpawned = 0; ProjectilesFired = 0;
        PickupsSpawned = 0; PickupsCollected = 0; BoundCount = 0;
        AllyHits = 0; AllyMeleeHits = 0; AllyBoltHits = 0; AllyAuraHits = 0;
        AllyChainHits = 0; AllyDamageDealt = 0;
        WildSpeciesSpawned.Clear();
        _rng = Rng.Stream(Run.Seed, "world");

        float z = ZoomValue();
        _camera = new Camera2D { Zoom = new Vector2(z, z), Enabled = true };
        AddChild(_camera);
        _backdrop = ArtBank.MakeSprite("bg_dusk", ViewSizeWorld());
        _backdrop.ZIndex = -100;
        _camera.AddChild(_backdrop);

        _platforms = new PlatformField();
        AddChild(_platforms);
        _platforms.Build(Run.Seed);
        PlatformCount = _platforms.GetSegmentCount();
        GroundContinuous = _platforms.IsGroundContinuous();

        _structures = new Node2D(); AddChild(_structures);
        _wild = new Node2D(); AddChild(_wild);
        _bound = new Node2D(); AddChild(_bound);
        _projectiles = new Node2D(); AddChild(_projectiles);
        _pickups = new Node2D(); AddChild(_pickups);

        Player = new Master();
        AddChild(Player);
        // @trace AXIS-001 出生点在第一层地板上方（y 更小）
        Player.GlobalPosition = new Vector2(0.0f,
            GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);
        // @trace CLIMB-001 把台沿矩形交给主角，启用自动上攀
        Player.SetLedges(_platforms.GetLedges());

        _spawner = new WildSpawner();
        AddChild(_spawner);
        ApplyTheme(1, false);
        SyncLegion();
    }

    public void EndRun() => ClearAll();

    private void ClearAll()
    {
        foreach (Node c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        Player = null;
        _platforms = null!; _camera = null!; _backdrop = null!;
        _wild = null!; _bound = null!; _projectiles = null!;
        _pickups = null!; _structures = null!; _spawner = null!;
    }

    /// <summary>军团数据 → 可视幻灵节点；军团一变就整体重建，避免增量同步错位。</summary>
    public void SyncLegion()
    {
        if (_bound == null || !IsInstanceValid(_bound)) return;
        foreach (Node c in _bound.GetChildren())
        {
            _bound.RemoveChild(c);
            c.QueueFree();
        }
        int n = Run.Legion.Count;
        Vector2 origin = Player != null && IsInstanceValid(Player)
            ? Player.GlobalPosition : Vector2.Zero;
        for (int i = 0; i < n; i++)
        {
            LegionUnit u = Run.Legion[i];
            // @trace FOLLOW-001 出生点只用一个「个人方向」做初始散开，真正的队形由 Spirit 自己算
            float a = Mathf.PosMod(u.Uid * 2.39996f, Mathf.Tau);
            var offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * GameDefs.FollowVerticalSquash)
                * (GameDefs.FollowAnchorMin + 0.6f);
            var s = new Spirit();
            _bound.AddChild(s);
            s.Setup(u.Def, u.Level, Faction.Bound, Run.Day, origin + offset, u.Uid);
        }
        BoundCount = n;
    }

    public void SetPhase(RunPhase phase, int day)
    {
        ApplyTheme(day, phase == RunPhase.Night);
        if (phase == RunPhase.Night)
        {
            _spawner.Begin(day, Run.Seed);
            if (Run.WellEssence > 0) Run.AddEssence(Run.WellEssence);
            if (Run.HutHeal > 0) Run.Heal(Run.HutHeal);
        }
    }

    public void ApplyTheme(int day, bool night)
    {
        if (_backdrop == null || !IsInstanceValid(_backdrop)) return;
        var t = GameDefs.Themes[GameDefs.ThemeIndex(day)];
        _backdrop.Texture = ArtBank.Tex(t.Bg);
        Vector2 size = ViewSizeWorld();
        float tw = Mathf.Max(1.0f, _backdrop.Texture.GetWidth());
        float th = Mathf.Max(1.0f, _backdrop.Texture.GetHeight());
        _backdrop.Scale = new Vector2(size.X / tw, size.Y / th);
        _backdrop.Modulate = night ? t.Night : t.Day;
    }

    public void OnDawn()
    {
        ClearContainer(_wild);
        ClearContainer(_projectiles);
        ClearContainer(_pickups);
    }

    private static void ClearContainer(Node c)
    {
        if (c == null || !IsInstanceValid(c)) return;
        foreach (Node n in c.GetChildren())
        {
            c.RemoveChild(n);
            n.QueueFree();
        }
    }

    public void AddStructureVisual(string kind)
    {
        if (_structures == null || !IsInstanceValid(_structures)) return;
        int n = _structures.GetChildCount();
        var holder = new Node2D
        {
            Position = new Vector2(-3.0f + (n * 1.4f) % 6.0f,
                GameDefs.LayerY(0) - 0.45f),
        };
        holder.AddChild(ArtBank.MakeBlock(new Vector2(0.7f, 0.7f),
            new Color(0.62f, 0.88f, 0.70f)));
        _structures.AddChild(holder);
    }

    public void SpawnWild(SpeciesDef def, int day)
    {
        if (_wild == null || !IsInstanceValid(_wild) || Player == null) return;
        float dist = GameRoot.SmokeMode
            ? _rng.RandfRange(0.9f, 1.6f)      // 冒烟注入：极短夜里保证弹道能命中
            : _rng.RandfRange(3.2f, 10.0f);
        float side = _rng.Randf() < 0.5f ? 1.0f : -1.0f;
        float x = Mathf.Clamp(Player.GlobalPosition.X + side * dist,
            -GameDefs.WorldHalfWidth, GameDefs.WorldHalfWidth);
        float topY = GameDefs.LayerY(GameDefs.LayerCount - 1);
        float bottomY = GameDefs.LayerY(0) - 0.6f;
        float y = Mathf.Clamp(Player.GlobalPosition.Y
            + _rng.RandfRange(-1.0f, 1.0f) * GameDefs.LayerSpacing, topY, bottomY);
        var s = new Spirit();
        _wild.AddChild(s);
        s.Setup(def, 1, Faction.Wild, day, new Vector2(x, y));
        WildSpawned++;
        WildSpeciesSpawned.Add(def.Id);
    }

    public Projectile? SpawnProjectile(Vector2 pos, Vector2 dir, float speed, int dmg,
        float life, Color color, Faction side, bool pierce, bool homing)
    {
        if (_projectiles == null || !IsInstanceValid(_projectiles)) return null;
        var p = new Projectile();
        _projectiles.AddChild(p);
        p.Setup(pos, dir, speed, dmg, life, color, side, pierce, homing);
        ProjectilesFired++;
        return p;
    }

    /// <summary>
    /// @trace WPN-001 幻灵对幻兽的伤害结算单点。伤害来自幻灵自己的攻击（含冷却），
    /// 不存在「幻灵师身边某个范围内统一结算」。这里同时记录统计供冒烟校验。
    /// </summary>
    public void DealDamage(Spirit target, int raw, AttackKind kind)
    {
        if (target == null || !IsInstanceValid(target)) return;
        int dmg = Mathf.Max(1, raw);
        target.TakeDamage(dmg);
        AllyDamageDealt += dmg;
        AllyHits++;
        switch (kind)
        {
            case AttackKind.Melee: AllyMeleeHits++; break;
            case AttackKind.Bolt: AllyBoltHits++; break;
            case AttackKind.Aura: AllyAuraHits++; break;
            default: AllyChainHits++; break;
        }
    }

    /// <summary>
    /// @trace FOLLOW-001 冒烟证据：幻灵到幻灵师的距离极差（max-min）。
    /// 「均分圆环」会接近 0；松散随从必然有明显离散。
    /// </summary>
    public float SpiritRadiusSpread()
    {
        if (Player == null || !IsInstanceValid(Player) || _bound == null || !IsInstanceValid(_bound))
            return 0.0f;
        float min = float.MaxValue, max = 0.0f;
        int n = 0;
        foreach (Node c in _bound.GetChildren())
        {
            if (c is not Spirit s || !IsInstanceValid(s)) continue;
            float d = Player.GlobalPosition.DistanceTo(s.GlobalPosition);
            min = Mathf.Min(min, d);
            max = Mathf.Max(max, d);
            n++;
        }
        return n >= 2 ? max - min : 0.0f;
    }

    public void SpawnSlash(Vector2 pos, float radius)
    {
        if (_projectiles == null || !IsInstanceValid(_projectiles)) return;
        var fx = ArtBank.MakeBlock(new Vector2(radius * 2.0f, 0.10f),
            new Color(1.0f, 0.9f, 0.6f, 0.55f));
        fx.Position = new Vector2(-radius, -0.05f);
        var holder = new Node2D { Position = pos };
        holder.AddChild(fx);
        _projectiles.AddChild(holder);
        SceneTreeTimer timer = GetTree().CreateTimer(0.12);
        timer.Timeout += () => { if (IsInstanceValid(holder)) holder.QueueFree(); };
    }

    public void OnWildKilled(SpeciesDef def, Vector2 at)
    {
        if (_pickups == null || !IsInstanceValid(_pickups)) return;
        int essence = GameDefs.EssenceFor(def.Tier);
        int coins = 1 + (int)def.Tier;
        int soul = _rng.Randf() < GameDefs.SoulChance(def.Tier) ? 1 : 0;
        Vector2 dropAt = GameRoot.SmokeMode && Player != null && IsInstanceValid(Player)
            ? Player.GlobalPosition : at;
        var e = new EssencePickup();
        _pickups.AddChild(e);
        e.Setup(dropAt, GameDefs.LayerY(0) - 0.25f, essence, coins, soul);
        PickupsSpawned++;
    }

    public void OnPickupCollected() => PickupsCollected++;

    public override void _Process(double delta)
    {
        if (Player == null || !IsInstanceValid(Player) || _camera == null) return;
        // @trace GND-001 运行时证据：玩家必须真的被第一层实体地板接住
        if (Player.IsOnFloor()) PlayerGrounded = true;
        Vector2 half = ViewSizeWorld() * 0.5f;
        float cx = Mathf.Clamp(Player.GlobalPosition.X,
            -GameDefs.WorldHalfWidth + half.X, GameDefs.WorldHalfWidth - half.X);
        float cy = Mathf.Min(GameDefs.LayerY(0) - half.Y * 0.4f,
            Player.GlobalPosition.Y - 1.0f);
        _camera.GlobalPosition = _camera.GlobalPosition.Lerp(new Vector2(cx, cy),
            Mathf.Clamp((float)delta * GameDefs.CameraSmooth, 0.0f, 1.0f));
    }
}
