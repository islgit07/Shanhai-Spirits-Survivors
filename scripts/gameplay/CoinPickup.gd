class_name CoinPickup
extends Node2D
## @trace COIN-001 金币掉落：重力落地 + 磁吸拾取；天亮由 World.on_dawn() 统一清除
## 简化：落地高度固定为底层（源工程按平台层做了 TryLanding），见迁移清单。

var value: int = 1
var _vel := Vector2.ZERO
var _floor_y := 0.0
var _landed := false

func setup(pos: Vector2, floor_y: float, val: int) -> void:
	global_position = pos
	_floor_y = floor_y
	value = val
	_vel = Vector2(randf_range(-1.2, 1.2), -randf_range(1.0, 3.0))
	add_child(ArtBank.make_sprite("coin", Vector2(0.45, 0.45)))

func _process(delta: float) -> void:
	if not _landed:
		_vel.y += GameConfig.COIN_GRAVITY * delta
		global_position += _vel * delta
		if global_position.y >= _floor_y:
			global_position.y = _floor_y
			_landed = true
	var p := get_tree().get_first_node_in_group("player") as Node2D
	if p == null:
		return
	var dist := global_position.distance_to(p.global_position)
	if dist < GameConfig.COIN_MAGNET_RANGE + RunState.magnet_bonus:
		global_position = global_position.lerp(p.global_position, clampf(delta * 8.0, 0.0, 1.0))
	if dist < GameConfig.COIN_PICK_RANGE:
		RunState.add_coins(value)
		queue_free()
