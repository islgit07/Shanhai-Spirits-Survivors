using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace RUN-ROSTER 局内调试 HUD：一个「模拟升级」按钮，代替尚未实现的战斗升级条件。
/// 点击 → 调 sibling CompanionOffer.Open()。战斗 / 魂魄掉落 / 真升级触发接上后，换掉这个触发口即可。
/// 不做常驻随从小面板（用户选的是 ESC 双栏查看，不是常驻 HUD）。
/// </summary>
public partial class CompanionHud : CanvasLayer
{
    public override void _Ready()
    {
        GetNode<Button>("HudRoot/OfferButton").Pressed += OnOfferPressed;
    }

    private void OnOfferPressed()
    {
        CompanionOffer? offer = GetParent().GetNodeOrNull<CompanionOffer>("CompanionOffer");
        if (offer == null)
        {
            GD.PushError("[HUD] CompanionOffer 节点不存在，无法打开三选一");
            return;
        }
        offer.Open();
        GD.Print("[HUD] 模拟升级 → 三选一");
    }
}
