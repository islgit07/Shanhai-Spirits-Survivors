
using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

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
    /// <summary>下穿单向平台窗口（秒）：触发后关闭平台碰撞的时长。取值须满足：
    /// 窗口内下落距离（≈2.2）能穿过本层（脚越过台缘 ≈0.9），又小于最小层高差（2.6）以免掉过下一层。</summary>
    public float DropThroughTime = GameDefs.MasterDropThroughTime;
    /// <summary>自动上攀（CLIMB-001）：台顶可高于脚底的最大距离（单位）。</summary>
    public float ClimbReachUp = GameDefs.MasterClimbReachUp;
    /// <summary>自动上攀：台顶可低于脚底的最大距离（单位）。</summary>
    public float ClimbReachDown = GameDefs.MasterClimbReachDown;
    /// <summary>自动上攀：平台边缘外探测带宽（单位）。</summary>
    public float ClimbEdgeBand = GameDefs.MasterClimbEdgeBand;
    /// <summary>自动上攀：翻上后向台内推进距离（单位）。</summary>
    public float ClimbStep = GameDefs.MasterClimbStep;
    /// <summary>自动上攀：两次翻越最小间隔（秒）。</summary>
    public float ClimbCooldown = GameDefs.MasterClimbCooldown;

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
        MaxFallSpeed = MaxFallSpeed, DropThroughTime = DropThroughTime,
        ClimbReachUp = ClimbReachUp, ClimbReachDown = ClimbReachDown,
        ClimbEdgeBand = ClimbEdgeBand, ClimbStep = ClimbStep, ClimbCooldown = ClimbCooldown,
    };
}

/// <summary>
/// 平台移动内核：游戏主角与白箱角色都走这一份代码，保证「白箱手感 == 游戏手感」。
/// 只负责水平速度 / 起跳 / 重力 / 自动上攀，不含受伤、边界与坠落重生（那些留在各自的角色里）。
///
/// @trace JUMP-002 终焉之莉莉式跳跃：变高跳(上升松键截断) + 顶点滞空 + 下落加重 + 下落限速。
/// @trace DROP-001 类银式下穿：站在单向平台上「下 + 跳」穿层落下；实体地板不受影响。
/// @trace CLIMB-001 终焉之莉莉式自动上攀：撞到上层单向平台边缘附近时，几何台沿探测并自动拉上台面。
/// </summary>
public sealed class PlatformMover
{
    /// <summary>可攀台沿：一段平台台面的水平区间与顶面 y（仅单向平台）。由地形喂入。</summary>
    public readonly struct Ledge
    {
        public readonly float X0;
        public readonly float X1;
        public readonly float Top;
        public Ledge(float x0, float x1, float top) { X0 = x0; X1 = x1; Top = top; }
    }

    public MoveTuning T;

    private float _coyote;
    private float _buffer;
    private bool _cutAvailable;   // 本次起跳后，是否还能触发一次「变高跳截断」
    private float _dropTimer;     // 下穿窗口倒计时：>0 期间与「单向平台层」的碰撞被关闭
    private float _climbCooldown; // 自动上攀冷却：>0 期间不触发下一次翻越（防抖）
    private List<Ledge> _ledges = new();   // 台沿列表（CLIMB-001）

    public bool Grounded { get; private set; }
    public bool JustJumped { get; private set; }

    public PlatformMover(MoveTuning tuning)
    {
        T = tuning;
    }

    /// <summary>注入地形台沿（CLIMB-001）。地形建好后由角色调用一次。</summary>
    public void SetLedges(List<Ledge> ledges) => _ledges = ledges ?? new List<Ledge>();

    /// <summary>
    /// 推进一步。
    /// jumpPressed = 本帧刚按下跳跃（用于起跳 / 写入缓冲）；
    /// jumpHeld = 跳跃键当前是否仍按住（用于变高跳：上升中松键即截断）；
    /// downHeld = 下键当前是否按住（配合 jumpPressed 在单向平台上触发下穿）。
    /// </summary>
    public void Step(CharacterBody2D body, float dirX, bool jumpPressed, bool jumpHeld, bool downHeld, float dt)
    {
        JustJumped = false;
        Grounded = body.IsOnFloor();

        // 土狼时间 / 跳跃缓冲：默认 0 时退化成「必须站在地上且当帧按跳」，与旧行为完全一致
        _coyote = Grounded ? T.CoyoteTime : Mathf.Max(0.0f, _coyote - dt);
        if (jumpPressed) _buffer = Mathf.Max(T.JumpBuffer, 0.02f);
        else _buffer = Mathf.Max(0.0f, _buffer - dt);

        // @trace DROP-001 类银式下穿：站在单向平台上按住「下」+ 本帧按「跳」→ 吞掉这次跳跃，
        // 改为穿透台面落下。窗口内只关「单向平台层」的碰撞位，实体地板（LayerSolid）不受影响。
        // 窗口时长足够穿过本层（脚越过台缘 ≈0.9）又够不到最小层高差的下一层（2.6），落下后正好被下一层接住。
        _dropTimer = Mathf.Max(0.0f, _dropTimer - dt);
        bool dropThrough = Grounded && downHeld && jumpPressed && StandingOnOneWay(body);
        if (dropThrough)
        {
            jumpPressed = false;
            _buffer = 0.0f; _coyote = 0.0f;
            _dropTimer = T.DropThroughTime;
        }
        if (_dropTimer > 0.0f) body.CollisionMask &= ~GameDefs.LayerOneWay;
        else body.CollisionMask |= GameDefs.LayerOneWay;

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
                body.Velocity = dropThrough
                    ? new Vector2(body.Velocity.X, 2.0f)    // 下穿：给一点向下初速，立即脱离台面
                    : new Vector2(body.Velocity.X, 0.0f);   // 贴地：不累积重力
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

        // 移动后的真实落地状态（用于本帧末尾的台沿判定与外部查询）
        bool onFloorNow = body.IsOnFloor();
        Grounded = onFloorNow;

        // @trace CLIMB-001 几何台沿探测：撞到上层单向平台边缘附近时自动翻上去。
        // 仅在空中、有水平输入、且冷却结束时触发；命中即把脚底拉上台顶并向台内推一步。
        _climbCooldown = Mathf.Max(0.0f, _climbCooldown - dt);
        if (!onFloorNow && dirX != 0.0f && _climbCooldown <= 0.0f && TryClimb(body, dirX))
        {
            _climbCooldown = T.ClimbCooldown;
            _cutAvailable = false;
            JustJumped = false;
            Grounded = true;   // 本帧视为已上台，避免下一帧落地前的误判
        }
    }

