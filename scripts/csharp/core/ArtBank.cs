using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace ART-001 美术加载与规格化。16 张 PNG 从 Unity 工程直接复用（Godot 不需要 .meta）。
/// Sprite2D.scale = 目标世界尺寸 / 纹理像素尺寸；缺图回退纯白纹理。
/// </summary>
public static class ArtBank
{
    private const string ArtPath = "res://assets/art/";
    private static readonly Dictionary<string, Texture2D> Cache = new();
    private static Texture2D? _fallback;

    public static Texture2D Tex(string key)
    {
        if (Cache.TryGetValue(key, out Texture2D? cached)) return cached;
        string path = ArtPath + key + ".png";
        Texture2D? t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        t ??= MakeFallback();
        Cache[key] = t;
        return t;
    }

    private static Texture2D MakeFallback()
    {
        if (_fallback != null) return _fallback;
        var img = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
        img.Fill(new Color(1, 1, 1, 1));
        _fallback = ImageTexture.CreateFromImage(img);
        return _fallback;
    }

    /// <summary>运行时生成的 2×2 纯白纹理：给场景里的 Sprite2D 填色用，不依赖任何 PNG 文件。</summary>
    public static Texture2D PlainTexture() => MakeFallback();

    public static Sprite2D MakeSprite(string key, Vector2 size)
    {
        var s = new Sprite2D { Texture = Tex(key) };
        float tw = Mathf.Max(1.0f, s.Texture.GetWidth());
        float th = Mathf.Max(1.0f, s.Texture.GetHeight());
        s.Scale = new Vector2(size.X / tw, size.Y / th);
        return s;
    }

    public static ColorRect MakeBlock(Vector2 size, Color color) => new()
    {
        Color = color,
        Size = size,
        Position = -size * 0.5f,
    };

    /// <summary>
    /// 世界内纯色方块：Sprite2D + 白纹理 + modulate 上色。
    /// Control（ColorRect）挂在 Node2D 父级下渲染时会相对碰撞体偏移约半格，
    /// 视觉与物理错位；Sprite2D 是纯 Node2D 渲染，中心即节点原点，永远与碰撞盒对齐。
    /// </summary>
    public static Sprite2D MakeBox(Vector2 size, Color color)
    {
        var s = new Sprite2D { Texture = MakeFallback(), Modulate = color };
        s.Scale = size / new Vector2(TexSize, TexSize);
        return s;
    }

    private const int TexSize = 2;
}
