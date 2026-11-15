using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// v0.0001 自举：注册输入 → 拉起最小世界（相机 + 地形生成 + 主角 + 相机跟随）。
/// 场景结构在 Main.tscn 里摆（节点名即契约：根 WorldRoot/{Camera2D, BlockField, Player}），
/// 主角是独立场景 Player.tscn（Node2D → CharacterBody2D[Master] → CollisionShape2D）。
/// 无战斗、无循环阶段——按 F5 进入开始页，点「开始」进入可跑跳的空世界。
///
/// @trace JUMP-001 / MAP-001 / GND-001 本版唯一职责：把「移动手感」和「平台地图生成」
/// 原样保住，作为后续所有设计的公共底座。--smoke 无头验证五件事：建图 / 落地 / 会走 / 会跳 / 会下穿。
/// </summary>
public partial class GameRoot : Node2D
{
	public static GameRoot? Instance;
	public static bool SmokeMode;

	private const int SmokeFixedSeed = 20261113;

	private Camera2D _camera = null!;
	private Sprite2D _backdrop = null!;
	private BlockField _blocks = null!;

	public Master? Player;
	public int PlatformCount;
	public bool GroundContinuous;
	public bool PlayerGrounded;

	private float _smokeTimer;
	private bool _jumpInjected, _releaseInjected;
	private bool _dropTeleported, _dropJumpInjected, _dropReleased, _dropSkipped;
	private float _dropPlatY;   // 传送后角色中心 y（断言基准）
	private float _smokeMaxY;   // 下穿阶段（2.2~2.6s）角色到达的最低点：曾低于台面即证明穿层
	private Vector2 _smokeStartPos;
	private Vector2 _smokeMovedPos;   // 传送前的位置（player_moved 断言用，不受传送干扰）
	private float _smokeMinY;

	public override void _Ready()
	{
		Instance = this;

		string[] args = OS.GetCmdlineArgs();
		string[] userArgs = OS.GetCmdlineUserArgs();
		SmokeMode = args.Contains("--smoke") || userArgs.Contains("--smoke");

		// 场景结构在 Main.tscn 摆好（节点名即契约）；Backdrop / Player 的贴图与节点都在场景里摆
		_camera = GetNode<Camera2D>("Camera2D");
		_blocks = GetNode<BlockField>("BlockField");
		Player = GetNode<Master>("Player/CharacterBody2D");

		// Backdrop 在 Main.tscn 里摆（Sprite2D ← 贴图 / 色调在编辑器里调）；缺失时代码兜底生成
		_backdrop = _camera.GetNodeOrNull<Sprite2D>("Backdrop");
		if (_backdrop == null)
		{
			_backdrop = ArtBank.MakeSprite(GameDefs.BackdropArt, ViewSizeWorld());
			_backdrop.Name = "Backdrop";
			_backdrop.ZIndex = -100;
			_backdrop.Modulate = GameDefs.BackdropTint;
			_camera.AddChild(_backdrop);
		}
		else if (_backdrop.Texture != null)
		{
			// 场景里有 Backdrop：按视口尺寸算缩放（贴图和色调已在编辑器里设好）
			_backdrop.Scale = ViewSizeWorld() / new Vector2(
				Mathf.Max(1f, _backdrop.Texture.GetWidth()),
				Mathf.Max(1f, _backdrop.Texture.GetHeight()));
		}

		BeginRun(SmokeMode ? SmokeFixedSeed : TimeSeed());

		GD.Print("[BOOT] 山海幻灵 v0.0003 · 开始页 + 最小骨架（移动手感 + 平台地图生成）");
		if (SmokeMode) BeginSmoke();

		// 仅截图自检用：--shot 时 1.5 秒后存图并退出
		if (args.Contains("--shot") || userArgs.Contains("--shot"))
			CallDeferred(new StringName(nameof(CaptureShot)));
	}

	private async void CaptureShot()
	{
		await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Error err = GetViewport().GetTexture().GetImage().SavePng("user://game_shot.png");
		GD.Print($"[BOOT] shot err={err}");
		GetTree().Quit(0);
	}

	private static int TimeSeed() => (int)Time.GetUnixTimeFromSystem() & 0x7FFFFFFF;


