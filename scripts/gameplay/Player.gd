class_name Player
extends CharacterBody2D
## @trace JUMP-001 纯平台跳跃：A/D + Space 重力跳 + 单向平台落地
## 迁移自 Unity PlayerController.cs（v6 已删除 W/S 一键跃层与自动吸附）。
## Godot 侧用 CharacterBody2D.move_and_slide()，不再手写距离判定。

var invuln: float = 0.0
var _sprite: Sprite2D
var _weapon: WeaponSystem

func _ready() -> void:
	add_to_group("player")
	collision_layer = 1
	collision_mask = 1
	var shape := RectangleShape2D.new()
	shape.size = GameConfig.PLAYER_SIZE
	var cs := CollisionShape2D.new()
	cs.shape = shape
	add_child(cs)
	_sprite = ArtBank.make_sprite("player", GameConfig.PLAYER_SIZE)
	add_child(_sprite)
	_weapon = WeaponSystem.new()
	_weapon.name = "WeaponSystem"
	add_child(_weapon)

func _physics_process(delta: float) -> void:
	var dir := Input.get_axis("move_left", "move_right")
	velocity.x = dir * (GameConfig.PLAYER_MOVE_SPEED + RunState.move_bonus)
	if is_on_floor():
		velocity.y = 0.0
		if Input.is_action_just_pressed("jump"):
			velocity.y = -(GameConfig.PLAYER_JUMP_SPEED + RunState.jump_bonus)
	else:
		velocity.y += GameConfig.GRAVITY * delta
	move_and_slide()
	# 掉出世界兜底：送回底层，防止相机跟到虚空
	if global_position.y < GameConfig.DROP_KILL_Y:
		global_position = Vector2(clampf(global_position.x, -GameConfig.WORLD_HALF_WIDTH, GameConfig.WORLD_HALF_WIDTH), GameConfig.layer_y(0) + 1.0)
		velocity = Vector2.ZERO
	if invuln > 0.0:
		invuln -= delta
		_sprite.modulate = Color(1.0, 0.55, 0.55) if invuln > 0.0 else Color.WHITE

func take_damage(amount: int) -> void:
	if invuln > 0.0 or GameManager.is_dead():
		return
	RunState.damage(maxi(1, amount - RunState.armor))
	invuln = GameConfig.PLAYER_INVULN
	if RunState.hp <= 0:
		GameManager.player_died()
