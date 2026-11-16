using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 随从 / 本命灵兽实体（v0.0005）。第 2 块只管「长什么样」，本块（SPIRIT-FOLLOW）把它升级成带碰撞体的角色：
///
/// · 根节点是 CharacterBody2D + CapsuleShape2D —— 有真实碰撞体，能站在方块上、能被主角踩在头上、
///   也会被主角和地形挡住（层规则见 GameDefs.LayerActor / LayerSpirit）。
/// · 跟随行为在子节点 Follow（SpiritFollow）里，UI 预览不启用它，所以选择页里实体静止不动。
/// · 外观仍来自 spirit_pet.tscn 的 Sprite2D（pet.png 占位），换美术只改场景贴图，代码不动。
/// · 碰撞体尺寸固定，Sprite 缩放只影响视觉——两者解耦，换成任意比例的美术都不会改变角色占位。
/// </summary>
[GlobalClass]
public partial class SpiritPet : CharacterBody2D
{
    /// <summary>占位贴图在场景里引用的路径（供静态自检 / 冒烟核对「确实来自 pet.png」）。</summary>
    public const string PetArtPath = "res://assets/art/pet.png";

    [ExportGroup("外观")]
    [Export] private float _worldHeight = 1.0f;   // 世界内的目标高度（单位），宽度按贴图长宽比等比缩放

    private Sprite2D _sprite = null!;

    /// <summary>本实例对应的随从 Id（未 Setup 前为空）。</summary>
    public string SpiritId { get; private set; } = "";
    /// <summary>显示名：本命取定义卡 DisplayName，队友取占位名「随从 X」。</summary>
    public string DisplayName { get; private set; } = "";
    public int Level { get; private set; } = 1;
    public int Quality { get; private set; } = 1;
    /// <summary>是否为开局选的本命（槽位 0；队伍里的其他随从为 false）。</summary>
    public bool IsFate { get; private set; }

    /// <summary>外观 Sprite（SpiritFollow 用它做呼吸浮动与朝向，不碰物理）。</summary>
    public Sprite2D Sprite => _sprite;

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
        AddToGroup(GameDefs.SpiritGroup);

        // 角色碰撞规则（与未来敌人共用一段）：随从被主角与地形挡住，彼此不硬碰。
        CollisionLayer = GameDefs.LayerSpirit;
        CollisionMask = GameDefs.LayerSolid | GameDefs.LayerOneWay | GameDefs.LayerActor;
        MotionMode = MotionModeEnum.Grounded;
    }

    /// <summary>
    /// 本命配置：套身份色 Tint，并按 pet.png 像素尺寸等比缩放到指定高度。
    /// def 为只读共享资源，此处只读它的 Tint / Id，不写回。
    /// displayHeight &gt; 0 时覆盖 _worldHeight——选择界面卡片用像素高度（UI 无相机缩放），
    /// 游戏内实例用默认世界高度（1 单位）。两种场景复用同一个 .tscn 实体。
    /// </summary>
    public void Setup(SpiritDef def, float displayHeight = -1f)
    {
        SpiritId = def.Id;
        DisplayName = def.DisplayName;
        Level = 1;
        Quality = 1;
        IsFate = true;
        ApplyLook(def.Tint, displayHeight);
    }

    /// <summary>
    /// 队伍里的占位随从：还没有专属定义卡与美术，用占位名 + 占位色区分。
    /// 技能 / 属性效果一律不做（由用户以后自己设计）。
    /// </summary>
    public void SetupCompanion(string id, string displayName, int level, int quality, Color tint)
    {
        SpiritId = id;
        DisplayName = displayName;
        Level = level;
        Quality = quality;
        IsFate = false;
        ApplyLook(tint, -1f);
    }

    /// <summary>合成 / 升级后同步数值（外观不变，避免重建实体打断跟随）。</summary>
    public void UpdateCompanionStats(int level, int quality)
    {
        Level = level;
        Quality = quality;
    }

    private void ApplyLook(Color tint, float displayHeight)
    {
        if (_sprite == null) _sprite = GetNode<Sprite2D>("Sprite2D");
        _sprite.Modulate = tint;

        var tex = _sprite.Texture;
        if (tex != null)
        {
            float h = Mathf.Max(1f, tex.GetHeight());
            float target = displayHeight > 0f ? displayHeight : _worldHeight;
            float uniform = target / h;
            _sprite.Scale = new Vector2(uniform, uniform);   // 等比：pet.png 是 512×508，不会拉伸变形
        }
    }

    /// <summary>当前实际使用的身份色（冒烟核对「换色区分」用）。</summary>
    public Color AppliedTint => _sprite != null ? _sprite.Modulate : Colors.White;

    /// <summary>实体引用的贴图路径（冒烟核对「来自 pet.png 场景」用）。</summary>
    public string TexturePath => _sprite?.Texture?.ResourcePath ?? "";
}
