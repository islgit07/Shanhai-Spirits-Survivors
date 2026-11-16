using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 本命灵兽实体（v0.0005 随从系统第 2 块）：主角养的灵兽在场景里的可见分身。
///
/// 生成方式是「实例化 scenes/spirits/spirit_pet.tscn」而不是凭空 new 节点——
/// 场景里摆好 Sprite2D 与 pet.png 占位贴图，随从的颜色由 SpiritDef.Tint 在 Setup 时套上去。
/// 这样以后换美术素材只需替换 .tscn 里的贴图，代码不动。
///
/// 本块只负责「选中的灵兽长什么样」：跟随移动 / 战斗 / 本命升级均属后续块，刻意不实现。
/// 定义卡只读共享，本脚本只在自己的实例上调 modulate / scale，不修改 SpiritDef。
/// </summary>
[GlobalClass]
public partial class SpiritPet : Node2D
{
    /// <summary>占位贴图在场景里引用的路径（供静态自检 / 冒烟核对「确实来自 pet.png」）。</summary>
    public const string PetArtPath = "res://assets/art/pet.png";

    [ExportGroup("外观")]
    [Export] private float _worldHeight = 1.0f;   // 世界内的目标高度（单位），宽度按贴图长宽比等比缩放

    private Sprite2D _sprite = null!;

    /// <summary>本实例对应的随从 Id（未 Setup 前为空）。</summary>
    public string SpiritId { get; private set; } = "";

    public override void _Ready()
    {
        _sprite = GetNode<Sprite2D>("Sprite2D");
    }

    /// <summary>
    /// 用本命定义卡配置外观：套身份色 Tint，并按 pet.png 像素尺寸等比缩放到指定高度。
    /// def 为只读共享资源，此处只读它的 Tint / Id，不写回。
    /// displayHeight &gt; 0 时覆盖 _worldHeight——选择界面卡片用像素高度（UI 无相机缩放），
    /// 游戏内实例用默认世界高度（1 单位）。两种场景复用同一个 .tscn 实体。
    /// </summary>
    public void Setup(SpiritDef def, float displayHeight = -1f)
    {
        SpiritId = def.Id;
        if (_sprite == null) _sprite = GetNode<Sprite2D>("Sprite2D");
        _sprite.Modulate = def.Tint;

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
