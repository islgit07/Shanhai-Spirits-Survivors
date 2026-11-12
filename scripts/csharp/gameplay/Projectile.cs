using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>@trace WPN-001 投射物：敌我共用，只按 Faction 切换命中组。</summary>
public partial class Projectile : Node2D
{
    public Vector2 Dir;
    public float Speed = 11.0f;
    public int Damage = 10;
    public float Life = 2.0f;
    public bool Pierce;
    public bool Homing;
    public Faction Side;

    private readonly HashSet<Node> _hit = new();

    public void Setup(Vector2 pos, Vector2 dir, float speed, int dmg, float life,
        Color color, Faction side, bool pierce, bool homing)
    {
        GlobalPosition = pos;
        Dir = dir.Normalized();
        Speed = speed;
        Damage = dmg;
        Life = life;
        Side = side;
        Pierce = pierce;
        Homing = homing;
        AddChild(ArtBank.MakeBlock(new Vector2(0.22f, 0.22f), color));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Life -= dt;
        if (Life <= 0.0f) { QueueFree(); return; }

        if (Homing)
        {
            Node2D? target = GetTree().GetNodesInGroup("wild").OfType<Node2D>()
                .Where(s => IsInstanceValid(s))
                .OrderBy(s => GlobalPosition.DistanceTo(s.GlobalPosition))
                .FirstOrDefault();
            if (target != null)
                Dir = Dir.Lerp((target.GlobalPosition - GlobalPosition).Normalized(),
                    Mathf.Clamp(dt * 6.0f, 0.0f, 1.0f)).Normalized();
        }

        GlobalPosition += Dir * Speed * dt;
        string group = Side == Faction.Wild ? "master" : "wild";
        foreach (Node n in GetTree().GetNodesInGroup(group))
        {
            if (_hit.Contains(n) || !IsInstanceValid(n) || n is not Node2D n2) continue;
            if (GlobalPosition.DistanceTo(n2.GlobalPosition) >= 0.6f) continue;
            _hit.Add(n);
            if (n is Spirit s)
            {
                if (Side == Faction.Bound) SpiritWorld.Active?.DealDamage(s, Damage, AttackKind.Bolt);
                else s.TakeDamage(Damage);
            }
            else if (n is Master m) m.TakeDamage(Damage);
            if (!Pierce) { QueueFree(); return; }
        }
    }
}
