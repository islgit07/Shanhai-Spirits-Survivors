using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 平台移动参数（单点定义）。游戏与白箱共用同一份结构：
/// 白箱里调出来的数值，写回 GameDefs 后就是游戏里的手感，不会再走样。
///
/// 默认值取自 GameDefs（与当前游戏一致）。本版以「蔚蓝（Celeste）式响应」为手感目标：
///   · 加速度曲线：地面/空中独立加减速 + 反向输入转向加成 —— 起停跟手
///   · 空中松开方向键只轻微减速（EndTraction）—— 保留跳跃动量
///   · 变高跳（短按矮、长按高）保留 —— 松键截断上升
///   · 顶点滞空默认关闭（ApexGravityMul = 1.0）—— 弧线干脆无浮空
///   · 下落比上升更重（fall &gt; 1）+ 最大下落速度封顶
///   · 蔚蓝式转角修正：上升撞到方块角时自动横移滑过，而不是撞头掉下来
/// 土狼时间 / 跳跃缓冲 / 下穿仍保留，给落地手感兜底。
/// </summary>
public sealed class MoveTuning
{
    public float MoveSpeed = GameDefs.MasterMoveSpeed;
    public float JumpSpeed = GameDefs.MasterJumpSpeed;
    public float Gravity = GameDefs.Gravity;
    /// <summary>地面加速度（单位/秒²）：0→满速 ≈ MoveSpeed/Accel 秒。</summary>
    public float GroundAccel = GameDefs.MasterGroundAccel;
    /// <summary>地面减速度（单位/秒²）：松键刹车。</summary>
    public float GroundDecel = GameDefs.MasterGroundDecel;
    /// <summary>空中加速度（单位/秒²）。</summary>
    public float AirAccel = GameDefs.MasterAirAccel;
    /// <summary>空中减速度基准（单位/秒²）：松开方向键时 × EndTraction。</summary>
    public float AirDecel = GameDefs.MasterAirDecel;
    /// <summary>反向输入时加速度倍率（急转身）。</summary>
    public float TurnAccelMul = GameDefs.MasterTurnAccelMul;
    /// <summary>空中松开方向键的减速倍率：越小动量保留越多。</summary>
    public float EndTraction = GameDefs.MasterEndTraction;
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
    /// <summary>转角修正：上升撞头时允许的最大自动横移（单位）。0 = 关闭。</summary>
    public float CornerCorrectionMax = GameDefs.MasterCornerCorrectionMax;
    /// <summary>转角修正后与方块的保留间隙（单位）。</summary>
    public float CornerCorrectionSkin = GameDefs.MasterCornerCorrectionSkin;
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
    /// <summary>自动上攀总开关（CLIMB-001）。默认取 GameDefs（当前整体关闭）；白箱可单独打开预调。</summary>
    public bool AutoClimbEnabled = GameDefs.MasterAutoClimbEnabled;
    // MAP-009 粘液块：站在粘液上减速，起跳只弹起一点点后立刻被黏回地面。
    public float SlimeSpeedMul = GameDefs.SlimeSpeedMul;
    public float SlimeAccelMul = GameDefs.SlimeAccelMul;
    public float SlimeHopSpeed = GameDefs.SlimeHopSpeed;
    public float SlimeHopGravity = GameDefs.SlimeHopGravity;
    public float SlimeSnapGravity = GameDefs.SlimeSnapGravity;
    public float SlimeSnapMaxFall = GameDefs.SlimeSnapMaxFall;
    public float SlimeSnapTime = GameDefs.SlimeSnapTime;

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
        GroundAccel = GroundAccel, GroundDecel = GroundDecel,
        AirAccel = AirAccel, AirDecel = AirDecel,
        TurnAccelMul = TurnAccelMul, EndTraction = EndTraction,
        CoyoteTime = CoyoteTime, JumpBuffer = JumpBuffer,
        JumpCutMul = JumpCutMul, FallGravityMul = FallGravityMul,
        ApexGravityMul = ApexGravityMul, ApexThreshold = ApexThreshold,
        MaxFallSpeed = MaxFallSpeed, DropThroughTime = DropThroughTime,
        CornerCorrectionMax = CornerCorrectionMax, CornerCorrectionSkin = CornerCorrectionSkin,
        ClimbReachUp = ClimbReachUp, ClimbReachDown = ClimbReachDown,
        ClimbEdgeBand = ClimbEdgeBand, ClimbStep = ClimbStep, ClimbCooldown = ClimbCooldown,
        AutoClimbEnabled = AutoClimbEnabled,
        SlimeSpeedMul = SlimeSpeedMul, SlimeAccelMul = SlimeAccelMul,
        SlimeHopSpeed = SlimeHopSpeed, SlimeHopGravity = SlimeHopGravity,
        SlimeSnapGravity = SlimeSnapGravity, SlimeSnapMaxFall = SlimeSnapMaxFall,
        SlimeSnapTime = SlimeSnapTime,
    };
}