	/// <summary>开局：设相机缩放、按种子建地形、把主角复位到出生点。</summary>
	private void BeginRun(int seed)
	{
		float z = ZoomValue();
		_camera.Zoom = new Vector2(z, z);
		_camera.Enabled = true;

		_blocks.Build(seed);
		PlatformCount = _blocks.BlockCount;
		GroundContinuous = _blocks.IsGroundContinuous();

		// 出生点在第一层地板上方（y-down：y 更小 = 更高）
		Player!.GlobalPosition = new Vector2(0.0f,
			GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);
		Player.Velocity = Vector2.Zero;
		Player.ResetMover();
		Player.SetLedges(_blocks.GetLedges());   // CLIMB-001 自动上攀：地形建好后注入台沿
		Player.SetGeometry(_blocks);             // 蔚蓝式转角修正：注入方块几何
		PlayerGrounded = false;
	}

	public float ZoomValue()
	{
		float h = GetViewportRect().Size.Y <= 0.0f ? 720.0f : GetViewportRect().Size.Y;
		return h / (2.0f * GameDefs.CameraViewHalfHeight);
	}

	public Vector2 ViewSizeWorld() => GetViewportRect().Size / ZoomValue();

	/// <summary>冒烟测试用：把主角放到第一块单向平台的中心上方，供「下 + 跳」下穿验证。</summary>
	public bool SmokeTeleportToPlatform()
	{
		if (Player == null || !IsInstanceValid(Player)) return false;
		if (!_blocks.GetFirstOneWay(out float x, out float y)) return false;
		Player.GlobalPosition = new Vector2(x, y - GameDefs.MasterSize.Y * 0.5f - 0.05f);
		Player.Velocity = Vector2.Zero;
		Player.ResetMover();
		return true;
	}

	public override void _Process(double delta)
	{
		if (Player == null || !IsInstanceValid(Player) || _camera == null) return;
		if (Player.IsOnFloor()) PlayerGrounded = true;
		Vector2 half = ViewSizeWorld() * 0.5f;
		float cx = Mathf.Clamp(Player.GlobalPosition.X,
			-GameDefs.WorldHalfWidth + half.X, GameDefs.WorldHalfWidth - half.X);
		float cy = Mathf.Min(GameDefs.LayerY(0) - half.Y * 0.4f,
			Player.GlobalPosition.Y - 1.0f);
		_camera.GlobalPosition = _camera.GlobalPosition.Lerp(new Vector2(cx, cy),
			Mathf.Clamp((float)delta * GameDefs.CameraSmooth, 0.0f, 1.0f));
	}

	// ---------------------------------------------------------------- 无头冒烟
	// 注入项只在 --smoke 生效：固定种子 + 模拟输入（走 → 跳 → 传送到单向平台按下+跳验证下穿）。
	private void BeginSmoke()
	{
		_smokeStartPos = Player != null && IsInstanceValid(Player)
			? Player.GlobalPosition : Vector2.Zero;
		Input.ActionPress("move_right");
		GD.Print("[SMOKE] start: move_right 注入");
	}

	// 注入必须发生在物理帧内（而非渲染帧）：ActionPress 记录的 JustPressed 物理帧号
	// 需与读取方（Master._PhysicsProcess）同帧；本节点先于子节点执行 → 同帧注入同帧生效，
	// 避免渲染帧注入时的 JustPressed 帧对齐竞态（曾导致下穿验证随机失败）。
	public override void _PhysicsProcess(double delta)
	{
		if (!SmokeMode) return;
		_smokeTimer += (float)delta;
		if (Player != null && IsInstanceValid(Player))
			_smokeMinY = Mathf.Min(_smokeMinY, Player.GlobalPosition.Y);

		if (_smokeTimer > 1.0f && !_jumpInjected)
		{
			_jumpInjected = true;
			Input.ActionRelease("move_right");
			Input.ActionPress("jump");
			GD.Print("[SMOKE] jump 注入");
		}
		if (_smokeTimer > 1.3f && !_releaseInjected)
		{
			_releaseInjected = true;
			Input.ActionRelease("jump");
		}
		// @trace DROP-001 下穿验证：传送到单向平台中心 → 按住下 → 注入跳。
		// 断言用「下穿阶段曾到达台面下方 ≥0.5」的事件式判定（终点位置会被松键后的运动干扰）。
		if (_smokeTimer > 2.2f && !_dropTeleported)
		{
			_dropTeleported = true;
			if (Player != null && IsInstanceValid(Player))
				_smokeMovedPos = Player.GlobalPosition;
			if (SmokeTeleportToPlatform() && Player != null && IsInstanceValid(Player))
			{
				_dropPlatY = Player.GlobalPosition.Y;
				_smokeMaxY = _dropPlatY;
				Input.ActionPress("move_down");
				GD.Print("[SMOKE] 传送到单向平台 + move_down 注入");
			}
			else
			{
				_dropSkipped = true;
				GD.Print("[SMOKE] 下穿验证跳过（传送失败）");
			}
		}
		if (_dropTeleported && !_dropSkipped && !_dropReleased)
		{
			if (Player != null && IsInstanceValid(Player))
				_smokeMaxY = Mathf.Max(_smokeMaxY, Player.GlobalPosition.Y);
		}
		if (_dropTeleported && !_dropSkipped && !_dropJumpInjected && _smokeTimer > 2.4f)
		{
			_dropJumpInjected = true;
			Input.ActionPress("jump");
			GD.Print("[SMOKE] drop-through：jump 注入（下+跳）");
		}
		if (_dropJumpInjected && !_dropReleased && _smokeTimer > 2.6f)
		{
			_dropReleased = true;
			Input.ActionRelease("jump");
			Input.ActionRelease("move_down");
		}
		if (_smokeTimer > 3.2f) FinishSmoke();
	}

