using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace CFG-001 / DEF-001 / SPEC-001
/// 集中数值表 + 三张设计表（学习 Boneraiser Minions 拆包结论：MinionDef / OfferDef / MetaDef）。
///
/// SPEC-001「幻灵 / 幻兽是同一种」的落地方式：
///   只有一张 SpeciesDef 表、一个 Spirit 类；阵营由 Faction 枚举决定，不改数值、不改技能、不改贴图。
///   野生幻兽从这张表按夜解锁刷出，唤灵选项也从这张表产出——野外打的和你能召的是同一个 id。
/// </summary>
public enum SpiritTier { Common = 0, Spirit = 1, Warden = 2, Sacred = 3 }
public enum Faction { Wild = 0, Bound = 1 }
public enum AttackKind { Melee = 0, Bolt = 1, Aura = 2, Chain = 3 }
// ★助手提案 Rest：候选池被 cap / 前置全部卡死时防止软锁，不引入新系统。
public enum OfferKind { Raise = 0, Bolster = 1, Fuse = 2, Rest = 3 }
public enum RunPhase { Menu = 0, Day = 1, Night = 2, Summon = 3, Dead = 4 }

/// <summary>物种定义：敌我共用。Traits 是 Lv1/Lv2/Lv3 三段质变描述，不是纯数值。</summary>
public sealed class SpeciesDef
{
    public string Id = "";
    public string Name = "";
    public string Sprite = "";
    public SpiritTier Tier;
    public int Hp;
    public int Damage;
    public float Speed;
    public float Range;
    public float Interval;
    public AttackKind Attack;
    public int Cap;              // 分类 cap：强制构筑多样性，堆单一高阶数学上不成立
    public int UnlockDay = 1;
    public string[] Traits = Array.Empty<string>();
}

/// <summary>融合配方（OfferDef 的献祭分支）。对应 BM 的 sCNI_Offer_* / BI_*_Dest。</summary>
public sealed class FuseRecipe
{
    public string Id = "";
    public string Title = "";
    public string ResultId = "";
    public SpiritTier CostTier;
    public int CostCount;
    public int CostMinLevel = 1;
    public int ResultLevel = 1;
    public int SoulCost;
    public int Weight = 10;
    public string RequiredLore = "";   // 局外 Lore 未解锁 → 该分支永不进池
    public string Desc = "";
}

/// <summary>局外 Lore（MetaDef）：改变的是候选池的可能性空间，不是单纯加数值。</summary>
public sealed class LoreDef
{
    public string Id = "";
    public string Name = "";
    public string Desc = "";
    public int MaxLevel = 1;
    public int BasePrice = 20;
    public int Price(int level) => BasePrice * (level + 1);
}

/// <summary>白天山海谷营地器具（原「建造物」重新定位，功能数量不变）。</summary>
public sealed class CampItem
{
    public string Id = "", Name = "", Desc = "", Kind = "";
    public int Price;
}

public static class GameDefs
{
    // ---------------------------------------------------------------- 阶段 / 世界
    public const float DayDuration = 20.0f;
    public const float NightDuration = 30.0f;
    public const int PickCount = 3;

    public const float WorldHalfWidth = 24.0f;
    public const float GroundY = -2.2f;
    public const int LayerCount = 10;
    public const float LayerSpacing = 3.2f;
    public const float PlatformThickness = 0.24f;
    public const float SpawnAboveGround = 1.0f;
    public const float FallKillY = 8.0f;
    public const bool GroundContinuous = true;   // @trace GND-001 口径常量

    public static float LayerY(int layer) =>
        GroundY - Mathf.Clamp(layer, 0, LayerCount - 1) * LayerSpacing;

    public static int SegmentsFor(float worldWidth) =>
        Mathf.Clamp(Mathf.RoundToInt(worldWidth / 8.0f), 5, 8);

