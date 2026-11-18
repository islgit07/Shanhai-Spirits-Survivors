using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace SPIRIT-FOLLOW 随从跟随（v0.0005）。参考 BoneraiserGodot 的 Minion._Process 编队逻辑，
/// 但把俯视无重力的「落点漂移」改成横板「踩平台走」：
///
///   · 个人落点（参考 minion_slot）：黄金角散布 + 半径按序号分档 + 纵向上浮压扁，任意数量都散得开。
///   · 闲逛漂移（参考 vIdle_X/Y）：每 1.2~2.8 秒换一个 ±范围 的小目标，平滑过去，不是死跟。
///   · 越远越快（参考 sMinion_GetMoveSpeed）：速度 = Base + 距离 × CatchUp，封顶 MaxSpeed；死区内停。
///   · 横板补充：跨层自动起跳、卡住自救跳、单向平台主动下穿、掉队 / 坠落瞬移回主角身边。
///   · 碰撞：本体是 CharacterBody2D（见 SpiritPet），被地形与主角挡住；
///     另外每帧做一次「让路 + AABB 硬分离」，保证永远不会和主角叠在一起。
///   · 被踩：主角站在头上时停止水平移动，让玩家站稳（离开后自动恢复跟随）。
///
/// 本组件只驱动移动与朝向，不产生伤害 / 技能 / 数值；敌人系统以后复用同一套层与分离规则。
/// </summary>
public partial class SpiritFollow : Node
{
    private SpiritPet _pet = null!;
    private Node2D? _target;
    private Vector2 _slotOffset;
    private Vector2 _wander;
    private Vector2 _wanderGoal;
    private float _wanderT;
    private float _phase;
    private float _jumpCooldown;
    private float _stuckTime;
    private float _dropTimer;
    private bool _onOneWayFloor;
    private bool _onSlimeFloor;          // 脚下是否粘液块（MAP-009：对所有角色生效）
    private float _launchGrace;          // 被弹跳块弹射后的离地宽限（秒）：期间不按贴地处理，避免 Y 被清零
    // 让路系统状态（2026-11-16 优化）：朝向 / 滞回 / 让路跳跃冷却
    private float _lastDirX = 1f;        // 主角最近一次有效移动方向（+1 右 / -1 左）
    private float _facingX = 1f;         // 落点镜像基准：落点整体放到主角「身后」
    private float _facingHoldT;          // 朝向翻转滞回倒计时
    private float _yieldJumpCooldown;    // 让路跳跃冷却
    private float _stuckJumpCooldown;    // 卡住自救跳冷却（原地连跳修复）
    // 防卡死寻路（SPIRIT-PATH）
    private IStandableGrid? _grid;       // 方块网格（BFS 寻路用；null = 不做寻路）
    private float _farTimer;             // 远离玩家的累计时长
    private float _seekTimer;            // 重算路径倒计时
    private float _seekDuration;         // 已处于寻路模式的时长
    private List<Vector2>? _path;        // 当前 BFS 路径（世界坐标）
    private int _pathIndex;              // 当前走向第几个路径点
    // 移动能力档案：BFS 认为这只随从能跳多高 / 跨多远。默认与主角满跳同量级；
    // 以后随从的移速 / 敏捷接入养成后，用 SpiritNavProfile.FromStats 换一份即可。
    private SpiritNavProfile _navProfile = SpiritNavProfile.Default;
    private bool _forceSeek;             // 冒烟专用：模拟「被地形卡死」，强制走寻路模式

    /// <summary>是否已接入跟随（UI 预览里保持 false，实体静止）。</summary>
    public bool Enabled { get; private set; }

    /// <summary>到「个人落点」的距离（冒烟核对跟随收敛用）。</summary>
    public float LastDistance { get; private set; }

    /// <summary>本帧是否被主角踩在头上。</summary>
    public bool IsStoodOn { get; private set; }

