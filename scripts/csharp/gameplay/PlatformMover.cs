using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// 平台移动参数（单点定义）。游戏与白箱共用同一份结构：
/// 白箱里调出来的数值，写回 GameDefs 后就是游戏里的手感，不会再走样。
///
/// 默认值取自 GameDefs（与当前游戏一致）；土狼时间 / 跳跃缓冲默认为 0，
/// 即「和现在完全一样」，只有在白箱里调大才会生效。
/// </summary>
public sealed class MoveTuning
{
    public float MoveSpeed = GameDefs.MasterMoveSpeed;
    public float JumpSpeed = GameDefs.MasterJumpSpeed;
    public float Gravity = GameDefs.Gravity;
    /// <summary>空中水平控制强度：1 = 与地面一样灵活（当前行为），0 = 起跳后锁死水平速度。</summary>
    public float AirControl = GameDefs.MasterAirControl;
    /// <summary>土狼时间：离开平台边缘后仍可起跳的宽限窗口（秒）。0 = 关闭。</summary>
    public float CoyoteTime = GameDefs.MasterCoyoteTime;
    /// <summary>跳跃缓冲：落地前提前按跳，落地瞬间自动起跳的窗口（秒）。0 = 关闭。</summary>
    public float JumpBuffer = GameDefs.MasterJumpBuffer;

    public static MoveTuning GameDefaults() => new();

    /// <summary>理论跳跃高度 = v²/(2g)。</summary>
    public float JumpHeight => JumpSpeed * JumpSpeed / (2.0f * Mathf.Max(0.01f, Gravity));

    /// <summary>理论滞空时间 = 2v/g。</summary>
    public float AirTime => 2.0f * JumpSpeed / Mathf.Max(0.01f, Gravity);

    /// <summary>理论水平跨距 = 移动速度 × 滞空时间（满速起跳）。</summary>
    public float JumpSpan => MoveSpeed * AirTime;

    public MoveTuning Clone() => new()
    {
        MoveSpeed = MoveSpeed, JumpSpeed = JumpSpeed, Gravity = Gravity,
        AirControl = AirControl, CoyoteTime = CoyoteTime, JumpBuffer = JumpBuffer,
    };
}

/// <summary>
/// 平台移动内核：游戏主角与白箱角色都走这一份代码，保证「白箱手感 == 游戏手感」。
/// 只负责水平速度 / 起跳 / 重力，不含受伤、边界与坠落重生（那些留在各自的角色里）。
/// </summary>
public sealed class PlatformMover
{
    public MoveTuning T;

    private float _coyote;
    private float _buffer;

    public bool Grounded { get; private set; }
    public bool JustJumped { get; private set; }

    public PlatformMover(MoveTuning tuning)
    {
        T = tuning;
    }

    public void Step(CharacterBody2D body, float dirX, bool jumpPressed, float dt)
    {
        JustJumped = false;
        Grounded = body.IsOnFloor();

        // 土狼时间 / 跳跃缓冲：默认 0 时退化成「必须站在地上且当帧按跳」，与旧行为完全一致
        _coyote = Grounded ? T.CoyoteTime : Mathf.Max(0.0f, _coyote - dt);
        if (jumpPressed) _buffer = Mathf.Max(T.JumpBuffer, 0.02f);
        else _buffer = Mathf.Max(0.0f, _buffer - dt);

        float control = Grounded ? 1.0f : T.AirControl;
        body.Velocity = new Vector2(dirX * T.MoveSpeed * control, body.Velocity.Y);

        if (Grounded)
        {
            body.Velocity = new Vector2(body.Velocity.X, 0.0f);
            if (_buffer > 0.0f)
            {
                body.Velocity = new Vector2(body.Velocity.X, -T.JumpSpeed);
                _buffer = 0.0f; _coyote = 0.0f; JustJumped = true;
            }
        }
        else
        {
            if (_buffer > 0.0f && _coyote > 0.0f)
            {
                body.Velocity = new Vector2(body.Velocity.X, -T.JumpSpeed);
                _buffer = 0.0f; _coyote = 0.0f; JustJumped = true;
            }
            else
            {
                body.Velocity = new Vector2(body.Velocity.X, body.Velocity.Y + T.Gravity * dt);
            }
        }

        body.MoveAndSlide();
    }

    /// <summary>重生 / 复位时清空窗口状态，避免复活瞬间误起跳。</summary>
    public void ResetWindows()
    {
        _coyote = 0.0f;
        _buffer = 0.0f;
    }
}