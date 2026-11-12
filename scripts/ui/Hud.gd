class_name Hud
extends CanvasLayer
## @trace HUD-001 局内 HUD：昼夜进度 / HP / 金币 / 建造 / 三选一 / 结算
## 迁移自 Unity HudController.cs（IMGUI）；本版改用 Godot CanvasLayer + Control。
## 源工程 UIS-002/UIS-003 的圆角皮肤与渐变进度条未迁移，见 docs/migration-map.md。

var _root: Control
var _top: Label
var _hp: ProgressBar
var _progress: ProgressBar
var _coins: Label
var _weapon: Label
var _kills: Label
var _hint: Label
var _day_panel: Control
var _night_panel: Control
var _upgrade_panel: Control
var _dead_panel: Control
var _shop_buttons: Array = []
var _card_buttons: Array = []
var _dead_text: Label

func _ready() -> void:
	_build()
	GameEvents.upgrade_offered.connect(_on_offered)
	GameEvents.run_ended.connect(_on_run_ended)
	GameEvents.hint.connect(_on_hint)
	GameEvents.structures_changed.connect(_on_structures)

func _build() -> void:
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_root)

	var bar := ColorRect.new()
	bar.color = Color(0.06, 0.07, 0.12, 0.86)
	bar.position = Vector2(0, 0)
	bar.size = Vector2(1280, 96)
	bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(bar)

	_top = UiSkin.label("", 24, UiSkin.GOLD)
	_top.position = Vector2(24, 8)
	_top.size = Vector2(700, 38)
	_root.add_child(_top)

	_hp = _make_bar(Vector2(24, 54), Vector2(260, 18), UiSkin.CRIMSON)
	_progress = _make_bar(Vector2(300, 54), Vector2(260, 18), UiSkin.BLUE)
	_coins = UiSkin.label("", 18, UiSkin.GOLD)
	_coins.position = Vector2(580, 46)
	_root.add_child(_coins)
	_weapon = UiSkin.label("", 18, UiSkin.JADE)
	_weapon.position = Vector2(760, 46)
	_root.add_child(_weapon)
	_kills = UiSkin.label("", 18, UiSkin.TEXT)
	_kills.position = Vector2(1000, 46)
	_root.add_child(_kills)

	_hint = UiSkin.label("", 16, UiSkin.JADE)
	_hint.position = Vector2(24, 100)
	_hint.size = Vector2(1230, 28)
	_root.add_child(_hint)

	_build_day()
	_build_night()
	_build_upgrade()
	_build_dead()

func _make_bar(pos: Vector2, size: Vector2, tint: Color) -> ProgressBar:
	var b := ProgressBar.new()
	b.position = pos
	b.size = size
	b.min_value = 0.0
	b.max_value = 100.0
	b.show_percentage = false
	b.modulate = tint
	_root.add_child(b)
	return b

func _make_panel() -> Control:
	var c := Control.new()
	c.position = Vector2(0, 0)
	c.size = Vector2(1280, 720)
	_root.add_child(c)
	return c

func _build_day() -> void:
	_day_panel = _make_panel()
	_day_panel.add_child(UiSkin.panel(Rect2(120, 170, 1040, 400), Color(0.10, 0.12, 0.18, 0.90)))
	var t := UiSkin.label("白天 · 经营（20 秒）  按 1~6 或点击购买，Enter 提前入夜", 22, UiSkin.GOLD)
	t.position = Vector2(150, 186)
	_day_panel.add_child(t)
	for i in range(GameConfig.SHOP_ITEMS.size()):
		var it: Dictionary = GameConfig.SHOP_ITEMS[i]
		var b := UiSkin.button("%d  %s   %d金   %s" % [i + 1, str(it["name"]), int(it["price"]), str(it["desc"])], 17)
		b.position = Vector2(150 + (i % 2) * 500, 236 + (i / 2) * 62)
		b.size = Vector2(470, 48)
		b.pressed.connect(_on_buy.bind(i))
		_day_panel.add_child(b)
		_shop_buttons.append(b)
	var s := UiSkin.label("建造物效果：金库每夜 +5 金 / 灯塔减速 / 医疗帐回血 / 拒马增伤 / 炮台（视觉效果已接入，自动开火未迁移）", 15, UiSkin.TEXT)
	s.position = Vector2(150, 500)
	s.size = Vector2(1000, 40)
	_day_panel.add_child(s)

