class_name Projectile
extends Node2D
## @trace WPN-001 子弹/投掷物：短命对象 + 距离命中
## 源工程用对象池；Godot 侧本垂直切片用 queue_free 短命对象（见迁移清单，对象池列为未迁移项）。

var dir: Vector2 = Vector2.RIGHT
var speed: float = 13.0
var damage: int = 10
var life: float = 1.6
var pierce: int = 0
var return_to_player: bool = false
var _hit: Array = []
var _origin: Vector2
var _body: ColorRect

func setup(pos: Vector2, d: Vector2, spd: float, dmg: int, lifetime: float, color: Color) -> void:
	global_position = pos
	_origin = pos
	dir = d.normalized()
	speed = spd
	damage = dmg
	life = lifetime
	_body = ArtBank.make_block(Vector2(0.22, 0.22), color)
	add_child(_body)

func _process(delta: float) -> void:
	life -= delta
	if life <= 0.0:
		queue_free()
		return
	if return_to_player:
		# 逐星轮：飞出 6 单位后折返
		if global_position.distance_to(_origin) >= 6.0:
			dir = -dir
			return_to_player = false
	global_position += dir * speed * delta
	for e in get_tree().get_nodes_in_group("enemies"):
		if _hit.has(e) or not is_instance_valid(e):
			continue
		var en := e as Node2D
		if en != null and en.global_position.distance_to(global_position) < 0.6:
			if en.has_method("take_damage"):
				en.call("take_damage", damage)
			_hit.append(e)
			if pierce <= 0:
				queue_free()
				return
			pierce -= 1