    // ---------------------------------------------------------------- 幻灵师（主角不直接攻击）
    // 跳跃手感目标 = 复刻《终焉之莉莉》：轻盈可控、顶点悬停、短按矮跳、长按照高。
    // 数值为本作调参（非原作提取），由跳跃白箱（scenes/Whitebox.tscn）继续微调后写回这里。
    public const float MasterMoveSpeed = 6.5f;     // 略快于旧 6.0，空中更易修正落点
    public const float Gravity = 22.0f;            // 低于旧 26，整体更飘
    public const float MasterJumpSpeed = 13.5f;    // 满跳高度 ≈ 4.26 > 层高 3.2 → 单跳可跨一层
    // 以下三项默认值 = 「与旧行为完全一致」；由跳跃白箱（scenes/Whitebox.tscn）调出后写回这里。
    public const float MasterAirControl = 0.9f;    // 空中水平控制（略低于地面，保留惯性）
    public const float MasterCoyoteTime = 0.10f;   // 土狼时间（离台沿后仍可起跳的宽限）
    public const float MasterJumpBuffer = 0.12f;   // 跳跃缓冲（落地前预按自动起跳）
    // 《终焉之莉莉》式跳跃细节
    public const float MasterJumpCutMul = 0.45f;   // 变高跳：上升松键截断系数（短按≈0.9 单位矮跳）
    public const float MasterFallGravityMul = 1.5f;// 下落比上升更重 → 落得利落
    public const float MasterApexGravityMul = 0.55f;// 顶点滞空：穿过顶点附近降低重力
    public const float MasterApexThreshold = 2.5f; // 顶点区速度阈值（单位/秒）
    public const float MasterMaxFallSpeed = 28.0f; // 终端下落速度封顶
    public const int MasterMaxHp = 100;
    public const float MasterInvuln = 0.55f;
    public static readonly Vector2 MasterSize = new(0.8f, 1.2f);
    // 验算：满跳高度 = (13.5²-2.5²)/(2×22) + 2.5²/(2×22×0.55) ≈ 4.26 > 层高 3.2 → 单跳可跨一层
    //        短按高度 ≈ 4.26 × 0.45² ≈ 0.86（矮跳）

    // ---------------------------------------------------------------- @trace FOLLOW-001 幻灵跟随
    // A 级依据（BM data.win VARI 变量真名）：minion_slot / minion_vx_base / minion_vy_base /
    // move_to_radius / sMinion_GetMoveSpeed / vPushRadius / vBob_X|Y / vIdle_X|Y / vIdleSpot。
    // 含义：每只幻灵有「自己的落点 = 幻灵师位置 + 个人偏移」，离得远加速追、靠近就停、
    // 彼此按互斥半径推开、静止时在闲逛点附近浮动 —— 所以是松散随从群，不是均分圆环。
    // 数值为本作调参（迁移是 B 级；原作常量未从字节码取得）。
    public const float FollowAnchorMin = 0.85f;      // 个人落点最小半径
    public const float FollowAnchorStep = 0.42f;     // 半径档距（uid % 5 档）
    public const float FollowAnchorSizeMul = 0.55f;  // 体积越大站得越外
    public const float FollowVerticalSquash = 0.50f; // 横板适配：垂直椭圆压扁（横向铺开为主）
    public const float FollowStopRadius = 0.18f;     // move_to_radius：进入死区即停
    public const float FollowBaseSpeed = 2.2f;       // 落点附近的最低速度
    public const float FollowCatchUp = 3.2f;         // 每 1 单位距离追加的速度
    public const float FollowMaxSpeed = 15.0f;       // 追人速度上限（不会瞬移）
    public const float FollowPushRadius = 0.62f;     // vPushRadius：互相推开
    public const float FollowMasterClear = 0.95f;    // 与幻灵师的净空
    public const float FollowWanderRadius = 0.35f;   // vIdleSpot：闲逛半径
    public const float FollowBobAmount = 0.07f;      // vBob：呼吸浮动幅度
    public const float FollowHeightBias = 0.26f;    // 个体高低偏置：避免全员一条水平线

    // ---------------------------------------------------------------- 波次
    public const int BudgetBase = 14;
    public const int BudgetPerDay = 7;
    public const int BudgetCap = 130;
    public const float SpawnIntervalMin = 0.22f;
    public const float SpawnIntervalMax = 0.85f;
    public const float SpawnWindowRatio = 0.86f;
    public const float HpScalePerDay = 0.16f;
    public const float DamageScalePerDay = 0.08f;
    public const float SpeedScalePerDay = 0.02f;

