using System.Collections.Generic;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace PLAT-001 / MAP-001 / GND-001 / AXIS-001 / CSHARP-001
/// 2D 地形层。v0.003 起数值统一从 GameDefs 读取，不再与 GDScript 各存一份。
///
/// @trace GND-001 硬约束（用户明确要求）：第一层恒为「唯一一段、覆盖整条可行走宽度、
/// 非单向碰撞」的实体地板，不参与随机、不允许分段。上层 L1..L9 才是断续单向平台。
/// @trace MAP-002 随机分布：上层平台的台数 / 位置 / 层高全部随机（种子可复现），
/// 同层相邻端点仍硬保证 MinGap。
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
    private const float MinGap = 3.0f;            // 相邻平台端点最小间隙（硬保证）
    private const int MinPlatformsPerLayer = 3;   // 每层期望台数下限（MAP-002 随机）
    private const int MaxPlatformsPerLayer = 5;   // 每层期望台数上限
    private const float LayerJitterY = 0.20f;     // 层高随机偏移幅度（±）：相邻层高差 2.6~3.4 浮动
    private const int PlaceAttempts = 24;         // 单台撒点重试上限（超限则该层少一台）
    private const float EdgeMargin = 0.5f;        // 平台端点离世界边缘的最小距离

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

        // @trace MAP-002 随机分布：每层台数（3~5）/ 中心位置（整宽撒点）/ 层高（基准 ±0.2）全随机；
        // 撒点带间距拒绝采样，同层相邻端点仍硬保证 MinGap。层高偏移使相邻层高差在 2.6~3.4 间浮动，
        // 落在单跳可达范围（满跳 ≈3.67）→ 竖向攀爬路线随种子变化，带来探索感。
        for (int layer = 1; layer < LayerCount; layer++)
        {
            float y = LayerY(layer) + _rng.RandfRange(-LayerJitterY, LayerJitterY);
            int count = _rng.RandiRange(MinPlatformsPerLayer, MaxPlatformsPerLayer);
            var placedX = new List<float>(count);
            var placedW = new List<float>(count);
            for (int i = 0; i < count; i++)
            {
                for (int attempt = 0; attempt < PlaceAttempts; attempt++)
                {
                    float w = _rng.RandfRange(MinWidth, MaxWidth);
                    float margin = w * 0.5f + EdgeMargin;
                    float cx = _rng.RandfRange(-WorldHalfWidth + margin, WorldHalfWidth - margin);
                    bool clear = true;
                    for (int j = 0; j < placedX.Count; j++)
                    {
                        if (Mathf.Abs(cx - placedX[j]) < (w + placedW[j]) * 0.5f + MinGap)
                        {
                            clear = false;
                            break;
                        }
                    }
                    if (!clear) continue;
                    placedX.Add(cx);
                    placedW.Add(w);
                    AddSegment(new Segment(layer, cx - w * 0.5f, cx + w * 0.5f, y, true));
                    break;
                }
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

    /// <summary>冒烟测试用：取第一块上层单向平台的中心点（台面 y）。</summary>
    public bool GetFirstOneWay(out float centerX, out float topY)
    {
        foreach (Segment s in _segments)
        {
            if (s.Layer == 0 || !s.OneWay) continue;
            centerX = (s.X0 + s.X1) * 0.5f;
            topY = s.Y;
            return true;
        }
        centerX = 0.0f; topY = 0.0f;
        return false;
    }


    private void AddSegment(Segment s)
    {
        var size = new Vector2(s.X1 - s.X0, PlatformThickness);
        var body = new StaticBody2D
        {
            Position = new Vector2((s.X0 + s.X1) * 0.5f, s.Y),
            // 碰撞分层：实体地板 LayerSolid（不可下穿）；单向平台 LayerOneWay（DROP-001 可下穿）
            CollisionLayer = s.OneWay ? GameDefs.LayerOneWay : GameDefs.LayerSolid,
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
