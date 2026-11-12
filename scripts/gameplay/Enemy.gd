class_name Enemy
extends Node2D
## @trace ECO-001 / ECO-002 六种敌人原型 + 属性随天缩放
## 迁移自 Unity Enemy.cs。跨层追击用 Y 向插值（源工程弧线插值的简化版，Q16 待确认）。
## 本版敌人仍是 Node2D + 距离判定，未换成 Rigidbody2D/Area2D（见迁移清单）。

var def: Dictionary = {}
var hp: int = 1
var damage: int = 8
var speed: float = 2.0
var _attack_timer := 0.0

func setup(d: Dictionary, day: int, pos: Vector2) -> void:
	def = d
	hp = roundi(float(d["hp"]) * (1.0 + GameConfig.HP_SCALE_PER_DAY * float(day - 1)))
	damage = roundi(float(d["damage"]) * (1.0 + GameConfig.DAMAGE_SCALE_PER_DAY * float(day - 1)))
	speed = float(d["speed"]) * (1.0 + GameConfig.SPEED_SCALE_PER_DAY * float(day - 1))
	global_position = pos
	add_to_group("enemies")
	add_child(ArtBank.make_sprite(str(d["sprite"]), Vector2(0.9, 0.9)))

func _process(delta: float) -> void:
	if not GameManager.is_night():
		return
	var p := get_tree().get_first_node_in_group("player") as Node2D
	if p == null:
		return
	var slow := BuildSystem.beacon_slow()
	var step := speed * slow * delta
	global_position.x += clampf(p.global_position.x - global_position.x, -step, step)
	global_position.y = lerpf(global_position.y, p.global_position.y + 0.4, clampf(delta * 1.6, 0.0, 1.0))
	if global_position.distance_to(p.global_position) < 0.9:
		_attack_timer -= delta
		if _attack_timer <= 0.0:
			_attack_timer = GameConfig.ENEMY_ATTACK_INTERVAL
			if p.has_method("take_damage"):
				p.call("take_damage", damage)
	else:
		_attack_timer = 0.0

func take_damage(amount: int) -> void:
	hp -= amount
	if hp <= 0:
		die()

func die() -> void:
	var at := global_position
	var id := str(def.get("id", "shambler"))
	RunState.kills += 1
	GameEvents.enemy_killed.emit(id, at)
	get_tree().call_group("world", "on_enemy_killed", id, at)
	queue_free()
