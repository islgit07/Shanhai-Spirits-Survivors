using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace MONSTER-001 随从近战攻击组件（v0.0007 追加，v0.0007.1 追击优化）：随从在攻击范围内
/// 自动挥击最近的一只怪；怪在迎击范围（SpiritEngageRange）内但超出攻击范围时，输出
/// 「战斗位移愿望」CombatWish 让随从短暂偏离跟随落点主动迎上去（限幅，打完回归队形）。
/// 独立组件节点（spirit_pet.tscn 里挂在 Follow 之下，与移动行为解耦）——
/// SpiritFollow 每帧读取 CombatWish 字段做被动叠加；被踩 / 让路 / BFS 寻路优先级更高，
/// 不受追击影响；无怪时 CombatWish 恒为零，跟随行为与无此组件完全一致。
/// 自检「随从无技能」边界靠本文件隔离保持（Skill/Attack/Damage 字面量不进 follow/pet 文件）。
/// 攻击即时命中（近战无弹道），命中时对宠物 Sprite 做一次缩放 punch（与呼吸浮动不冲突——
/// 浮动只动 position，punch 只动 scale）。伤害数值统一在 GameDefs；技能系统由用户后续设计。
/// </summary>
public partial class SpiritMeleeAttack : Node
{
    private SpiritPet _pet = null!;
    private SpiritFollow _follow = null!;
    private MonsterField? _field;
    private bool _enabled;
    private float _cooldown;
    private Tween? _punch;
    public override void _Ready()
    {
        _follow = GetParent<SpiritFollow>();          // 本组件是 Follow 的子节点：战斗位移写它的 CombatWish
        _pet = _follow.GetParent<SpiritPet>();        // 宠物是 Follow 的父节点
        SetPhysicsProcess(false);
    }

    /// <summary>接入攻击（随从生成 / 队伍同步时由 SpiritFollowGroup 调用；field = 怪物对象池）。
    /// 冷却随机相位：多只随从的伤害曲线错开，避免「同帧齐射 → 1 秒真空」的脉冲。</summary>
    public void Enable(MonsterField field)
    {
        _field = field;
        _cooldown = (float)GD.RandRange(0.0, GameDefs.SpiritAttackCooldown);
        _enabled = true;
        SetPhysicsProcess(true);
    }

    /// <summary>停用攻击（不影响 Follow 的移动行为）；战斗位移愿望立即清零。</summary>
    public void Disable()
    {
        SetEnabledState(false);
    }

    /// <summary>开关攻击（保留已注入的怪物池引用；--monster-smoke 确定性用：开启时冷却归零立即出手）。</summary>
    public void SetEnabled(bool on)
    {
        SetEnabledState(on);
        if (on) _cooldown = 0f;
    }

    private void SetEnabledState(bool on)
    {
        _enabled = on && _field != null;
        SetPhysicsProcess(_enabled);
        if (!_enabled) _follow.CombatWish = Vector2.Zero;   // 停用瞬间清掉残留的追击愿望
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_enabled || _field == null) return;
        _cooldown = Mathf.Max(0f, _cooldown - (float)delta);
        _follow.CombatWish = ComputeEngageOffset();
        if (_cooldown > 0f) return;

        // 最近且在攻击范围内的活怪（平方距离比较，零分配；≤8 只全遍历成本可忽略）
        var active = _field.Active;
        Monster? target = null;
        float best = GameDefs.SpiritAttackRange * GameDefs.SpiritAttackRange;
        Vector2 pos = _pet.GlobalPosition;
        for (int i = 0; i < active.Count; i++)
        {
            float d = pos.DistanceSquaredTo(active[i].GlobalPosition);
            if (d <= best)
            {
                best = d;
                target = active[i];
            }
        }
        if (target == null) return;

        target.TakeDamage(GameDefs.SpiritAttackDamage);
        PlayHitPunch();
        _cooldown = GameDefs.SpiritAttackCooldown;
    }

    /// <summary>
    /// 战斗位移愿望：最近怪在攻击范围内 → 零（站桩输出）；在迎击范围内但超出攻击范围 →
    /// 朝怪方向偏移（目标点到怪 0.6 格，留出重叠余量，限幅 SpiritEngageMaxOffset）；
    /// 超出迎击范围 → 零（跟队，不掉队追怪）。每帧重算，怪死 / 走远自动归零回归队形。
    /// </summary>
    private Vector2 ComputeEngageOffset()
    {
        var active = _field!.Active;
        if (active.Count == 0) return Vector2.Zero;
        Vector2 pos = _pet.GlobalPosition;
        Monster? nearest = null;
        float best = float.MaxValue;
        for (int i = 0; i < active.Count; i++)
        {
            float d = pos.DistanceSquaredTo(active[i].GlobalPosition);
            if (d < best)
            {
                best = d;
                nearest = active[i];
            }
        }
        if (nearest == null) return Vector2.Zero;
        float dist = Mathf.Sqrt(best);
        if (dist <= GameDefs.SpiritAttackRange) return Vector2.Zero;
        if (dist > GameDefs.SpiritEngageRange) return Vector2.Zero;
        Vector2 dir = (nearest.GlobalPosition - pos) / dist;
        float pull = Mathf.Min(dist - GameDefs.SpiritAttackRange * 0.6f, GameDefs.SpiritEngageMaxOffset);
        return dir * pull;
    }

    /// <summary>命中反馈：宠物 Sprite 缩放 punch（呼吸浮动只动 position，两者不冲突）。</summary>
    private void PlayHitPunch()
    {
        var sprite = _pet.Sprite;
        if (sprite == null) return;
        _punch?.Kill();
        _punch = CreateTween();
        _punch.TweenProperty(sprite, "scale", sprite.Scale * 1.18f, 0.06f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _punch.TweenProperty(sprite, "scale", sprite.Scale, 0.08f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
    }
}
