using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 单枚魂魄拾取物（场景 soul_pickup.tscn 的根）：Area2D 感应玩家走过去（mask=LayerActor，
/// 只有玩家能捡——随从踩过不收），收集即 SaveGame.AddSouls + 缩放反馈后由 SoulField 回池。
/// 待机时轻微上下浮动（Tween 每次激活重建）；原地不动，不追踪玩家（用户指定玩家自己去捡）。
/// </summary>
public partial class SoulPickup : Area2D
{
    private Master? _player;
    private bool _active;
    private bool _collecting;   // 拾取只触发一次（反馈播完才回池）
    private Sprite2D? _sprite;
    private Tween? _bob;

    /// <summary>是否在地上等待拾取（池诊断 / 冒烟读取）。</summary>
    public bool Active => _active;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = GameDefs.LayerActor;
        _sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
        BodyEntered += OnBodyEntered;
        Deactivate();
    }

    /// <summary>池激活：全量重置（位置由 SoulField 先设好），启动浮动动画。</summary>
    public void Activate(Master player)
    {
        _player = player;
        _collecting = false;
        _active = true;
        Visible = true;
        Monitoring = true;
        if (_sprite != null)
        {
            _sprite.Modulate = Colors.White;
            _sprite.Scale = Vector2.One;
            _sprite.Position = Vector2.Zero;
            _bob?.Kill();
            _bob = CreateTween().SetLoops();
            _bob.TweenProperty(_sprite, "position:y", -0.08f, 0.5f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            _bob.TweenProperty(_sprite, "position:y", 0.0f, 0.5f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }
    }

    /// <summary>池回收：停感应 / 隐藏 / 停动画；位置由 SoulField 挪到屏外。</summary>
    public void Deactivate()
    {
        _active = false;
        _collecting = false;
        Visible = false;
        Monitoring = false;
        _bob?.Kill();
        _bob = null;
        if (_sprite != null) _sprite.Position = Vector2.Zero;
    }

    private void OnBodyEntered(Node body)
    {
        if (!_active || _collecting || body != _player) return;
        _collecting = true;
        SaveGame.AddSouls(GameDefs.SoulPickupValue);
        GD.Print($"[SOUL] 玩家拾取魂魄 +{GameDefs.SoulPickupValue} souls={SaveGame.Data.Souls}");
        _bob?.Kill();
        if (_sprite != null)
        {
            var tw = CreateTween();
            tw.TweenProperty(_sprite, "scale", Vector2.One * 1.6f, 0.12f)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            tw.Parallel().TweenProperty(_sprite, "modulate:a", 0.0f, 0.12f);
        }
        // 反馈播完回池；SceneTreeTimer 回调带存活防护（同 SpawnDust 教训）
        var field = GetParent() as SoulField;
        GetTree().CreateTimer(0.15).Timeout += () =>
        {
            if (IsInstanceValid(this) && field != null && IsInstanceValid(field)) field.Release(this);
        };
    }
}