/// <summary>
/// 平台移动内核：游戏主角与白箱角色都走这一份代码，保证「白箱手感 == 游戏手感」。
/// 只负责水平速度 / 起跳 / 重力 / 自动上攀，不含受伤、边界与坠落重生（那些留在各自的角色里）。
///
/// @trace JUMP-002 终焉之莉莉式跳跃：变高跳(上升松键截断) + 顶点滞空 + 下落加重 + 下落限速。
/// @trace DROP-001 类银式下穿：站在单向平台上「下 + 跳」穿层落下；实体地板不受影响。
/// @trace CLIMB-001 终焉之莉莉式自动上攀：撞到上层单向平台边缘附近时，几何台沿探测并自动拉上台面。
/// @trace MAP-009 粘液块：脚下地板在 SlimeBlockGroup 时水平减速；按跳只给极小上跳，
///   随后 SlimeSnapGravity 把这点高度瞬间压回地面（"跳起来被立刻黏住回去"）。
///   效果全在本内核里，Master / WhiteboxPlayer 零改动，保证「白箱手感 == 游戏手感」。
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
    private bool _onOneWayFloor;  // 脚下地面是否单向平台（站立静止帧沿用上次结论）
    private bool _onSlimeFloor;   // MAP-009：脚下地面是否粘液块（站立静止帧沿用上次结论）
    private float _slimeSnap;     // MAP-009：>0 期间处于「黏回」窗口，用超大重力压回地面
    private bool _externalLaunch; // BOUNCE-JELLY：外部弹射后的一帧仍按空中处理，避免被贴地清零 Y
    private List<Ledge> _ledges = new();   // 台沿列表（CLIMB-001）
    private IBlockGeometry? _geometry;     // 方块几何（转角修正用）
    private static readonly StringName SlimeGroup = new(GameDefs.SlimeBlockGroup);

    public bool Grounded { get; private set; }
    public bool JustJumped { get; private set; }

    public PlatformMover(MoveTuning tuning)
    {
        T = tuning;
    }

    /// <summary>注入地形台沿（CLIMB-001）。地形建好后由角色调用一次。</summary>
    public void SetLedges(List<Ledge> ledges) => _ledges = ledges ?? new List<Ledge>();

    /// <summary>注入方块几何（转角修正用）。由地形建好后由角色调用。</summary>
    public void SetGeometry(IBlockGeometry geometry) => _geometry = geometry;

    /// <summary>
    /// BOUNCE-JELLY：外部弹射（弹跳块）。在 Step 之后调用，设置上抛速度并标记下一帧仍按空中处理——
    /// 否则下一帧 IsOnFloor() 还是 true，贴地分支会把 vy 直接清零，弹射永远飞不起来。
    /// </summary>
    public void ApplyExternalLaunch(CharacterBody2D body, float speed)
    {
        body.Velocity = new Vector2(body.Velocity.X, -Mathf.Abs(speed));
        _externalLaunch = true;
    }

    /// <summary>
    /// 推进一步。
    /// jumpPressed = 本帧刚按下跳跃（用于起跳 / 写入缓冲）；
    /// jumpHeld = 跳跃键当前是否仍按住（用于变高跳：上升中松键即截断）；
    /// downHeld = 下键当前是否按住（配合 jumpPressed 在单向平台上触发下穿）。
    /// </summary>
    public void Step(CharacterBody2D body, float dirX, bool jumpPressed, bool jumpHeld, bool downHeld, float dt)
    {
        JustJumped = false;
        // BOUNCE-JELLY：上一帧由弹跳块外部弹射过 → 本帧即便 IsOnFloor() 仍为 true 也按空中处理，
        // 保证 -BounceSpeed 不被贴地分支清零；标记一次性消费。
        bool launched = _externalLaunch;
        _externalLaunch = false;
        Grounded = body.IsOnFloor() && !launched;
        // MAP-009：本帧脚下是否粘液。站立静止帧（vy=0 无新滑动碰撞）沿用上次结论。
        bool slimeFloor = _onSlimeFloor;
        _slimeSnap = Mathf.Max(0.0f, _slimeSnap - dt);

        // 土狼时间 / 跳跃缓冲：默认 0 时退化成「必须站在地上且当帧按跳」，与旧行为完全一致
        _coyote = Grounded ? T.CoyoteTime : Mathf.Max(0.0f, _coyote - dt);
        if (jumpPressed) _buffer = Mathf.Max(T.JumpBuffer, 0.02f);
        else _buffer = Mathf.Max(0.0f, _buffer - dt);

        // @trace DROP-001 类银式下穿：站在单向平台上按住「下」+ 本帧按「跳」→ 吞掉这次跳跃，
        // 改为穿透台面落下。窗口内只关「单向平台层」的碰撞位，实体地板（LayerSolid）不受影响。
        // 窗口时长足够穿过本层（脚越过台缘 ≈0.9）又够不到最小层高差的下一层（2.6），落下后正好被下一层接住。
        _dropTimer = Mathf.Max(0.0f, _dropTimer - dt);
        bool dropThrough = Grounded && downHeld && jumpPressed && _onOneWayFloor;
        if (dropThrough)
        {
            jumpPressed = false;
            _buffer = 0.0f; _coyote = 0.0f;
            _dropTimer = T.DropThroughTime;
        }
        if (_dropTimer > 0.0f) body.CollisionMask &= ~GameDefs.LayerOneWay;
        else body.CollisionMask |= GameDefs.LayerOneWay;

        // MAP-009：粘液块上（含黏回窗口）水平减速——顶速与加/减速同时降低。
        bool slimeActive = slimeFloor || _slimeSnap > 0.0f;
        body.Velocity = new Vector2(ApplyHorizontal(body.Velocity.X, dirX, Grounded, dt, slimeActive),
            body.Velocity.Y);

        if (Grounded)
        {
            if (_buffer > 0.0f)
            {
                // 粘液上按跳：只给极小上跳（看得见"跳起来"），随即进入黏回窗口被压回去。
                float jumpVy = slimeFloor ? -T.SlimeHopSpeed : -T.JumpSpeed;
                body.Velocity = new Vector2(body.Velocity.X, jumpVy);
                _buffer = 0.0f; _coyote = 0.0f; JustJumped = true;
                _cutAvailable = !slimeFloor;                  // 黏跳不参与变高跳截断
                if (slimeFloor) _slimeSnap = T.SlimeSnapTime;
            }
            else
            {
                if (_slimeSnap > 0.0f) _slimeSnap = 0.0f;     // 已被黏回地面，窗口提前结束
                body.Velocity = dropThrough
                    ? new Vector2(body.Velocity.X, 2.0f)    // 下穿：给一点向下初速，立即脱离台面
                    : new Vector2(body.Velocity.X, 0.0f);   // 贴地：不累积重力
            }
        }
        else
        {
            if (_buffer > 0.0f && _coyote > 0.0f && _slimeSnap <= 0.0f)
            {
                body.Velocity = new Vector2(body.Velocity.X, -T.JumpSpeed);
                _buffer = 0.0f; _coyote = 0.0f; JustJumped = true; _cutAvailable = true;
            }
            else if (_slimeSnap > 0.0f)
            {
                // MAP-009 黏回：上升用略大重力压低最高点，一到顶点就用超大重力瞬间拽回地面。
                body.Velocity = new Vector2(body.Velocity.X,
                    ComputeSlimeVertical(body.Velocity.Y, dt, T));
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

        // 蔚蓝式转角修正：上升中头部碰到方块时，若角部另一侧有横向空间，
        // 自动横移滑过角部（保留 CornerCorrectionSkin 间隙），替代生硬的撞头反弹。
        TryCornerCorrection(body);

        // 移动后的真实落地状态（用于本帧末尾的台沿判定与外部查询）
        bool onFloorNow = body.IsOnFloor();
        Grounded = onFloorNow;
        UpdateFloorContact(body, onFloorNow);

        // @trace CLIMB-001 几何台沿探测：撞到上层单向平台边缘附近时自动翻上去。
        // 仅在空中、有水平输入、且冷却结束时触发；命中即把脚底拉上台顶并向台内推一步。
        _climbCooldown = Mathf.Max(0.0f, _climbCooldown - dt);
        if (!onFloorNow && dirX != 0.0f && _climbCooldown <= 0.0f && TryClimb(body, dirX))
        {
            _climbCooldown = T.ClimbCooldown;
            _cutAvailable = false;
            JustJumped = false;
            Grounded = true;   // 本帧视为已上台，避免下一帧落地前的误判
            _onOneWayFloor = false;   // 翻上台沿后脚下地面类型未知，下帧落地判定刷新
            _onSlimeFloor = false;
        }
    }

    /// <summary>
    /// 蔚蓝式水平运动：加速度逼近目标速度，反向输入 × TurnAccelMul（急转身）。
    /// 空中松开方向键时只以 AirDecel × EndTraction 轻减速（保留跳跃动量）。
    /// MAP-009：slime = true 时顶速 × SlimeSpeedMul、加/减速 × SlimeAccelMul（走上去明显变慢）。
    /// </summary>
    private float ApplyHorizontal(float current, float dirX, bool grounded, float dt, bool slime)
    {
        float speedMul = slime ? T.SlimeSpeedMul : 1.0f;
        float accelMul = slime ? T.SlimeAccelMul : 1.0f;
        if (dirX != 0.0f)
        {
            float target = dirX * T.MoveSpeed * speedMul;
            float accel = (current * dirX < 0.0f
                ? (grounded ? T.GroundAccel : T.AirAccel) * T.TurnAccelMul   // 反向 = 急转身
                : (grounded ? T.GroundAccel : T.AirAccel)) * accelMul;
            return Mathf.MoveToward(current, target, accel * dt);
        }
        float decel = (grounded ? T.GroundDecel : T.AirDecel * T.EndTraction) * accelMul;
        return Mathf.MoveToward(current, 0.0f, decel * dt);
    }

    /// <summary>
    /// MAP-009 纯逻辑：黏回窗口内一帧的垂直速度积分（Step 与无头探针共用，保证验证的就是实跑逻辑）。
    /// 上升段用 SlimeHopGravity 压低最高点；到顶点后用 SlimeSnapGravity 瞬间拽回；
    /// 下压速度封顶 SlimeSnapMaxFall，防止一帧穿块。
    /// </summary>
    public static float ComputeSlimeVertical(float vy, float dt, MoveTuning t)
    {
        float g = vy < 0.0f ? t.SlimeHopGravity : t.SlimeSnapGravity;
        float next = vy + g * dt;
        return next > t.SlimeSnapMaxFall ? t.SlimeSnapMaxFall : next;   // 只封顶向下速度，上升段不受影响
    }

    /// <summary>MAP-009 纯逻辑：粘液块上的水平目标速度（减速倍率可直接断言）。</summary>
    public static float ComputeSlimeTargetSpeed(float dirX, MoveTuning t)
        => dirX * t.MoveSpeed * t.SlimeSpeedMul;

    /// <summary>
    /// MAP-009 纯逻辑：模拟一次完整黏跳，返回最高离地高度（y-down 取反）与窗口内是否被翻成向下速度。
    /// 起跳帧的当帧位移也计入（Step 在 Grounded 分支直接给 vy，MoveAndSlide 立刻按它移动）。
    /// Step / 无头冒烟 / 多种子探针共用这一份模型，避免三处积分口径漂移。
    /// </summary>
    public static void SimulateSlimeHop(MoveTuning t, float dt, out float apex, out bool snapped)
    {
        int steps = Mathf.CeilToInt(t.SlimeSnapTime / dt);
        float vy = -t.SlimeHopSpeed;
        float y = vy * dt;
        apex = -y;
        snapped = false;
        for (int i = 0; i < steps; i++)
        {
            vy = ComputeSlimeVertical(vy, dt, t);
            y += vy * dt;
            apex = Mathf.Max(apex, -y);
            if (vy > 0.0f) snapped = true;
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
        if (!T.AutoClimbEnabled) return false;   // 总开关：默认取 GameDefs（当前关闭）；白箱可单独打开预调
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

    /// <summary>
    /// 转角修正（蔚蓝式）：仅上升撞到方块时生效。身体大部分已越过方块边缘
    /// （交叠 ≤ CornerCorrectionMax）→ 沿空侧横移刚好脱离方块（保留 skin 间隙）；
    /// 身体居中或深撞时照常撞头，绝不长距离瞬移。
    /// </summary>
    private void TryCornerCorrection(CharacterBody2D body)
    {
        IBlockGeometry? geometry = _geometry;
        if (T.CornerCorrectionMax <= 0.0f || geometry == null) return;
        if (body.Velocity.Y >= 0.0f) return;   // 只修上升撞头
        if (body.GetSlideCollisionCount() == 0) return;

        float halfW = GameDefs.MasterSize.X * 0.5f;
        for (int i = 0; i < body.GetSlideCollisionCount(); i++)
        {
            var ci = body.GetSlideCollision(i);
            if (ci.GetNormal().Y <= 0.7f) continue;                 // 只要天花板（y-down：法线朝下）
            if (ci.GetCollider() is not StaticBody2D block) continue;
            float bcX = block.GlobalPosition.X;                     // 1×1 方块中心
            float px0 = body.GlobalPosition.X - halfW;
            float px1 = body.GlobalPosition.X + halfW;
            float bx0 = bcX - 0.5f, bx1 = bcX + 0.5f;
            float overlapLeft = px1 - bx0;                          // 身体右缘探入方块左缘的深度
            float overlapRight = bx1 - px0;                         // 身体左缘探入方块右缘的深度
            float dx;
            if (px0 < bx0 && overlapLeft > 0.0f && overlapLeft <= T.CornerCorrectionMax)
                dx = -(overlapLeft + T.CornerCorrectionSkin);       // 身体挂在方块左缘 → 向左滑出
            else if (px1 > bx1 && overlapRight > 0.0f && overlapRight <= T.CornerCorrectionMax)
                dx = overlapRight + T.CornerCorrectionSkin;         // 身体挂在方块右缘 → 向右滑出
            else continue;                                          // 居中或深撞 → 正常撞头
            if (!SpanClear(geometry, body, body.GlobalPosition.X + dx, halfW)) continue;
            body.GlobalPosition = new Vector2(body.GlobalPosition.X + dx, body.GlobalPosition.Y);
            break;
        }
    }

    /// <summary>修正后身体水平跨度在头部所在行及其上一行不得再有实体块。</summary>
    private bool SpanClear(IBlockGeometry geometry, CharacterBody2D body, float newX, float halfW)
    {
        float headY = body.GlobalPosition.Y - GameDefs.MasterSize.Y * 0.5f;
        for (int r = -1; r <= 0; r++)
        {
            float y = headY + r * 0.5f;
            for (float px = newX - halfW; px <= newX + halfW; px += 0.2f)
            {
                if (geometry.WorldToCell(new Vector2(px, y), out int row, out int col)
                    && geometry.IsBlocked(row, col))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 记录脚下地面类型（是否单向平台 / 是否粘液块）：读本次 MoveAndSlide 的地板碰撞体。
    /// 站立静止帧（vy=0 无新滑动碰撞）沿用上一次结论，保证「站着按 下+跳」可靠下穿，
    /// 也保证站在粘液块上不动时减速不会抖动。判定本体收口在 SampleFloorContact，
    /// 与 SpiritFollow 随从跟随共用同一份（原两份逐字同构实现，P2 收口）。
    /// </summary>
    private void UpdateFloorContact(CharacterBody2D body, bool onFloorNow)
    {
        if (!onFloorNow)
        {
            _onOneWayFloor = false;
            _onSlimeFloor = false;
            return;
        }
        var contact = SampleFloorContact(body);
        if (contact == null) return;   // 站立静止帧沿用上一次结论
        _onOneWayFloor = contact.Value.OneWay;
        _onSlimeFloor = contact.Value.Slime;
    }

    /// <summary>
    /// 采样本次 MoveAndSlide 的地板碰撞类型：是否单向平台（可主动下穿）/ 是否粘液块（减速黏跳）。
    /// 没有朝上的地板碰撞（站立静止帧）返回 null = 调用方沿用上一次结论；
    /// 粘液块靠碰撞体组别识别（BlockField 实例化时加入），跨块站立时任一粘液块都算。
    /// </summary>
    public static (bool OneWay, bool Slime)? SampleFloorContact(CharacterBody2D body)
    {
        bool found = false, oneWay = false, slime = false;
        for (int i = 0; i < body.GetSlideCollisionCount(); i++)
        {
            var ci = body.GetSlideCollision(i);
            if (ci.GetNormal().Y >= -0.7f) continue;   // 只看地板法线（朝上）
            if (ci.GetCollider() is PhysicsBody2D pb)
            {
                found = true;
                if ((pb.CollisionLayer & GameDefs.LayerOneWay) != 0u) oneWay = true;
                if (pb.IsInGroup(SlimeGroup)) slime = true;
            }
        }
        return found ? (oneWay, slime) : null;
    }

    /// <summary>重生 / 复位时清空窗口状态，避免复活瞬间误起跳 / 误翻越 / 残留黏滞。</summary>
    public void ResetWindows()
    {
        _coyote = 0.0f;
        _buffer = 0.0f;
        _cutAvailable = false;
        _dropTimer = 0.0f;
        _climbCooldown = 0.0f;
        _onOneWayFloor = false;
        _onSlimeFloor = false;
        _slimeSnap = 0.0f;
    }
}

/// <summary>
/// 角色输入驱动的共用注入层：游戏主角（Master）与白箱角色（WhiteboxPlayer）的 _PhysicsProcess
/// 原先各写一份逐字相同的「读输入轴 + 推进移动内核 + 弹跳后发制人」。收口到内核旁一处，
/// 保证「白箱手感 == 游戏手感」在注入层也成立（改手感注入只改这一处）。
/// </summary>
public static class MoverDriver
{
    private static readonly StringName Left = new("move_left");
    private static readonly StringName Right = new("move_right");
    private static readonly StringName Jump = new("jump");
    private static readonly StringName Down = new("move_down");

    /// <summary>
    /// 读输入并推进一步移动内核；若本次物理帧有弹跳块请求，在 Step 之后应用弹射
    /// （先应用会被贴地逻辑当帧把 Y 清零）。调用方随后自行清掉自己的弹跳请求标志。
    /// </summary>
    public static void StepBody(CharacterBody2D body, PlatformMover mover, double delta, bool bounceRequested)
    {
        float dt = (float)delta;
        float dir = Input.GetAxis(Left, Right);
        mover.Step(body, dir, Input.IsActionJustPressed(Jump), Input.IsActionPressed(Jump),
            Input.IsActionPressed(Down), dt);

        if (bounceRequested)
            mover.ApplyExternalLaunch(body, GameDefs.BounceSpeed);
    }
}
