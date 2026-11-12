class_name GameConfig
extends RefCounted
## @trace CFG-001 集中数值表：所有可调数值单点定义
## 迁移自 Unity GameConfig.cs（源工程 v0.0003），数值保持一致，未擅自调整平衡。
## 公式与极值验算见 docs/migration-map.md 第 3 节。

# ---------------------------------------------------------------- 阶段时长
const DAY_DURATION := 20.0
const NIGHT_DURATION := 30.0
const PICK_COUNT := 3

# ---------------------------------------------------------------- 世界与平台
const WORLD_HALF_WIDTH := 24.0
const GROUND_Y := -2.2
const LAYER_COUNT := 10
const LAYER_SPACING := 3.2
const PLATFORM_SEGMENTS_MIN := 5
const PLATFORM_SEGMENTS_MAX := 8
const PLATFORM_MIN_WIDTH := 3.4
const PLATFORM_MAX_WIDTH := 7.0
const PLATFORM_MIN_GAP := 1.8
const PLATFORM_THICKNESS := 0.24
# @trace GND-001 第一层恒为「唯一一段 / 覆盖 [-W,+W] / 非单向」的实体地板。
# 实现在 scripts/csharp/PlatformField.cs（C# 迁移 Phase 1），此处只留口径常量与自检修缮。
const GROUND_CONTINUOUS := true
const SPAWN_ABOVE_GROUND := 1.0
# Godot 2D 为 y-down：向下坠落是 y 增大，越过 GROUND_Y + 10 单位即判定出界。
const FALL_KILL_Y := 8.0

static func layer_y(i: int) -> float:
	# y-down：层号越大 y 越小（屏幕越高），跳跃 -y 才能逐层向上。
	return GROUND_Y - float(clampi(i, 0, LAYER_COUNT - 1)) * LAYER_SPACING

static func clamp_layer(i: int) -> int:
	return clampi(i, 0, LAYER_COUNT - 1)

static func segments_for(world_width: float) -> int:
	# 源工程 v6：平台段数随世界宽度自适应，避免放大地图后平台稀疏
	return clampi(roundi(world_width / 8.0), PLATFORM_SEGMENTS_MIN, PLATFORM_SEGMENTS_MAX)

# ---------------------------------------------------------------- 玩家
const PLAYER_MOVE_SPEED := 6.0
const GRAVITY := 26.0
const PLAYER_JUMP_SPEED := 14.2
const PLAYER_MAX_HP := 100
const PLAYER_FIRE_INTERVAL := 0.36
const PLAYER_FIRE_RANGE := 7.0
const PLAYER_DAMAGE := 10
const PLAYER_INVULN := 0.55
const PLAYER_SIZE := Vector2(0.8, 1.2)
const BULLET_SPEED := 13.0
# 验算：跳跃高度 v²/(2g) = 14.2²/(2×26) ≈ 3.88 > 层高 3.2 → 单跳可跨一层

# ---------------------------------------------------------------- 敌人波次
const SPAWN_INTERVAL_MIN := 0.22
const SPAWN_INTERVAL_MAX := 0.85
const SPAWN_WINDOW_RATIO := 0.86
const BUDGET_BASE := 14
const BUDGET_PER_DAY := 7
const BUDGET_CAP := 130
const HP_SCALE_PER_DAY := 0.16
const DAMAGE_SCALE_PER_DAY := 0.08
const SPEED_SCALE_PER_DAY := 0.02
const ENEMY_CONTACT_DAMAGE := 8
const ENEMY_ATTACK_INTERVAL := 0.9
const SPAWN_NEAR_MIN := 3.2
const SPAWN_NEAR_MAX := 10.0
const SPAWN_SAME_LAYER_RATIO := 0.55

static func budget(day: int) -> int:
	# @trace ECO-002 预算制波次 B(d)=min(130, 14+7(d-1))
	return mini(BUDGET_CAP, BUDGET_BASE + BUDGET_PER_DAY * maxi(0, day - 1))

