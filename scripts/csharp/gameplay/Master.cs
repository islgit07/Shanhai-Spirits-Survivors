using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace MASTER-001 / JUMP-001 山海谷幻灵师。
/// 学习 BM 的核心约束：主角完全不攻击，只负责走位 / 跳跃 / 拾取，全部输出由幻灵完成。
/// 纯平台跳跃：A/D + Space + CharacterBody2D 真实重力；第一层全覆盖硬约束保持不变。
/// </summary>
public partial class Master : CharacterBody2D
{
    private float _invuln;
    private Sprite2D _sprite = null!;
    private static readonly StringName Left = new("move_left");
    private static readonly StringName Right = new("move_right");
    private static readonly StringName Jump = new("jump");

    public override void _Ready()
    {
        AddToGroup("master");
        CollisionLayer = 1u;
        CollisionMask = 1u;
        var shape = new RectangleShape2D { Size = GameDefs.MasterSize };
        AddChild(new CollisionShape2D { Shape = shape });
        _sprite = ArtBank.MakeSprite("player", GameDefs.MasterSize);
        AddChild(_sprite);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        float dir = Input.GetAxis(Left, Right);
        Velocity = new Vector2(dir * GameDefs.MasterMoveSpeed, Velocity.Y);
        if (IsOnFloor())
        {
            Velocity = new Vector2(Velocity.X, 0.0f);
            if (Input.IsActionJustPressed(Jump))
                Velocity = new Vector2(Velocity.X, -GameDefs.MasterJumpSpeed);
        }
        else
        {
            Velocity = new Vector2(Velocity.X, Velocity.Y + GameDefs.Gravity * dt);
        }
        MoveAndSlide();

        // @trace GND-001 水平活动域锁在第一层全覆盖地板之内
        float edge = GameDefs.WorldHalfWidth - GameDefs.MasterSize.X * 0.5f;
        GlobalPosition = new Vector2(Mathf.Clamp(GlobalPosition.X, -edge, edge), GlobalPosition.Y);
        // @trace AXIS-001 y-down：坠落是 y 增大
        if (GlobalPosition.Y > GameDefs.FallKillY)
        {
            GlobalPosition = new Vector2(GlobalPosition.X,
                GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);
            Velocity = Vector2.Zero;
        }
        if (_invuln > 0.0f)
        {
            _invuln -= dt;
            _sprite.Modulate = _invuln > 0.0f
                ? new Color(1.0f, 0.55f, 0.55f) : new Color(1, 1, 1);
        }
    }

    public void TakeDamage(int amount)
    {
        if (_invuln > 0.0f || Run.Phase == RunPhase.Dead || Run.Phase == RunPhase.Menu) return;
        Run.Damage(amount);
        _invuln = GameDefs.MasterInvuln;
        if (Run.Hp <= 0) GameRoot.Instance?.PlayerDied();
    }
}