    /// <summary>
    /// 纯几何台沿探测（CLIMB-001）。给定角色位置、碰撞半高与水平输入方向，遍历台沿列表，
    /// 命中则返回拉上去后的吸附位置（脚底贴台顶 + 向台内推进 ClimbStep）。
    /// 判定：① 台顶在脚底附近（上可达 reachUp、下可达 reachDown）；
    ///       ② 角色中心落在平台「边缘探测带」内（从左侧逼近落在 [X0-band, X0+step]，右侧反之）；
    ///       ③ 朝台内方向移动（dirX 与逼近方向一致）。
    /// 不依赖碰撞/射线，因此上层单向平台侧面不挡人也能稳定触发。
    /// </summary>
    public static bool ComputeClimb(Vector2 pos, float halfH, float dirX, List<Ledge> ledges,
        float reachUp, float reachDown, float edgeBand, float step, out Vector2 snap)
    {
        snap = pos;
        if (dirX == 0.0f || ledges == null) return false;
        float footY = pos.Y + halfH;
        foreach (Ledge L in ledges)
        {
            // 垂直：台顶须在脚底附近（高于脚底不超过 reachUp，低于脚底不超过 reachDown）
            if (L.Top < footY - reachUp) continue;
            if (L.Top > footY + reachDown) continue;
            // 水平：角色中心须落在平台边缘探测带内（窄带，避免干扰从下方正常穿台）
            if (dirX > 0.0f)
            {
                if (pos.X < L.X0 - edgeBand || pos.X > L.X0 + step) continue;
            }
            else
            {
                if (pos.X > L.X1 + edgeBand || pos.X < L.X1 - step) continue;
            }
            // 命中：把脚底拉上台顶，并向台内推进 step 防立即掉边
            snap = new Vector2(pos.X + dirX * step, L.Top - halfH);
            return true;
        }
        return false;
    }

    /// <summary>用当前台沿列表与调参，对真实角色做一次探测与吸附。</summary>
    private bool TryClimb(CharacterBody2D body, float dirX)
    {
        if (_ledges.Count == 0) return false;
        if (ComputeClimb(body.GlobalPosition, GameDefs.MasterSize.Y * 0.5f, dirX, _ledges,
                T.ClimbReachUp, T.ClimbReachDown, T.ClimbEdgeBand, T.ClimbStep, out Vector2 snap))
        {
            body.GlobalPosition = snap;
            body.Velocity = new Vector2(body.Velocity.X, 0.0f);
            return true;
        }
        return false;
    }

    /// <summary>脚下（脚底向下 0.25）是否踩在单向平台层上。射线只查 LayerOneWay，
    /// 实体地板不命中 → 站实体地板「下 + 跳」仍是正常跳，不会误吞跳跃。</summary>
    private static bool StandingOnOneWay(CharacterBody2D body)
    {
        float halfH = GameDefs.MasterSize.Y * 0.5f;
        Vector2 from = body.GlobalPosition;
        var query = PhysicsRayQueryParameters2D.Create(
            from, from + new Vector2(0.0f, halfH + 0.25f),
            GameDefs.LayerOneWay,
            new Godot.Collections.Array<Rid> { body.GetRid() });
        return body.GetWorld2D().DirectSpaceState.IntersectRay(query).Count > 0;
    }

    /// <summary>重生 / 复位时清空窗口状态，避免复活瞬间误起跳 / 误翻越。</summary>
    public void ResetWindows()
    {
        _coyote = 0.0f;
        _buffer = 0.0f;
        _cutAvailable = false;
        _dropTimer = 0.0f;
        _climbCooldown = 0.0f;
    }
}
