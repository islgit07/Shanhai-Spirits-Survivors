class_name World
extends Node2D
## @trace WORLD-001 / AREA-001 世界组装：2D 场景根 / 相机双向跟随 / 四套区域主题
## 全部节点运行时程序化构建，不依赖手写的复杂 .tscn。
## 相机用 zoom 把 720px 视口映射为 13 个世界单位高（对应源工程 orthoSize 6.5）。

var platform_count: int = 0
var ground_continuous: bool = false
var player_grounded: bool = false
var enemies_spawned: int = 0
var projectiles_fired: int = 0
var coins_spawned: int = 0
var player: Node2D = null

# C# [GlobalClass] 组件（scripts/csharp/PlatformField.cs）；GDScript 侧以 Node2D 静态类型持有。
var _platforms: Node2D
var _spawner: EnemySpawner
var _camera: Camera2D
var _backdrop: Sprite2D
var _projectiles: Node2D
var _enemies: Node2D
var _coins: Node2D
var _structures: Node2D
var _rng: RandomNumberGenerator

func _ready() -> void:
	add_to_group("world")

func zoom_value() -> float:
	var h := get_viewport_rect().size.y
	if h <= 0.0:
		h = 720.0
	return h / (2.0 * GameConfig.CAMERA_VIEW_HALF_HEIGHT)

func view_size_world() -> Vector2:
	return get_viewport_rect().size / zoom_value()

func begin_run() -> void:
	_clear_all()
	platform_count = 0
	ground_continuous = false
	player_grounded = false
	enemies_spawned = 0
	projectiles_fired = 0
	coins_spawned = 0
	_rng = RunSeed.make_rng(RunState.seed_value, "world")

	_camera = Camera2D.new()
	_camera.zoom = Vector2(zoom_value(), zoom_value())
	_camera.enabled = true
	add_child(_camera)
	# 背景挂在相机下，天然跟随；铺满当前视野
	_backdrop = ArtBank.make_sprite("bg_dusk", view_size_world())
	_backdrop.z_index = -100
	_camera.add_child(_backdrop)

	_platforms = PlatformField.new()
	add_child(_platforms)
	_platforms.call("Build", RunState.seed_value)
	platform_count = int(_platforms.call("GetSegmentCount"))
	ground_continuous = bool(_platforms.call("IsGroundContinuous"))

	_structures = Node2D.new()
	add_child(_structures)
	_enemies = Node2D.new()
	add_child(_enemies)
	_projectiles = Node2D.new()
	add_child(_projectiles)
	_coins = Node2D.new()
	add_child(_coins)

	player = Player.new()
	add_child(player)
	# y-down：地板上方 1 单位 = y 更小。出生点必须落在第一层之上才能被实体地板接住。
	player.global_position = Vector2(0.0, GameConfig.layer_y(0) - GameConfig.SPAWN_ABOVE_GROUND)

	_spawner = EnemySpawner.new()
	add_child(_spawner)
	apply_theme(1, false)

func end_run() -> void:
	_clear_all()

func _clear_all() -> void:
	for c in get_children():
		remove_child(c)
		c.queue_free()
	player = null
	_platforms = null
	_spawner = null
	_camera = null
	_backdrop = null
	_enemies = null
	_projectiles = null
	_coins = null
	_structures = null

func _process(delta: float) -> void:
	if player == null or _camera == null or not is_instance_valid(player):
		return
	# @trace GND-001 运行时证据：玩家必须真的被第一层实体地板接住（验证 y-down 修正生效）。
	var body := player as CharacterBody2D
	if body != null and body.is_on_floor():
		player_grounded = true
	var half := view_size_world() * 0.5
	var cx := clampf(player.global_position.x, -GameConfig.WORLD_HALF_WIDTH + half.x, GameConfig.WORLD_HALF_WIDTH - half.x)
	# y-down：向上跳是 y 减小，故相机用 minf 跟随，且不允许沉到地面以下。
	var cy := minf(GameConfig.layer_y(0) - half.y * 0.4, player.global_position.y - 1.0)
	_camera.global_position = _camera.global_position.lerp(Vector2(cx, cy), clampf(delta * GameConfig.CAMERA_SMOOTH, 0.0, 1.0))

