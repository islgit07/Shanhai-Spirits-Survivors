using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace MONSTER-001 地面追击怪（v0.0007）：幸存者玩法的第一个敌人。纯躲避——怪没有血量，
/// 碰到玩家就造成接触伤害（无敌窗口由 Master 内部消化），玩家靠跑跳甩开。
/// 复用 PlatformMover 移动内核：粘液减速 / 弹跳块弹射 / 转角修正与主角同口径——
/// 加入 EnemyGroup 后 BlockField 触碰感应会按 IBounceable 调 Bounce()（设计预留，零新增接线）。
/// 实例由 MonsterField 对象池管理：激活时全量重置状态，回收时隐藏并移出屏外，
/// 正常游玩绝不 Instantiate / QueueFree（GodotPrompter 池化规范）。
/// </summary>
public partial class Monster : CharacterBody2D, IBounceable
{
    private readonly PlatformMover _mover = new(new MoveTuning
    {
        MoveSpeed = GameDefs.MonsterMoveSpeed,
        JumpSpeed = GameDefs.MonsterJumpSpeed,
    });

    private Master? _player;
    private bool _active;
    private bool _touchingPlayer;   // Touch 感应区与玩家重叠（entered/exited 信号维护，逐帧零重叠查询）
    private bool _bounceRequested;  // 弹跳块请求：Step 之后再应用（先应用会被贴地分支当帧清零 Y）
    private float _stuckTime;
    private float _jumpCooldown;    // 自救跳冷却
    private float _chaseJumpCooldown;   // 追层跳冷却
    private Sprite2D? _sprite;
    private Tween? _fxTween;        // 受击闪烁 / 死亡淡出共用（互斥，后启动的先 Kill 前一个）
    private float _spriteScale = 1f;   // 贴图等比缩放（Ready 计算；死亡淡出改缩放后由 Activate 还原）

    /// <summary>当前血量（随从近战扣减，归零即死亡：停 AI、掉魂魄、淡出回池）。</summary>
    public int Hp { get; private set; } = GameDefs.MonsterHp;

    /// <summary>是否已死亡（停 AI / 停接触伤害；淡出由 BeginDeath 驱动，回池归 MonsterField）。</summary>
    public bool Dead { get; private set; }

    /// <summary>血量归零（MonsterField 监听：移出 Active、掉魂魄、淡出后回池）。</summary>
    [Signal] public delegate void DiedEventHandler();

    /// <summary>是否处于激活（场上追击）状态（池诊断 / 冒烟读取）。</summary>
    public bool Active => _active;

    public override void _Ready()
    {
        AddToGroup(GameDefs.EnemyGroup);
        CollisionLayer = GameDefs.LayerActor;
        CollisionMask = GameDefs.LayerSolid | GameDefs.LayerOneWay | GameDefs.LayerActor;
        MotionMode = MotionModeEnum.Grounded;
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        if (_sprite != null)
        {
            // 等比缩放到 MonsterSize（不拉伸），与 SpiritPet.ApplyLook 同口径
            var tex = _sprite.Texture;
            if (tex != null)
            {
                _spriteScale = GameDefs.MonsterSize.Y / Mathf.Max(1f, tex.GetHeight());
                _sprite.Scale = new Vector2(_spriteScale, _spriteScale);
            }
        }
        var touch = GetNodeOrNull<Area2D>("Touch");
        if (touch != null)
        {
            touch.BodyEntered += OnTouchEntered;
            touch.BodyExited += OnTouchExited;
        }
        Deactivate();
    }

    /// <summary>弹跳块触发（BlockField 感应区按 EnemyGroup 调用）：只置标志，Step 之后应用。</summary>
    public void Bounce() => _bounceRequested = true;

    /// <summary>
    /// 池激活：全量重置状态（GodotPrompter 池规范——位置由池先设好），接入追击目标与方块几何。
    /// </summary>
    public void Activate(Master player, IBlockGeometry geometry)
    {
        _player = player;
        _mover.SetGeometry(geometry);
        _mover.ResetWindows();
        _touchingPlayer = false;
        _bounceRequested = false;
        _stuckTime = 0f;
        _jumpCooldown = 0f;
        _chaseJumpCooldown = 0f;
        Velocity = Vector2.Zero;
        Hp = GameDefs.MonsterHp;
        Dead = false;
        _fxTween?.Kill();
        _fxTween = null;
        if (_sprite != null)
        {
            _sprite.FlipH = false;
            _sprite.Modulate = Colors.White;
            _sprite.Scale = Vector2.One * _spriteScale;
        }
        _active = true;
        Visible = true;
        SetPhysicsProcess(true);
    }

