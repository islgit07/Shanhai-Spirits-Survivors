using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace MONSTER-001 怪物对象池与刷怪口（v0.0007）：_Ready 预实例化 MonsterPoolSize 只怪物，
/// 激活 / 回收全部走池（激活全量重置状态，回收隐藏 + 移出屏外），正常游玩绝不
/// Instantiate / QueueFree——v0.0008 波次系统直接在这份池上加 WaveDirector，容量按幸存者量级预留。
/// 刷怪入口：SpawnAt（确定性落点，冒烟 / 波次用）/ SpawnNearPlayer（调试按钮，玩家附近随机落点）。
/// 职责边界：只管「生成与回收」；追击 AI 在 Monster，数值在 GameDefs，波次节奏留给 v0.0008。
/// </summary>
public partial class MonsterField : Node2D
{
    private const string MonsterScenePath = "res://scenes/monsters/monster_shambler.tscn";

    private readonly List<Monster> _pool = new();
    private readonly List<Monster> _active = new();
    private PackedScene? _monsterScene;
    private Master? _player;
    private BlockField? _blocks;
    private SoulField? _souls;   // 怪物死亡掉魂魄（死亡链路注入，见 Bind）

    /// <summary>当前场上激活的怪物（顺序 = 激活顺序，冒烟 / 波次读取）。</summary>
    public IReadOnlyList<Monster> Active => _active;

    /// <summary>魂魄场引用（冒烟核对掉落数量用）。</summary>
    public SoulField? Souls => _souls;

    /// <summary>池预实例化总数（冒烟核对）。</summary>
    public int PoolSize => _pool.Count;

    public override void _Ready()
    {
        _monsterScene = GD.Load<PackedScene>(MonsterScenePath);
        if (_monsterScene == null)
        {
            GD.PushError($"[MONSTER] 无法加载怪物场景 {MonsterScenePath}");
            return;
        }
        // 池预实例化（GodotPrompter：批量生成放加载期，不在游玩中 Instantiate）
        for (int i = 0; i < GameDefs.MonsterPoolSize; i++)
        {
            var monster = _monsterScene.Instantiate<Monster>();
            monster.Name = $"Monster_{i}";
            AddChild(monster);
            _pool.Add(monster);
            monster.Died += () => OnMonsterDied(monster);   // 死亡链路：掉魂魄 + 淡出回池
        }
        if (SmokeFlags.Has("--monster-smoke")) CallDeferred(new StringName(nameof(RunSmoke)));
    }

