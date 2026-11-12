using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace SPEC-001 / ECO-001 幻灵与野生幻兽是「同一种」：
///   同一个类、同一张 SpeciesDef、同一套数值与 Lv1~3 质变、同一张贴图。
///   Faction 只切换 AI 目标与色调，不派生子类、不复制属性表。
///   野生幻兽只会攻击幻灵师（对应 BM minion 不朽、meanies 追玩家）；
///   但敌我都可受击、都可死亡，结构完全对称。
///
/// @trace FOLLOW-001 跟随逻辑迁移自 Boneraiser Minions 的拆包结论（A 级依据 = data.win VARI 变量名）：
///   minion_slot / minion_vx_base / minion_vy_base / move_to_radius / sMinion_GetMoveSpeed /
///   vPushRadius / vBob_X|Y / vIdle_X|Y / vIdleSpot —— 每只召唤物有「自己的落点」，
///   离得远就加速追、靠近就停、彼此推开、静止时在闲逛点附近浮动。
///   所以幻灵不是均分圆环，而是一坨松散的随从群；横板下再把垂直方向压扁并夹在可行走高度内。
/// </summary>
public partial class Spirit : Node2D
{
    public Faction Side;
    public SpeciesDef Def = null!;
    public int Level = 1;
    public int Uid;

    /// <summary>上一帧位移速度（世界单位/秒）。远端弹道据此打提前量。</summary>
    public Vector2 LastVelocity { get; private set; } = Vector2.Zero;

    private int _hp;
    private int _damage;
    private float _speed;
    private float _attackTimer;
    private Sprite2D _sprite = null!;

    // ---- FOLLOW-001 个人落点状态
    private Vector2 _slotOffset;
    private Vector2 _wander;
    private Vector2 _wanderGoal;
    private float _wanderT;
    private float _phase;
    private float _bodySize = 0.9f;
    private bool _facingLeft;
    private Vector2 _lastTargetVelocity = Vector2.Zero;

    public void Setup(SpeciesDef def, int level, Faction side, int day, Vector2 pos, int uid = 0)
    {
        Def = def;
        Level = Mathf.Clamp(level, 1, GameDefs.MaxUnitLevel);
        Side = side;
        Uid = uid;
        GlobalPosition = pos;

        float scale = side == Faction.Wild
            ? 1.0f + GameDefs.HpScalePerDay * Math.Max(0, day - 1)
            : 1.0f;
        _hp = Mathf.RoundToInt(def.Hp * scale);
        _damage = side == Faction.Wild
            ? Mathf.RoundToInt(def.Damage * (1.0f + GameDefs.DamageScalePerDay * Math.Max(0, day - 1)))
            : Mathf.RoundToInt(def.Damage * GameDefs.LevelDamageMul(Level))
              + (GameRoot.SmokeMode ? 200 : 0);   // 冒烟注入：仅 --smoke，保证短夜必出击杀
        _speed = def.Speed * (side == Faction.Wild
            ? 1.0f + GameDefs.SpeedScalePerDay * Math.Max(0, day - 1) : 1.0f);

        AddToGroup(side == Faction.Wild ? "wild" : "bound");
        _bodySize = 0.9f * (1.0f + 0.12f * (Level - 1));
        _sprite = ArtBank.MakeSprite(def.Sprite, new Vector2(_bodySize, _bodySize));
        _sprite.Modulate = GameDefs.FactionTint(side);   // 同源可视证据：只改色调
        AddChild(_sprite);

        // Lv2 / Lv3 用顶点小点标记，不改贴图
        for (int i = 1; i < Level; i++)
        {
            var pip = ArtBank.MakeBlock(new Vector2(0.14f, 0.14f),
                new Color(1.0f, 0.86f, 0.4f));
            pip.Position = new Vector2(-0.16f + i * 0.16f, -0.58f);
            AddChild(pip);
        }
        _attackTimer = 0.15f;

        // FOLLOW-001 落点：黄金角散布（任意数量都散得开）+ 按 uid 分档半径 + 体积越大站得越外
        float ang = Mathf.PosMod(uid * 2.39996f, Mathf.Tau);
        float rad = GameDefs.FollowAnchorMin
                  + (uid % 5) * GameDefs.FollowAnchorStep
                  + _bodySize * GameDefs.FollowAnchorSizeMul;
        // 高低偏置：横板里全员贴地会排成一条线，交错开才像「一坨随从」
        float bias = (uid % 2 == 0 ? -1.0f : 1.0f) * GameDefs.FollowHeightBias * (1.0f + (uid % 3) * 0.35f);
        _slotOffset = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * GameDefs.FollowVerticalSquash) * rad
                    + new Vector2(0.0f, bias);
        _phase = (uid % 23) * 0.41f;
        _wanderT = (float)GD.RandRange(0.0, 1.8);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Node2D? master = GetTree().GetFirstNodeInGroup("master") as Node2D;
        if (master == null) return;
        if (Run.Phase != RunPhase.Night) return;