static func spawn_interval(day: int) -> float:
	# @trace WAVE-001 自适应间隔 clamp(30×0.86/B(d), 0.22, 0.85)
	var raw := NIGHT_DURATION * SPAWN_WINDOW_RATIO / float(maxi(1, budget(day)))
	return clampf(raw, SPAWN_INTERVAL_MIN, SPAWN_INTERVAL_MAX)

# ---------------------------------------------------------------- 金币 / 拾取
const COIN_MAGNET_RANGE := 1.6
const COIN_PICK_RANGE := 0.55
const COIN_GRAVITY := 18.0

# ---------------------------------------------------------------- 六把武器
# @trace WPN-001 kind: bullet / fan / chain / boomerang / aura
const WEAPONS := [
	{"id":"revolver", "name":"左轮",     "kind":"bullet",    "damage":10, "interval":0.36, "range":7.0, "count":1, "spread":0.0,  "speed":13.0, "price":0},
	{"id":"scatter",  "name":"散铳",     "kind":"bullet",    "damage":6,  "interval":0.62, "range":5.5, "count":5, "spread":0.55, "speed":11.0, "price":30},
	{"id":"dagger",   "name":"仪式短刃", "kind":"fan",       "damage":14, "interval":0.48, "range":2.2, "count":1, "spread":2.62, "speed":0.0,  "price":45},
	{"id":"chain",    "name":"引雷法杖", "kind":"chain",     "damage":12, "interval":0.75, "range":6.5, "count":3, "spread":0.0,  "speed":0.0,  "price":60},
	{"id":"starwheel","name":"逐星轮",   "kind":"boomerang", "damage":9,  "interval":0.70, "range":6.0, "count":1, "spread":0.0,  "speed":9.0,  "price":75},
	{"id":"stasis",   "name":"静滞力场", "kind":"aura",      "damage":5,  "interval":0.50, "range":2.6, "count":1, "spread":0.0,  "speed":0.0,  "price":100},
]

static func weapon(id: String) -> Dictionary:
	for w in WEAPONS:
		if w["id"] == id:
			return w
	return WEAPONS[0]

# ---------------------------------------------------------------- 敌人原型
# @trace ECO-001 六种原型按夜解锁；cost 为波次预算消耗
const ENEMIES := [
	{"id":"shambler","name":"游尸",  "hp":24, "damage":8,  "speed":1.8, "cost":1, "sprite":"enemy_shambler","unlock":1},
	{"id":"runner",  "name":"疾影",  "hp":16, "damage":6,  "speed":3.4, "cost":1, "sprite":"enemy_runner",  "unlock":2},
	{"id":"brute",   "name":"山魈",  "hp":62, "damage":14, "speed":1.3, "cost":3, "sprite":"enemy_brute",   "unlock":3},
	{"id":"floater", "name":"游魂",  "hp":20, "damage":7,  "speed":2.2, "cost":2, "sprite":"enemy_floater", "unlock":4},
	{"id":"spitter", "name":"吐雾",  "hp":30, "damage":9,  "speed":1.6, "cost":2, "sprite":"enemy_spitter", "unlock":5},
	{"id":"elite",   "name":"精英",  "hp":95, "damage":16, "speed":1.9, "cost":5, "sprite":"enemy_elite",   "unlock":6},
]

