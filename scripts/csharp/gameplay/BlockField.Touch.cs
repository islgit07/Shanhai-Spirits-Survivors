using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// BlockField 的「方块触碰与特效」partial 部分：脆弱木板闪烁后碎（FRAGILE-BLINK）、
/// 蹦蹦床三段动画与冲击环（BOUNCE-JELLY）、宝箱打开。
/// 方法体逐字沿用原单文件实现（行为零改动）；方法顺序有意保持
/// StartFragileBlink → FinishFragileBreak → BlinkThenFree → Jelly → SpawnBounceRing → OpenTreasure，
/// 静态自检按相邻方法名切片校验方法体，勿随意调序。
/// </summary>
public partial class BlockField
{
    /// <summary>
    /// 触碰感应链路（BUGFIX-FRAGILE）：感应区只由方块预制体自带，SpawnNode 取 "Touch" 接线。
    /// 曾经这里还有一份代码动态创建感应区的死实现，位置完全埋在方块内部（踩不到），已删除，
    /// 避免「两份几何、其中一份是错的」再次误导维护者。
    /// </summary>
    private void OnTouched(int r, int c, Node body)
    {
        if (!_alive[r, c]) return;

        // 「角色」= 玩家 / 随从 / 未来怪物。弹跳块对三者都有效；
        // 脆弱木板与宝箱仍只给玩家（随从踩碎木板会让 AI 掉坑，宝箱属于玩家收益）。
        bool isMaster = body.IsInGroup(GameDefs.MasterGroup);
        bool isActor = isMaster
                       || body.IsInGroup(GameDefs.SpiritGroup)
                       || body.IsInGroup(GameDefs.EnemyGroup);

        switch (_type[r, c])
        {
            case BlockType.Fragile:
                if (!isMaster) break;
                StartFragileBlink(r, c);
                break;
            case BlockType.Bounce:
                if (!isActor) break;
                Jelly(r, c);
                (body as IBounceable)?.Bounce();
                break;
            case BlockType.Treasure:
                if (!isMaster) break;
                OpenTreasure(r, c);
                break;
        }
    }

    /// <summary>
    /// BUGFIX-FRAGILE：Area2D.BodyEntered 属于物理回调，里面同步 RemoveChild 一个 CollisionObject
    /// 会触发 Godot 的 "Removing a CollisionObject node during a physics callback is not allowed"。
    /// QueueFree 是帧末延迟释放（自动从父节点摘除），这里只置空引用，不做同步移除。
    /// </summary>
    private static void DeferredRemove(Node2D? n)
    {
        if (n != null && IsInstanceValid(n)) n.QueueFree();
    }

    /// <summary>
    /// FRAGILE-BLINK：踩中只启动闪烁 —— 碰撞、感应、_alive 全部不动，玩家仍可站在上面；
    /// _blinking 标记防止闪烁期间重复触碰重启动画。真正的判碎交给闪烁结束回调。
    /// </summary>
    private void StartFragileBlink(int r, int c)
    {
        if (!_alive[r, c] || _blinking[r, c]) return;
        _blinking[r, c] = true;
        FragileBlinkStartedCount++;
        BlinkThenFree(r, c, _nodes[r, c], _visuals[r, c]);
        GD.Print($"[BLOCKS] fragile blink start ({c},{r}) duration={GameDefs.FragileBlinkDuration}s");
    }

    /// <summary>
    /// 闪烁结束才判碎：此时才把格子从实体/可达集合摘掉、统计 +1、释放节点。
    /// 由 Tween 在 idle 帧回调（不是物理回调），QueueFree 仍是帧末延迟释放。
    /// </summary>
    private void FinishFragileBreak(int r, int c, Node2D? body)
    {
        if (!_blinking[r, c]) return;
        _blinking[r, c] = false;
        if (!_alive[r, c]) return;
        _alive[r, c] = false;
        _reachable[r, c] = false;
        _nodes[r, c] = null;
        _visuals[r, c] = null;
        BlockCount--;
        FragileBrokenCount++;
        FragileBlinkFinishedCount++;
        DeferredRemove(body);
        GD.Print($"[BLOCKS] fragile broken ({c},{r}) after blink");
    }

    /// <summary>
    /// FRAGILE-BLINK：绑定在 Sprite2D 上的一次性 Tween，做 2 次「木色 ↔ 白色」闪烁
    /// 只改颜色、绝不动 scale，满 1.5 秒后回调 FinishFragileBreak 才真正碎掉。
    /// Tween 宿主是 visual：换种子 Build 释放整张地图时 tween 自动失效，
    /// 回调不会打到已释放节点；回调内部再做 _blinking 状态校验。
    /// </summary>
    private void BlinkThenFree(int r, int c, Node2D? body, Sprite2D? visual)
    {
        if (body == null || !IsInstanceValid(body) || visual == null || !IsInstanceValid(visual))
        {
            FinishFragileBreak(r, c, body);
            return;
        }

        Color baseTint = Tint(BlockType.Fragile);
        Color flashTint = Colors.White;
        float half = GameDefs.FragileBlinkDuration / (GameDefs.FragileBlinkCycles * 2f);
        var tw = visual.CreateTween();
        for (int i = 0; i < GameDefs.FragileBlinkCycles; i++)
        {
            // 只改颜色，绝不动 scale —— 闪完必须原尺寸消失，不要放大/回弹。
            tw.TweenProperty(visual, "modulate", flashTint, half)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            tw.TweenProperty(visual, "modulate", baseTint, half)
                .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        }
        tw.TweenCallback(Callable.From(() => FinishFragileBreak(r, c, body)));
    }