    public static int Budget(int day) =>
        Mathf.Min(BudgetCap, BudgetBase + BudgetPerDay * Mathf.Max(0, day - 1));

    public static float SpawnInterval(int day) =>
        Mathf.Clamp(NightDuration * SpawnWindowRatio / Mathf.Max(1, Budget(day)),
            SpawnIntervalMin, SpawnIntervalMax);

    // ---------------------------------------------------------------- 灵韵 / 经济（BM 骨头模型）
    // ★助手提案：BM 的 vBones_NextLevel 精确公式未从字节码取得，这里用自定线性递推，
    //   不冒充原作数值。设计意图一致：阈值动态增长，后期需要更多击杀才能触发一次唤灵。
    public static int EssenceCost(int level) => 8 + 6 * (level - 1);
    public static int EssenceFor(SpiritTier tier) => tier switch
    {
        SpiritTier.Common => 2, SpiritTier.Spirit => 4,
        SpiritTier.Warden => 8, _ => 12,
    };
    public static float SoulChance(SpiritTier tier) => tier switch
    {
        SpiritTier.Spirit => 0.10f, SpiritTier.Warden => 0.25f,
        SpiritTier.Sacred => 1.0f, _ => 0.0f,
    };
    public const int BaseLegionCap = 8;
    public const float EssenceMagnetRange = 1.6f;
    public const float EssencePickRange = 0.55f;
    public const float EssenceGravity = 18.0f;

    // 等级带来的「质变」而非纯数值（对应 BM 的 Blst1 / Blst2）
    public static float LevelDamageMul(int level) => level switch { 1 => 1.0f, 2 => 1.6f, _ => 2.4f };
    public static float LevelIntervalMul(int level) => level switch { 1 => 1.0f, 2 => 0.85f, _ => 0.70f };
    public static int LevelChainJumps(int level) => level + 1;
    public static bool BoltPierces(int level) => level >= 2;
    public static bool BoltHomes(int level) => level >= 3;
    public static float AuraRadiusMul(int level) => level switch { 1 => 1.0f, 2 => 1.3f, _ => 1.6f };
    public const int MaxUnitLevel = 3;   // Lv3 不是终点，是「可献祭材料」

