using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace MONSTER-001 随从近战攻击组件（v0.0007 追加）：随从在攻击范围内自动挥击最近的一只怪。
/// 独立组件节点（spirit_pet.tscn 的 MeleeAttack 子节点，与 Follow 同模式）——
/// 随从的移动 / 跟随行为（SpiritFollow）一字不改，攻击是纯叠加；自检「随从无技能」边界
/// 靠本文件隔离保持（Skill/Attack/Damage 不进 follow/pet 文件）。
/// 攻击即时命中（近战无弹道），冷却内不再出手；目标只从 MonsterField.Active 里选
/// （濒死 / 已死的怪在死亡处理时就移出了 Active）。伤害数值统一在 GameDefs，
/// 本组件不做技能、不做元素、不做成长——那由用户后续自己设计。
/// </summary>
public partial class SpiritMeleeAttack : Node
{
    private SpiritPet _pet = null!;
    private MonsterField? _field;
    private bool _enabled;
    private float _cooldown;

    public override void _Ready()
    {
        _pet = GetParent<SpiritPet>();
        SetPhysicsProcess(false);
    }

    /// <summary>接入攻击（随从生成 / 队伍同步时由 SpiritFollowGroup 调用；field = 怪物对象池）。</summary>
    public void Enable(MonsterField field)
    {
        _field = field;
        _cooldown = 0f;
        _enabled = true;
        SetPhysicsProcess(true);
    }

    /// <summary>停用攻击（冒烟确定性 / 队伍解散用；不影响 Follow 的移动行为）。</summary>
    public void Disable()
    {
        _enabled = false;
        SetPhysicsProcess(false);
    }

    /// <summary>开关攻击（保留已注入的怪物池引用；--monster-smoke 确定性用）。</summary>
    public void SetEnabled(bool on)
    {
        _enabled = on && _field != null;
        SetPhysicsProcess(_enabled);
        if (_enabled) _cooldown = 0f;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_enabled || _field == null) return;
        _cooldown = Mathf.Max(0f, _cooldown - (float)delta);
        if (_cooldown > 0f) return;

        // 最近且在范围内的活怪（平方距离比较，零分配；≤8 只全遍历成本可忽略）
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
        _cooldown = GameDefs.SpiritAttackCooldown;
    }
}
