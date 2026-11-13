using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// v0.0001 最小世界：背景 + 地形 + 主角 + 相机跟随。
/// 战斗、刷怪、掉落、循环阶段全部不存在——等本轮对话逐项设计后再加回来。
/// 所有节点运行时生成，Main.tscn 只挂 GameRoot。
/// </summary>
public partial class WorldRoot : Node2D
{
    public Master? Player;
    public int PlatformCount;
    public bool GroundContinuous;
    public bool PlayerGrounded;

    private Camera2D _camera = null!;
    private Sprite2D _backdrop = null!;
    private PlatformField _platforms = null!;

    public void BeginRun(int seed)
    {
        ClearAll();

        float z = ZoomValue();
        _camera = new Camera2D { Zoom = new Vector2(z, z), Enabled = true };
        AddChild(_camera);
        _backdrop = ArtBank.MakeSprite(GameDefs.BackdropArt, ViewSizeWorld());
        _backdrop.ZIndex = -100;
        _backdrop.Modulate = GameDefs.BackdropTint;
        _camera.AddChild(_backdrop);

        _platforms = new PlatformField();
        AddChild(_platforms);
        _platforms.Build(seed);
        PlatformCount = _platforms.GetSegmentCount();
        GroundContinuous = _platforms.IsGroundContinuous();

        Player = new Master();
        AddChild(Player);
        // 出生点在第一层地板上方（y-down：y 更小 = 更高）
        Player.GlobalPosition = new Vector2(0.0f,
            GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);

    }

    public float ZoomValue()
    {
        float h = GetViewportRect().Size.Y <= 0.0f ? 720.0f : GetViewportRect().Size.Y;
        return h / (2.0f * GameDefs.CameraViewHalfHeight);
    }

    public Vector2 ViewSizeWorld() => GetViewportRect().Size / ZoomValue();

    /// <summary>冒烟测试用：把主角放到第一块单向平台的中心上方，供「下 + 跳」下穿验证。</summary>
    public bool SmokeTeleportToPlatform()
    {
        if (Player == null || !IsInstanceValid(Player) || _platforms == null) return false;
        if (!_platforms.GetFirstOneWay(out float x, out float y)) return false;
        Player.GlobalPosition = new Vector2(x, y - 0.05f);
        Player.Velocity = Vector2.Zero;
        Player.ResetMover();
        return true;
    }

    public override void _Process(double delta)
    {
        if (Player == null || !IsInstanceValid(Player) || _camera == null) return;
        if (Player.IsOnFloor()) PlayerGrounded = true;
        Vector2 half = ViewSizeWorld() * 0.5f;
        float cx = Mathf.Clamp(Player.GlobalPosition.X,
            -GameDefs.WorldHalfWidth + half.X, GameDefs.WorldHalfWidth - half.X);
        float cy = Mathf.Min(GameDefs.LayerY(0) - half.Y * 0.4f,
            Player.GlobalPosition.Y - 1.0f);
        _camera.GlobalPosition = _camera.GlobalPosition.Lerp(new Vector2(cx, cy),
            Mathf.Clamp((float)delta * GameDefs.CameraSmooth, 0.0f, 1.0f));
    }

    private void ClearAll()
    {
        foreach (Node c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        Player = null;
        _camera = null!;
        _backdrop = null!;
        _platforms = null!;
        PlatformCount = 0;
        GroundContinuous = false;
        PlayerGrounded = false;
    }
}
