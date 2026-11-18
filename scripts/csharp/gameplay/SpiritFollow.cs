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
///
/// P1 拆分（partial，同一个类）：本文件 = 状态 + 接入 + _PhysicsProcess 编排与移动积分；
/// 让路 / 朝向在 SpiritFollow.Yield.cs；BFS 防卡死寻路在 SpiritFollow.Seek.cs；
/// 被踩 / 分离 / 地板接触在 SpiritFollow.Body.cs。拆方法时逐字搬移，仅把段间共享局部量
/// 改为参数 / 返回值，求值顺序与数值口径不变。
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
    private float _launchGrace;          // 被弹跳块弹射后的离地宽限（秒）：期间不按贴地处理，避免 Y 被清零
    private float _yieldJumpCooldown;    // 让路跳跃冷却（2026-11-16 让路系统，判定在移动积分段）
    private float _stuckJumpCooldown;    // 卡住自救跳冷却（原地连跳修复）

    /// <summary>是否已接入跟随（UI 预览里保持 false，实体静止）。</summary>
    public bool Enabled { get; private set; }

    /// <summary>到「个人落点」的距离（冒烟核对跟随收敛用）。</summary>
    public float LastDistance { get; private set; }

    /// <summary>本帧是否被主角踩在头上。</summary>
    public bool IsStoodOn { get; private set; }

    /// <summary>累计主动起跳次数（跨层 / 让路 / 自救），冒烟用「静止时不该连跳」。</summary>
    public int JumpCount { get; private set; }

    /// <summary>
    /// 战斗位移愿望：由随从的近战战斗组件每帧写入（怪在迎击范围内但超出攻击范围时输出朝怪偏移），
    /// 本文件只做被动叠加——以字段注入而非类型引用，保持「战斗逻辑不进跟随文件」的自检边界。
    /// 让路（保玩家通道）与 BFS 寻路（保回家）优先级更高，两者激活时不叠加。
    /// </summary>
    public Vector2 CombatWish;

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
            ProcessRidden(playerPos, dt);
            return;
        }

        // ---- 闲逛漂移 + 呼吸浮动：跟随有生气而不是硬贴（只动状态与 Sprite，不动物理体）
        ProcessIdle(dt);

        // ---- 落点朝向：主角持续往一边走时，整组落点镜像到「身后」（带滞回，防左右横跳）
        Vector2 dynamicSlot = UpdateFacing(dt, out bool playerMoving);

        Vector2 pos = _pet.GlobalPosition;
        // 主角与随从的「实际高度差」：跨层跳跃只认这个，不认落点高度 ——
        // 落点可以为了队形抬高，但绝不该因此诱发跳跃（原地连跳的老根因）。
        float playerDy = playerPos.Y - pos.Y;
        Vector2 wish = playerPos + dynamicSlot + _wander;

        // ---- 让路 v2：清空主角前进方向的「通道」（策略与历史见 SpiritFollow.Yield.cs）
        bool yielding;
        float ahead;
        wish = ApplyYield(pos, playerPos, wish, playerMoving, out yielding, out ahead);

        // ---- 防卡死寻路（SPIRIT-PATH）：长时间离玩家很远 → BFS 找一条回得去的路（见 SpiritFollow.Seek.cs）
        bool seeking;
        bool teleported;
        wish = ApplySeek(pos, playerPos, wish, dynamicSlot, dt, out seeking, out teleported);
        if (teleported) return;

        // ---- 战斗位移叠加（MONSTER-001）：战斗组件发现射程外的怪时输出迎击偏移（以自身当前位置
        // 为基准的追击落点，逐帧重算逐步逼近），怪死 / 走远后愿望归零自动回归队形；
        // 让路（保玩家通道）与 BFS 寻路（保回家）优先级更高，两者激活时不叠加。
        if (!yielding && !seeking && CombatWish != Vector2.Zero)
            wish = pos + CombatWish;

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

    /// <summary>闲逛漂移（参考 vIdle_X/Y）：每 1.2~2.8 秒换一个 ±范围 的小目标，平滑过去；
    /// 加上正弦呼吸浮动（只动 Sprite，不动物理体，否则会和落点互相拉扯）。</summary>
    private void ProcessIdle(float dt)
    {
        _wanderT -= dt;
        if (_wanderT <= 0f)
        {
            _wanderT = (float)GD.RandRange(1.2, 2.8);
            float r = GameDefs.SpiritFollowWanderRange;
            _wanderGoal = new Vector2((float)GD.RandRange(-r, r), 0f);
        }
        _wander = _wander.Lerp(_wanderGoal, Mathf.Clamp(dt * 0.9f, 0f, 1f));

        _phase += dt * 2.1f;
        if (_pet.Sprite != null)
            _pet.Sprite.Position = new Vector2(0f, Mathf.Sin(_phase) * GameDefs.SpiritBobHeight);
    }
}
