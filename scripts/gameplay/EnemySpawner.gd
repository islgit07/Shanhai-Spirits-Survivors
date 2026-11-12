class_name EnemySpawner
extends Node
## @trace WAVE-001 / ECO-002 预算制波次 + 自适应刷怪间隔
## B(d)=min(130,14+7(d-1))，间隔 clamp(30×0.86/B(d), 0.22, 0.85)，与源工程一致。

var day: int = 1
var remaining: int = 0
var spawned: int = 0
var _timer := 0.0
var _rng: RandomNumberGenerator

func begin(d: int, seed_value: int) -> void:
	day = d
	remaining = GameConfig.budget(d)
	# 冒烟模式夜晚只有 0.45 秒，首刷必须立刻发生，否则整条战斗链路测不到
	_timer = 0.01 if GameManager.smoke_mode else 0.3
	_rng = RunSeed.make_rng(seed_value, "spawn-%d" % d)

func _process(delta: float) -> void:
	if not GameManager.is_night() or remaining <= 0:
		return
	_timer -= delta
	if _timer > 0.0:
		return
	_timer = GameConfig.spawn_interval(day)
	if GameManager.smoke_mode:
		_timer = minf(_timer, 0.06)
	var pool: Array = []
	for e in GameConfig.ENEMIES:
		if int(e["unlock"]) <= day and int(e["cost"]) <= remaining:
			pool.append(e)
	if pool.is_empty():
		return
	var d: Dictionary = pool[_rng.randi_range(0, pool.size() - 1)]
	remaining -= int(d["cost"])
	spawned += 1
	var world := GameManager.get_world() as World
	if world != null:
		world.spawn_enemy(d, day)