func _build_night() -> void:
	_night_panel = _make_panel()
	var t := UiSkin.label("夜晚 · 幸存（30 秒）    A/D 移动   Space 跳跃   F 换武器   Esc 暂停", 22, UiSkin.BLUE)
	t.position = Vector2(0, 640)
	t.size = Vector2(1280, 40)
	t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_night_panel.add_child(t)

func _build_upgrade() -> void:
	_upgrade_panel = _make_panel()
	_upgrade_panel.add_child(UiSkin.panel(Rect2(140, 180, 1000, 380), Color(0.10, 0.12, 0.20, 0.94)))
	var t := UiSkin.label("天明 · 三选一    按 1/2/3 选牌   Q 重掷(3金)   E 跳过(+5金)", 22, UiSkin.GOLD)
	t.position = Vector2(170, 196)
	_upgrade_panel.add_child(t)
	for i in range(GameConfig.PICK_COUNT):
		var b := UiSkin.button("", 19)
		b.position = Vector2(170 + i * 320, 270)
		b.size = Vector2(290, 190)
		b.pressed.connect(_on_pick.bind(i))
		_upgrade_panel.add_child(b)
		_card_buttons.append(b)

func _build_dead() -> void:
	_dead_panel = _make_panel()
	_dead_panel.add_child(UiSkin.panel(Rect2(300, 220, 680, 280), Color(0.16, 0.08, 0.10, 0.95)))
	_dead_text = UiSkin.label("", 24, UiSkin.CRIMSON)
	_dead_text.position = Vector2(320, 260)
	_dead_text.size = Vector2(640, 200)
	_dead_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_dead_text.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_dead_panel.add_child(_dead_text)

func _process(_delta: float) -> void:
	if not visible:
		return
	var gm := GameManager
	_top.text = "第 %d 天  ｜  %s" % [gm.day, gm.phase_label()]
	_hp.max_value = float(maxi(1, RunState.max_hp))
	_hp.value = float(RunState.hp)
	_progress.value = gm.progress_ratio() * 100.0
	_coins.text = "金币 %d" % RunState.coins
	_weapon.text = "武器 " + str(GameConfig.weapon(RunState.weapon_id)["name"])
	_kills.text = "击杀 %d  ｜  区域 %s" % [RunState.kills, str(GameConfig.THEMES[GameConfig.theme_index(gm.day)]["name"])]
	_day_panel.visible = gm.is_day()
	_night_panel.visible = gm.is_night()
	_upgrade_panel.visible = gm.is_upgrade()
	_dead_panel.visible = gm.is_dead()

func _on_offered(options: Array) -> void:
	for i in range(_card_buttons.size()):
		var b: Button = _card_buttons[i]
		if i < options.size():
			var c: Dictionary = options[i]
			b.text = "%d   %s\n\n%s" % [i + 1, str(c["name"]), str(c["desc"])]
			b.disabled = false
		else:
			b.text = "—"
			b.disabled = true

func _on_run_ended(stats: Dictionary) -> void:
	_dead_text.text = "本 局 结 束\n\n第 %d 天   ｜   击杀 %d   ｜   获得余烬 %d\n\nR 重开一局        M 返回主页面" % [
		int(stats.get("day", 0)), int(stats.get("kills", 0)), int(stats.get("embers", 0)),
	]

func _on_hint(text: String) -> void:
	_hint.text = text

func _on_structures(_counts: Dictionary) -> void:
	_hint.text = "建造完成：" + str(BuildSystem.summary())

func _on_buy(i: int) -> void:
	GameManager.try_buy(i)

func _on_pick(i: int) -> void:
	GameManager.choose_card(i)

func _slot(i: int) -> void:
	if GameManager.is_day():
		GameManager.try_buy(i)
	elif GameManager.is_upgrade():
		GameManager.choose_card(i)

func _unhandled_input(event: InputEvent) -> void:
	if not visible:
		return
	for i in range(6):
		if event.is_action_pressed("slot%d" % (i + 1)):
			_slot(i)
	if event.is_action_pressed("next_weapon"):
		GameManager.cycle_weapon()
	if event.is_action_pressed("pause"):
		GameManager.toggle_pause()
	if event.is_action_pressed("start_night"):
		GameManager.start_night_early()
	if event.is_action_pressed("reroll"):
		if GameManager.is_upgrade():
			GameManager.reroll()
		else:
			_hint.text = "本版商品表固定，白天无需刷新（源工程随机商店未迁移）"
	if event.is_action_pressed("skip"):
		GameManager.skip()
	if event.is_action_pressed("restart"):
		GameManager.restart_run()
	if event.is_action_pressed("menu"):
		GameManager.return_to_menu()
