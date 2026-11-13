
using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace JUMP-001 山海谷幻灵师（v0.0001：只有移动与跳跃，无战斗、无受伤）。
/// A/D + Space 真实重力 + S/↓ 下穿单向平台；第一层全覆盖硬约束保持不变。
/// 移动内核与白箱共用 PlatformMover：白箱里调出的手感 = 游戏里的手感。
/// </summary>
public partial class Master : CharacterBody2D, IBounceable
{
    private Sprite2D _sprite = null!;
    private Vector2 _spriteBaseScale;
    private bool _wasGrounded = true;
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
        _spriteBaseScale = _sprite.Scale;
        AddChild(_sprite);
    }


    /// <summary>重生 / 冒烟传送辅助：清空移动内核窗口状态（土狼 / 缓冲 / 下穿 / 翻越冷却）。</summary>
    public void ResetMover() => _mover.ResetWindows();

    /// <summary>注入地形台沿（CLIMB-001 自动上攀用），由 GameRoot 在地形建好后调用。</summary>
    public void SetLedges(List<PlatformMover.Ledge> ledges) => _mover.SetLedges(ledges);

    /// <summary>弹跳标志：由 BlockField 触碰感应置位；在 Step 之后应用（先应用会被贴地逻辑清零 Y）。</summary>
    public bool BounceRequested;

    public void Bounce() => BounceRequested = true;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        float dir = Input.GetAxis(Left, Right);
        _mover.Step(this, dir, Input.IsActionJustPressed(Jump), Input.IsActionPressed(Jump),
            Input.IsActionPressed(Down), dt);

        // 弹跳方块：Step 之后再应用弹射速度（贴地逻辑会把 Y 清零，弹射必须后发制人）
        if (BounceRequested)
        {
            BounceRequested = false;
            Velocity = new Vector2(Velocity.X, -GameDefs.BounceSpeed);
        }

        // 表现层反馈：落地挤压 / 起跳拉伸 + 脚下灰尘（纯视觉，不碰物理）
        bool groundedNow = IsOnFloor();
        if (!_wasGrounded && groundedNow) OnLanded();
        else if (_mover.JustJumped) OnJumped();
        _wasGrounded = groundedNow;

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

    private void OnLanded()
    {
        _sprite.Scale = new Vector2(_spriteBaseScale.X * 1.22f, _spriteBaseScale.Y * 0.78f);
        var tw = CreateTween();
        tw.TweenProperty(_sprite, "scale", _spriteBaseScale, 0.14f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        SpawnDust(1.0f);
    }

    private void OnJumped()
    {
        _sprite.Scale = new Vector2(_spriteBaseScale.X * 0.82f, _spriteBaseScale.Y * 1.18f);
        var tw = CreateTween();
        tw.TweenProperty(_sprite, "scale", _spriteBaseScale, 0.16f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        SpawnDust(0.7f);
    }

    /// <summary>脚下灰尘：小矩形原地膨胀 + 淡出后自毁（strength 1.0 = 落地，0.7 = 起跳）。</summary>
    private void SpawnDust(float strength)
    {
        var dust = new ColorRect
        {
            Color = new Color(0.90f, 0.88f, 0.82f, 0.50f * strength),
            Size = new Vector2(0.7f, 0.25f),
        };
        dust.Position = new Vector2(-dust.Size.X * 0.5f, GameDefs.MasterSize.Y * 0.5f - dust.Size.Y * 0.5f);
        dust.PivotOffset = dust.Size * 0.5f;
        AddChild(dust);
        var tw = CreateTween().SetParallel();
        tw.TweenProperty(dust, "scale", new Vector2(1.9f, 1.7f), 0.22f).From(Vector2.One)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(dust, "modulate:a", 0.0f, 0.22f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.Chain().TweenCallback(Callable.From(dust.QueueFree));
    }
}
