using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// 平台移动参数（单点定义）。游戏与白箱共用同一份结构：
/// 白箱里调出来的数值，写回 GameDefs 后就是游戏里的手感，不会再走样。
///
/// 默认值取自 GameDefs（与当前游戏一致）。本版以「复刻终焉之莉莉」的手感为目标：
///   · 变高跳（短按矮、长按高）—— 莉莉系 2D 动作游戏的核心跳跃手感
///   · 顶点滞空（apex hang）—— 上升/下落穿过顶点附近时降低重力，悬停感更柔
///   · 下落比上升更重（fall &gt; 1）—— 落得快、起得飘，整体更「轻盈可控」
///   · 最大下落速度封顶 —— 防止长坠落失控
/// 土狼时间 / 跳跃缓冲 / 空中控制仍保留，给落地手感兜底。
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
    /// <summary>变高跳截断：上升中松开跳跃键时，把上升速度乘这个系数（一次）。
    /// 1.0 = 关闭变高跳（松键不影响）；越小短按跳得越低。莉莉式短跳 ≈ 0.45。</summary>
    public float JumpCutMul = GameDefs.MasterJumpCutMul;
    /// <summary>下落重力倍率：下落段重力 = Gravity × 此值。&gt;1 让下落比上升更利落。</summary>
    public float FallGravityMul = GameDefs.MasterFallGravityMul;
    /// <summary>顶点滞空倍率：上升/下落穿过「顶点阈值」速度内时，重力 = Gravity × 此值（&lt;1 更飘）。</summary>
    public float ApexGravityMul = GameDefs.MasterApexGravityMul;
    /// <summary>顶点阈值：当 |垂直速度| 小于此值（单位/秒）时进入滞空区，应用 ApexGravityMul。</summary>
    public float ApexThreshold = GameDefs.MasterApexThreshold;
    /// <summary>最大下落速度（单位/秒）：封顶终端速度，防止长坠落失控。</summary>
    public float MaxFallSpeed = GameDefs.MasterMaxFallSpeed;

    public static MoveTuning GameDefaults() => new();

    /// <summary>理论满跳高度（不截断、全程长按）。顶点滞空区会多给一点高度，这里精确计入。</summary>
    public float JumpHeight
    {
        get
        {
            float V = JumpSpeed, g = Gravity, t = ApexThreshold, a = ApexGravityMul;
            if (t <= 0.0f || a >= 1.0f)
                return V * V / (2.0f * Mathf.Max(0.01f, g));
            float hNormal = (V * V - t * t) / (2.0f * g);   // 正常重力段 V → t
            float hApex = (t * t) / (2.0f * g * a);          // 顶点滞空段 t → 0
            return hNormal + hApex;
        }
    }

    /// <summary>理论满跳滞空时间（秒）。</summary>
    public float AirTime
    {
        get
        {
            float V = JumpSpeed, g = Gravity, t = ApexThreshold, a = ApexGravityMul, f = FallGravityMul;
            if (t <= 0.0f)
                return 2.0f * V / Mathf.Max(0.01f, g);
            float rise = (V - t) / g + t / (g * a);
            float vf = Mathf.Sqrt(t * t + f * (V * V - t * t));   // 落地瞬间速度
            float fall = t / (g * a) + (vf - t) / (g * f);
            return rise + fall;
        }
    }

    /// <summary>理论短按高度（变高跳）：高度 ∝ 速度平方，截断后 ≈ 满跳 × JumpCutMul²。</summary>
    public float ShortHopHeight => JumpHeight * JumpCutMul * JumpCutMul;

    /// <summary>理论水平跨距 = 移动速度 × 滞空时间（满速起跳）。</summary>
    public float JumpSpan => MoveSpeed * AirTime;

    public MoveTuning Clone() => new()
    {
        MoveSpeed = MoveSpeed, JumpSpeed = JumpSpeed, Gravity = Gravity,
        AirControl = AirControl, CoyoteTime = CoyoteTime, JumpBuffer = JumpBuffer,
        JumpCutMul = JumpCutMul, FallGravityMul = FallGravityMul,
        ApexGravityMul = ApexGravityMul, ApexThreshold = ApexThreshold,
        MaxFallSpeed = MaxFallSpeed,
    };
}

/// <summary>
/// 平台移动内核：游戏主角与白箱角色都走这一份代码，保证「白箱手感 == 游戏手感」。
/// 只负责水平速度 / 起跳 / 重力，不含受伤、边界与坠落重生（那些留在各自的角色里）。
///
/// @trace JUMP-002 终焉之莉莉式跳跃：变高跳(上升松键截断) + 顶点滞空 + 下落加重 + 下落限速。
/// </summary>
public sealed class PlatformMover
{
    public MoveTuning T;

    private float _coyote;
    private float _buffer;
    private bool _cutAvailable;   // 本次起跳后，是否还能触发一次「变高跳截断」

    public bool Grounded { get; private set; }
    public bool JustJumped { get; private set; }

    public PlatformMover(MoveTuning tuning)
    {
        T = tuning;
    }

    /// <summary>
    /// 推进一步。
    /// jumpPressed = 本帧刚按下跳跃（用于起跳 / 写入缓冲）；
    /// jumpHeld = 跳跃键当前是否仍按住（用于变高跳：上升中松键即截断）。
    /// </summary>
    public void Step(CharacterBody2D body, float dirX, bool jumpPressed, bool jumpHeld, float dt)
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
            if (_buffer > 0.0f)
            {
                body.Velocity = new Vector2(body.Velocity.X, -T.JumpSpeed);
                _buffer = 0.0f; _coyote = 0.0f; JustJumped = true; _cutAvailable = true;
            }
            else
            {
                body.Velocity = new Vector2(body.Velocity.X, 0.0f);   // 贴地：不累积重力
            }
        }
        else
        {
            if (_buffer > 0.0f && _coyote > 0.0f)
            {
                body.Velocity = new Vector2(body.Velocity.X, -T.JumpSpeed);
                _buffer = 0.0f; _coyote = 0.0f; JustJumped = true; _cutAvailable = true;
            }
            else
            {
                // 可变重力：上升段正常；下落段加重（FallGravityMul）；
                // 穿过顶点附近（|vy| < ApexThreshold）时降低重力，制造悬停飘感。
                float g = T.Gravity;
                float vy = body.Velocity.Y;
                if (vy < 0.0f)
                    g *= Mathf.Abs(vy) < T.ApexThreshold ? T.ApexGravityMul : 1.0f;
                else
                    g *= vy < T.ApexThreshold ? T.ApexGravityMul : T.FallGravityMul;
                vy += g * dt;
                if (vy > T.MaxFallSpeed) vy = T.MaxFallSpeed;   // 终端速度封顶
                body.Velocity = new Vector2(body.Velocity.X, vy);
            }
        }

        // 变高跳：本次起跳后，上升途中松开跳跃键 → 截断一次上升速度（短按矮跳）
        if (_cutAvailable && !jumpHeld && body.Velocity.Y < 0.0f)
        {
            body.Velocity = new Vector2(body.Velocity.X, body.Velocity.Y * T.JumpCutMul);
            _cutAvailable = false;
        }
        if (Grounded) _cutAvailable = false;

        body.MoveAndSlide();
    }

    /// <summary>重生 / 复位时清空窗口状态，避免复活瞬间误起跳。</summary>
    public void ResetWindows()
    {
        _coyote = 0.0f;
        _buffer = 0.0f;
        _cutAvailable = false;
    }
}