    public override void _PhysicsProcess(double delta)
    {
        // 掉出世界的怪物直接回收：池所有者统一管理生命周期，怪物自身不做释放
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            Monster m = _active[i];
            if (m.GlobalPosition.Y > GameDefs.FallKillY) Release(m);
        }
    }

    /// <summary>绑定追击目标、方块几何与魂魄场（GameRoot 在地形建好后调用一次）。</summary>
    public void Bind(Master player, BlockField blocks, SoulField souls)
    {
        _player = player;
        _blocks = blocks;
        _souls = souls;
    }

    /// <summary>
    /// 确定性生成：取一个非激活实例放到 pos 并全量重置状态。达到场上上限 / 池耗尽时
    /// PushWarning 并拒绝（绝不静默扩容——容量问题要在开发期暴露，波次系统再按需调参）。
    /// </summary>
    public Monster? SpawnAt(Vector2 pos)
    {
        if (_active.Count >= GameDefs.MonsterMaxActive)
        {
            GD.PushWarning($"[MONSTER] 达到场上上限 {GameDefs.MonsterMaxActive}，拒绝生成");
            return null;
        }
        Monster? monster = _pool.Find(m => !m.Active);
        if (monster == null)
        {
            GD.PushWarning($"[MONSTER] 池耗尽（{GameDefs.MonsterPoolSize}），拒绝生成");
            return null;
        }
        monster.GlobalPosition = pos;
        monster.Activate(_player!, _blocks!);
        _active.Add(monster);
        GD.Print($"[MONSTER] spawn at ({pos.X:F1},{pos.Y:F1}) active={_active.Count}");
        return monster;
    }

    /// <summary>调试入口：玩家附近随机落点（水平 4~8 格，地面层上方 2 格落下）。</summary>
    public void SpawnNearPlayer()
    {
        if (_player == null || !IsInstanceValid(_player)) return;
        float side = GD.Randf() < 0.5f ? -1f : 1f;
        float dist = (float)GD.RandRange(GameDefs.MonsterSpawnMinDist, GameDefs.MonsterSpawnMaxDist);
        SpawnAt(new Vector2(_player.GlobalPosition.X + side * dist, GameDefs.LayerY(0) - 2f));
    }

    /// <summary>回收一只怪物：隐藏 / 停处理 / 挪到屏外；激活时由 Activate 全量重置状态。</summary>
    public void Release(Monster monster)
    {
        // 死亡淡出路径在 Died 时已先行移出 Active，此处 Remove 兼容两种来源
        _active.Remove(monster);
        monster.Deactivate();
        monster.GlobalPosition = new Vector2(-10000f, -10000f);
    }

    /// <summary>清空场上怪物（每局开始时调用；池实例保留复用）。</summary>
    public void ReleaseAll()
    {
        for (int i = _active.Count - 1; i >= 0; i--) Release(_active[i]);
    }

    /// <summary>
    /// 怪物死亡链路（Died 信号同步触发）：立即移出 Active（随从停止选它）→ 死亡位置掉魂魄
    /// → 淡出动画（BeginDeath）→ 淡出结束后回池（定时器带存活防护，同 SpawnDust 教训）。
    /// </summary>
    private void OnMonsterDied(Monster monster)
    {
        _active.Remove(monster);
        _souls?.SpawnAt(monster.GlobalPosition);
        monster.BeginDeath();
        var tree = GetTree();
        tree.CreateTimer(GameDefs.MonsterDeathFadeTime + 0.05).Timeout += () =>
        {
            if (IsInstanceValid(monster) && IsInstanceValid(this)) Release(monster);
        };
    }

    // ---------------------------------------------------------------- 无头冒烟
    private async void RunSmoke()
    {
        GameRoot? root = GameRoot.Instance;
        if (_player == null || _blocks == null || root == null)
        {
            GD.PushError("[MONSTER] monster-smoke 缺少主角 / 地形 / GameRoot");
            GetTree().Quit(1);
            return;
        }
        root.SpiritGroup.SetMeleeEnabled(false);   // 冒烟期间关闭随从攻击：接触 / 死亡测试时序不受随从补刀干扰

        // 1) 池就绪：预实例化数量与空场起步
        bool poolOk = PoolSize == GameDefs.MonsterPoolSize && _active.Count == 0;
        int soulsSnapshot = SaveGame.Data.Souls;   // 拾取测试会加魂，结束前还原不污染存档

        // 2) 生成 + 落地：怪物是真实物理体，空中生成会落到地面（LayerY(0) 地面层）
        Vector2 spawnPos = new(_player.GlobalPosition.X + 5f, GameDefs.LayerY(0) - 3f);
        Monster? first = SpawnAt(spawnPos);
        bool spawnOk = first != null && first.Active && _active.Count == 1;
        await SmokeFlags.WaitPhysicsFrames(this, 40);
        bool landedOk = first != null && first.IsOnFloor();

        // 3) 追踪：玩家原地不动，怪物应在 1 秒内明显逼近（且尚未贴身，与伤害测试解耦）
        float dx0 = Mathf.Abs(_player.GlobalPosition.X - first!.GlobalPosition.X);
        await SmokeFlags.WaitPhysicsFrames(this, 60);
        float dx1 = Mathf.Abs(_player.GlobalPosition.X - first.GlobalPosition.X);
        bool chaseOk = dx1 < dx0 - 1.0f;

        // 4) 池复用：回收 first 再生成，应拿到同一实例（不新建节点）
        Release(first);
        bool releasedOk = _active.Count == 0 && !first.Active;
        Monster? reused = SpawnAt(spawnPos);
        bool reuseOk = reused == first && _active.Count == 1;

        // 5) 接触伤害 + 无敌帧（确定性布局）：玩家传送回出生点（GND-001 保证 row 0 地面连续），
        //    怪物放到 3 格外的地面层上方落下 → 自己走向玩家 → 接触扣血。
        //    用「轮询血量变化」测量，天然对齐无敌窗口相位，不依赖固定帧数采样。
        _player.GlobalPosition = new Vector2(0f, GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);
        _player.Velocity = Vector2.Zero;
        _player.ResetMover();
        reused!.GlobalPosition = _player.GlobalPosition + new Vector2(-3.0f, -1.5f);
        int hp0 = _player.Hp;
        await SmokeFlags.WaitPhysicsFrames(this, 30);   // 怪物落地并开始走近
        int wait1 = 0;   // 等第一次扣血（走近 + 贴身），超时 2 秒
        while (_player.Hp == hp0 && wait1 < 120) { await SmokeFlags.WaitPhysicsFrames(this, 1); wait1++; }
        bool contactOk = _player.Hp == hp0 - GameDefs.MonsterContactDamage && wait1 < 120;
        int hpAfterFirst = _player.Hp;   // 第二次扣血必须等无敌窗口（1.0s ≈ 60 帧）过去才发生
        int wait2 = 0;
        while (_player.Hp == hpAfterFirst && wait2 < 150) { await SmokeFlags.WaitPhysicsFrames(this, 1); wait2++; }
        bool invulnOk = _player.Hp == hpAfterFirst - GameDefs.MonsterContactDamage
                        && wait2 >= (int)(GameDefs.MasterInvulnTime * 60) - 15;

        // 5b) 怪物血量 / 死亡 / 魂魄掉落（直接调 TakeDamage，随从攻击已关闭不受干扰）
        int mHp0 = reused!.Hp;
        reused.TakeDamage(1);
        bool hpDamageOk = reused.Hp == mHp0 - 1 && !reused.Dead;
        reused.TakeDamage(reused.Hp);   // 一击打空血 → 死亡链路（掉魂魄 + 淡出回池）
        bool dieOk = reused.Dead && !_active.Contains(reused);
        await SmokeFlags.WaitPhysicsFrames(this, 25);   // 淡出 0.25s + 回池定时器
        bool dieReleaseOk = !reused.Active && reused.GlobalPosition.X <= -100f && !reused.Visible;
        bool soulDropOk = _souls?.SpawnedCount >= 1;   // 累计口径：顺路被玩家捡走不影响

        // 5c) 随从近战：塞满临时队伍（5 只环绕，最内环距玩家 0.75 格）→ 靶子怪放在本命脚下
        //     ——靶子怪贴到玩家身边后必进多只随从的射程被围殴秒杀（单本命站 1.55 环够不着，
        //     曾导致靶子怪反把玩家磨死）。轮询血量直至死亡，证明攻击组件真的在出手。
        root.SpiritGroup.SetMeleeEnabled(true);
        _player.SetHpForSmoke(GameDefs.MasterMaxHp);
        root.Companions.TryApply("A", out _);
        root.Companions.TryApply("B", out _);
        root.Companions.TryApply("C", out _);
        root.Companions.TryApply("D", out _);
        root.RefreshCompanions();
        SpiritPet? pet = root.SpiritGroup.Primary;
        Monster? target = pet != null && IsInstanceValid(pet)
            ? SpawnAt(pet.GlobalPosition + new Vector2(0.6f, -0.3f)) : null;
        bool companionAttackOk = false;
        if (target != null)
        {
            int waitAtk = 0;
            while (!target.Dead && waitAtk < 300) { await SmokeFlags.WaitPhysicsFrames(this, 1); waitAtk++; }
            companionAttackOk = target.Dead && target.Hp == 0;
        }
        root.Companions.Clear();
        root.RefreshCompanions();   // 恢复单本命队伍（临时队友本来就是当局数据）
        root.SpiritGroup.SetMeleeEnabled(false);
        Vector2 lastSoulPos = target?.GlobalPosition ?? Vector2.Zero;   // 死亡点 = 魂魄落点
        await SmokeFlags.WaitPhysicsFrames(this, 25);   // 淡出 + 回池 + 第二枚魂魄落地

        // 6) 场上上限：刷满 MonsterMaxActive 后再刷应被拒绝（激活数不再增长）
        Release(reused);
        for (int i = 0; i < GameDefs.MonsterMaxActive + 2; i++)
            SpawnAt(new Vector2(_player.GlobalPosition.X + 8f + i, GameDefs.LayerY(0) - 2f));
        bool capOk = _active.Count == GameDefs.MonsterMaxActive;

        // 7) 坠落回收：把一只怪扔到 FallKillY 以下，池应在几帧内回收并归还
        Monster faller = _active[0];
        faller.GlobalPosition = new Vector2(0f, GameDefs.FallKillY + 2f);
        await SmokeFlags.WaitPhysicsFrames(this, 5);
        bool recycleOk = !faller.Active && !_active.Contains(faller);

        // 8) 魂魄拾取：先在远离玩家 12 格处直接打死一只怪（随从攻击已关闭，魂魄没人顺路捡走，
        //    5c 的魂魄可能在玩家脚边出生即被捡，故拾取测试用专用怪保证确定性）→ 玩家传送到魂魄上
        //    → Area2D 感应 → SaveGame 加魂（轮询，超时 2 秒）
        Monster? farMonster = SpawnAt(new Vector2(_player.GlobalPosition.X - 12f, GameDefs.LayerY(0) - 2f));
        bool soulPickupOk = false;
        if (farMonster != null)
        {
            Vector2 soulPos = farMonster.GlobalPosition;
            farMonster.TakeDamage(farMonster.Hp);   // 直接打死（死亡链路：掉魂魄 + 淡出回池）
            await SmokeFlags.WaitPhysicsFrames(this, 25);
            int soulsNow = SaveGame.Data.Souls;
            _player.GlobalPosition = soulPos + new Vector2(0f, -0.2f);
            _player.Velocity = Vector2.Zero;
            _player.ResetMover();
            int waitPick = 0;
            while (SaveGame.Data.Souls == soulsNow && waitPick < 120) { await SmokeFlags.WaitPhysicsFrames(this, 1); waitPick++; }
            soulPickupOk = SaveGame.Data.Souls == soulsNow + GameDefs.SoulPickupValue && waitPick < 120;
        }

        // 9) 死亡回开始页：血量打到 1 → 场上任何一只怪贴身接触 → Died 信号 → GameRoot 置
        //    PlayerDiedSeen（同步信号；定格 0.8 秒后才切场景，冒烟在检测到死亡后立即退出）
        _player.SetHpForSmoke(1);
        SpawnAt(_player.GlobalPosition + new Vector2(0.5f, -0.6f));
        int wait3 = 0;
        while (!_player.Dead && wait3 < 150) { await SmokeFlags.WaitPhysicsFrames(this, 1); wait3++; }
        bool deathOk = root.PlayerDiedSeen && _player.Dead;

        bool bounceOk = first is IBounceable;   // 弹跳块对怪生效的接口口径（BlockField 按 EnemyGroup 调用）

        var assertions = new Dictionary<string, bool>
        {
            ["monster_pool_ok"] = poolOk,
            ["monster_spawn_ok"] = spawnOk,
            ["monster_landed_ok"] = landedOk,
            ["monster_chase_ok"] = chaseOk,
            ["monster_pool_reuse_ok"] = releasedOk && reuseOk,
            ["monster_contact_damage_ok"] = contactOk,
            ["monster_invuln_ok"] = invulnOk,
            ["monster_cap_ok"] = capOk,
            ["monster_fall_recycle_ok"] = recycleOk,
            ["monster_death_ok"] = deathOk,
            ["monster_bounce_capable_ok"] = bounceOk,
            ["monster_hp_damage_ok"] = hpDamageOk,
            ["monster_die_release_ok"] = dieOk && dieReleaseOk,
            ["soul_drop_ok"] = soulDropOk,
            ["companion_attack_ok"] = companionAttackOk,
            ["soul_pickup_ok"] = soulPickupOk,
        };
        bool ok = assertions.Values.All(v => v);
        SaveGame.SetSoulsForSmoke(soulsSnapshot);   // 还原玩家存档魂魄（拾取测试的净零证明见报告）

        var report = new Dictionary<string, object>
        {
            ["project"] = "山海幻灵",
            ["version"] = ProjectSettings.GetSetting("application/config/version", "dev").AsString(),
            ["scene"] = "MonsterField",
            ["result"] = ok ? "PASS" : "FAIL",
            ["pool_size"] = PoolSize,
            ["active_after_cap"] = _active.Count,
            ["hp_after_contact"] = _player.Hp,
            ["chase_dx_before"] = (float)Math.Round(dx0, 2),
            ["chase_dx_after"] = (float)Math.Round(dx1, 2),
            ["player_died_seen"] = root.PlayerDiedSeen,
            ["souls_on_ground"] = _souls?.ActiveCount ?? -1,
            ["souls_spawned"] = _souls?.SpawnedCount ?? -1,
            ["souls_snapshot_restored"] = SaveGame.Data.Souls == soulsSnapshot,
            ["assertions"] = assertions,
        };

        GD.Print("MONSTER_SMOKE_JSON_BEGIN");
        GD.Print(JsonSerializer.Serialize(report, SmokeFlags.IndentedJson));
        GD.Print("MONSTER_SMOKE_JSON_END");
        GD.Print($"[MONSTER] smoke result={(ok ? "PASS" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