    // ---------------------------------------------------------------- 表 1：SpeciesDef（敌我同源）
    public static readonly SpeciesDef[] Species =
    {
        new() { Id="qingxiao", Name="青魈", Sprite="enemy_shambler", Tier=SpiritTier.Common,
            Hp=26, Damage=7, Speed=1.9f, Range=0.9f, Interval=0.90f, Attack=AttackKind.Melee,
            Cap=6, UnlockDay=1, Traits=new[]{"扑击单体","一次连击两下","击退并溅射"} },
        new() { Id="chiwu", Name="赤羽", Sprite="enemy_runner", Tier=SpiritTier.Common,
            Hp=18, Damage=6, Speed=2.8f, Range=6.0f, Interval=1.05f, Attack=AttackKind.Bolt,
            Cap=6, UnlockDay=1, Traits=new[]{"单枚灵羽","灵羽可穿透","灵羽自行追踪"} },
        new() { Id="xuanjia", Name="玄甲", Sprite="enemy_brute", Tier=SpiritTier.Common,
            Hp=64, Damage=11, Speed=1.3f, Range=1.0f, Interval=1.20f, Attack=AttackKind.Melee,
            Cap=5, UnlockDay=2, Traits=new[]{"厚甲抵近","震地波","反弹近身伤害"} },
        new() { Id="youming", Name="幽溟", Sprite="enemy_floater", Tier=SpiritTier.Common,
            Hp=22, Damage=5, Speed=2.2f, Range=2.2f, Interval=0.80f, Attack=AttackKind.Aura,
            Cap=5, UnlockDay=2, Traits=new[]{"小范围溟气","溟气扩域","溟气滞留减速"} },
        new() { Id="tuwu", Name="吐雾", Sprite="enemy_spitter", Tier=SpiritTier.Common,
            Hp=30, Damage=8, Speed=1.7f, Range=5.0f, Interval=1.10f, Attack=AttackKind.Chain,
            Cap=5, UnlockDay=3, Traits=new[]{"雾锁两体","雾锁三体","雾锁四体且递增"} },
        new() { Id="shanxiao", Name="山魈", Sprite="enemy_elite", Tier=SpiritTier.Spirit,
            Hp=90, Damage=16, Speed=2.0f, Range=1.2f, Interval=0.85f, Attack=AttackKind.Melee,
            Cap=4, UnlockDay=3, Traits=new[]{"裂岩重击","三连碎岩","崩山范围击"} },
        new() { Id="lingyuan", Name="灵猿", Sprite="pet", Tier=SpiritTier.Spirit,
            Hp=54, Damage=13, Speed=2.6f, Range=7.0f, Interval=0.70f, Attack=AttackKind.Bolt,
            Cap=3, UnlockDay=4, Traits=new[]{"连珠三矢","矢可穿透","矢自寻敌"} },
        new() { Id="bixi", Name="赑屃", Sprite="boss", Tier=SpiritTier.Warden,
            Hp=180, Damage=20, Speed=1.2f, Range=2.8f, Interval=0.90f, Attack=AttackKind.Aura,
            Cap=2, UnlockDay=5, Traits=new[]{"负岳气场","气场扩域","气场镇压"} },
        new() { Id="taowu", Name="梼杌", Sprite="boss", Tier=SpiritTier.Warden,
            Hp=150, Damage=24, Speed=1.9f, Range=6.0f, Interval=0.75f, Attack=AttackKind.Chain,
            Cap=2, UnlockDay=6, Traits=new[]{"凶纹三跳","凶纹四跳","凶纹五跳不衰减"} },
        new() { Id="baize", Name="白泽", Sprite="pet", Tier=SpiritTier.Sacred,
            Hp=240, Damage=32, Speed=2.4f, Range=8.0f, Interval=0.55f, Attack=AttackKind.Bolt,
            Cap=1, UnlockDay=6, Traits=new[]{"通灵一矢","一矢穿阵","万灵归矢"} },
    };

    public static SpeciesDef SpeciesById(string id) =>
        Species.FirstOrDefault(s => s.Id == id) ?? Species[0];

    public static IEnumerable<SpeciesDef> Unlocked(int day) => Species.Where(s => s.UnlockDay <= day);

    public static string TierName(SpiritTier tier) => tier switch
    {
        SpiritTier.Common => "凡阶", SpiritTier.Spirit => "灵阶",
        SpiritTier.Warden => "魁阶", _ => "圣阶",
    };

    /// <summary>阵营只改色调，不改数值 / 技能 / 贴图 —— SPEC-001 的可视证据。</summary>
    public static Color FactionTint(Faction side) => side == Faction.Wild
        ? new Color(1.00f, 0.68f, 0.62f, 1.0f)
        : new Color(0.62f, 1.00f, 0.86f, 1.0f);

    // ---------------------------------------------------------------- 表 2：FuseRecipe（OfferDef 融合分支）
    // ★助手提案：配方结构学习 BM（2/3/5 同类 → 高阶；Lv3 + 英魂 → 恶魔化），
    //   具体物种与数值为山海谷设定自定，不是原作数据。
    public static readonly FuseRecipe[] Fuses =
    {
        new() { Id="fuse_pair", Title="合灵 · 双凡", ResultId="shanxiao",
            CostTier=SpiritTier.Common, CostCount=2, CostMinLevel=1, ResultLevel=1,
            Weight=18, Desc="献祭 2 只凡阶幻灵，契为 1 只山魈（灵阶）" },
        new() { Id="fuse_trio", Title="合灵 · 三凡", ResultId="lingyuan",
            CostTier=SpiritTier.Common, CostCount=3, CostMinLevel=1, ResultLevel=1,
            Weight=14, Desc="献祭 3 只凡阶幻灵，契为 1 只灵猿（灵阶）" },
        new() { Id="fuse_five", Title="升魁 · 三灵", ResultId="bixi",
            CostTier=SpiritTier.Spirit, CostCount=3, CostMinLevel=1, ResultLevel=1,
            Weight=8, RequiredLore="lore_warden", Desc="献祭 3 只灵阶幻灵，契为 1 只赑屃（魁阶）" },
        new() { Id="fuse_lv3", Title="凶化 · 满阶", ResultId="taowu",
            CostTier=SpiritTier.Common, CostCount=1, CostMinLevel=3, ResultLevel=2,
            SoulCost=1, Weight=4, RequiredLore="lore_warden",
            Desc="献祭 1 只 Lv3 幻灵 + 1 枚灵魄，契为 1 只 Lv2 梼杌（魁阶）" },
        new() { Id="fuse_sacred", Title="通圣 · 双魁", ResultId="baize",
            CostTier=SpiritTier.Warden, CostCount=2, CostMinLevel=1, ResultLevel=1,
            SoulCost=1, Weight=2, RequiredLore="lore_sacred",
            Desc="献祭 2 只魁阶幻灵 + 1 枚灵魄，契为 1 只白泽（圣阶）" },
    };

