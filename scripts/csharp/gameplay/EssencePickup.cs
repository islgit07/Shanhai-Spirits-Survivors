using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace ESSN-001 / COIN-001 灵韵掉落：重力落地 + 磁吸拾取。
/// 一次拾取同时入账灵韵（经验/召唤阀门）、贝币（营地）、灵魄（稀有跃迁资源）。
/// </summary>
public partial class EssencePickup : Node2D
{
    private int _essence;
    private int _coins;
    private int _soul;
    private float _floorY;
    private bool _landed;
    private Vector2 _vel;

    public void Setup(Vector2 pos, float floorY, int essence, int coins, int soul)
    {
        GlobalPosition = pos;
        _floorY = floorY;
        _essence = essence;
        _coins = coins;
        _soul = soul;
        _vel = new Vector2((float)GD.RandRange(-1.2, 1.2), -(float)GD.RandRange(1.0, 3.0));
        var sprite = ArtBank.MakeSprite("coin", new Vector2(0.45f, 0.45f));
        sprite.Modulate = soul > 0
            ? new Color(0.85f, 0.62f, 1.0f)
            : new Color(0.58f, 1.0f, 0.82f);
        AddChild(sprite);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!_landed)
        {
            _vel.Y += GameDefs.EssenceGravity * dt;
            GlobalPosition += _vel * dt;
            if (GlobalPosition.Y >= _floorY)
            {
                GlobalPosition = new Vector2(GlobalPosition.X, _floorY);
                _landed = true;
            }
        }
        Node2D? p = GetTree().GetFirstNodeInGroup("master") as Node2D;
        if (p == null) return;
        float dist = GlobalPosition.DistanceTo(p.GlobalPosition);
        if (dist < GameDefs.EssenceMagnetRange)
            GlobalPosition = GlobalPosition.Lerp(p.GlobalPosition, Mathf.Clamp(dt * 8.0f, 0.0f, 1.0f));
        if (dist < GameDefs.EssencePickRange)
        {
            Run.AddEssence(_essence);
            if (_coins > 0) Run.AddCoins(_coins);
            if (_soul > 0) Run.AddSoul(_soul);
            SpiritWorld.Active?.OnPickupCollected();
            QueueFree();
        }
    }
}
