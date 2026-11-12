extends Node
## @trace ROGUE-002 本局构筑单点（autoload）
## 迁移自 Unity RunState.cs：全部构筑倍率集中在此，玩法层只读不改。

var seed_value: int = 0
var day: int = 1
var coins: int = 0
var kills: int = 0
var boss_kills: int = 0
var hp: int = 100
var max_hp: int = 100

var damage_bonus: int = 0
var interval_mul: float = 1.0
var move_bonus: float = 0.0
var jump_bonus: float = 0.0
var range_bonus: float = 0.0
var coin_value: int = 1
var magnet_bonus: float = 0.0
var armor: int = 0
var regen: int = 0
var dawn_coin: int = 0

var weapon_id: String = "revolver"
var owned_weapons: Array = ["revolver"]
var cards: Array = []
var structures: Dictionary = {}

func reset(new_seed: int, start_weapon: String, meta: Dictionary) -> void:
	seed_value = new_seed
	day = 1
	coins = int(meta.get("coins", 0))
	kills = 0
	boss_kills = 0
	max_hp = GameConfig.PLAYER_MAX_HP + int(meta.get("max_hp", 0))
	hp = max_hp

	damage_bonus = int(meta.get("damage", 0))
	interval_mul = float(meta.get("interval", 1.0))
	move_bonus = float(meta.get("movespeed", 0.0))
	jump_bonus = 0.0
	range_bonus = 0.0
	coin_value = 1
	magnet_bonus = 0.0
	armor = 0
	regen = 0
	dawn_coin = 0

	weapon_id = start_weapon if start_weapon != "" else "revolver"
	owned_weapons = [weapon_id]
	cards = []
	structures = {}
	GameEvents.coins_changed.emit(coins)
	GameEvents.hp_changed.emit(hp, max_hp)
	GameEvents.weapon_changed.emit(weapon_id)
	GameEvents.structures_changed.emit(structures)

func add_coins(n: int) -> void:
	coins = maxi(0, coins + n)
	GameEvents.coins_changed.emit(coins)

## @trace ROGUE-003 卡牌统一在此结算，避免玩法层散落 if
func apply_upgrade(card: Dictionary) -> void:
	var v: float = float(card.get("value", 0))
	match str(card.get("effect", "")):
		"damage":    damage_bonus += int(v)
		"interval":  interval_mul *= v
		"maxhp":     max_hp += int(v); hp = max_hp
		"movespeed": move_bonus += v
		"jump":      jump_bonus += v
		"range":     range_bonus += v
		"coinvalue": coin_value += int(v)
		"magnet":    magnet_bonus += v
		"armor":     armor += int(v)
		"regen":     regen += int(v)
		"dawncoin":  dawn_coin += int(v)
		_:           push_warning("未知卡牌 effect: " + str(card.get("effect")))
	cards.append(str(card.get("id", "?")))
	if str(card.get("effect", "")) == "maxhp":
		GameEvents.hp_changed.emit(hp, max_hp)

func damage(amount: int) -> void:
	hp = maxi(0, hp - amount)
	GameEvents.hp_changed.emit(hp, max_hp)

func heal(amount: int) -> void:
	hp = mini(max_hp, hp + amount)
	GameEvents.hp_changed.emit(hp, max_hp)

func set_weapon(id: String) -> void:
	weapon_id = id
	if not owned_weapons.has(id):
		owned_weapons.append(id)
	GameEvents.weapon_changed.emit(weapon_id)