    // ---------------------------------------------------------------- 表 3：LoreDef（MetaDef）
    public static readonly LoreDef[] Lores =
    {
        new() { Id="lore_warden", Name="灵契广闻", Desc="解锁魁阶唤灵与融合候选", MaxLevel=1, BasePrice=40 },
        new() { Id="lore_sacred", Name="圣兽录",   Desc="解锁圣阶融合候选",       MaxLevel=1, BasePrice=80 },
        new() { Id="lore_options",Name="多闻",     Desc="唤灵候选由 3 增至 4",    MaxLevel=1, BasePrice=60 },
        new() { Id="lore_cap",    Name="广纳",     Desc="军团上限 +2 / 级",       MaxLevel=2, BasePrice=35 },
        new() { Id="lore_essence",Name="聚灵",     Desc="开局灵韵 +10 / 级",      MaxLevel=3, BasePrice=20 },
        new() { Id="lore_reroll", Name="回溯符",   Desc="每局唤灵重掷 +2 / 级",   MaxLevel=2, BasePrice=30 },
    };

    public static LoreDef LoreById(string id) => Lores.FirstOrDefault(l => l.Id == id) ?? Lores[0];

    // ---------------------------------------------------------------- 白天营地
    public static readonly CampItem[] CampItems =
    {
        new() { Id="altar",  Name="灵契坛", Desc="军团上限 +1",       Price=12, Kind="altar"  },
        new() { Id="well",   Name="聚灵井", Desc="每夜 +6 灵韵",      Price=18, Kind="well"   },
        new() { Id="lamp",   Name="引灵灯", Desc="幻灵攻速 +8%",      Price=16, Kind="lamp"   },
        new() { Id="stele",  Name="镇兽碑", Desc="野生幻兽减速 15%",  Price=14, Kind="stele"  },
        new() { Id="hut",    Name="疗愈庐", Desc="每夜回 12 HP",      Price=14, Kind="hut"    },
        new() { Id="herbs",  Name="草木膏", Desc="立即回 30 HP",      Price=8,  Kind="instant"},
    };

    // ---------------------------------------------------------------- 区域主题
    public const int ThemePeriod = 3;
    public static readonly (string Name, string Bg, Color Day, Color Night)[] Themes =
    {
        ("暮色山海", "bg_dusk",    new Color(1.00f,0.95f,0.84f), new Color(0.52f,0.62f,0.92f)),
        ("霜寒雪谷", "bg_frost",   new Color(0.93f,0.97f,1.00f), new Color(0.55f,0.70f,0.98f)),
        ("灰烬荒墟", "bg_ash",     new Color(1.00f,0.90f,0.78f), new Color(0.62f,0.52f,0.60f)),
        ("星蚀深渊", "bg_eclipse", new Color(0.95f,0.90f,1.00f), new Color(0.48f,0.44f,0.72f)),
    };

    public static int ThemeIndex(int day) =>
        Mathf.Max(0, day - 1) / ThemePeriod % Themes.Length;

    public const float CameraViewHalfHeight = 6.5f;
    public const float CameraSmooth = 6.0f;
}
