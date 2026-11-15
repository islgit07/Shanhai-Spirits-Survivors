using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace UIS-001 统一 UI 调色板 + 系统中文字体。
/// Godot 内置字体不含 CJK，用 SystemFont 请求系统中文字体（不分发字体文件）。
/// 已知限制：目标机器若无中文字体会显示方块，见 docs/refactor-v0.003.md。
/// </summary>
public static class UiSkin
{
    public static readonly Color Gold = new(1.00f, 0.84f, 0.46f);
    public static readonly Color Blue = new(0.55f, 0.76f, 1.00f);
    public static readonly Color Crimson = new(0.95f, 0.42f, 0.42f);
    public static readonly Color Jade = new(0.48f, 0.88f, 0.66f);
    public static readonly Color Ink = new(0.07f, 0.08f, 0.13f);
    public static readonly Color Panel = new(0.13f, 0.15f, 0.23f, 0.94f);
    public static readonly Color Text = new(0.92f, 0.92f, 0.96f);

    private static SystemFont? _font;
    private static readonly StringName FontKey = new("font");
    private static readonly StringName FontSizeKey = new("font_size");
    private static readonly StringName FontColorKey = new("font_color");

    public static SystemFont CjkFont()
    {
        if (_font != null) return _font;
        _font = new SystemFont();
        var names = new Godot.Collections.Array<string>
        {
            "Microsoft YaHei", "Microsoft YaHei UI", "Noto Sans CJK SC", "SimHei", "sans-serif",
        };
        _font.Set("font_names", names);
        return _font;
    }

    public static Label MakeLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride(FontKey, CjkFont());
        l.AddThemeFontSizeOverride(FontSizeKey, size);
        l.AddThemeColorOverride(FontColorKey, color);
        return l;
    }

    public static Button MakeButton(string text, int size = 18)
    {
        var b = new Button { Text = text };
        b.AddThemeFontOverride(FontKey, CjkFont());
        b.AddThemeFontSizeOverride(FontSizeKey, size);
        b.AddThemeColorOverride(FontColorKey, Text);
        return b;
    }

    public static ProgressBar MakeBar(Vector2 pos, Vector2 size, Color tint) => new()
    {
        Position = pos,
        Size = size,
        MinValue = 0.0,
        MaxValue = 100.0,
        ShowPercentage = false,
        Modulate = tint,
    };

    public static ColorRect MakePanel(Rect2 rect, Color? color = null) => new()
    {
        Color = color ?? Panel,
        Position = rect.Position,
        Size = rect.Size,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>
    /// 开发用地图种子徽章：常驻屏幕角落（默认右上角；传 topLeft 可指定固定位置避开白箱图例）。
    /// 只显示当前地形种子，便于复现地图；不提供输入 / 切换功能。
    /// 返回 Label，换种子时调用 RefreshSeed 更新。
    /// </summary>
    public static Label MakeSeedBadge(Node parent, int seed, Vector2? topLeft = null)
    {
        var layer = new CanvasLayer { Name = "DevSeedUi" };
        parent.AddChild(layer);

        const float W = 200f, H = 32f, M = 14f, T = 14f;
        var bg = MakePanel(new Rect2(Vector2.Zero, new Vector2(W, H)),
                           new Color(Ink.R, Ink.G, Ink.B, 0.82f));
        bg.Name = "SeedBack";
        var label = MakeLabel(SeedText(seed), 15, Gold);
        label.Name = "SeedLabel";
        label.MouseFilter = Control.MouseFilterEnum.Ignore;

        if (topLeft.HasValue)
        {
            bg.Position = topLeft.Value;
            label.Position = topLeft.Value + new Vector2(9f, 5f);
            label.Size = new Vector2(W - 18f, H - 10f);
        }
        else
        {
            AnchorTopRight(bg, W, H, T, M);
            AnchorTopRight(label, W - 18f, H - 10f, T + 5f, M + 9f);
        }
        layer.AddChild(bg);
        layer.AddChild(label);
        return label;
    }

    public static string SeedText(int seed) => $"SEED  {seed}";

    public static void RefreshSeed(Label label, int seed) => label.Text = SeedText(seed);

    private static void AnchorTopRight(Control c, float w, float h, float top, float rightMargin)
    {
        c.AnchorLeft = 1f;
        c.AnchorRight = 1f;
        c.AnchorTop = 0f;
        c.AnchorBottom = 0f;
        c.OffsetLeft = -rightMargin - w;
        c.OffsetRight = -rightMargin;
        c.OffsetTop = top;
        c.OffsetBottom = top + h;
    }
}