# ---------------------------------------------------------------- 肉鸽升级卡
# @trace ROGUE-001 源工程 41 张，本垂直切片精简迁移 14 张（见 docs/migration-map.md）
const UPGRADES := [
	{"id":"power",   "name":"锋锐",   "desc":"子弹伤害 +3",        "effect":"damage",      "value":3},
	{"id":"haste",   "name":"疾发",   "desc":"开火间隔 ×0.92",     "effect":"interval",    "value":0.92},
	{"id":"vigor",   "name":"体魄",   "desc":"最大 HP +20 并回满", "effect":"maxhp",       "value":20},
	{"id":"swift",   "name":"轻身",   "desc":"移动速度 +0.8",      "effect":"movespeed",   "value":0.8},
	{"id":"leap",    "name":"踏空",   "desc":"跳跃初速 +1.2",      "effect":"jump",        "value":1.2},
	{"id":"range",   "name":"远目",   "desc":"射程 +1.5",          "effect":"range",       "value":1.5},
	{"id":"greed",   "name":"拾金",   "desc":"金币价值 +2",        "effect":"coinvalue",   "value":2},
	{"id":"magnet",  "name":"磁引",   "desc":"拾取范围 +1.0",      "effect":"magnet",      "value":1.0},
	{"id":"pierce",  "name":"破甲",   "desc":"伤害 +5",            "effect":"damage",      "value":5},
	{"id":"bulwark", "name":"荆棘甲", "desc":"受伤 -2",            "effect":"armor",       "value":2},
	{"id":"regen",   "name":"回春",   "desc":"每夜开始回 15 HP",   "effect":"regen",       "value":15},
	{"id":"fortune", "name":"富饶",   "desc":"天亮额外 +8 金币",   "effect":"dawncoin",    "value":8},
	{"id":"fury",    "name":"狂猎",   "desc":"开火间隔 ×0.85",     "effect":"interval",    "value":0.85},
	{"id":"echo",    "name":"回响",   "desc":"子弹伤害 +6",        "effect":"damage",      "value":6},
]

# ---------------------------------------------------------------- 白天商品 / 建造
# @trace BLD-001 建造物差异化：金库产金 / 灯塔减速 / 医疗帐回血
const SHOP_ITEMS := [
	{"id":"turret", "name":"木炮台", "desc":"夜里自动开火",       "price":12, "kind":"turret"},
	{"id":"vault",  "name":"金库",   "desc":"每夜 +5 金币",       "price":18, "kind":"vault"},
	{"id":"beacon", "name":"灯塔",   "desc":"近身敌人减速 15%",   "price":16, "kind":"beacon"},
	{"id":"tent",   "name":"医疗帐", "desc":"每夜回 12 HP",       "price":14, "kind":"tent"},
	{"id":"wall",   "name":"拒马",   "desc":"近身敌人受伤 +2",    "price":10, "kind":"wall"},
	{"id":"heal",   "name":"包扎",   "desc":"立即回 30 HP",       "price":8,  "kind":"instant"},
]

# ---------------------------------------------------------------- 局外成长
# @trace META-001 余烬 + 5 项永久升级；价格 = 20×(lv+1)
const META_UPGRADES := [
	{"id":"body",  "name":"体魄", "desc":"初始最大 HP +10 / 级",   "max":5},
	{"id":"edge",  "name":"锋锐", "desc":"初始伤害 +2 / 级",       "max":5},
	{"id":"wealth","name":"富饶", "desc":"开局金币 +5 / 级",       "max":5},
	{"id":"light", "name":"轻捷", "desc":"移动速度 +0.2 / 级",     "max":5},
	{"id":"arms",  "name":"兵械", "desc":"开火间隔 ×0.97 / 级",    "max":5},
]

static func meta_price(level: int) -> int:
	return 20 * (level + 1)

# ---------------------------------------------------------------- 区域主题
# @trace AREA-001 四套区域每 3 夜轮换；RGB 为白天/夜晚 tint 倍率
const THEME_PERIOD := 3
const THEMES := [
	{"name":"暮色荒原", "bg":"bg_dusk",    "day":[1.00,0.95,0.84], "night":[0.52,0.62,0.92]},
	{"name":"霜寒要塞", "bg":"bg_frost",   "day":[0.93,0.97,1.00], "night":[0.55,0.70,0.98]},
	{"name":"灰烬地窟", "bg":"bg_ash",     "day":[1.00,0.90,0.78], "night":[0.62,0.52,0.60]},
	{"name":"星蚀深渊", "bg":"bg_eclipse", "day":[0.95,0.90,1.00], "night":[0.48,0.44,0.72]},
]

static func theme_index(day: int) -> int:
	return floori(float(maxi(1, day) - 1) / float(THEME_PERIOD)) % THEMES.size()

# ---------------------------------------------------------------- 相机
const CAMERA_VIEW_HALF_HEIGHT := 6.5
const CAMERA_SMOOTH := 6.0
