using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 本命随从定义（v0.0005 随从系统第 1 块）：主角养的灵兽的「身份数据」。
/// 用户术语：随从 = 主角养的灵兽 = 旧设定的幻灵（幻灵/幻兽同源，只靠 Faction 区分）。
///
/// 本轮只做数据底座：一张 .tres = 一条随从，放进 resources/spirits/ 即被 SpiritCatalog 自动发现，
/// 加新随从无需改代码。刻意只有身份元数据（Id / DisplayName / Lore），
/// 不含技能、升级数值、跟随参数——具体技能由用户之后自行设计，逐块再加。
///
/// Resource 为只读共享引用（定义卡），运行期不修改，故无需 duplicate()。
/// </summary>
[GlobalClass]
public partial class SpiritDef : Resource
{
    [ExportGroup("身份")]
    /// <summary>稳定标识（英文小写 snake_case，如 zhulong），供代码与配置引用；不可重名。</summary>
    [Export] public string Id { get; set; } = "";
    /// <summary>游戏内显示名（中文，如「烛龙」）。</summary>
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>一句话背景，仅作占位身份说明，不是技能描述。</summary>
    [Export] public string Lore { get; set; } = "";

    [ExportGroup("占位美术")]
    /// <summary>
    /// 占位色：随从实体（spirit_pet.tscn）用 pet.png 这张占位图，靠 modulate 套此色区分不同灵兽。
    /// 用户明确「以后会替换美术素材」——届时换贴图即可，这个字段只是当前区分手段，非技能/数值。
    /// </summary>
    [Export] public Color Tint { get; set; } = Colors.White;
}
