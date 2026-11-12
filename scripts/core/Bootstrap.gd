extends Node2D
## @trace BOOT-001 程序化自举：注册输入映射、构建三根、绑定状态机
## 迁移自 Unity DemoBootstrap.cs 的 RuntimeInitializeOnLoadMethod 零配置自举思路。
## Godot 侧主场景 Main.tscn 只有一个 Node2D + 本脚本，其余节点全部运行时生成，
## 不手写复杂 .tscn / .tres 序列化文件（专家规范：程序化构建优先）。

const MainMenuScript := preload("res://scripts/ui/MainMenu.gd")
const WorldScript := preload("res://scripts/gameplay/World.gd")
const HudScript := preload("res://scripts/ui/Hud.gd")

const ACTION_KEYS := {
	"move_left":   [KEY_A, KEY_LEFT],
	"move_right":  [KEY_D, KEY_RIGHT],
	"jump":        [KEY_SPACE, KEY_W, KEY_UP],
	"next_weapon": [KEY_F],
	"pause":       [KEY_ESCAPE, KEY_P],
	"start_night": [KEY_ENTER, KEY_KP_ENTER],
	"slot1":       [KEY_1],
	"slot2":       [KEY_2],
	"slot3":       [KEY_3],
	"slot4":       [KEY_4],
	"slot5":       [KEY_5],
	"slot6":       [KEY_6],
	"reroll":      [KEY_Q],
	"skip":        [KEY_E],
	"restart":     [KEY_R],
	"menu":        [KEY_M],
}

func _ready() -> void:
	_register_actions()
	var menu: CanvasLayer = MainMenuScript.new()
	var world: Node2D = WorldScript.new()
	var hud: CanvasLayer = HudScript.new()
	menu.name = "MenuRoot"
	world.name = "WorldRoot"
	hud.name = "HudRoot"
	# 暂停时 UI 仍需接收输入
	menu.process_mode = Node.PROCESS_MODE_ALWAYS
	hud.process_mode = Node.PROCESS_MODE_ALWAYS
	add_child(menu)
	add_child(world)
	add_child(hud)
	GameManager.bind(menu, world, hud)
	# `--` 之后的用户参数在 Godot 里走 get_cmdline_user_args()，两个来源都查一遍
	var args := OS.get_cmdline_args()
	var user_args := OS.get_cmdline_user_args()
	print("[BOOT] 山海幻灵 v0.001 自举完成 ｜ args=" + str(args) + " ｜ user=" + str(user_args))
	if args.has("--smoke") or user_args.has("--smoke"):
		GameManager.begin_smoke()

func _register_actions() -> void:
	for action in ACTION_KEYS.keys():
		if not InputMap.has_action(action):
			InputMap.add_action(action, 0.2)
		for code in ACTION_KEYS[action]:
			var e := InputEventKey.new()
			e.physical_keycode = int(code)
			InputMap.action_add_event(action, e)
