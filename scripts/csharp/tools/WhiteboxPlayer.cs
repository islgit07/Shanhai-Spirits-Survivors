using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 白箱角色：只做「跑 + 跳」，移动完全走 PlatformMover（与游戏主角同一份内核）。
/// 独立脚本文件（一个场景节点 = 一个脚本类），这样 Whitebox.tscn 的 Player 节点
/// 可以直接在 Godot 编辑器里编辑碰撞体 / 相机。
/// </summary>
public partial class WhiteboxPlayer : CharacterBody2D, IBounceable
{
    // 与 Master.cs 一致：输入动作名缓存为 StringName，避免每帧构造。
    private static readonly StringName Left = new("move_left");
    private static readonly StringName Right = new("move_right");
    private static readonly StringName Jump = new("jump");
    private static readonly StringName Down = new("move_down");

    private MoveTuning _tuning = MoveTuning.GameDefaults();

    /// <summary>调参对象：赋值时同步到移动内核，保证「白箱滑条 = 实机手感」。</summary>
    public MoveTuning Tuning
    {
        get => _tuning;
        set
        {
            _tuning = value;
            if (_mover != null) _mover.T = value;
        }
    }

    /// <summary>本次起跳后的实测最高点（脱离地面那一刻开始计）。</summary>
    public float MeasuredApex { get; private set; }

    private readonly PlatformMover _mover = null!;
    private Sprite2D _placeholder = null!;
    private float _jumpStartY;
    private bool _tracking;

    public bool Grounded => _mover != null && _mover.Grounded;

    /// <summary>注入地形台沿（CLIMB-001 自动上攀用），由白箱在地形建好后调用。</summary>
    public void SetLedges(List<PlatformMover.Ledge> ledges) => _mover.SetLedges(ledges);

    /// <summary>注入方块几何（蔚蓝式转角修正用），由白箱在地形建好后调用。</summary>
    public void SetGeometry(IBlockGeometry geometry) => _mover.SetGeometry(geometry);

    /// <summary>弹跳标志：由 BlockField 触碰感应置位，Step 之后应用（与游戏主角一致）。</summary>
    public bool BounceRequested;

    public void Bounce() => BounceRequested = true;

    // 注意：字段初始化顺序，_mover 依赖 Tuning，因此在 _Ready 里构造
    public WhiteboxPlayer()
    {
        _mover = new PlatformMover(MoveTuning.GameDefaults());
    }

    public override void _Ready()
    {
        AddToGroup("master");   // BlockField 触碰感应（脆弱/弹跳/宝箱）按此组识别
        CollisionLayer = 1u;
        CollisionMask = GameDefs.LayerSolid | GameDefs.LayerOneWay;
        _mover.T = _tuning;

        // 零 PNG：场景里的 Visual 只带 modulate；纹理用运行时 2×2 白图铺成角色尺寸。
        _placeholder = GetNodeOrNull<Sprite2D>("Visual")
            ?? ArtBank.MakeBox(GameDefs.MasterSize, new Color(0.85f, 0.88f, 0.95f));
        _placeholder.Texture = ArtBank.PlainTexture();
        _placeholder.Scale = GameDefs.MasterSize / 2f;
        if (_placeholder.GetParent() == null) AddChild(_placeholder);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        float dir = Input.GetAxis(Left, Right);
        _mover.Step(this, dir, Input.IsActionJustPressed(Jump), Input.IsActionPressed(Jump),
            Input.IsActionPressed(Down), dt);

        if (BounceRequested)
        {
            BounceRequested = false;
            Velocity = new Vector2(Velocity.X, -GameDefs.BounceSpeed);
        }

        if (_mover.JustJumped)
        {
            _tracking = true;
            _jumpStartY = GlobalPosition.Y;
            MeasuredApex = 0.0f;
        }
        else if (_tracking)
        {
            MeasuredApex = Mathf.Max(MeasuredApex, _jumpStartY - GlobalPosition.Y);
            if (_mover.Grounded) _tracking = false;
        }

        if (GlobalPosition.Y > GameDefs.FallKillY) Respawn();
    }

    public void Respawn()
    {
        GlobalPosition = new Vector2(0.0f, GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);
        Velocity = Vector2.Zero;
        MeasuredApex = 0.0f;
        _tracking = false;
        _mover.ResetWindows();
    }

    public override void _Process(double delta)
    {
        // @trace GND-001 世界边界：与游戏主角一致，锁在方块地图之内
        float edge = GameDefs.WorldHalfWidth - GameDefs.MasterSize.X * 0.5f;
        GlobalPosition = new Vector2(Mathf.Clamp(GlobalPosition.X, -edge, edge), GlobalPosition.Y);
    }
}