        Vector2 before = GlobalPosition;
        if (Side == Faction.Wild) MoveWild(master, dt);
        else MoveBound(master, dt);
        LastVelocity = (GlobalPosition - before) / Mathf.Max(0.0001f, dt);

        _attackTimer -= dt;
        if (_attackTimer > 0.0f) return;
        _attackTimer = Mathf.Max(0.12f, Def.Interval *
            (Side == Faction.Bound
                ? GameDefs.LevelIntervalMul(Level) * Run.AllyIntervalMul
                : 1.0f));
        if (Side == Faction.Wild) AttackMaster(master);
        else AttackWilds();
    }

    private void MoveWild(Node2D master, float dt)
    {
        Vector2 to = master.GlobalPosition - GlobalPosition;
        float step = _speed * Run.WildSlow * dt;
        Vector2 next = new(
            GlobalPosition.X + Mathf.Clamp(to.X, -step, step),
            Mathf.Lerp(GlobalPosition.Y, master.GlobalPosition.Y + 0.35f,
                Mathf.Clamp(dt * 1.6f, 0.0f, 1.0f)));
        GlobalPosition = ClampToField(next);
        FaceTo(master.GlobalPosition.X - GlobalPosition.X);
    }

    /// <summary>
    /// @trace FOLLOW-001 个人落点跟随：远了加速追、近了停下、互相推开、不糊在幻灵师身上。
    /// 横板适配：落点垂直方向压扁、并夹在第一层地板与最高层之间。
    /// </summary>
    private void MoveBound(Node2D master, float dt)
    {
        _wanderT -= dt;
        if (_wanderT <= 0.0f)
        {
            _wanderT = (float)GD.RandRange(1.2, 2.8);
            _wanderGoal = new Vector2(
                (float)GD.RandRange(-GameDefs.FollowWanderRadius, GameDefs.FollowWanderRadius),
                (float)GD.RandRange(-GameDefs.FollowWanderRadius, GameDefs.FollowWanderRadius)
                    * GameDefs.FollowVerticalSquash);
        }
        _wander = _wander.Lerp(_wanderGoal, Mathf.Clamp(dt * 0.85f, 0.0f, 1.0f));

        _phase += dt * 2.1f;
        var bob = new Vector2(Mathf.Cos(_phase * 0.7f) * GameDefs.FollowBobAmount * 0.5f,
            Mathf.Sin(_phase) * GameDefs.FollowBobAmount);

        Vector2 want = ClampToField(master.GlobalPosition + _slotOffset + _wander + bob);
        Vector2 to = want - GlobalPosition;
        float dist = to.Length();
        Vector2 before = GlobalPosition;

        // sMinion_GetMoveSpeed：离落点越远追得越快；move_to_radius：进入死区就停
        if (dist > GameDefs.FollowStopRadius)
        {
            float speed = Mathf.Clamp(GameDefs.FollowBaseSpeed + dist * GameDefs.FollowCatchUp,
                GameDefs.FollowBaseSpeed, GameDefs.FollowMaxSpeed);
            GlobalPosition += to / dist * speed * dt;
        }

        Separate(dt);                 // vPushRadius：互相推开
        ClearMaster(master, dt);      // 不许压在幻灵师身上
        GlobalPosition = ClampToField(GlobalPosition);

        FaceTo(GlobalPosition.X - before.X);
    }

    /// <summary>@trace FOLLOW-001 vPushRadius：同容器幻灵靠太近就互相推开。</summary>
    private void Separate(float dt)
    {
        Node? parent = GetParent();
        if (parent == null) return;
        float r = GameDefs.FollowPushRadius;
        foreach (Node n in parent.GetChildren())
        {
            if (ReferenceEquals(n, this) || n is not Spirit other || !IsInstanceValid(other)) continue;
            if (other.Side != Side) continue;
            Vector2 d = GlobalPosition - other.GlobalPosition;
            float len = d.Length();
            if (len < 0.001f)
            {
                GlobalPosition += new Vector2((float)GD.RandRange(-1.0, 1.0),
                    (float)GD.RandRange(-1.0, 1.0)) * 0.6f * dt;
                continue;
            }
            if (len < r)
                GlobalPosition += d / len * ((r - len) / r) * 5.2f * dt;
        }
    }

    /// <summary>与幻灵师保持净空，保证主角永远看得见。</summary>
    private void ClearMaster(Node2D master, float dt)
    {
        Vector2 d = GlobalPosition - master.GlobalPosition;
        float len = d.Length();
        float need = GameDefs.FollowMasterClear + _bodySize * 0.35f;
        if (len < need && len > 0.001f)
            GlobalPosition += d / len * (need - len) * Mathf.Min(1.0f, 12.0f * dt);
    }

    private static Vector2 ClampToField(Vector2 p)
    {
        float top = GameDefs.LayerY(GameDefs.LayerCount - 1) + 0.6f;
        float bottom = GameDefs.LayerY(0) - 0.30f;
        float edge = GameDefs.WorldHalfWidth - 0.4f;
        return new Vector2(Mathf.Clamp(p.X, -edge, edge), Mathf.Clamp(p.Y, top, bottom));
    }

    private void FaceTo(float dx)
    {
        if (Mathf.Abs(dx) < 0.002f || _sprite == null) return;
        _facingLeft = dx < 0.0f;
        _sprite.FlipH = _facingLeft;
    }

    private void AttackMaster(Node2D master)
    {
        float dist = GlobalPosition.DistanceTo(master.GlobalPosition);
        if (Def.Attack is AttackKind.Melee or AttackKind.Aura)
        {
            if (dist < 1.0f && master is Master m) m.TakeDamage(_damage);
            return;
        }
        if (dist > Def.Range) return;
        _lastTargetVelocity = Vector2.Zero;   // 幻灵师没有速度接口，直线瞄当前点即可
        Vector2 dir = LeadAim(master.GlobalPosition, 9.0f, false) - GlobalPosition;
        SpiritWorld.Active?.SpawnProjectile(GlobalPosition, dir.Normalized(), 9.0f, _damage, 2.0f,
            new Color(1.0f, 0.55f, 0.45f), Faction.Wild, false, false);
    }

    /// <summary>
    /// @trace WPN-001 远程攻击：直线弹打提前量（否则永远打空移动目标），追踪弹直接瞄当前位置。
    /// </summary>
    private Vector2 LeadAim(Vector2 targetPos, float projectileSpeed, bool homing)
    {
        if (homing) return targetPos;
        float flight = GlobalPosition.DistanceTo(targetPos) / Mathf.Max(0.01f, projectileSpeed);
        return targetPos + _lastTargetVelocity * flight * 0.9f;
    }

    private void AttackWilds()
    {
        var wilds = GetTree().GetNodesInGroup("wild")
            .OfType<Spirit>().Where(s => IsInstanceValid(s)).ToList();
        if (wilds.Count == 0) return;
        float range = Def.Range * (Def.Attack == AttackKind.Aura
            ? GameDefs.AuraRadiusMul(Level) : 1.0f);

        switch (Def.Attack)
        {
            case AttackKind.Melee:
            {
                Spirit? t = Nearest(wilds, GlobalPosition, range, null);
                if (t == null) return;
                FaceTo(t.GlobalPosition.X - GlobalPosition.X);
                // 近战是「一下扫击」：先在目标处出招，再结算伤害（伤害只结算一次）
                SpiritWorld.Active?.SpawnSlash(t.GlobalPosition, Mathf.Max(0.35f, _bodySize * 0.7f));
                Hit(t, Def.Attack);
                break;
            }
            case AttackKind.Bolt:
            {
                Spirit? t = Nearest(wilds, GlobalPosition, range, null);
                if (t == null) return;
                FaceTo(t.GlobalPosition.X - GlobalPosition.X);
                _lastTargetVelocity = t.LastVelocity;
                Vector2 aim = LeadAim(t.GlobalPosition, 11.0f, GameDefs.BoltHomes(Level));
                Vector2 dir = (aim - GlobalPosition).Normalized();
                SpiritWorld.Active?.SpawnProjectile(GlobalPosition, dir, 11.0f, _damage, 2.0f,
                    new Color(0.62f, 1.0f, 0.86f), Faction.Bound,
                    GameDefs.BoltPierces(Level), GameDefs.BoltHomes(Level));
                break;
            }
            case AttackKind.Aura:
            {
                int hit = 0;
                foreach (Spirit w in wilds)
                    if (GlobalPosition.DistanceTo(w.GlobalPosition) <= range)
                    { Hit(w, Def.Attack); hit++; }
                if (hit > 0) SpiritWorld.Active?.SpawnSlash(GlobalPosition, range);
                break;
            }
            case AttackKind.Chain:
            {
                var used = new HashSet<Spirit>();
                Spirit? cur = Nearest(wilds, GlobalPosition, range, null);
                int dmg = _damage;
                for (int i = 0; i < GameDefs.LevelChainJumps(Level) && cur != null; i++)
                {
                    used.Add(cur);
                    SpiritWorld.Active?.DealDamage(cur, dmg, Def.Attack);
                    dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.8f));
                    cur = Nearest(wilds, cur.GlobalPosition, 3.5f, used);
                }
                break;
            }
        }
    }

    private void Hit(Spirit target, AttackKind kind)
    {
        if (SpiritWorld.Active != null) SpiritWorld.Active.DealDamage(target, _damage, kind);
        else target.TakeDamage(_damage);
    }

    private static Spirit? Nearest(IEnumerable<Spirit> pool, Vector2 from, float maxDist,
        HashSet<Spirit>? exclude)
    {
        Spirit? best = null;
        float bestD = maxDist;
        foreach (Spirit s in pool)
        {
            if (exclude != null && exclude.Contains(s)) continue;
            float d = from.DistanceTo(s.GlobalPosition);
            if (d < bestD) { bestD = d; best = s; }
        }
        return best;
    }

    public void TakeDamage(int amount)
    {
        _hp -= amount;
        if (_hp <= 0) Die();
    }

    private void Die()
    {
        if (Side == Faction.Wild)
        {
            Run.Kills++;
            SpiritWorld.Active?.OnWildKilled(Def, GlobalPosition);
            Signals.EmitWildKilled(GameDefs.EssenceFor(Def.Tier));
        }
        else
        {
            Legion.Remove(Uid);
        }
        QueueFree();
    }
}