    /// <summary>池回收：停处理、隐藏；位置由池统一挪到屏外。正常游玩绝不 QueueFree。</summary>
    public void Deactivate()
    {
        _active = false;
        _touchingPlayer = false;
        _bounceRequested = false;
        Velocity = Vector2.Zero;
        Visible = false;
        SetPhysicsProcess(false);
    }

    /// <summary>
    /// 受击入口（随从近战组件调用）。无无敌帧——多只随从同时命中可叠加；
    /// 受击白闪反馈（HDR 提亮后回落）；血量归零 → Dead + Died（MonsterField 掉魂魄并回池）。
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (Dead || amount <= 0) return;
        Hp -= amount;
        if (_sprite != null)
        {
            _fxTween?.Kill();
            _fxTween = CreateTween();
            _fxTween.TweenProperty(_sprite, "modulate", Colors.White, 0.12f)
                .From(new Color(2.2f, 2.2f, 2.2f, 1f));
        }
        if (Hp > 0) return;
        Hp = 0;
        Dead = true;
        EmitSignal(SignalName.Died);
    }

    /// <summary>死亡淡出（MonsterField 在 Died 后调用）：停处理，缩小淡出；回池由 MonsterField 定时器执行。</summary>
    public void BeginDeath()
    {
        SetPhysicsProcess(false);
        _fxTween?.Kill();
        if (_sprite == null) return;
        _fxTween = CreateTween();
        _fxTween.SetParallel(true);
        _fxTween.TweenProperty(_sprite, "modulate:a", 0.0f, GameDefs.MonsterDeathFadeTime);
        _fxTween.TweenProperty(_sprite, "scale", Vector2.One * (_spriteScale * 0.4f), GameDefs.MonsterDeathFadeTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_active || Dead || _player == null || !IsInstanceValid(_player)) return;
        float dt = (float)delta;

        // ---- 追击方向：朝玩家水平逼近（死区内停，防贴脸抖动）
        float dx = _player.GlobalPosition.X - GlobalPosition.X;
        float dir = Mathf.Abs(dx) > 0.1f ? Mathf.Sign(dx) : 0f;
        bool wantJump = ShouldJumpToChase(dt, dir);

        _mover.Step(this, dir, wantJump, false, false, dt);

        // 弹跳块：Step 之后再应用弹射速度（贴地逻辑会把 Y 清零，弹射必须后发制人）
        if (_bounceRequested)
        {
            _bounceRequested = false;
            _mover.ApplyExternalLaunch(this, GameDefs.BounceSpeed);
        }

        // ---- 接触伤害：每帧尝试一次，无敌窗口由 Master 消化（怪侧无计时器、零分配）
        if (_touchingPlayer)
            _player.TakeDamage(GameDefs.MonsterContactDamage);

        // ---- 朝向：按水平移动方向（enemy_shambler.png 素材默认朝右）
        if (_sprite != null && Mathf.Abs(Velocity.X) > 0.05f)
            _sprite.FlipH = Velocity.X < 0f;
    }

    /// <summary>
    /// 起跳决策：① 卡住自救——想走却走不动（台沿 / 拐角 / 被挡）超过 MonsterStuckTime；
    /// ② 追层——玩家实际高于自身超过 MonsterChaseUpThreshold 且水平够近。
    /// 跳高约 2 行（玩家满跳 3.4 行），玩家垂直爬层就能甩开，保留「躲」的空间。
    /// </summary>
    private bool ShouldJumpToChase(float dt, float dir)
    {
        _jumpCooldown = Mathf.Max(0f, _jumpCooldown - dt);
        _chaseJumpCooldown = Mathf.Max(0f, _chaseJumpCooldown - dt);
        if (!IsOnFloor()) return false;

        if (dir != 0f && Mathf.Abs(Velocity.X) < 0.3f)
        {
            _stuckTime += dt;
            if (_stuckTime > GameDefs.MonsterStuckTime && _jumpCooldown <= 0f)
            {
                _stuckTime = 0f;
                _jumpCooldown = GameDefs.MonsterStuckJumpCooldown;
                return true;
            }
        }
        else _stuckTime = 0f;

        if (_player != null)
        {
            float dy = _player.GlobalPosition.Y - GlobalPosition.Y;   // y-down：负 = 玩家更高
            if (dy < -GameDefs.MonsterChaseUpThreshold
                && Mathf.Abs(_player.GlobalPosition.X - GlobalPosition.X) < 2.0f
                && _chaseJumpCooldown <= 0f)
            {
                _chaseJumpCooldown = GameDefs.MonsterStuckJumpCooldown;
                return true;
            }
        }
        return false;
    }

    private void OnTouchEntered(Node body)
    {
        if (body == _player) _touchingPlayer = true;
    }

    private void OnTouchExited(Node body)
    {
        if (body == _player) _touchingPlayer = false;
    }
}
