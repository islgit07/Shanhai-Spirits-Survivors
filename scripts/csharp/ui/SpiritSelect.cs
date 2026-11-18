using System;
using System.Linq;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 开局本命灵兽选择界面（v0.0005 随从系统第 2 块）。
///
/// 卡片不是写死在场景里，而是遍历 SpiritCatalog.All 动态生成——
/// 以后加一只随从 = 往 resources/spirits/ 丢一张定义卡，这里自动多一张卡，零代码改动。
/// 每张卡的预览直接实例化 scenes/spirits/spirit_pet.tscn（与游戏内实体同一个场景），
/// 用 SpiritDef.Tint 换色区分，“随从由 tscn 生成”在 UI 与游戏两处一致。
///
/// 选中结果写进 SceneTree meta（SpiritCatalog.ChosenIdMeta），换场景不丢，GameRoot 据此实例化本命。
/// --smoke 时自动选第一只并进游戏，让整条「开始页 → 选择 → 游戏」无头链路可验证。
/// </summary>
public partial class SpiritSelect : Control
{
    private const string GameScenePath = "res://scenes/Main.tscn";
    private const string PetScenePath = "res://scenes/spirits/spirit_pet.tscn";

    private Button[] _cards = Array.Empty<Button>();

    public override void _Ready()
    {
        StyleStaticText();
        BuildCards();

        if (IsSmokeRequested())
        {
            CallDeferred(new StringName(nameof(AutoPickSmoke)));
            return;
        }

        if (_cards.Length > 0) _cards[0].GrabFocus();
    }

    private static bool IsSmokeRequested() => SmokeFlags.Has("--smoke");

    private void StyleStaticText()
    {
        Style(GetNode<Label>("Title"), 40, UiSkin.Gold);
        Style(GetNode<Label>("Hint"), 20, UiSkin.Text);
        Style(GetNode<Label>("Footer"), 18, UiSkin.Blue);
    }

    private static void Style(Label label, int size, Color color)
    {
        label.HorizontalAlignment = HorizontalAlignment.Center;
        UiSkin.StyleLabel(label, size, color);
    }

    /// <summary>按目录顺序生成卡片；卡上的灵兽预览来自 spirit_pet.tscn 实例，非代码拼节点。</summary>
    private void BuildCards()
    {
        var box = GetNode<HBoxContainer>("Cards");
        var defs = SpiritCatalog.All;
        var petScene = GD.Load<PackedScene>(PetScenePath);
        if (petScene == null)
        {
            GD.PushError($"[FATE] 无法加载随从场景 {PetScenePath}");
            return;
        }

        _cards = new Button[defs.Count];
        for (int i = 0; i < defs.Count; i++)
        {
            SpiritDef def = defs[i];
            string id = def.Id;   // 闭包捕获稳定副本，避免循环变量问题

            var card = new Button
            {
                CustomMinimumSize = new Vector2(300f, 400f),
                FocusMode = Control.FocusModeEnum.All,
            };
            var panel = new StyleBoxFlat
            {
                BgColor = UiSkin.CardBg,
                BorderColor = def.Tint,
                BorderWidthLeft = 2,
                BorderWidthRight = 2,
                BorderWidthTop = 2,
                BorderWidthBottom = 2,
                CornerRadiusTopLeft = 10,
                CornerRadiusTopRight = 10,
                CornerRadiusBottomLeft = 10,
                CornerRadiusBottomRight = 10,
            };
            card.AddThemeStyleboxOverride("normal", panel);
            card.AddThemeStyleboxOverride("hover", panel);
            card.AddThemeStyleboxOverride("pressed", panel);
            card.AddThemeStyleboxOverride("focus", panel);

            // 随从预览：直接实例化 tscn，按 UI 像素高度配置，颜色来自定义卡。
            var preview = petScene.Instantiate<SpiritPet>();
            preview.Name = "Preview";
            preview.Position = new Vector2(150f, 118f);
            preview.Setup(def, 170f);
            card.AddChild(preview);

            var nameLabel = MakeCardLabel(def.DisplayName, def.Tint, 28,
                new Vector2(0f, 214f), new Vector2(300f, 46f));
            card.AddChild(nameLabel);

            var loreLabel = MakeCardLabel(def.Lore, UiSkin.Text, 16,
                new Vector2(14f, 262f), new Vector2(272f, 82f));
            loreLabel.AutowrapMode = TextServer.AutowrapMode.Word;
            card.AddChild(loreLabel);

            var confirm = MakeCardLabel("Enter / A 选择", UiSkin.Gold, 17,
                new Vector2(0f, 352f), new Vector2(300f, 34f));
            card.AddChild(confirm);

            card.Pressed += () => OnPick(id);
            box.AddChild(card);
            _cards[i] = card;
        }

        int n = _cards.Length;
        for (int i = 0; i < n; i++)
        {
            _cards[i].FocusNeighborLeft = _cards[(i + n - 1) % n].GetPath();
            _cards[i].FocusNeighborRight = _cards[(i + 1) % n].GetPath();
        }
        GD.Print($"[FATE] select cards={n} ids=[{string.Join(",", defs.Select(d => d.Id))}]");
    }

    private static Label MakeCardLabel(string text, Color color, int size, Vector2 pos, Vector2 box)
    {
        var label = new Label
        {
            Text = text,
            Position = pos,
            Size = box,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        UiSkin.StyleLabel(label, size, color);
        return label;
    }

    private void OnPick(string id)
    {
        GetTree().SetMeta(SpiritCatalog.ChosenIdMeta, id);
        GD.Print($"[FATE] chosen id={id} → {GameScenePath}");
        GetTree().ChangeSceneToFile(GameScenePath);
    }

    private void AutoPickSmoke()
    {
        var defs = SpiritCatalog.All;
        if (defs.Count > 0) OnPick(defs[0].Id);
        else GetTree().ChangeSceneToFile(GameScenePath);
    }
}
