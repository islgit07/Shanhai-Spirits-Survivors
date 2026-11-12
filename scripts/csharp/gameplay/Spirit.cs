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
/// </summary>
public partial class Spirit : Node2D
{
    public Faction Side;
    public SpeciesDef Def = null!;
    public int Level = 1;
    public int Uid;

    private int _hp;
    private int _damage;
    private float _speed;
    private float _attackTimer;
    private int _formIndex;
    private int _formCount;
    private Sprite2D _sprite = null!;

    public void Setup(SpeciesDef def, int level, Faction side, int day, Vector2 pos,
        int uid = 0, int formIndex = 0, int formCount = 1)
    {
        Def = def;
        Level = Mathf.Clamp(level, 1, GameDefs.MaxUnitLevel);
        Side = side;
        Uid = uid;
        _formIndex = formIndex;
        _formCount = Math.Max(1, formCount);
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
        float size = 0.9f * (1.0f + 0.12f * (Level - 1));
        _sprite = ArtBank.MakeSprite(def.Sprite, new Vector2(size, size));
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
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Node2D? master = GetTree().GetFirstNodeInGroup("master") as Node2D;
        if (master == null) return;
        if (Run.Phase != RunPhase.Night) return;

        if (Side == Faction.Wild) MoveWild(master, dt);
        else MoveBound(master, dt);

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
        GlobalPosition = new Vector2(
            GlobalPosition.X + Mathf.Clamp(to.X, -step, step),
            Mathf.Lerp(GlobalPosition.Y, master.GlobalPosition.Y + 0.35f,
                Mathf.Clamp(dt * 1.6f, 0.0f, 1.0f)));
    }

    private void MoveBound(Node2D master, float dt)
    {
        float angle = _formIndex * Mathf.Tau / _formCount;
        var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.55f) * 1.9f;
        Vector2 target = master.GlobalPosition + offset;
        GlobalPosition = GlobalPosition.Lerp(target, Mathf.Clamp(dt * 4.5f, 0.0f, 1.0f));
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
        Vector2 dir = (master.GlobalPosition - GlobalPosition).Normalized();
        SpiritWorld.Active?.SpawnProjectile(GlobalPosition, dir, 9.0f, _damage, 2.0f,
            new Color(1.0f, 0.55f, 0.45f), Faction.Wild, false, false);
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
                if (t != null) t.TakeDamage(_damage);
                break;
            }
            case AttackKind.Bolt:
            {
                Spirit? t = Nearest(wilds, GlobalPosition, range, null);
                if (t != null)
                    SpiritWorld.Active?.SpawnProjectile(GlobalPosition,
                        (t.GlobalPosition - GlobalPosition).Normalized(), 11.0f, _damage, 2.0f,
                        new Color(0.62f, 1.0f, 0.86f), Faction.Bound,
                        GameDefs.BoltPierces(Level), GameDefs.BoltHomes(Level));
                break;
            }
            case AttackKind.Aura:
            {
                int hit = 0;
                foreach (Spirit w in wilds)
                    if (GlobalPosition.DistanceTo(w.GlobalPosition) <= range)
                    { w.TakeDamage(_damage); hit++; }
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
                    cur.TakeDamage(dmg);
                    dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.8f));
                    Spirit? next = Nearest(wilds, cur.GlobalPosition, 3.5f, used);
                    cur = next;
                }
                break;
            }
        }
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