## @trace AREA-001 四套区域每 3 夜轮换 + 昼夜 tint
func apply_theme(day: int, is_night: bool) -> void:
	if _backdrop == null:
		return
	var t: Dictionary = GameConfig.THEMES[GameConfig.theme_index(day)]
	_backdrop.texture = ArtBank.tex(str(t["bg"]))
	var size := view_size_world()
	var tw := maxf(1.0, float(_backdrop.texture.get_width()))
	var th := maxf(1.0, float(_backdrop.texture.get_height()))
	_backdrop.scale = Vector2(size.x / tw, size.y / th)
	var rgb: Array = t["night"] if is_night else t["day"]
	_backdrop.modulate = Color(float(rgb[0]), float(rgb[1]), float(rgb[2]), 1.0)

func set_phase(p: int, day: int) -> void:
	var night := GameManager.is_night()
	apply_theme(day, night)
	if night and _spawner != null:
		_spawner.begin(day, RunState.seed_value)

## 天亮：清掉未拾取金币、残余敌人与子弹（对应源工程 DespawnAtDawn + ClearRunState）
func on_dawn() -> void:
	_clear_container(_enemies)
	_clear_container(_projectiles)
	_clear_container(_coins)

func _clear_container(c: Node) -> void:
	if c == null:
		return
	for n in c.get_children():
		c.remove_child(n)
		n.queue_free()

func add_structure(kind: String) -> void:
	if _structures == null:
		return
	var colors := {
		"turret": Color(0.90, 0.70, 0.30), "vault": Color(0.95, 0.85, 0.35),
		"beacon": Color(0.50, 0.80, 1.00), "tent": Color(0.50, 0.90, 0.60),
		"wall": Color(0.70, 0.60, 0.50),
	}
	var holder := Node2D.new()
	var n := _structures.get_child_count()
	holder.position = Vector2(-3.0 + fmod(float(n) * 1.4, 6.0), GameConfig.layer_y(0) - 0.45)
	holder.add_child(ArtBank.make_block(Vector2(0.7, 0.7), colors.get(kind, Color.WHITE)))
	_structures.add_child(holder)

func spawn_enemy(def: Dictionary, day: int) -> void:
	if _enemies == null or player == null or _rng == null:
		return
	var dist := _rng.randf_range(GameConfig.SPAWN_NEAR_MIN, GameConfig.SPAWN_NEAR_MAX)
	# 冒烟模式夜晚只有 0.8 秒，敌人须在近场，否则子弹飞不到就天亮被清
	if GameManager.smoke_mode:
		dist = _rng.randf_range(0.9, 1.6)
	var side := 1.0 if _rng.randf() < 0.5 else -1.0
	var x := clampf(player.global_position.x + side * dist, -GameConfig.WORLD_HALF_WIDTH, GameConfig.WORLD_HALF_WIDTH)
	# y-down：最高层 y 最小，故 clamp 的下界是最顶层、上界是底层地面之上。
	var top_y := GameConfig.layer_y(GameConfig.LAYER_COUNT - 1)
	var bottom_y := GameConfig.layer_y(0) - 0.6
	var y := clampf(player.global_position.y + _rng.randf_range(-1.0, 1.0) * GameConfig.LAYER_SPACING,
		top_y, bottom_y)
	var e := Enemy.new()
	_enemies.add_child(e)
	e.setup(def, day, Vector2(x, y))
	enemies_spawned += 1

func spawn_projectile(pos: Vector2, d: Vector2, spd: float, dmg: int, lifetime: float, color: Color) -> Projectile:
	if _projectiles == null:
		return null
	var pr := Projectile.new()
	_projectiles.add_child(pr)
	pr.setup(pos, d, spd, dmg, lifetime, color)
	projectiles_fired += 1
	return pr

## 近战 / 光环的统一视觉反馈（短命横条）
func spawn_slash(pos: Vector2, radius: float) -> void:
	if _projectiles == null:
		return
	var fx := ArtBank.make_block(Vector2(radius * 2.0, 0.10), Color(1.0, 0.9, 0.6, 0.55))
	fx.position = Vector2(-radius, -0.05)
	var holder := Node2D.new()
	holder.position = pos
	holder.add_child(fx)
	_projectiles.add_child(holder)
	var t := get_tree().create_timer(0.12)
	t.timeout.connect(func(): if is_instance_valid(holder): holder.queue_free())

func on_enemy_killed(id: String, at: Vector2) -> void:
	if _coins == null or _rng == null:
		return
	var n := 2 if (id == "elite" or id == "brute") else 1
	for i in range(n):
		var c := CoinPickup.new()
		_coins.add_child(c)
		c.setup(at + Vector2(_rng.randf_range(-0.3, 0.3), 0.0), GameConfig.layer_y(0) - 0.25, RunState.coin_value)
		coins_spawned += 1