    /// <summary>累计主动起跳次数（跨层 / 让路 / 自救），冒烟用「静止时不该连跳」。</summary>
    public int JumpCount { get; private set; }

    /// <summary>最近一次 BFS 寻路是否找到路径。</summary>
    public bool LastPathFound { get; private set; }

    /// <summary>本次接入以来是否成功找到过路径（冒烟核对「卡死时确实靠寻路回来」）。</summary>
    public bool EverFoundPath { get; private set; }

    /// <summary>是否已拿到方块网格（冒烟诊断）。</summary>
    public bool HasGrid => _grid != null;

    /// <summary>脚下是否粘液块（冒烟核对「粘液对随从也生效」）。</summary>
    public bool IsOnSlime => _onSlimeFloor;

    /// <summary>进入寻路模式的次数（冒烟诊断）。</summary>
    public int SeekStarts { get; private set; }

    private bool _wasSeeking;

    public override void _Ready()
    {
        _pet = GetParent<SpiritPet>();
        // 先于主角处理：让路 / 分离在本帧先行，主角不会被随从顶住。
        ProcessPhysicsPriority = -10;
        SetPhysicsProcess(false);
    }

    /// <summary>
    /// 接入跟随：target = 主角（或以后的其他跟随对象），slotIndex 决定个人落点，
    /// grid = 方块网格（提供后启用 BFS 防卡死寻路；null 则只做普通跟随）。
    /// </summary>
    public void Enable(Node2D target, int slotIndex, IStandableGrid? grid = null)
    {
        _target = target;
        _grid = grid;
        _farTimer = 0f;
        _seekTimer = 0f;
        _seekDuration = 0f;
        _path = null;
        _pathIndex = 0;
        EverFoundPath = false;
        LastPathFound = false;
        _forceSeek = false;
        _slotOffset = SlotOffset(slotIndex);
        _wander = Vector2.Zero;
        _wanderGoal = Vector2.Zero;
        _wanderT = 0f;
        _phase = slotIndex * 0.7f;   // 不同相位，呼吸不同步
        _jumpCooldown = 0f;
        _stuckTime = 0f;
        _dropTimer = 0f;
        _facingHoldT = 0f;
        _yieldJumpCooldown = 0f;
        _stuckJumpCooldown = 0f;
        Enabled = true;
        SetPhysicsProcess(true);
    }

    public void Disable()
    {
        Enabled = false;
        _target = null;
        SetPhysicsProcess(false);
    }

    /// <summary>
    /// 设置移动能力档案（移速 / 敏捷 → 跳高与跨度）。属性变化后调用一次即可，
    /// 下一次寻路就按新能力算；不调用则一直用基础档案。
    /// </summary>
    public void SetNavProfile(SpiritNavProfile profile) => _navProfile = profile;

    /// <summary>按移速 / 敏捷直接换算并设置（以后接随从养成时的入口）。</summary>
    public void SetMoveStats(float moveSpeed, float agility) =>
        _navProfile = SpiritNavProfile.FromStats(moveSpeed, agility);

    /// <summary>
    /// 冒烟专用：模拟「被地形卡死，直线回不去」——强制进入寻路模式，直到真的回到玩家身边。
    /// 正常游戏不会调用；用来验证 BFS 路径确实被采用并走通。
    /// </summary>
    public void ForceSeekForSmoke()
    {
        _forceSeek = true;
        _farTimer = GameDefs.SpiritFindPathDelay;
    }

    /// <summary>立即贴到落点（生成 / 瞬移用，避免新实体从远处跑过来）。</summary>
    public void SnapToTarget()
    {
        if (_target == null || !IsInstanceValid(_target)) return;
        _pet.GlobalPosition = _target.GlobalPosition + _slotOffset;
        _pet.Velocity = Vector2.Zero;
    }

