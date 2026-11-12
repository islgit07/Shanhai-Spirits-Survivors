extends Node
## @trace EVT-001 全局信号总线（autoload）
## 迁移自 Unity GameEvents.cs 的事件总线；Godot 用 signal 替代 C# event + 委托表。
## 注意：autoload 单例常驻，跨局重开不销毁，因此订阅方必须在 _exit_tree 自己断开。

signal phase_changed(phase: int, day: int)
signal day_started(day: int)
signal night_started(day: int)
signal dawn_started(day: int)
signal upgrade_offered(options: Array)
signal run_ended(stats: Dictionary)
signal coins_changed(total: int)
signal hp_changed(current: int, maximum: int)
signal enemy_killed(enemy_id: String, at: Vector2)
signal weapon_changed(weapon_id: String)
signal structures_changed(counts: Dictionary)
signal hint(text: String)
signal meta_changed()

## 清空一次性运行时引用。跨局重开时由 GameManager 调用。
func reset_runtime() -> void:
	# signal 连接由各订阅节点自行管理；此处只作占位，避免误以为总线会重置订阅。
	pass
