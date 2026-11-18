using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace MONSTER-001 魂魄拾取物对象池（v0.0007 追加）：怪物死亡原地掉一枚魂魄
/// （用户定义：魂魄 = 怪物死亡掉落物），留在地上等玩家自己走过去捡——不追踪、不磁吸。
/// 拾取即 SaveGame.AddSouls（养成界面的货币同一份）。对象池预实例化，激活全量重置，
/// 回收隐藏屏外，正常游玩绝不 Instantiate / QueueFree（与 MonsterField 同一套池规范）。
/// </summary>
public partial class SoulField : Node2D
{
    private const string SoulScenePath = "res://scenes/fx/soul_pickup.tscn";

    private readonly List<SoulPickup> _pool = new();
    private Master? _player;
    private PackedScene? _soulScene;

    /// <summary>当前地上等待拾取的魂魄数（诊断 / 冒烟读取）。</summary>
    public int ActiveCount { get; private set; }

    /// <summary>本次运行累计掉落的魂魄总数（只增；顺路被捡也不影响，冒烟断言用）。</summary>
    public int SpawnedCount { get; private set; }

    public override void _Ready()
    {
        _soulScene = GD.Load<PackedScene>(SoulScenePath);
        if (_soulScene == null)
        {
            GD.PushError($"[SOUL] 无法加载魂魄场景 {SoulScenePath}");
            return;
        }
        // 池预实例化（加载期批量生成，游玩中零 Instantiate）
        for (int i = 0; i < GameDefs.SoulPickupPoolSize; i++)
        {
            var soul = _soulScene.Instantiate<SoulPickup>();
            soul.Name = $"Soul_{i}";
            AddChild(soul);
            _pool.Add(soul);
        }
    }

    /// <summary>绑定收集者（GameRoot 调用一次）。</summary>
    public void Bind(Master player) => _player = player;

    /// <summary>在怪物死亡位置掉一枚魂魄；池耗尽 PushWarning 拒绝（不静默扩容）。</summary>
    public void SpawnAt(Vector2 pos)
    {
        SoulPickup? soul = _pool.Find(s => !s.Active);
        if (soul == null)
        {
            GD.PushWarning($"[SOUL] 魂魄池耗尽（{GameDefs.SoulPickupPoolSize}），本次掉落丢弃");
            return;
        }
        soul.GlobalPosition = pos;
        soul.Activate(_player!);
        ActiveCount++;
        SpawnedCount++;
        GD.Print($"[SOUL] drop at ({pos.X:F1},{pos.Y:F1}) on_ground={ActiveCount}");
    }

    /// <summary>回收一枚魂魄（玩家拾取后由 SoulPickup 回调，或开局清空）。</summary>
    public void Release(SoulPickup soul)
    {
        if (!soul.Active) return;
        soul.Deactivate();
        soul.GlobalPosition = new Vector2(-10000f, -10000f);
        ActiveCount--;
    }

    /// <summary>清空地上魂魄（每局开始时调用；池实例保留复用）。</summary>
    public void ReleaseAll()
    {
        foreach (SoulPickup s in _pool) Release(s);
        SpawnedCount = 0;
    }
}
