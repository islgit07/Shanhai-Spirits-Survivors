using System;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// BlockField 的「配色与调试查询」partial 部分：方块配色（白箱图例与方块动画共用）+
/// GetFirstSlime / GetFirstFragile / GetFirstBounce / GetFirstOneWay。
/// 四个 GetFirst* 只服务 --smoke / 多种子探针的传送定位，不参与正常游戏流程
/// （P4：测试专用查询归位到独立文件；用 partial 保持私有状态封装不变）。
/// </summary>
public partial class BlockField
{
    /// <summary>配色：实心普通 = 绿 / 单向普通 = 紫（沿用旧平台配色），其余块一眼可辨。白箱图例共用。</summary>
    public static Color Tint(BlockType t, bool oneWay = false) => t switch
    {
        BlockType.Normal => oneWay ? new Color(0.42f, 0.40f, 0.52f) : new Color(0.30f, 0.36f, 0.30f),
        BlockType.Fragile => new Color(0.72f, 0.55f, 0.33f),
        BlockType.Bounce => new Color(0.95f, 0.55f, 0.35f),
        BlockType.Buildable => new Color(0.55f, 0.62f, 0.72f),
        BlockType.Treasure => new Color(0.95f, 0.80f, 0.35f),
        BlockType.Slime => new Color(0.36f, 0.78f, 0.42f),   // 黏液：荧光青绿，一眼可辨
        _ => Colors.White,
    };

    /// <summary>
    /// MAP-009 冒烟测试用：取第一块「可站立」的粘液块（上方 2 格净空、非单向），
    /// 返回中心 x 与可行走台面 y（块上缘），供「站在粘液上按跳 → 立刻被黏回」验证。
    /// 只用于 --smoke 传送，不参与正常游戏流程。
    /// </summary>
    public bool GetFirstSlime(out float centerX, out float topY)
    {
        for (int r = 1; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _type[r, c] == BlockType.Slime
                    && !_oneWay[r, c] && CanStandOn(r, c))
                {
                    centerX = ColX(c) + 0.5f;
                    topY = RowTopY(r) - 1f;
                    return true;
                }
        centerX = 0f;
        topY = 0f;
        return false;
    }

    /// <summary>
    /// BUGFIX-FRAGILE 冒烟测试用：取第一块「能从上方踩到」的脆弱木板，返回中心 x 与台面 y。
    /// 要求正上方一格无任何方块且满足 CanStandOn，保证角色自由落下时不会先踩到别的块。
    /// 只用于 --smoke 传送，不参与正常游戏流程。
    /// </summary>
    public bool GetFirstFragile(out float centerX, out float topY)
    {
        for (int r = 1; r < GameDefs.BlockRows - 1; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _type[r, c] == BlockType.Fragile
                    && !_oneWay[r, c] && !_alive[r + 1, c] && CanStandOn(r, c))
                {
                    centerX = ColX(c) + 0.5f;
                    topY = RowTopY(r) - 1f;
                    return true;
                }
        centerX = 0f;
        topY = 0f;
        return false;
    }

    /// <summary>
    /// BOUNCE-JELLY 冒烟测试用：取第一块「能从上方踩到」的弹跳块，返回中心 x 与台面 y。
    /// 与 GetFirstFragile 同口径：正上方一格空且可站，保证自由落下触发真实感应与弹射。
    /// </summary>
    public bool GetFirstBounce(out float centerX, out float topY)
    {
        for (int r = 1; r < GameDefs.BlockRows - 1; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _type[r, c] == BlockType.Bounce
                    && !_oneWay[r, c] && !_alive[r + 1, c] && CanStandOn(r, c))
                {
                    centerX = ColX(c) + 0.5f;
                    topY = RowTopY(r) - 1f;
                    return true;
                }
        centerX = 0f;
        topY = 0f;
        return false;
    }

    /// <summary>冒烟测试用：取第一块单向块（row ≥1）的中心 x 与可行走台面 y（块上缘），供「下 + 跳」下穿验证。</summary>
    public bool GetFirstOneWay(out float centerX, out float topY)
    {
        for (int r = 1; r < GameDefs.BlockRows; r++)
            for (int c = 0; c < GameDefs.BlockCols; c++)
                if (_alive[r, c] && _oneWay[r, c])
                {
                    centerX = ColX(c) + 0.5f;
                    topY = RowTopY(r) - 1f;
                    var b = _nodes[r, c] as StaticBody2D;
                    GD.Print($"[BLOCKDBG] first oneway r={r} c={c} type={_type[r, c]} oneWay={_oneWay[r, c]} bodyLayer={(b != null ? b.CollisionLayer : 99u)}");
                    for (int rr = 1; rr <= 2; rr++)
                        for (int cc = Math.Max(0, c - 2); cc <= Math.Min(GameDefs.BlockCols - 1, c + 2); cc++)
                            if (_alive[rr, cc])
                            {
                                var bb = _nodes[rr, cc] as StaticBody2D;
                                GD.Print($"[BLOCKDBG]   ({cc},{rr}) type={_type[rr, cc]} oneWay={_oneWay[rr, cc]} layer={(bb != null ? bb.CollisionLayer : 99u)}");
                            }
                    return true;
                }
        centerX = 0f;
        topY = 0f;
        return false;
    }
}
