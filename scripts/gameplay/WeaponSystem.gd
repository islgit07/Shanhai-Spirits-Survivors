class_name WeaponSystem
extends Node
## @trace WPN-001 / WPN-002 六把武器：数据表驱动 + 自动索敌开火
## 迁移自 Unity WeaponSystem.cs；宠物/炮塔自动开火未迁移（本版仅玩家开火）。

var _timer := 0.0

func _process(delta: float) -> void:
	if not GameManager.is_night():
		return
	_timer -= delta
	if _timer > 0.0:
		return
	var w := GameConfig.weapon(RunState.weapon_id)
	_timer = maxf(0.08, float(w["interval"]) * RunState.interval_mul)
	_fire(w)

func _fire(w: Dictionary) -> void:
	var world := GameManager.get_world() as World
	var owner_node := get_parent() as Node2D
	if world == null or owner_node == null:
		return
	var origin := owner_node.global_position
	var dmg := int(w["damage"]) + RunState.damage_bonus
	var rng := float(w["range"]) + RunState.range_bonus
	var target := _nearest_enemy(origin, rng)
	match str(w["kind"]):
		"bullet":
			if target == null:
				return
			var base := (target.global_position - origin).normalized()
			var count := int(w["count"])
			for i in range(count):
				var a := 0.0
				if count > 1:
					a = lerpf(-float(w["spread"]) * 0.5, float(w["spread"]) * 0.5, float(i) / float(count - 1))
				world.spawn_projectile(origin, base.rotated(a), float(w["speed"]), dmg, 1.6, Color(1.0, 0.88, 0.45))
		"fan":
			# 仪式短刃：150° 扇形近战判定，无子弹
			var hit := 0
			for e in get_tree().get_nodes_in_group("enemies"):
				var en := e as Node2D
				if en == null:
					continue
				var v := en.global_position - origin
				if v.length() <= rng and absf(v.angle()) < 1.31:
					if en.has_method("take_damage"):
						en.call("take_damage", dmg)
					hit += 1
			if hit > 0:
				world.spawn_slash(origin, rng)
		"chain":
			# 引雷法杖：3 跳连锁，每跳 ×0.78
			if target == null:
				return
			var cur: Node2D = target
			var used: Array = []
			var d := float(dmg)
			for i in range(int(w["count"])):
				if cur == null or used.has(cur):
					break
				used.append(cur)
				if cur.has_method("take_damage"):
					cur.call("take_damage", roundi(d))
				d *= 0.78
				cur = _nearest_enemy(cur.global_position, 3.5, used)
		"boomerang":
			if target == null:
				return
			var p := world.spawn_projectile(origin, (target.global_position - origin).normalized(), float(w["speed"]), dmg, 2.4, Color(0.6, 0.9, 1.0))
			if p != null:
				p.return_to_player = true
		"aura":
			# 静滞力场：持续光环，不索敌开火
			var n := 0
			for e in get_tree().get_nodes_in_group("enemies"):
				var en := e as Node2D
				if en != null and en.global_position.distance_to(origin) <= rng:
					if en.has_method("take_damage"):
						en.call("take_damage", dmg)
					n += 1
			if n > 0:
				world.spawn_slash(origin, rng)

func _nearest_enemy(from: Vector2, max_dist: float, exclude: Array = []) -> Node2D:
	var best := max_dist
	var found: Node2D = null
	for e in get_tree().get_nodes_in_group("enemies"):
		if exclude.has(e) or not is_instance_valid(e):
			continue
		var en := e as Node2D
		if en == null:
			continue
		var d := from.distance_to(en.global_position)
		if d < best:
			best = d
			found = en
	return found