    /// <summary>
    /// 固定五格落点（本命 + 最多 4 名队友）。因为随从之间也有碰撞体，落点必须保证
    /// 任意两格的最小间距 ≥ 0.8 格（碰撞体宽 0.62），否则一生成就互相挤。
    /// 左右成对、前后错开：0/1 在两侧略高，2/3 在两侧再高一点，4 在正上方。
    /// 超过 5 只时退回黄金角外扩（当前 MaxSlots + 本命 = 5，正常走不到）。
    /// </summary>
    private static readonly Vector2[] FixedSlots =
    {
        new( 1.55f, -0.15f),
        new(-1.55f, -0.15f),
        new( 0.75f, -0.45f),
        new(-0.75f, -0.45f),
        new( 0.00f, -0.75f),
    };

    public static Vector2 SlotOffset(int index)
    {
        if (index >= 0 && index < FixedSlots.Length) return FixedSlots[index];
        float ang = Mathf.PosMod(index * 2.39996f, Mathf.Tau);
        float rad = 1.90f + (index - FixedSlots.Length) * 0.35f;
        return new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad * 0.30f - 0.45f);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Enabled || _target == null || !IsInstanceValid(_target)) return;
        float dt = (float)delta;
        Vector2 playerPos = _target.GlobalPosition;

        // ---- 被踩：主角站在头顶 → 停住，让玩家站稳（离开后本分支不再进入）
        IsStoodOn = ProbeStoodOn(playerPos);
        if (IsStoodOn)
        {
            float rideVy = _pet.IsOnFloor()
                ? 0f
                : Mathf.Min(_pet.Velocity.Y + GameDefs.Gravity * dt, GameDefs.MasterMaxFallSpeed);
            _pet.Velocity = new Vector2(0f, rideVy);
            _pet.MoveAndSlide();
            UpdateFloorContact();
            SeparateFrom(playerPos);   // 被踩时也不许和主角同位
            return;
        }

        // ---- 闲逛漂移（参考 vIdle_X/Y）：小目标 + 平滑，跟随有生气而不是硬贴
        _wanderT -= dt;
        if (_wanderT <= 0f)
        {
            _wanderT = (float)GD.RandRange(1.2, 2.8);
            float r = GameDefs.SpiritFollowWanderRange;
            _wanderGoal = new Vector2((float)GD.RandRange(-r, r), 0f);
        }
        _wander = _wander.Lerp(_wanderGoal, Mathf.Clamp(dt * 0.9f, 0f, 1f));

        // ---- 呼吸浮动：只动 Sprite，不动物理体（否则会和落点互相拉扯）
        _phase += dt * 2.1f;
        if (_pet.Sprite != null)
            _pet.Sprite.Position = new Vector2(0f, Mathf.Sin(_phase) * GameDefs.SpiritBobHeight);

        // ---- 主角速度：判断它「在往哪走」。让路必须让到身后，否则会越让越挡。
        Vector2 playerVel = _target is CharacterBody2D cb ? cb.Velocity : Vector2.Zero;
        bool playerMoving = Mathf.Abs(playerVel.X) > GameDefs.SpiritYieldMinSpeed;
        if (playerMoving) _lastDirX = Mathf.Sign(playerVel.X);

        // ---- 落点朝向：主角持续往一边走时，整组落点镜像到「身后」（带滞回，防左右横跳）
        _facingHoldT = Mathf.Max(0f, _facingHoldT - dt);
        if (playerMoving && _lastDirX != _facingX && _facingHoldT <= 0f)
        {
            _facingX = _lastDirX;
            _facingHoldT = GameDefs.SpiritFacingHoldTime;
        }
        // 纵向偏移夹紧：落在「站同一地面就够得到」的范围内，否则会一直被判定为要跨层 → 原地连跳。
        Vector2 dynamicSlot = new Vector2(
            _slotOffset.X * -_facingX,
            Mathf.Clamp(_slotOffset.Y, -GameDefs.SpiritSlotLiftMax, GameDefs.SpiritSlotDropMax));

        Vector2 pos = _pet.GlobalPosition;
        // 主角与随从的「实际高度差」：跨层跳跃只认这个，不认落点高度 ——
        // 落点可以为了队形抬高，但绝不该因此诱发跳跃（原地连跳的老根因）。
        float playerDy = playerPos.Y - pos.Y;
        Vector2 wish = playerPos + dynamicSlot + _wander;

        // ---- 让路 v2：清空主角前进方向的「通道」。
        // 旧版是「从主角径向退开」——主角向左走、随从也在左边时，径向退同样是向左，于是它一路退、
        // 一路挡在主角前面。新版改成：主角前方通道内 → 让到身后；贴太近 → 径向兜底退开。
        Vector2 relNow = pos - playerPos;
        float ahead = relNow.X * _lastDirX;   // > 0 表示位于主角前进方向那一侧
        bool inLane = playerMoving
                      && ahead > -0.15f && ahead < GameDefs.SpiritYieldLaneLength
                      && Mathf.Abs(relNow.Y) < GameDefs.SpiritYieldLaneHalfHeight;
        bool yielding = false;
        if (inLane)
        {
            wish = playerPos + new Vector2(-_lastDirX * 1.25f, -0.15f);   // 身后 1.25 格，略抬高
            yielding = true;
        }
        else if (relNow.LengthSquared() < GameDefs.SpiritAvoidRadius * GameDefs.SpiritAvoidRadius
                 && relNow.LengthSquared() > 0.0001f)
        {
            wish = pos + relNow.Normalized() * GameDefs.SpiritAvoidRadius;
            yielding = true;
        }

        // ---- 防卡死寻路（SPIRIT-PATH）：长时间离玩家很远 → 在方块网格上 BFS 找一条回得去的路。
        // 正常跟随（直线够得到）用不上它；只有被墙 / 高台隔断、怎么走都回不去时才接管。
        bool seeking = false;
        float distToPlayer = relNow.Length();
        if (_grid != null)
        {
            bool farEnough = distToPlayer > GameDefs.SpiritFindPathDist;
            if (_forceSeek)
            {
                farEnough = true;
                if (distToPlayer < 2.5f) _forceSeek = false;   // 真回到身边才解除（冒烟用）
            }
            if (farEnough) _farTimer += dt;
            else _farTimer = Mathf.Max(0f, _farTimer - dt * 2f);   // 靠近比远离消得快，避免状态残留

            if (_farTimer >= GameDefs.SpiritFindPathDelay)
            {
                if (!_wasSeeking)
                {
                    _wasSeeking = true;
                    SeekStarts++;
                }
                seeking = true;
                _seekDuration += dt;
                _seekTimer -= dt;
                if (_path == null || _seekTimer <= 0f)
                {
                    _path = SpiritPathfinder.FindPath(_grid, pos, playerPos, _navProfile);
                    LastPathFound = _path != null;
                    if (_path != null) EverFoundPath = true;
                    _pathIndex = 0;
                    _seekTimer = GameDefs.SpiritRepathInterval;
                }

                if (_path != null)
                {
                    while (_pathIndex < _path.Count
                           && pos.DistanceTo(_path[_pathIndex]) < 0.45f)
                        _pathIndex++;   // 到达当前路径点 → 走下一个

                    if (_pathIndex < _path.Count)
                    {
                        wish = _path[_pathIndex];
                    }
                    else
                    {
                        // 路径走完：交回普通跟随
                        _farTimer = 0f;
                        _seekDuration = 0f;
                        _path = null;
                    }
                }

                // 兜底：寻路模式下迟迟回不去（路被堵死 / BFS 太乐观）→ 直接瞬移
                if (_seekDuration > GameDefs.SpiritSeekTimeout)
                {
                    _pet.GlobalPosition = playerPos + dynamicSlot + new Vector2(0f, -0.5f);
                    _pet.Velocity = Vector2.Zero;
                    _farTimer = 0f;
                    _seekDuration = 0f;
                    _path = null;
                    _dropTimer = 0f;
                    return;
                }
            }
            else _wasSeeking = false;
        }

        float dx = wish.X - pos.X;
        float dy = wish.Y - pos.Y;
        LastDistance = (wish - pos).Length();

        // 弹跳块：弹射后的离地宽限期内，即便 IsOnFloor 仍为 true 也按空中处理。
        // 只保护一帧不够——弹射首帧只离开约 0.37 格，胶囊仍在方块接触范围内，第二帧会把 Y 清零。
        _launchGrace = Mathf.Max(0f, _launchGrace - dt);
        bool onFloor = _pet.IsOnFloor() && _launchGrace <= 0f;
        _jumpCooldown = Mathf.Max(0f, _jumpCooldown - dt);
        _dropTimer = Mathf.Max(0f, _dropTimer - dt);
        _yieldJumpCooldown = Mathf.Max(0f, _yieldJumpCooldown - dt);
        _stuckJumpCooldown = Mathf.Max(0f, _stuckJumpCooldown - dt);

        // 主动下穿单向平台：窗口内只关单向层，实体地板不受影响
        if (_dropTimer > 0f) _pet.CollisionMask &= ~GameDefs.LayerOneWay;
        else _pet.CollisionMask |= GameDefs.LayerOneWay;

        // ---- 水平：越远越快（Base + dist × CatchUp，封顶），死区内停
        // 让路时直接给满速：主角朝它冲过来时能立刻闪开，不会堵在主角面前被推着走。
        float cap = yielding
            ? GameDefs.SpiritFollowMaxSpeed
            : Mathf.Clamp(GameDefs.SpiritFollowBaseSpeed + Mathf.Abs(dx) * GameDefs.SpiritFollowCatchUp,
                          GameDefs.SpiritFollowBaseSpeed, GameDefs.SpiritFollowMaxSpeed);
        // MAP-009 粘液块：对所有角色生效 —— 随从踩上去同样降速（连让路满速也一起被压）。
        if (_onSlimeFloor) cap *= GameDefs.SlimeSpeedMul;
        float targetVx = Mathf.Abs(dx) > GameDefs.SpiritFollowStopRadius ? Mathf.Sign(dx) * cap : 0f;
        float accel = onFloor ? GameDefs.SpiritFollowAccelGround : GameDefs.SpiritFollowAccelAir;
        float vx = Mathf.MoveToward(_pet.Velocity.X, targetVx, accel * dt);

        // ---- 垂直：贴地清零；空中重力（下落加重，与主角同口径）
        float vy = _pet.Velocity.Y;
        if (onFloor && _dropTimer <= 0f)
        {
            vy = 0f;
        }
        else
        {
            float g = GameDefs.Gravity * (vy > 0f ? GameDefs.MasterFallGravityMul : 1f);
            vy = Mathf.Min(vy + g * dt, GameDefs.MasterMaxFallSpeed);
        }

        // ---- 跨层：主角确实在上一层（实际高度差 > 0.9 格）且水平够近 → 起跳。
        // 站同一地面时高度差只有 ≈0.16，任何队形落点都不会触发跳跃。
        // 寻路模式下按「路径点是否在上方」判断跳跃；正常跟随按「主角是否真在上一层」判断。
        bool wantUp = seeking
            ? dy < -0.60f
            : playerDy < -GameDefs.SpiritJumpUpThreshold;
        if (onFloor && !_onSlimeFloor && wantUp && Mathf.Abs(dx) < 2.4f && _jumpCooldown <= 0f)
        {
            vy = -GameDefs.SpiritFollowJumpSpeed;
            _jumpCooldown = GameDefs.SpiritFollowJumpCooldown;
            JumpCount++;
        }

        // ---- 让路且贴得近：直接跳过去（从主角身体上方越过，比在脚下横向硬挤快得多）。
        // 只在主角真的在移动时跳——玩家停下后继续让路跳会变成原地弹跳。
        if (yielding && playerMoving && onFloor && !_onSlimeFloor && ahead < 0.95f && _yieldJumpCooldown <= 0f)
        {
            vy = -GameDefs.SpiritFollowJumpSpeed * 0.85f;
            _yieldJumpCooldown = GameDefs.SpiritYieldJumpCooldown;
            JumpCount++;
        }

        // ---- 卡住自救：想走却走不动（台沿 / 拐角）→ 跳一下。
        // 只在距离还远时触发，并拉长冷却：归位途中的微小卡顿不值得连跳。
        if (onFloor && !_onSlimeFloor && Mathf.Abs(dx) > 1.0f && Mathf.Abs(vx) < 0.6f)
        {
            _stuckTime += dt;
            if (_stuckTime > GameDefs.SpiritFollowStuckTime && _stuckJumpCooldown <= 0f)
            {
                vy = -GameDefs.SpiritFollowJumpSpeed;
                _jumpCooldown = GameDefs.SpiritFollowJumpCooldown;
                _stuckJumpCooldown = GameDefs.SpiritStuckJumpCooldown;
                _stuckTime = 0f;
                JumpCount++;
            }
        }
        else _stuckTime = 0f;

        // ---- 目标在下方且站在单向平台上 → 主动下穿（宽平台上走不到边也跟得下去）
        if (onFloor && dy > 0.80f && _onOneWayFloor && _dropTimer <= 0f)
            _dropTimer = GameDefs.SpiritFollowDropTime;

        _pet.Velocity = new Vector2(vx, vy);
        _pet.MoveAndSlide();
        UpdateFloorContact();

        // 弹跳块：MoveAndSlide 之后才应用上抛（否则会被贴地分支清零），下一帧按空中处理。
        if (_pet.ConsumeBounce())
        {
            _pet.Velocity = new Vector2(_pet.Velocity.X, -GameDefs.BounceSpeed);
            _launchGrace = 0.20f;
        }

        // ---- 硬分离：与主角 AABB 重叠就沿水平轴推出（物理兜底，保证永不同位）
        SeparateFrom(playerPos);
        // ---- 随从之间软分离：物理碰撞允许 safe_margin 的轻微穿透，这里补一道，
        //      保证任意两只随从的中心距 ≥ 碰撞宽度（只推自己，避免互相推产生振荡）。
        SeparateFromPets();

        // ---- 世界边界（与主角同口径）
        float edge = GameDefs.WorldEdge(GameDefs.SpiritBodyWidth);
        _pet.GlobalPosition = new Vector2(
            Mathf.Clamp(_pet.GlobalPosition.X, -edge, edge), _pet.GlobalPosition.Y);

        // ---- 掉队 / 坠落：直接回到主角身边（不让玩家等它绕回来）
        Vector2 rel = _pet.GlobalPosition - playerPos;
        if (_pet.GlobalPosition.Y > GameDefs.FallKillY
            || rel.LengthSquared() > GameDefs.SpiritFollowTeleportDist * GameDefs.SpiritFollowTeleportDist)
        {
            _pet.GlobalPosition = playerPos + _slotOffset + new Vector2(0f, -0.5f);
            _pet.Velocity = Vector2.Zero;
            _dropTimer = 0f;
        }

        // ---- 朝向：按水平移动方向（pet.png 默认朝右）
        if (_pet.Sprite != null && Mathf.Abs(vx) > 0.05f)
            _pet.Sprite.FlipH = vx < 0f;
    }

    /// <summary>
    /// 被踩检测：在随从头顶放一个薄矩形做形状查询，只查 LayerActor（主角 / 未来敌人）。
    /// 用几何查询而不是反查主角的碰撞列表，是因为主角的物理步进晚于随从（本节点优先级 -10）。
    /// 查询参数与形状在字段里缓存（P3：_PhysicsProcess 每帧调用，原先每次都 new 两个原生对象）；
    /// 形状尺寸 / 碰撞层不变，只有 Transform 逐帧刷新，判定结果与逐帧新建完全一致。
    /// </summary>
    private static readonly RectangleShape2D StoodOnShape = new() { Size = new Vector2(0.70f, 0.30f) };
    private readonly PhysicsShapeQueryParameters2D _stoodOnQuery = new()
    {
        Shape = StoodOnShape,
        CollisionMask = GameDefs.LayerActor,
        CollideWithBodies = true,
        CollideWithAreas = false,
    };

    private bool ProbeStoodOn(Vector2 playerPos)
    {
        if (Mathf.Abs(playerPos.X - _pet.GlobalPosition.X) > 0.75f) return false;
        if (playerPos.Y > _pet.GlobalPosition.Y - 0.2f) return false;

        _stoodOnQuery.Transform = new Transform2D(0f,
            _pet.GlobalPosition + new Vector2(0f, -(GameDefs.SpiritBodyHeight * 0.5f + 0.10f)));
        return _pet.GetWorld2D().DirectSpaceState.IntersectShape(_stoodOnQuery, 1).Count > 0;
    }

    /// <summary>
    /// AABB 硬分离：与主角水平投影重叠时，沿水平轴把随从推到刚好不重叠。
    /// 只推水平轴——垂直推会把随从塞进地板或顶到主角头顶。
    /// </summary>
    private void SeparateFrom(Vector2 playerPos)
    {
        // 额外留 0.12 格余量：在真正撞上之前就推开，主角的物理碰撞基本碰不到随从。
        float halfW = (GameDefs.SpiritBodyWidth + GameDefs.MasterSize.X) * 0.5f + GameDefs.SpiritPushMargin;
        float halfH = (GameDefs.SpiritBodyHeight + GameDefs.MasterSize.Y) * 0.5f;
        Vector2 d = _pet.GlobalPosition - playerPos;
        if (Mathf.Abs(d.X) >= halfW || Mathf.Abs(d.Y) >= halfH) return;

        // 主角在随从正上方（踩头 / 从头顶越过）时不横推，否则会把随从从脚下挤走、玩家会掉下来。
        // y-down：主角在上方 → d.Y > 0。
        if (d.Y > halfH * 0.55f) return;

        float push = halfW - Mathf.Abs(d.X) + 0.02f;
        float sign = d.X > 0.001f ? 1f
                   : d.X < -0.001f ? -1f
                   : -_lastDirX;   // 完全重合时推到主角身后
        _pet.GlobalPosition = new Vector2(_pet.GlobalPosition.X + sign * push, _pet.GlobalPosition.Y);
    }

    /// <summary>
    /// 与其他随从保持间距：按胶囊真实形状算距离，不足一个碰撞宽度就把自己推开。
    /// 只推自己（不推对方）——双方都只负责「离开」，不会互相加力导致抖动。
    /// </summary>
    private void SeparateFromPets()
    {
        float minDist = GameDefs.SpiritBodyWidth + 0.02f;
        foreach (Node node in GetTree().GetNodesInGroup(GameDefs.SpiritGroup))
        {
            if (node == _pet || node is not SpiritPet other) continue;
            var (dist, d) = SpiritFollowGroup.Capsule(_pet.GlobalPosition, other.GlobalPosition);
            if (dist >= minDist) continue;
            Vector2 dir = d.LengthSquared() > 0.0001f ? d.Normalized() : new Vector2(-_lastDirX, 0f);
            _pet.GlobalPosition += dir * (minDist - dist);
        }
    }

    /// <summary>记录脚下是否单向平台（决定能不能主动下穿）；静止帧沿用上次结论。
    /// 判定与主角共用 PlatformMover.SampleFloorContact（原两份逐字同构实现，P2 收口）。</summary>
    private void UpdateFloorContact()
    {
        if (!_pet.IsOnFloor())
        {
            _onOneWayFloor = false;
            _onSlimeFloor = false;
            return;
        }
        var contact = PlatformMover.SampleFloorContact(_pet);
        if (contact == null) return;   // 站立静止帧沿用上一次结论
        _onOneWayFloor = contact.Value.OneWay;
        _onSlimeFloor = contact.Value.Slime;
    }
}
