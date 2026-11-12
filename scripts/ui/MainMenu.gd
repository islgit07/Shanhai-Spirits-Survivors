class_name MainMenu
extends CanvasLayer
## @trace MENU-001 主页面：标题 / 出征准备 / 永夜营地（局外成长）/ 战绩
## 迁移自 Unity MainMenuController.cs；本版用 Godot Control 节点 + 鼠标点击，
## 手柄焦点导航（源工程 PAD-002/003/004）未迁移。

var _root: Control
var _start_weapon: String = "revolver"
var _weapon_buttons: Array = []
var _upgrade_buttons: Array = []
var _stats: Label
var _hint: Label
var _clear_confirm: int = 0

func _ready() -> void:
	_build()
	refresh()

func _build() -> void:
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_root)
	var bg := ColorRect.new()
	bg.color = UiSkin.INK
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	_root.add_child(bg)

	var title := UiSkin.label("山 海 幻 灵", 54, UiSkin.GOLD)
	title.position = Vector2(0, 44)
	title.size = Vector2(1280, 80)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_root.add_child(title)
	var sub := UiSkin.label("横板昼夜幸存者  ·  白天经营 / 夜晚幸存  ·  Godot 4 · 2D", 18, UiSkin.TEXT)
	sub.position = Vector2(0, 122)
	sub.size = Vector2(1280, 30)
	sub.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_root.add_child(sub)

	_root.add_child(UiSkin.panel(Rect2(60, 170, 540, 410)))
	_root.add_child(UiSkin.panel(Rect2(680, 170, 540, 410)))
	var h1 := UiSkin.label("出征准备 · 选起始武器", 22, UiSkin.GOLD)
	h1.position = Vector2(84, 184)
	_root.add_child(h1)
	var h2 := UiSkin.label("永夜营地 · 局外成长", 22, UiSkin.BLUE)
	h2.position = Vector2(704, 184)
	_root.add_child(h2)

	for i in range(GameConfig.WEAPONS.size()):
		var b := UiSkin.button("", 17)
		b.position = Vector2(84, 226 + i * 48)
		b.size = Vector2(492, 40)
		b.pressed.connect(_on_weapon_pressed.bind(i))
		_root.add_child(b)
		_weapon_buttons.append(b)

	for i in range(GameConfig.META_UPGRADES.size()):
		var b := UiSkin.button("", 17)
		b.position = Vector2(704, 226 + i * 48)
		b.size = Vector2(492, 40)
		b.pressed.connect(_on_upgrade_pressed.bind(i))
		_root.add_child(b)
		_upgrade_buttons.append(b)

	_stats = UiSkin.label("", 17, UiSkin.TEXT)
	_stats.position = Vector2(704, 478)
	_stats.size = Vector2(492, 60)
	_root.add_child(_stats)

	var clear_btn := UiSkin.button("清空存档（需点两次确认）", 15)
	clear_btn.position = Vector2(704, 532)
	clear_btn.size = Vector2(492, 34)
	clear_btn.pressed.connect(_on_clear_pressed)
	_root.add_child(clear_btn)

	var start := UiSkin.button("开  始  新  的  一  局", 24)
	start.position = Vector2(440, 606)
	start.size = Vector2(400, 58)
	start.pressed.connect(_on_start_pressed)
	_root.add_child(start)

	_hint = UiSkin.label("", 16, UiSkin.JADE)
	_hint.position = Vector2(0, 672)
	_hint.size = Vector2(1280, 28)
	_hint.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_root.add_child(_hint)

func refresh() -> void:
	if _weapon_buttons.is_empty():
		return
	if not MetaProgress.unlocked.has(_start_weapon):
		_start_weapon = "revolver"
	for i in range(GameConfig.WEAPONS.size()):
		var w: Dictionary = GameConfig.WEAPONS[i]
		var owned: bool = MetaProgress.unlocked.has(w["id"])
		var mark := "◆ " if _start_weapon == w["id"] else "   "
		var tail := "已解锁" if owned else ("%d 余烬解锁" % int(w["price"]))
		var b: Button = _weapon_buttons[i]
		b.text = "%s%s    %s" % [mark, str(w["name"]), tail]
	for i in range(GameConfig.META_UPGRADES.size()):
		var u: Dictionary = GameConfig.META_UPGRADES[i]
		var lv := MetaProgress.upgrade_level(str(u["id"]))
		var b: Button = _upgrade_buttons[i]
		var price_text := "已满级" if lv >= 5 else ("%d 余烬" % GameConfig.meta_price(lv))
		b.text = "%s  Lv.%d/5    %s    %s" % [str(u["name"]), lv, str(u["desc"]), price_text]
	_stats.text = "余烬 %d  ｜  最远第 %d 天  ｜  累计 %d 局  ｜  累计击杀 %d" % [
		MetaProgress.embers, MetaProgress.best_day, MetaProgress.total_runs, MetaProgress.total_kills,
	]
	_hint.text = "按 Enter 直接开局  ｜  鼠标点选武器 / 升级  ｜  手柄操作未在本版迁移"

func _on_weapon_pressed(i: int) -> void:
	var w: Dictionary = GameConfig.WEAPONS[i]
	var id := str(w["id"])
	if not MetaProgress.unlocked.has(id):
		if not MetaProgress.unlock_weapon(id):
			_hint.text = "余烬不足，无法解锁 " + str(w["name"])
			return
	_start_weapon = id
	_hint.text = "起始武器：" + str(w["name"])
	refresh()

func _on_upgrade_pressed(i: int) -> void:
	var u: Dictionary = GameConfig.META_UPGRADES[i]
	if MetaProgress.buy_upgrade(str(u["id"])):
		_hint.text = "已升级 " + str(u["name"])
	else:
		_hint.text = "余烬不足或已满级"
	refresh()

func _on_clear_pressed() -> void:
	_clear_confirm += 1
	if _clear_confirm >= 2:
		MetaProgress.clear_all()
		_clear_confirm = 0
		_start_weapon = "revolver"
		_hint.text = "存档已清空"
	else:
		_hint.text = "再点一次确认清空存档（不可撤销）"
	refresh()

func _on_start_pressed() -> void:
	GameManager.start_run(_start_weapon)

func _unhandled_input(event: InputEvent) -> void:
	if not visible:
		return
	if event.is_action_pressed("start_night"):
		GameManager.start_run(_start_weapon)
