
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace JUMP-001 山海谷幻灵师（v0.0001：只有移动与跳跃，无战斗、无受伤）。
/// A/D + Space 真实重力 + S/↓ 下穿单向平台；第一层全覆盖硬约束保持不变。
/// 移动内核与白箱共用 PlatformMover：白箱里调出的手感 = 游戏里的手感。
/// </summary>
public partial class Master : CharacterBody2D
{
    private Sprite2D _sprite = null!;
    // 移动内核与白箱共用（见 PlatformMover）：白箱里调出的数值写回 GameDefs 即生效
    private readonly PlatformMover _mover = new(MoveTuning.GameDefaults());
    private static readonly StringName Left = new("move_left");
    private static readonly StringName Right = new("move_right");
    private static readonly StringName Jump = new("jump");
    private static readonly StringName Down = new("move_down");

    public override void _Ready()
    {
        AddToGroup("master");
        CollisionLayer = 1u;
        CollisionMask = GameDefs.LayerSolid | GameDefs.LayerOneWay;
        // 碰撞体在 Player.tscn 里摆（CapsuleShape2D，包围盒 = GameDefs.MasterSize 0.8×1.2）
        _sprite = ArtBank.MakeSprite("player", GameDefs.MasterSize);
        AddChild(_sprite);
    }


    /// <summary>重生 / 冒烟传送辅助：清空移动内核窗口状态（土狼 / 缓冲 / 下穿）。</summary>
    public void ResetMover() => _mover.ResetWindows();

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        float dir = Input.GetAxis(Left, Right);
        _mover.Step(this, dir, Input.IsActionJustPressed(Jump), Input.IsActionPressed(Jump),
            Input.IsActionPressed(Down), dt);

        // @trace GND-001 水平活动域锁在第一层全覆盖地板之内
        float edge = GameDefs.WorldHalfWidth - GameDefs.MasterSize.X * 0.5f;
        GlobalPosition = new Vector2(Mathf.Clamp(GlobalPosition.X, -edge, edge), GlobalPosition.Y);
        // @trace AXIS-001 y-down：坠落是 y 增大
        if (GlobalPosition.Y > GameDefs.FallKillY)
        {
            GlobalPosition = new Vector2(GlobalPosition.X,
                GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);
            Velocity = Vector2.Zero;
            _mover.ResetWindows();
        }
    }
}
