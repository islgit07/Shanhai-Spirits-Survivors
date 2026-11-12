using System.Collections.Generic;
using Godot;

/// <summary>
/// @trace PLAT-001 / MAP-001 / GND-001 / CSHARP-001 / AXIS-001
/// 2D 地形层（C# 迁移 Phase 1）。替代原 scripts/gameplay/PlatformField.gd。
///
/// @trace GND-001 硬约束：第一层（Layer 0）恒为「唯一一段、覆盖整条可行走宽度、
/// 非单向碰撞」的实体地板，不参与随机、不允许分段。上层 L1..L9 才是断续单向平台。
///
/// @trace CSHARP-001 首个 C# 玩法组件：[GlobalClass] + 静态类型 + typed signal，
/// GDScript 侧经 Build/GetSegmentCount/IsGroundContinuous 桥接，工具链见 docs/csharp-migration.md。
///
/// @trace AXIS-001 Godot 2D 是 y-down：层号越大 y 越小（屏幕更高），跳跃是 -y。
/// 数值在 Phase 2 统一到 C# GameConfig 之前，与 GDScript GameConfig.gd 保持一致。
/// </summary>
[GlobalClass]
public partial class PlatformField : Node2D
{
    /// <summary>地形构建完成。[param segmentCount] 段总数；[param groundContinuous] 第一层是否连续全覆盖。</summary>
    [Signal]
    public delegate void GroundBuiltEventHandler(int segmentCount, bool groundContinuous);

    // ---- 与 GameConfig.gd 对齐的过渡期常量（Phase 2 收敛到 C# GameConfig） ----
    public const float WorldHalfWidth = 24.0f;
    public const float GroundY = -2.2f;
    public const int LayerCount = 10;
    public const float LayerSpacing = 3.2f;
    public const float PlatformThickness = 0.24f;
    public const float MinWidth = 3.4f;
    public const float MaxWidth = 7.0f;
    public const float MinGap = 1.8f;

    public readonly struct Segment
    {
        public readonly int Layer;
        public readonly float X0;
        public readonly float X1;
        public readonly float Y;
        public readonly bool OneWay;

        public Segment(int layer, float x0, float x1, float y, bool oneWay)
        {
            Layer = layer;
            X0 = x0;
            X1 = x1;
            Y = y;
            OneWay = oneWay;
        }
    }

    private readonly List<Segment> _segments = new();
    private RandomNumberGenerator _rng = new();

    public int SegmentCount => _segments.Count;

    /// <summary>GDScript 互操作入口：方法调用比属性反射更稳定。</summary>
    public int GetSegmentCount() => _segments.Count;

    /// <summary>层号 → 世界 y。y-down：高层在屏幕上方。</summary>
    public static float LayerY(int layer) =>
        GroundY - Mathf.Clamp(layer, 0, LayerCount - 1) * LayerSpacing;

    public static int SegmentsFor(float worldWidth) =>
        Mathf.Clamp(Mathf.RoundToInt(worldWidth / 8.0f), 5, 8);

    /// <summary>按种子重建整张地形。可重复调用，旧节点一律安全延迟释放。</summary>
    public void Build(int seedValue)
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _segments.Clear();
        _rng = new RandomNumberGenerator { Seed = DeriveSeed(seedValue) };

        // GND-001：唯一一段、覆盖 [-W, +W]、实体地板。刻意不走随机分支。
        AddSegment(new Segment(0, -WorldHalfWidth, WorldHalfWidth, LayerY(0), false));

        float width = WorldHalfWidth * 2.0f;
        int count = SegmentsFor(width);
        float step = width / count;
        for (int layer = 1; layer < LayerCount; layer++)
        {
            for (int i = 0; i < count; i++)
            {
                float center = -WorldHalfWidth + step * (i + 0.5f)
                               + _rng.RandfRange(-step * 0.18f, step * 0.18f);
                float maxWidth = Mathf.Max(1.6f, step - MinGap);
                float w = _rng.RandfRange(MinWidth, Mathf.Min(MaxWidth, maxWidth));
                AddSegment(new Segment(layer, center - w * 0.5f, center + w * 0.5f, LayerY(layer), true));
            }
        }

        bool continuous = IsGroundContinuous();
        GD.Print($"[GROUND] segments={_segments.Count} ground_continuous={continuous}");
        EmitSignal(SignalName.GroundBuilt, _segments.Count, continuous);
    }

    /// <summary>GND-001 运行时不变量：第一层恰好一段、非单向、左右都盖满可行走宽度。</summary>
    public bool IsGroundContinuous()
    {
        int groundCount = 0;
        foreach (Segment s in _segments)
        {
            if (s.Layer != 0)
                continue;
            groundCount++;
            if (s.OneWay || s.X0 > -WorldHalfWidth + 0.001f || s.X1 < WorldHalfWidth - 0.001f)
                return false;
        }
        return groundCount == 1;
    }

    /// <summary>取指定层的随机可行走 x；第一层恒覆盖全域。</summary>
    public float RandomX(int layer)
    {
        var candidates = new List<Segment>();
        foreach (Segment s in _segments)
        {
            if (s.Layer == layer)
                candidates.Add(s);
        }
        if (candidates.Count == 0)
            return 0.0f;
        Segment pick = candidates[_rng.RandiRange(0, candidates.Count - 1)];
        return _rng.RandfRange(pick.X0 + 0.4f, pick.X1 - 0.4f);
    }

    private void AddSegment(Segment s)
    {
        var size = new Vector2(s.X1 - s.X0, PlatformThickness);
        var body = new StaticBody2D
        {
            Position = new Vector2((s.X0 + s.X1) * 0.5f, s.Y),
            CollisionLayer = 1,
        };
        var collision = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = size },
            OneWayCollision = s.OneWay,
            OneWayCollisionMargin = 0.2f,
        };
        body.AddChild(collision);
        var tint = s.Layer == 0 ? new Color(0.30f, 0.36f, 0.30f) : new Color(0.42f, 0.40f, 0.52f);
        body.AddChild(new ColorRect
        {
            Color = tint,
            Size = size,
            Position = new Vector2(-size.X * 0.5f, -size.Y * 0.5f),
        });
        AddChild(body);
        _segments.Add(s);
    }

    /// <summary>确定性种子：FNV-1a("platforms") xor 局种子。与 GDScript RunSeed 独立，但同种子结果稳定。</summary>
    private static ulong DeriveSeed(int seedValue)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            foreach (char c in "platforms")
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash ^ (ulong)(long)seedValue;
        }
    }
}
