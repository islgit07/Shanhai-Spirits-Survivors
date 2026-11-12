class_name PlatformField
extends Node2D
## @trace PLAT-001 / MAP-001 多层断续平台（Godot 2D 原生物理）
## 对应 Unity PlatformField.cs。本版改用 StaticBody2D + CollisionShape2D 单向平台，
## 顺带补上源工程 L3 硬缺口（原为距离判定 + 手写单向平台）。
## L0 连续地板；L1..L9 每层 n 段随机平台，段宽 3.4~7.0、间隙由步长保证 >=1.8。

var segments: Array = []
var _rng: RandomNumberGenerator

func build(seed_value: int) -> void:
	for c in get_children():
		remove_child(c)
		c.queue_free()
	segments = []
	_rng = RunSeed.make_rng(seed_value, "platforms")
	var width := GameConfig.WORLD_HALF_WIDTH * 2.0
	_add(0, -GameConfig.WORLD_HALF_WIDTH, GameConfig.WORLD_HALF_WIDTH, false)
	var n := GameConfig.segments_for(width)
	var step := width / float(n)
	for layer in range(1, GameConfig.LAYER_COUNT):
		for i in range(n):
			var cx := -GameConfig.WORLD_HALF_WIDTH + step * (float(i) + 0.5)
			cx += _rng.randf_range(-step * 0.18, step * 0.18)
			var max_w := maxf(1.6, step - GameConfig.PLATFORM_MIN_GAP)
			var w := _rng.randf_range(GameConfig.PLATFORM_MIN_WIDTH, minf(GameConfig.PLATFORM_MAX_WIDTH, max_w))
			_add(layer, cx - w * 0.5, cx + w * 0.5, true)

func _add(layer: int, x0: float, x1: float, one_way: bool) -> void:
	var y := GameConfig.layer_y(layer)
	var size := Vector2(x1 - x0, GameConfig.PLATFORM_THICKNESS)
	var body := StaticBody2D.new()
	body.position = Vector2((x0 + x1) * 0.5, y)
	body.collision_layer = 1
	var shape := RectangleShape2D.new()
	shape.size = size
	var cs := CollisionShape2D.new()
	cs.shape = shape
	cs.one_way_collision = one_way
	if one_way:
		cs.one_way_collision_margin = 0.2
	body.add_child(cs)
	var color := Color(0.30, 0.36, 0.30) if layer == 0 else Color(0.42, 0.40, 0.52)
	body.add_child(ArtBank.make_block(size, color))
	add_child(body)
	segments.append({"layer": layer, "x0": x0, "x1": x1, "y": y})

func random_x(layer: int) -> float:
	var candidates: Array = []
	for s in segments:
		if int(s["layer"]) == layer:
			candidates.append(s)
	if candidates.is_empty():
		return 0.0
	var s: Dictionary = candidates[_rng.randi_range(0, candidates.size() - 1)]
	return _rng.randf_range(float(s["x0"]) + 0.4, float(s["x1"]) - 0.4)
