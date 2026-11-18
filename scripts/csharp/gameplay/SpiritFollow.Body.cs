using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// SpiritFollow 的「身体规则」partial 部分：被踩分支、头顶踩踏探测（ProbeStoodOn）、
/// 与主角的 AABB 硬分离、与随从间的胶囊软分离、脚下地面接触（单向平台 / 粘液）。
/// 这些是「角色与角色 / 角色与地面」的通用规则，敌人系统以后按同一套口径复用。
/// 方法体逐字沿用原单文件实现，仅把被踩分支从 _PhysicsProcess 提为方法（行为零改动）。
/// </summary>
public partial class SpiritFollow
{
    private bool _onOneWayFloor;         // 脚下是否单向平台（决定能不能主动下穿）
    private bool _onSlimeFloor;          // 脚下是否粘液块（MAP-009：对所有角色生效）

    /// <summary>脚下是否粘液块（冒烟核对「粘液对随从也生效」）。</summary>
    public bool IsOnSlime => _onSlimeFloor;

    /// <summary>被踩分支：垂直速度清零 / 自然下落贴头，水平完全交出，让玩家站稳。</summary>
    private void ProcessRidden(Vector2 playerPos, float dt)
    {
        float rideVy = _pet.IsOnFloor()
            ? 0f
            : Mathf.Min(_pet.Velocity.Y + GameDefs.Gravity * dt, GameDefs.MasterMaxFallSpeed);
        _pet.Velocity = new Vector2(0f, rideVy);
        _pet.MoveAndSlide();
        UpdateFloorContact();
        SeparateFrom(playerPos);   // 被踩时也不许和主角同位
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
