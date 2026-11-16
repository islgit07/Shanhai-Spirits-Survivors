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
    // 让路系统状态（2026-11-16 优化）：朝向 / 滞回 / 让路跳跃冷却
    private float _lastDirX = 1f;        // 主角最近一次有效移动方向（+1 右 / -1 左）
    private float _facingX = 1f;         // 落点镜像基准：落点整体放到主角「身后」
    private float _facingHoldT;          // 朝向翻转滞回倒计时
    private float _yieldJumpCooldown;    // 让路跳跃冷却

    /// <summary>是否已接入跟随（UI 预览里保持 false，实体静止）。</summary>
    public bool Enabled { get; private set; }

    /// <summary>到「个人落点」的距离（冒烟核对跟随收敛用）。</summary>
    public float LastDistance { get; private set; }

    /// <summary>本帧是否被主角踩在头上。</summary>
    public bool IsStoodOn { get; private set; }

    public override void _Ready()
    {
        _pet = GetParent<SpiritPet>();
        // 先于主角处理：让路 / 分离在本帧先行，主角不会被随从顶住。
        ProcessPhysicsPriority = -10;
        SetPhysicsProcess(false);
    }

    /// <summary>接入跟随：target = 主角（或以后的其他跟随对象），slotIndex 决定个人落点。</summary>
    public void Enable(Node2D target, int slotIndex)
    {
        _target = target;
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
    /// 个人落点：黄金角（2.39996 ≈ 137.5°）保证任意数量都散得开；
    /// 半径按 idx % 3 分三档形成「远近不一的一坨」；横向为主、纵向压扁并整体上浮。
    /// </summary>
    public static Vector2 SlotOffset(int index)
    {
        float ang = Mathf.PosMod(index * 2.39996f, Mathf.Tau);
        float rad = GameDefs.SpiritFollowAnchorMin + (index % 3) * GameDefs.SpiritFollowAnchorStep;
        return new Vector2(
            Mathf.Cos(ang) * rad,
            Mathf.Sin(ang) * rad * GameDefs.SpiritFollowAnchorFlatten - GameDefs.SpiritFollowAnchorLift);
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
        Vector2 dynamicSlot = new Vector2(_slotOffset.X * -_facingX, _slotOffset.Y);

        Vector2 pos = _pet.GlobalPosition;
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

        float dx = wish.X - pos.X;
        float dy = wish.Y - pos.Y;
        LastDistance = (wish - pos).Length();

        bool onFloor = _pet.IsOnFloor();
        _jumpCooldown = Mathf.Max(0f, _jumpCooldown - dt);
        _dropTimer = Mathf.Max(0f, _dropTimer - dt);
        _yieldJumpCooldown = Mathf.Max(0f, _yieldJumpCooldown - dt);

        // 主动下穿单向平台：窗口内只关单向层，实体地板不受影响
        if (_dropTimer > 0f) _pet.CollisionMask &= ~GameDefs.LayerOneWay;
        else _pet.CollisionMask |= GameDefs.LayerOneWay;

        // ---- 水平：越远越快（Base + dist × CatchUp，封顶），死区内停
        // 让路时直接给满速：主角朝它冲过来时能立刻闪开，不会堵在主角面前被推着走。
        float cap = yielding
            ? GameDefs.SpiritFollowMaxSpeed
            : Mathf.Clamp(GameDefs.SpiritFollowBaseSpeed + Mathf.Abs(dx) * GameDefs.SpiritFollowCatchUp,
                          GameDefs.SpiritFollowBaseSpeed, GameDefs.SpiritFollowMaxSpeed);
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

        // ---- 跨层：目标明显在上方且水平够近 → 起跳（满跳高度与主角一致，保证跟得上）
        bool wantUp = dy < -0.70f;
        if (onFloor && wantUp && Mathf.Abs(dx) < 2.4f && _jumpCooldown <= 0f)
        {
            vy = -GameDefs.SpiritFollowJumpSpeed;
            _jumpCooldown = GameDefs.SpiritFollowJumpCooldown;
        }

        // ---- 让路且贴得近：直接跳过去（从主角身体上方越过，比在脚下横向硬挤快得多）
        if (yielding && onFloor && ahead < 0.95f && _yieldJumpCooldown <= 0f)
        {
            vy = -GameDefs.SpiritFollowJumpSpeed * 0.85f;
            _yieldJumpCooldown = GameDefs.SpiritYieldJumpCooldown;
        }

        // ---- 卡住自救：想走却走不动（台沿 / 拐角）→ 跳一下
        if (onFloor && Mathf.Abs(dx) > 0.4f && Mathf.Abs(vx) < 0.6f)
        {
            _stuckTime += dt;
            if (_stuckTime > GameDefs.SpiritFollowStuckTime && _jumpCooldown <= 0f)
            {
                vy = -GameDefs.SpiritFollowJumpSpeed;
                _jumpCooldown = GameDefs.SpiritFollowJumpCooldown;
                _stuckTime = 0f;
            }
        }
        else _stuckTime = 0f;

        // ---- 目标在下方且站在单向平台上 → 主动下穿（宽平台上走不到边也跟得下去）
        if (onFloor && dy > 0.80f && _onOneWayFloor && _dropTimer <= 0f)
            _dropTimer = GameDefs.SpiritFollowDropTime;

        _pet.Velocity = new Vector2(vx, vy);
        _pet.MoveAndSlide();
        UpdateFloorContact();

        // ---- 硬分离：与主角 AABB 重叠就沿水平轴推出（物理兜底，保证永不同位）
        SeparateFrom(playerPos);

        // ---- 世界边界（与主角同口径）
        float edge = GameDefs.WorldHalfWidth - GameDefs.SpiritBodyWidth * 0.5f;
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
    /// </summary>
    private bool ProbeStoodOn(Vector2 playerPos)
    {
        if (Mathf.Abs(playerPos.X - _pet.GlobalPosition.X) > 0.75f) return false;
        if (playerPos.Y > _pet.GlobalPosition.Y - 0.2f) return false;

        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(0.70f, 0.30f) },
            Transform = new Transform2D(0f,
                _pet.GlobalPosition + new Vector2(0f, -(GameDefs.SpiritBodyHeight * 0.5f + 0.10f))),
            CollisionMask = GameDefs.LayerActor,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };
        return _pet.GetWorld2D().DirectSpaceState.IntersectShape(query, 1).Count > 0;
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

    /// <summary>记录脚下是否单向平台（决定能不能主动下穿）；静止帧沿用上次结论。</summary>
    private void UpdateFloorContact()
    {
        if (!_pet.IsOnFloor())
        {
            _onOneWayFloor = false;
            return;
        }
        bool found = false, oneWay = false;
        for (int i = 0; i < _pet.GetSlideCollisionCount(); i++)
        {
            var ci = _pet.GetSlideCollision(i);
            if (ci.GetNormal().Y >= -0.7f) continue;
            if (ci.GetCollider() is PhysicsBody2D pb)
            {
                found = true;
                if ((pb.CollisionLayer & GameDefs.LayerOneWay) != 0u) oneWay = true;
            }
        }
        if (found) _onOneWayFloor = oneWay;
    }
}
