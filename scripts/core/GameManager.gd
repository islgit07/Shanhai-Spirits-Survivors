extends Node
## @trace GAME-001 昼夜状态机（autoload）
## 迁移自 Unity GameManager.cs 三态状态机；扩展为 MENU / DAY / NIGHT / UPGRADE / DEAD 五态。
## 世界与 UI 由 Bootstrap 构建后通过 bind() 注入，避免状态机直接依赖具体场景结构。

enum Phase { MENU, DAY, NIGHT, UPGRADE, DEAD }

const SMOKE_STEP := 0.80
const SMOKE_TARGET_DAY := 3

var phase: int = Phase.MENU
var day: int = 1
var timer: float = 0.0
var smoke_mode: bool = false
var offered: Array = []

var _menu: CanvasLayer = null
var _world: Node2D = null
var _hud: CanvasLayer = null
var _smoke_events: Array = []
var _run_end_emitted: bool = false

func bind(menu: CanvasLayer, world: Node2D, hud: CanvasLayer) -> void:
	_menu = menu
	_world = world
	_hud = hud
	world.visible = false
	hud.visible = false
	menu.visible = true
	if menu.has_method("refresh"):
		menu.call("refresh")

func get_world() -> Node2D:
	return _world

# 通过 autoload 实例访问 enum 在 GDScript 里不可靠，统一用方法暴露阶段判断。
func is_night() -> bool:
	return phase == Phase.NIGHT

func is_day() -> bool:
	return phase == Phase.DAY

func is_upgrade() -> bool:
	return phase == Phase.UPGRADE

func is_dead() -> bool:
	return phase == Phase.DEAD

func is_menu() -> bool:
	return phase == Phase.MENU

func phase_label() -> String:
	match phase:
		Phase.MENU: return "主页面"
		Phase.DAY: return "白天 · 经营"
		Phase.NIGHT: return "夜晚 · 幸存"
		Phase.UPGRADE: return "天明 · 三选一"
		Phase.DEAD: return "本局结束"
	return "未知"

func _process(delta: float) -> void:
	if phase == Phase.DAY or phase == Phase.NIGHT:
		timer -= delta
		if timer <= 0.0:
			_advance()

func _advance() -> void:
	if phase == Phase.DAY:
		enter_night()
	elif phase == Phase.NIGHT:
		enter_upgrade()

func phase_duration(p: int) -> float:
	if smoke_mode:
		return SMOKE_STEP
	if p == Phase.DAY:
		return GameConfig.DAY_DURATION
	if p == Phase.NIGHT:
		return GameConfig.NIGHT_DURATION
	return 0.0

func progress_ratio() -> float:
	var d := phase_duration(phase)
	if d <= 0.0:
		return 0.0
	return clampf(1.0 - timer / d, 0.0, 1.0)

func start_run(start_weapon: String) -> void:
	var s := int(Time.get_unix_time_from_system()) & 0x7FFFFFFF
	RunState.reset(s, start_weapon, MetaProgress.apply_to_run())
	day = 1
	offered = []
	_run_end_emitted = false
	if _world != null and _world.has_method("begin_run"):
		_world.call("begin_run")
	if _world != null:
		_world.visible = true
	if _hud != null:
		_hud.visible = true
	if _menu != null:
		_menu.visible = false
	GameEvents.hint.emit("第 1 天 · 白天经营")
	_smoke_log("start_run")
	enter_day()

func enter_day() -> void:
	phase = Phase.DAY
	timer = phase_duration(phase)
	RunState.day = day
	var regen := RunState.regen + int(RunState.structures.get("tent", 0)) * 12
	if regen > 0:
		RunState.heal(regen)
	if _world != null and _world.has_method("set_phase"):
		_world.call("set_phase", phase, day)
	GameEvents.day_started.emit(day)
	GameEvents.phase_changed.emit(phase, day)
	_smoke_log("day_started")

func enter_night() -> void:
	phase = Phase.NIGHT
	timer = phase_duration(phase)
	if _world != null and _world.has_method("set_phase"):
		_world.call("set_phase", phase, day)
	GameEvents.night_started.emit(day)
	GameEvents.phase_changed.emit(phase, day)
	_smoke_log("night_started")

func enter_upgrade() -> void:
	phase = Phase.UPGRADE
	var vault_bonus := int(RunState.structures.get("vault", 0)) * 5
	var bonus := vault_bonus + RunState.dawn_coin
	if bonus > 0:
		RunState.add_coins(bonus)
	if _world != null and _world.has_method("on_dawn"):
		_world.call("on_dawn")
	if _world != null and _world.has_method("set_phase"):
		_world.call("set_phase", phase, day)
	GameEvents.dawn_started.emit(day)
	GameEvents.phase_changed.emit(phase, day)
	offered = UpgradeSystem.pick(RunState.seed_value, day, GameConfig.PICK_COUNT)
	GameEvents.upgrade_offered.emit(offered)
	_smoke_log("upgrade_offered")
	if smoke_mode:
		call_deferred("_smoke_resolve")

func choose_card(idx: int) -> void:
	if phase != Phase.UPGRADE or idx < 0 or idx >= offered.size():
		return
	RunState.apply_upgrade(offered[idx])
	_smoke_log("card_chosen")
	day += 1
	enter_day()

func reroll() -> void:
	if phase != Phase.UPGRADE or RunState.coins < 3:
		return
	RunState.add_coins(-3)
	offered = UpgradeSystem.pick(RunState.seed_value, day + 977, GameConfig.PICK_COUNT)
	GameEvents.upgrade_offered.emit(offered)

func skip() -> void:
	if phase != Phase.UPGRADE:
		return
	RunState.add_coins(5)
	day += 1
	enter_day()