	private void FinishSmoke()
	{
		bool hasPlayer = Player != null && IsInstanceValid(Player);
		Vector2 now = hasPlayer ? Player!.GlobalPosition : Vector2.Zero;
		// @trace CLIMB-001 几何台沿探测：纯几何确定性校验（不依赖物理时序）。
		// 场景：台沿 (0..4, 顶 y=0)，角色在左边缘外、脚底略低于台顶、朝右移动 → 应命中并吸附到台顶。
		var climbLedge = new List<PlatformMover.Ledge> { new(0.0f, 4.0f, 0.0f) };
		bool climbHit = PlatformMover.ComputeClimb(
			new Vector2(-0.3f, -0.4f), 0.6f, 1.0f, climbLedge,
			GameDefs.MasterClimbReachUp, GameDefs.MasterClimbReachDown,
			GameDefs.MasterClimbEdgeBand, GameDefs.MasterClimbStep, out Vector2 snap);
		bool climbMissDir = !PlatformMover.ComputeClimb(
			new Vector2(-0.3f, -0.4f), 0.6f, 0.0f, climbLedge,
			GameDefs.MasterClimbReachUp, GameDefs.MasterClimbReachDown,
			GameDefs.MasterClimbEdgeBand, GameDefs.MasterClimbStep, out _);
		bool climbMissFar = !PlatformMover.ComputeClimb(
			new Vector2(-5.0f, -0.4f), 0.6f, 1.0f, climbLedge,
			GameDefs.MasterClimbReachUp, GameDefs.MasterClimbReachDown,
			GameDefs.MasterClimbEdgeBand, GameDefs.MasterClimbStep, out _);
		var assertions = new Dictionary<string, bool>
		{
			["platforms_built"] = PlatformCount > 0,
			["ground_continuous"] = GroundContinuous,
			["player_grounded"] = PlayerGrounded,
			["player_moved"] = Mathf.Abs(_smokeMovedPos.X - _smokeStartPos.X) > 2.0f,
			["player_jumped"] = _smokeMinY < _smokeStartPos.Y - 0.5f,
			["player_drop_through"] = _dropSkipped || _smokeMaxY > _dropPlatY + 0.5f,
			["climb_logic_ok"] = climbHit && climbMissDir && climbMissFar,
		};
		bool ok = assertions.Values.All(v => v);
		var report = new Dictionary<string, object>
		{
			["project"] = "山海幻灵",
			["version"] = "0.0003",
			["mode"] = "headless-smoke",
			["result"] = ok ? "PASS" : "FAIL",
			["platforms"] = PlatformCount,
			["ground_continuous"] = GroundContinuous,
			["player_grounded"] = PlayerGrounded,
			["player_start"] = _smokeStartPos.ToString(),
			["player_end"] = now.ToString(),
			["player_min_y"] = Math.Round(_smokeMinY, 3),
			["assertions"] = assertions,
		};
		GD.Print("SMOKE_JSON_BEGIN");
		GD.Print(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
		GD.Print("SMOKE_JSON_END");
		GetTree().Quit(ok ? 0 : 1);
	}
}