    /// <summary>
    /// BOUNCE-JELLY 蹦蹦床动画（三段式，只动 Visual 子节点）：
    /// ① 踩沉压扁 + 亮黄高亮 → ② 纵向过冲拉长、块面上抬 → ③ Elastic 余震颤回原样。
    /// 碰撞形状、弹射速度、地图几何一概不动；动画期间重复触碰不重播，弹射不受影响。
    /// </summary>
    private void Jelly(int r, int c)
    {
        var visual = _visuals[r, c];
        if (visual == null || _jellyBusy[r, c]) return;
        _jellyBusy[r, c] = true;
        BounceJellyStartedCount++;
        GD.Print($"[BLOCKS] bounce jelly start ({c},{r})");

        Vector2 baseScale = visual.Scale;
        Vector2 basePos = visual.Position;
        Color baseTint = Tint(BlockType.Bounce);
        Color flashTint = new(1.0f, 0.95f, 0.55f, 1.0f);
        // Tween 宿主是 visual：换种子 Build 释放节点时动画自动停止，不会打到已释放节点。
        var tw = visual.CreateTween();

        // ① 下压：网面被踩沉，横向鼓起、纵向压扁，同时闪一下亮黄
        tw.TweenProperty(visual, "scale",
                new Vector2(baseScale.X * GameDefs.BounceSquashX, baseScale.Y * GameDefs.BounceSquashY),
                GameDefs.BounceSquashTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(visual, "position:y", basePos.Y + GameDefs.BounceDipY,
                GameDefs.BounceSquashTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(visual, "modulate", flashTint, GameDefs.BounceSquashTime);

        // ② 回弹：纵向拉长过冲、块面上抬、高亮回落
        tw.TweenProperty(visual, "scale",
                new Vector2(baseScale.X * GameDefs.BounceStretchX, baseScale.Y * GameDefs.BounceStretchY),
                GameDefs.BounceReleaseTime)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(visual, "position:y", basePos.Y - GameDefs.BounceLiftY,
                GameDefs.BounceReleaseTime)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(visual, "modulate", baseTint, GameDefs.BounceReleaseTime);

        // ③ 余震：Elastic 自带数次衰减抖动，像蹦床网面颤悠着停住
        tw.TweenProperty(visual, "scale", baseScale, GameDefs.BounceSettleTime)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(visual, "position:y", basePos.Y, GameDefs.BounceSettleTime)
            .SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        tw.TweenCallback(Callable.From(() => _jellyBusy[r, c] = false));

        SpawnBounceRing(r, c);
    }

    /// <summary>
    /// BOUNCE-JELLY 冲击环：Line2D 程序化扁椭圆，从块面扩散并淡出。
    /// 零 PNG / 零新资源 / 零碰撞；Tween 绑定环自身，播完 QueueFree。
    /// </summary>
    private void SpawnBounceRing(int r, int c)
    {
        var body = _nodes[r, c];
        if (body == null || !IsInstanceValid(body)) return;

        const int segments = 24;
        var ring = new Line2D
        {
            Name = "BounceRing",
            Position = new Vector2(0.0f, -0.35f),   // 块顶面上方一点
            ZIndex = 5,
            Width = 0.055f,
            DefaultColor = new Color(1.0f, 0.90f, 0.55f, 0.9f),
            Scale = Vector2.One * GameDefs.BounceRingStart,
        };
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.Tau / segments;
            points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.45f);   // 扁椭圆，像地面冲击波
        }
        ring.Points = points;
        body.AddChild(ring);

        var tw = ring.CreateTween().SetParallel(true);
        tw.TweenProperty(ring, "scale", Vector2.One * GameDefs.BounceRingEnd, GameDefs.BounceRingTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(ring, "modulate:a", 0.0f, GameDefs.BounceRingTime)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tw.Chain().TweenCallback(Callable.From(ring.QueueFree));
    }

    /// <summary>宝箱块：触碰打开，移除碰撞，掉落占位宝物（内容后续版本设计）。</summary>
    private void OpenTreasure(int r, int c)
    {
        if (!_alive[r, c]) return;
        _alive[r, c] = false;
        _reachable[r, c] = false;
        var n = _nodes[r, c];
        _nodes[r, c] = null;
        DeferredRemove(n);   // 触碰回调里不能同步移除碰撞体（统一帧末延迟释放）
        BlockCount--;

        if (SceneLootDrop != null)
        {
            var loot = SceneLootDrop.Instantiate<Node2D>();
            loot.Name = $"Loot_{c}_{r}";
            loot.Position = BlockCenter(r, c);
            // 零 PNG：掉落物与方块共用运行时白纹理；0.15 × 2px = 0.3 世界单位。
            var lootVisual = loot.GetNodeOrNull<Sprite2D>("Visual");
            if (lootVisual != null)
            {
                lootVisual.Texture = ArtBank.PlainTexture();
                lootVisual.Scale = Vector2.One * 0.15f;
            }
            AddChild(loot);
        }
        GD.Print($"[LOOT] 宝箱打开 ({c},{r}) —— 占位宝物，内容待设计");
    }
}
