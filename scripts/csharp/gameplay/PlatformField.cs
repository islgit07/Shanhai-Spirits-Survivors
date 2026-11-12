using System.Collections.Generic;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace PLAT-001 / MAP-001 / GND-001 / AXIS-001 / CSHARP-001
/// 2D 地形层。v0.003 起数值统一从 GameDefs 读取，不再与 GDScript 各存一份。
///
/// @trace GND-001 硬约束（用户明确要求）：第一层恒为「唯一一段、覆盖整条可行走宽度、
/// 非单向碰撞」的实体地板，不参与随机、不允许分段。上层 L1..L9 才是断续单向平台。
/// @trace AXIS-001 Godot 2D 为 y-down：层号越大 y 越小，跳跃是 -y。
/// </summary>
[GlobalClass]
public partial class PlatformField : Node2D
{
    [Signal]
    public delegate void GroundBuiltEventHandler(int segmentCount, bool groundContinuous);

    private const float WorldHalfWidth = GameDefs.WorldHalfWidth;
    private const int LayerCount = GameDefs.LayerCount;
    private const float PlatformThickness = GameDefs.PlatformThickness;
    private const float MinWidth = 3.4f;
    private const float MaxWidth = 7.0f;
    private const float MinGap = 1.8f;

    private const float GroundY = GameDefs.GroundY;
    private const float LayerSpacing = GameDefs.LayerSpacing;

    public readonly struct Segment
    {
        public readonly int Layer;
        public readonly float X0;
        public readonly float X1;
        public readonly float Y;
        public readonly bool OneWay;

        public Segment(int layer, float x0, float x1, float y, bool oneWay)
        {
            Layer = layer; X0 = x0; X1 = x1; Y = y; OneWay = oneWay;
        }
    }

    private readonly List<Segment> _segments = new();
    private RandomNumberGenerator _rng = new();

    public int SegmentCount => _segments.Count;
    public int GetSegmentCount() => _segments.Count;

    public static float LayerY(int layer) =>
        GroundY - Mathf.Clamp(layer, 0, LayerCount - 1) * LayerSpacing;

    /// <summary>按种子重建整张地形，可重复调用，旧节点延迟释放。</summary>
    public void Build(int seedValue)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _segments.Clear();
        _rng = new RandomNumberGenerator { Seed = Rng.DeriveSeed64(seedValue, "platforms") };

        // GND-001：唯一一段、覆盖 [-W,+W]、实体地板。刻意不走随机分支。
        AddSegment(new Segment(0, -WorldHalfWidth, WorldHalfWidth, LayerY(0), false));

        float width = WorldHalfWidth * 2.0f;
        int count = GameDefs.SegmentsFor(width);
        float step = width / count;
        for (int layer = 1; layer < LayerCount; layer++)
        {
            for (int i = 0; i < count; i++)
            {
                float center = -WorldHalfWidth + step * (i + 0.5f)
                               + _rng.RandfRange(-step * 0.18f, step * 0.18f);
                float maxWidth = Mathf.Max(1.6f, step - MinGap);
                float w = _rng.RandfRange(MinWidth, Mathf.Min(MaxWidth, maxWidth));
                AddSegment(new Segment(layer, center - w * 0.5f, center + w * 0.5f,
                    LayerY(layer), true));
            }
        }
        bool continuous = IsGroundContinuous();
        GD.Print($"[GROUND] segments={_segments.Count} ground_continuous={continuous}");
        EmitSignal(SignalName.GroundBuilt, _segments.Count, continuous);
    }

    /// <summary>GND-001 运行时不变量：第一层恰好一段、非单向、左右盖满可行走宽度。</summary>
    public bool IsGroundContinuous()
    {
        int groundCount = 0;
        foreach (Segment s in _segments)
        {
            if (s.Layer != 0) continue;
            groundCount++;
            if (s.OneWay || s.X0 > -WorldHalfWidth + 0.001f || s.X1 < WorldHalfWidth - 0.001f)
                return false;
        }
        return groundCount == 1;
    }

    public float RandomX(int layer)
    {
        var candidates = new List<Segment>();
        foreach (Segment s in _segments)
            if (s.Layer == layer) candidates.Add(s);
        if (candidates.Count == 0) return 0.0f;
        Segment pick = candidates[_rng.RandiRange(0, candidates.Count - 1)];
        return _rng.RandfRange(pick.X0 + 0.4f, pick.X1 - 0.4f);
    }

    private void AddSegment(Segment s)
    {
        var size = new Vector2(s.X1 - s.X0, PlatformThickness);
        var body = new StaticBody2D
        {
            Position = new Vector2((s.X0 + s.X1) * 0.5f, s.Y),
            CollisionLayer = 1u,
        };
        body.AddChild(new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = size },
            OneWayCollision = s.OneWay,
            OneWayCollisionMargin = 0.2f,
        });
        var tint = s.Layer == 0
            ? new Color(0.30f, 0.36f, 0.30f) : new Color(0.42f, 0.40f, 0.52f);
        body.AddChild(new ColorRect
        {
            Color = tint, Size = size,
            Position = new Vector2(-size.X * 0.5f, -size.Y * 0.5f),
        });
        AddChild(body);
        _segments.Add(s);
    }
}
