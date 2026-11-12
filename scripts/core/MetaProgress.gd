extends Node
## @trace META-001 局外成长（autoload，user:// JSON 明文存档）
## 迁移自 Unity MetaProgress.cs（PlayerPrefs）；Godot 改用 FileAccess + JSON。
## 诚实边界：JSON 明文与源工程 PlayerPrefs 同为 demo 级取舍，正式项目需加密与账号体系。

const SAVE_PATH := "user://shanhai_meta.json"

var embers: int = 0
var levels: Dictionary = {}
var unlocked: Array = ["revolver"]
var best_day: int = 0
var total_runs: int = 0
var total_kills: int = 0

func _ready() -> void:
	load_game()

func default_levels() -> Dictionary:
	var d := {}
	for u in GameConfig.META_UPGRADES:
		d[u["id"]] = 0
	return d

func load_game() -> void:
	levels = default_levels()
	var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
	if f == null:
		return
	var text := f.get_as_text()
	f.close()
	var parsed: Variant = JSON.parse_string(text)
	if typeof(parsed) != TYPE_DICTIONARY:
		push_warning("存档解析失败，使用默认值")
		return
	var d: Dictionary = parsed
	embers = int(d.get("embers", 0))
	best_day = int(d.get("best_day", 0))
	total_runs = int(d.get("total_runs", 0))
	total_kills = int(d.get("total_kills", 0))
	var u: Variant = d.get("unlocked", ["revolver"])
	if typeof(u) == TYPE_ARRAY:
		unlocked = u
	var lv: Variant = d.get("levels", {})
	if typeof(lv) == TYPE_DICTIONARY:
		for k in levels.keys():
			levels[k] = int((lv as Dictionary).get(k, 0))

func save_game() -> void:
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f == null:
		push_warning("存档写入失败: " + SAVE_PATH)
		return
	f.store_string(JSON.stringify({
		"embers": embers, "levels": levels, "unlocked": unlocked,
		"best_day": best_day, "total_runs": total_runs, "total_kills": total_kills,
	}, "\t"))
	f.close()
	GameEvents.meta_changed.emit()

## 开局把永久升级注入本局构筑；字段名与 RunState.reset 对齐。
func apply_to_run() -> Dictionary:
	return {
		"max_hp":   int(levels.get("body", 0)) * 10,
		"damage":   int(levels.get("edge", 0)) * 2,
		"coins":    int(levels.get("wealth", 0)) * 5,
		"movespeed": float(int(levels.get("light", 0))) * 0.2,
		"interval": pow(0.97, float(int(levels.get("arms", 0)))),
	}

func upgrade_level(id: String) -> int:
	return int(levels.get(id, 0))

func buy_upgrade(id: String) -> bool:
	var lv := upgrade_level(id)
	if lv >= 5 or embers < GameConfig.meta_price(lv):
		return false
	embers -= GameConfig.meta_price(lv)
	levels[id] = lv + 1
	save_game()
	return true

func unlock_weapon(id: String) -> bool:
	if unlocked.has(id):
		return false
	var w := GameConfig.weapon(id)
	var price := int(w["price"])
	if price <= 0 or embers < price:
		return false
	embers -= price
	unlocked.append(id)
	save_game()
	return true

## 死亡结算：余烬 = 每夜 3 + 每 10 杀 1 + 每个 Boss 8（与源工程公式一致）
func record_run(day: int, kills: int, boss_kills: int) -> Dictionary:
	var gain := day * 3 + floori(float(kills) / 10.0) + boss_kills * 8
	embers += gain
	total_runs += 1
	total_kills += kills
	best_day = maxi(best_day, day)
	save_game()
	return {"embers": gain, "day": day, "kills": kills}

func clear_all() -> void:
	embers = 0
	levels = default_levels()
	unlocked = ["revolver"]
	best_day = 0
	total_runs = 0
	total_kills = 0
	save_game()
