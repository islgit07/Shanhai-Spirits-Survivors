using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace JUMP-001 山海谷幻灵师（v0.0001：只有移动与跳跃，无战斗、无受伤）。
/// A/D + Space 真实重力 + S/↓ 下穿单向平台；第一层全覆盖硬约束保持不变。
/// 移动内核与白箱共用 PlatformMover：白箱里调出的手感 = 游戏里的手感。
/// </summary>
public partial class Master : CharacterBody2D, IBounceable
{
	private Sprite2D _sprite = null!;
	private Vector2 _spriteBaseScale;
	private bool _wasGrounded = true;
	[Export] public PackedScene? DustScene;
	// 移动内核与白箱共用（见 PlatformMover）：白箱里调出的数值写回 GameDefs 即生效
	private readonly PlatformMover _mover = new(MoveTuning.GameDefaults());

	public override void _Ready()
	{
		AddToGroup("master");
		// 角色碰撞（与随从 / 未来敌人共用一套规则）：
		//   layer 保留 LayerSolid —— 脆弱 / 弹跳 / 宝箱方块用 Area2D(mask=1) 识别玩家，改掉会全部失联；
		//   mask 含 LayerSpiritTop —— 玩家可以踩在随从头顶那块小平台上；
		//   mask 不含 LayerSpirit —— 主角水平方向永远不被随从顶住（"随从挡路"的根治）；
		//   mask 含 LayerActor  —— 敌人系统接入后，主角与敌人互相阻挡。
		CollisionLayer = GameDefs.LayerSolid | GameDefs.LayerActor;
		CollisionMask = GameDefs.LayerSolid | GameDefs.LayerOneWay
			| GameDefs.LayerActor | GameDefs.LayerSpiritTop;
		DustScene ??= GD.Load<PackedScene>("res://scenes/fx/dust_puff.tscn");
		// Visual 在 Player.tscn 里摆（Sprite2D ← 贴图在编辑器里拖 PNG 替换）
		_sprite = GetNodeOrNull<Sprite2D>("Visual");
		if (_sprite == null)
		{
			_sprite = ArtBank.MakeSprite("player", GameDefs.MasterSize);
			_sprite.Name = "Visual";
			AddChild(_sprite);
		}
		_spriteBaseScale = _sprite.Scale;
	}


	/// <summary>重生 / 冒烟传送辅助：清空移动内核窗口状态（土狼 / 缓冲 / 下穿 / 翻越冷却）。</summary>
	public void ResetMover() => _mover.ResetWindows();

	/// <summary>注入地形台沿（CLIMB-001 自动上攀用），由 GameRoot 在地形建好后调用。</summary>
	public void SetLedges(List<PlatformMover.Ledge> ledges) => _mover.SetLedges(ledges);

	/// <summary>注入方块几何（蔚蓝式转角修正用），由 GameRoot 在地形建好后调用。</summary>
	public void SetGeometry(IBlockGeometry geometry) => _mover.SetGeometry(geometry);

	/// <summary>弹跳标志：由 BlockField 触碰感应置位；在 Step 之后应用（先应用会被贴地逻辑清零 Y）。</summary>
	public bool BounceRequested;

	public void Bounce() => BounceRequested = true;

	public override void _PhysicsProcess(double delta)
	{
		// 输入读取 / 内核推进 / 弹跳后发制人与白箱共用 MoverDriver（白箱手感 == 游戏手感）
		MoverDriver.StepBody(this, _mover, delta, BounceRequested);
		BounceRequested = false;   // 弹跳请求已由驱动层消费（Step 之后应用，避免被贴地清零）

		// 表现层反馈：落地挤压 / 起跳拉伸 + 脚下灰尘（纯视觉，不碰物理）
		bool groundedNow = IsOnFloor();
		if (!_wasGrounded && groundedNow) OnLanded();
		else if (_mover.JustJumped) OnJumped();
		_wasGrounded = groundedNow;

		// @trace GND-001 水平活动域锁在第一层全覆盖地板之内
		float edge = GameDefs.WorldEdge(GameDefs.MasterSize.X);
		GlobalPosition = new Vector2(Mathf.Clamp(GlobalPosition.X, -edge, edge), GlobalPosition.Y);
		// @trace AXIS-001 y-down：坠落是 y 增大
		if (GlobalPosition.Y > GameDefs.FallKillY)
		{
			GlobalPosition = new Vector2(GlobalPosition.X,
				GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);
			Velocity = Vector2.Zero;
			_mover.ResetWindows();
		}
	}

	private void OnLanded()
	{
		_sprite.Scale = new Vector2(_spriteBaseScale.X * 1.22f, _spriteBaseScale.Y * 0.78f);
		var tw = CreateTween();
		tw.TweenProperty(_sprite, "scale", _spriteBaseScale, 0.14f)
			.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		SpawnDust(1.0f);
	}

	private void OnJumped()
	{
		_sprite.Scale = new Vector2(_spriteBaseScale.X * 0.82f, _spriteBaseScale.Y * 1.18f);
		var tw = CreateTween();
		tw.TweenProperty(_sprite, "scale", _spriteBaseScale, 0.16f)
			.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		SpawnDust(0.7f);
	}

	/// <summary>脚下灰尘：优先实例化 dust_puff.tscn（CpuParticles2D one-shot），无场景时回退 Sprite2D + tween。</summary>
	private void SpawnDust(float strength)
	{
		Vector2 dustPos = new(0.0f, GameDefs.MasterSize.Y * 0.5f - 0.125f);
		if (DustScene != null)
		{
			var dust = DustScene.Instantiate<Node2D>();
			dust.Position = dustPos;
			AddChild(dust);
			if (dust is CpuParticles2D p)
			{
				p.Color = new Color(p.Color.R, p.Color.G, p.Color.B, p.Color.A * strength);
				p.Amount = Mathf.Max(1, (int)(p.Amount * strength));
				p.Emitting = true;
				// 闹钟归 SceneTree 所有：若延迟期间切了场景，dust 已随场景树释放，
				// 直接调 QueueFree 会打到已释放节点（刷怪时代高频特效会放大成高频报错）。
				GetTree().CreateTimer(p.Lifetime + 0.1).Timeout += () =>
				{
					if (IsInstanceValid(p)) p.QueueFree();
				};
			}
			else
			{
				GetTree().CreateTimer(0.3).Timeout += () =>
				{
					if (IsInstanceValid(dust)) dust.QueueFree();
				};
			}
			return;
		}
		var fallback = ArtBank.MakeBox(new Vector2(0.7f, 0.25f),
			new Color(0.90f, 0.88f, 0.82f, 0.50f * strength));
		fallback.Position = dustPos;
		AddChild(fallback);
		var tw = CreateTween().SetParallel();
		tw.TweenProperty(fallback, "scale", new Vector2(1.9f, 1.7f), 0.22f).From(Vector2.One)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tw.TweenProperty(fallback, "modulate:a", 0.0f, 0.22f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tw.Chain().TweenCallback(Callable.From(fallback.QueueFree));
	}
}