func try_buy(index: int) -> void:
	if phase != Phase.DAY or index < 0 or index >= GameConfig.SHOP_ITEMS.size():
		return
	var item: Dictionary = GameConfig.SHOP_ITEMS[index]
	var price := int(item["price"])
	if RunState.coins < price:
		GameEvents.hint.emit("金币不足")
		return
	RunState.add_coins(-price)
	var kind := str(item["kind"])
	if kind == "instant":
		RunState.heal(30)
	else:
		RunState.structures[kind] = int(RunState.structures.get(kind, 0)) + 1
		GameEvents.structures_changed.emit(RunState.structures)
		if _world != null and _world.has_method("add_structure"):
			_world.call("add_structure", kind)
	GameEvents.hint.emit("已购入 " + str(item["name"]))
	_smoke_log("buy")

func cycle_weapon() -> void:
	var idx := RunState.owned_weapons.find(RunState.weapon_id)
	if idx < 0:
		idx = 0
	RunState.set_weapon(str(RunState.owned_weapons[(idx + 1) % RunState.owned_weapons.size()]))

func start_night_early() -> void:
	if phase == Phase.DAY:
		enter_night()

func toggle_pause() -> void:
	if phase == Phase.DAY or phase == Phase.NIGHT:
		get_tree().paused = not get_tree().paused
		GameEvents.hint.emit("已暂停" if get_tree().paused else "继续")

func player_died() -> void:
	if phase == Phase.DEAD or phase == Phase.MENU:
		return
	phase = Phase.DEAD
	_run_end_emitted = true
	var reward := MetaProgress.record_run(day, RunState.kills, RunState.boss_kills)
	GameEvents.run_ended.emit({
		"day": day, "kills": RunState.kills,
		"embers": int(reward["embers"]), "cards": RunState.cards.duplicate(),
		"weapon": RunState.weapon_id,
	})
	GameEvents.phase_changed.emit(phase, day)
	if _world != null and _world.has_method("set_phase"):
		_world.call("set_phase", phase, day)
	_smoke_log("run_ended")
	if smoke_mode:
		call_deferred("_finish_smoke")

func return_to_menu() -> void:
	get_tree().paused = false
	phase = Phase.MENU
	if _world != null and _world.has_method("end_run"):
		_world.call("end_run")
	if _world != null:
		_world.visible = false
	if _hud != null:
		_hud.visible = false
	if _menu != null:
		_menu.visible = true
		if _menu.has_method("refresh"):
			_menu.call("refresh")
	GameEvents.phase_changed.emit(phase, day)
	_smoke_log("return_to_menu")

func restart_run() -> void:
	get_tree().paused = false
	start_run(RunState.weapon_id)

# ---------------------------------------------------------------- 无头冒烟
## @trace TEST-001 无头冒烟测试：自动跑完 3 昼夜并校验 10 项断言
func begin_smoke() -> void:
	smoke_mode = true
	_smoke_events = []
	start_run("revolver")
	# 冒烟专用注入：一击必杀，用于在极短的夜晚里跑通「开火→击杀→掉金币」链路。
	# 只在 --smoke 模式生效，不触碰正常游戏数值。
	RunState.damage_bonus += 200

func _smoke_resolve() -> void:
	if not smoke_mode or phase != Phase.UPGRADE:
		return
	if day >= SMOKE_TARGET_DAY:
		player_died()
	else:
		choose_card(0)

func _smoke_log(event: String) -> void:
	if not smoke_mode:
		return
	_smoke_events.append(event)
	print("[SMOKE] " + event + " day=" + str(day))

func _finish_smoke() -> void:
	var platforms := -1
	var spawned := -1
	var fired := -1
	var coins := -1
	if _world != null:
		var v1: Variant = _world.get("platform_count")
		var v2: Variant = _world.get("enemies_spawned")
		var v3: Variant = _world.get("projectiles_fired")
		var v4: Variant = _world.get("coins_spawned")
		if v1 != null: platforms = int(v1)
		if v2 != null: spawned = int(v2)
		if v3 != null: fired = int(v3)
		if v4 != null: coins = int(v4)
	var kills_recorded := RunState.kills > 0
	var coins_dropped := coins > 0
	return_to_menu()
	var assertions := {
		"reached_target_day": day >= SMOKE_TARGET_DAY,
		"run_ended": _run_end_emitted,
		"platforms_built": platforms > 0,
		"enemies_spawned": spawned > 0,
		"projectiles_fired": fired > 0,
		"kills_recorded": kills_recorded,
		"coins_dropped": coins_dropped,
		"save_file_written": FileAccess.file_exists("user://shanhai_meta.json"),
		"returned_to_menu": is_menu(),
		"events_ordered": _smoke_events.has("day_started") and _smoke_events.has("night_started") and _smoke_events.has("run_ended"),
	}
	var ok := true
	for k in assertions.keys():
		if not bool(assertions[k]):
			ok = false
	var report := {
		"project": "山海幻灵", "version": "0.001",
		"engine": str(Engine.get_version_info().get("string", "")),
		"mode": "headless-smoke", "result": "PASS" if ok else "FAIL",
		"days_reached": day, "kills": RunState.kills,
		"cards_taken": RunState.cards.size(),
		"platforms": platforms, "enemies_spawned": spawned,
		"projectiles_fired": fired, "coins_spawned": coins,
		"smoke_injection": "damage_bonus +200；敌人近场 0.9~1.6 单位；阶段 0.8 秒（三项均仅 --smoke 模式）",
		"events": _smoke_events.duplicate(), "assertions": assertions,
	}
	print("SMOKE_JSON_BEGIN")
	print(JSON.stringify(report, "\t"))
	print("SMOKE_JSON_END")
	get_tree().quit(0 if ok else 1)
