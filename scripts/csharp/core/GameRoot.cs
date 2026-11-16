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
	// MAP-TYPE：地形生成策略口。默认地形；以后换火山/雪山只需替换这个实例，其余代码不动。
	private IMapGenerator _mapGenerator = new DefaultMapGenerator();
	public IMapGenerator MapGenerator { get => _mapGenerator; set => _mapGenerator = value; }
	// 默认地形配置卡片：Inspector 里改 resources/maps/default.tres 的数值即可生效；加载失败兜底代码默认。
	private const string DefaultMapConfigPath = "res://resources/maps/default.tres";
	private MapGenConfig _mapConfig = GD.Load<MapGenConfig>(DefaultMapConfigPath) ?? MapGenConfig.CreateDefault();
	public MapGenConfig MapConfig { get => _mapConfig; set => _mapConfig = value; }
	private int _currentSeed;
	private Label _seedLabel = null!;

	public Master? Player;
	public int PlatformCount;
	public bool GroundContinuous;
	public bool PlayerGrounded;
	public float VerticalClearRatio;   // MAP-005：上下隔开 2 格的方块占比
	public int VerticalClearCount;
	public int VerticalClearTotal;
	public int OneWayCount;
	public int SingleBlockCount;
	public float SingleBlockRatio;
	public int MaxReachableRow;
	public bool HasPathToTop;
	public int PathCount;
	public int ReachablePathCount;
	// MAP-009 粘液块统计（由 BlockField 读取，冒烟断言用）
	public int SlimeCount;
	public int SlimeGroupedCount;
	// BUGFIX-FRAGILE 脆弱木板统计（由 BlockField 读取，冒烟断言用）
	public int FragileCount;
	public int FragileTouchWiredCount;

	private float _smokeTimer;
	private bool _jumpInjected, _releaseInjected;
	private bool _dropTeleported, _dropJumpInjected, _dropReleased, _dropSkipped;
	private float _dropPlatY;   // 传送后角色中心 y（断言基准）
	private float _smokeMaxY;   // 下穿阶段（2.2~2.6s）角色到达的最低点：曾低于台面即证明穿层
	private Vector2 _smokeStartPos;
	private Vector2 _smokeMovedPos;   // 传送前的位置（player_moved 断言用，不受传送干扰）
	private float _smokeMinY;
	// MAP-009 冒烟阶段（3.2~4.4s）：传送到粘液块 → 测水平减速 → 按跳测「立刻被黏回」
	private bool _slimeTeleported, _slimeSkipped, _slimeMoveReleased, _slimeJumpInjected, _slimeJumpReleased;
	private bool _slimeLandedChecked, _slimeLanded;
	private float _slimePlatY, _slimeApexY, _slimeGroundSpeed;
	// BUGFIX-FRAGILE 冒烟阶段（4.5~7.0s）：传送到脆弱块上方 → 自由落下 → 断言闪时可站、闪完才碎
	private bool _fragileTeleported, _fragileSkipped;
	private int _fragileBrokenBefore, _fragileBlinkStartBefore, _fragileBlinkFinishBefore, _fragileBlocksBefore;
	private float _fragilePlatY;
	// FRAGILE-BLINK：闪烁期间玩家仍站在同一块上；闪烁结束后该块才判碎释放
	private bool _fragileBlinkSeen, _fragileStandChecked, _fragileStoodDuringBlink;

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
		_seedLabel = UiSkin.MakeSeedBadge(this, _currentSeed);   // 开发用：常驻显示当前地图种子，便于复现
		GD.Print($"[BOOT] seed_ui=on seed={_currentSeed}");

		GD.Print("[BOOT] 山海幻灵 v0.0004 · 开始页 + 最小骨架（移动手感 + 平台地图生成）");
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

	/// <summary>种子徽章当前文本（节点可能尚未创建，做显式空值保护）。</summary>
	private string SeedUiText() => _seedLabel != null && IsInstanceValid(_seedLabel)
		? _seedLabel.Text : string.Empty;


	/// <summary>开局：设相机缩放、按种子建地形、把主角复位到出生点。</summary>
	private void BeginRun(int seed)
	{
		_currentSeed = seed;
		float z = ZoomValue();
		_camera.Zoom = new Vector2(z, z);
		_camera.Enabled = true;

		_mapGenerator.Generate(_blocks, seed, _mapConfig);
		PlatformCount = _blocks.BlockCount;
		GroundContinuous = _blocks.IsGroundContinuous();
		VerticalClearRatio = _blocks.VerticalClearRatio;
		VerticalClearCount = _blocks.VerticalClearCount;
		VerticalClearTotal = _blocks.VerticalClearTotal;
		OneWayCount = _blocks.OneWayCount;
		SingleBlockCount = _blocks.SingleBlockCount;
		SingleBlockRatio = _blocks.SingleBlockRatio;
		MaxReachableRow = _blocks.MaxReachableRow;
		HasPathToTop = _blocks.HasPathToTop;
		PathCount = _blocks.PathCount;
		ReachablePathCount = _blocks.ReachablePathCount;
		SlimeCount = _blocks.SlimeCount;
		SlimeGroupedCount = _blocks.SlimeGroupedCount;
		FragileCount = _blocks.FragileCount;
		FragileTouchWiredCount = _blocks.FragileTouchWiredCount;

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

	/// <summary>MAP-009 冒烟测试用：把主角放到第一块可站立的粘液块上，供减速 / 黏跳验证。</summary>
	private bool SmokeTeleportToSlime()
	{
		if (Player == null || !IsInstanceValid(Player)) return false;
		if (!_blocks.GetFirstSlime(out float x, out float y)) return false;
		Player.GlobalPosition = new Vector2(x, y - GameDefs.MasterSize.Y * 0.5f - 0.05f);
		Player.Velocity = Vector2.Zero;
		Player.ResetMover();
		return true;
	}

	/// <summary>BUGFIX-FRAGILE 冒烟测试用：把主角放到第一块脆弱木板上方 0.6 格，供自由落下踩碎验证。</summary>
	private bool SmokeTeleportToFragile()
	{
		if (Player == null || !IsInstanceValid(Player)) return false;
		if (!_blocks.GetFirstFragile(out float x, out float y)) return false;
		_fragilePlatY = y;   // 冒烟断言「闪烁期间还站在同一块上」的台面基准
		Player.GlobalPosition = new Vector2(x, y - GameDefs.MasterSize.Y * 0.5f - 0.6f);
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
		// @trace MAP-009 粘液块运行时验证：传送到粘液块 → 短按右移测地面顶速 → 按跳测黏回。
		// 全程不注入水平输入到跳跃阶段，避免角色走出 1×1 粘液块导致断言失真。
		if (_smokeTimer > 3.2f && !_slimeTeleported)
		{
			_slimeTeleported = true;
			if (SmokeTeleportToSlime() && Player != null && IsInstanceValid(Player))
			{
				_slimePlatY = Player.GlobalPosition.Y;
				_slimeApexY = _slimePlatY;
				Input.ActionPress("move_right");
				GD.Print("[SMOKE] 传送到粘液块 + move_right 注入");
			}
			else
			{
				_slimeSkipped = true;
				GD.Print("[SMOKE] 粘液块验证跳过（传送失败）");
			}
		}
		if (_slimeTeleported && !_slimeSkipped && !_slimeMoveReleased && _smokeTimer > 3.32f)
		{
			_slimeMoveReleased = true;
			_slimeGroundSpeed = Player != null && IsInstanceValid(Player) ? Player.Velocity.X : 0f;
			Input.ActionRelease("move_right");
		}
		if (_slimeTeleported && !_slimeSkipped && !_slimeJumpInjected && _smokeTimer > 3.4f)
		{
			_slimeJumpInjected = true;
			Input.ActionPress("jump");
			GD.Print("[SMOKE] 粘液块：jump 注入");
		}
		if (_slimeJumpInjected && !_slimeJumpReleased && _smokeTimer > 3.55f)
		{
			_slimeJumpReleased = true;
			Input.ActionRelease("jump");
		}
		if (_slimeJumpInjected && !_slimeSkipped && _smokeTimer <= 4.0f
			&& Player != null && IsInstanceValid(Player))
			_slimeApexY = Mathf.Min(_slimeApexY, Player.GlobalPosition.Y);
		if (_slimeJumpInjected && !_slimeSkipped && !_slimeLandedChecked && _smokeTimer > 4.0f)
		{
			_slimeLandedChecked = true;
			_slimeLanded = Player != null && IsInstanceValid(Player)
				&& Player.IsOnFloor()
				&& Mathf.Abs(Player.GlobalPosition.Y - _slimePlatY) < 0.3f;
		}
		// @trace BUGFIX-FRAGILE 脆弱木板运行时验证：传送到可踩的脆弱块上方 → 自由落下 → 该块必须被踩碎。
		// 不注入任何输入，靠真实物理让角色进入顶面感应区，避免「传送即判定」的假阳性。
		if (_smokeTimer > 4.5f && !_fragileTeleported)
		{
			_fragileTeleported = true;
			_fragileBrokenBefore = _blocks.FragileBrokenCount;
			_fragileBlinkStartBefore = _blocks.FragileBlinkStartedCount;
			_fragileBlinkFinishBefore = _blocks.FragileBlinkFinishedCount;
			_fragileBlocksBefore = _blocks.BlockCount;
			if (SmokeTeleportToFragile())
				GD.Print("[SMOKE] 传送到脆弱木板上方 + 自由落下");
			else
			{
				_fragileSkipped = true;
				GD.Print("[SMOKE] 脆弱木板验证跳过（传送失败）");
			}
		}
		// FRAGILE-BLINK：闪烁启动当帧取样（尚未结束、尚未判碎），证明「踩上不是立刻消失」。
		if (!_fragileSkipped && !_fragileBlinkSeen
			&& _blocks.FragileBlinkStartedCount > _fragileBlinkStartBefore)
			_fragileBlinkSeen = _blocks.FragileBlinkFinishedCount == _fragileBlinkFinishBefore
								&& _blocks.FragileBrokenCount == _fragileBrokenBefore;

		// 闪烁中（约 5.1s）取样：方块仍在实体集合里、玩家脚底仍贴着同一块台面。
		if (!_fragileSkipped && !_fragileStandChecked && _smokeTimer > 5.1f)
		{
			_fragileStandChecked = true;
			_fragileStoodDuringBlink = _fragileBlinkSeen
				&& Player != null && IsInstanceValid(Player)
				&& Player.IsOnFloor()
				&& Mathf.Abs(Player.GlobalPosition.Y + GameDefs.MasterSize.Y * 0.5f - _fragilePlatY) < 0.35f
				&& _blocks.FragileBlinkFinishedCount == _fragileBlinkFinishBefore
				&& _blocks.FragileBrokenCount == _fragileBrokenBefore
				&& _blocks.BlockCount == _fragileBlocksBefore;
		}

		// 7.0s 收尾：给 1.5 秒闪烁留足余量，再验「闪完才判碎 + 才释放」
		if (_smokeTimer > 7.0f) FinishSmoke();
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
		// MAP-009：纯逻辑黏跳积分 + 粘液目标顶速（确定性断言，与运行时结果互为佐证）
		MoveTuning tuning = MoveTuning.GameDefaults();
		PlatformMover.SimulateSlimeHop(tuning, 1.0f / 60.0f,
			out float slimeApexSim, out bool slimeSnapped);
		float slimeTarget = PlatformMover.ComputeSlimeTargetSpeed(1.0f, tuning);
		float slimeRise = _slimePlatY - _slimeApexY;   // 真实运行时最高离地高度（y-down 取反）
		var assertions = new Dictionary<string, bool>
		{
			["platforms_built"] = PlatformCount > 0,
			["ground_continuous"] = GroundContinuous,
			["player_grounded"] = PlayerGrounded,
			["player_moved"] = Mathf.Abs(_smokeMovedPos.X - _smokeStartPos.X) > 2.0f,
			["player_jumped"] = _smokeMinY < _smokeStartPos.Y - 0.5f,
			["player_drop_through"] = _dropSkipped || _smokeMaxY > _dropPlatY + 0.5f,
			["climb_logic_ok"] = climbHit && climbMissDir && climbMissFar,
			["vertical_clear_80pct"] = VerticalClearRatio >= GameDefs.BlockVerticalClearRatio,
			["few_single_blocks"] = SingleBlockRatio <= GameDefs.BlockSingleRatioMax,
			["path_to_top_ok"] = HasPathToTop,
			["multi_path_ok"] = ReachablePathCount >= GameDefs.BackbonePathMin,
			["seed_ui_ok"] = SeedUiText() == UiSkin.SeedText(_currentSeed),
			// MAP-009：数量 / 分组 / 减速 / 黏跳 / 黏回
			["slime_blocks_ok"] = SlimeCount >= 1 && SlimeCount <= GameDefs.BlockSlimeMaxCount
								  && SlimeGroupedCount == SlimeCount,
			["slime_slow_ok"] = !_slimeSkipped
								&& _slimeGroundSpeed <= GameDefs.MasterMoveSpeed * GameDefs.SlimeSpeedMul + 0.25f
								&& Mathf.IsEqualApprox(slimeTarget, tuning.MoveSpeed * tuning.SlimeSpeedMul)
								&& slimeTarget < tuning.MoveSpeed * 0.5f,
			["slime_no_jump_ok"] = !_slimeSkipped && _slimeLanded
								   && slimeRise > 0.02f && slimeRise <= GameDefs.SlimeHopApexMax,
			["slime_snap_ok"] = slimeApexSim > 0.0f && slimeApexSim <= GameDefs.SlimeHopApexMax
								&& slimeSnapped,
			// BUGFIX-FRAGILE：接线完整 + 闪烁结束后真碎 + 方块总数确实减少
			["fragile_break_ok"] = !_fragileSkipped
								   && FragileCount > 0
								   && FragileTouchWiredCount == FragileCount
								   && _blocks.FragileBrokenCount > _fragileBrokenBefore
								   && _blocks.BlockCount < _fragileBlocksBefore,
			// FRAGILE-BLINK：踩上只启动闪烁（1.5 秒 / 3 次白闪，纯颜色无缩放），此时还没碎
			["fragile_blink_ok"] = _fragileBlinkSeen
								   && Mathf.IsEqualApprox(GameDefs.FragileBlinkDuration, 1.5f)
								   && GameDefs.FragileBlinkCycles == 3,
			// 闪烁期间方块仍是实体、玩家仍站在上面
			["fragile_standable_during_blink"] = _fragileStoodDuringBlink,
			// 闪烁结束后才判碎、才释放（不是立刻消失，也不是永远留着）
			["fragile_blink_finished_ok"] = _fragileBlinkSeen
											&& _blocks.FragileBlinkStartedCount > _fragileBlinkStartBefore
											&& _blocks.FragileBlinkFinishedCount > _fragileBlinkFinishBefore,
		};
		bool ok = assertions.Values.All(v => v);
		var report = new Dictionary<string, object>
		{
			["project"] = "山海幻灵",
			["version"] = "0.0004",
			["mode"] = "headless-smoke",
			["result"] = ok ? "PASS" : "FAIL",
			["platforms"] = PlatformCount,
			["ground_continuous"] = GroundContinuous,
			["vertical_clear"] = $"{VerticalClearCount}/{VerticalClearTotal}",
			["vertical_clear_ratio"] = Math.Round(VerticalClearRatio, 4),
			["vertical_clear_target"] = GameDefs.BlockVerticalClearRatio,
			["single_blocks"] = $"{SingleBlockCount}/{VerticalClearTotal}",
			["single_block_ratio"] = Math.Round(SingleBlockRatio, 4),
			["single_block_max"] = GameDefs.BlockSingleRatioMax,
			["max_reachable_row"] = MaxReachableRow,
			["path_to_top"] = HasPathToTop,
			["paths"] = $"{ReachablePathCount}/{PathCount}",
			["seed"] = _currentSeed,
			["seed_ui"] = SeedUiText(),
			["oneway_blocks"] = OneWayCount,
			// MAP-009 粘液块证据
			["slime_blocks"] = $"{SlimeCount}/{GameDefs.BlockSlimeMaxCount}",
			["slime_grouped"] = SlimeGroupedCount,
			["slime_weight_per_run"] = GameDefs.BlockWeightSlime,
			["slime_ground_speed"] = Math.Round(_slimeGroundSpeed, 3),
			["slime_normal_top_speed"] = Math.Round(tuning.MoveSpeed, 3),
			["slime_top_speed"] = Math.Round(slimeTarget, 3),
			["slime_hop_rise"] = Math.Round(slimeRise, 3),
			["slime_hop_rise_sim"] = Math.Round(slimeApexSim, 3),
			["slime_hop_rise_max"] = GameDefs.SlimeHopApexMax,
			["slime_landed_back"] = _slimeLanded,
			// BUGFIX-FRAGILE 脆弱木板证据
			["fragile_blocks"] = FragileCount,
			["fragile_touch_wired"] = FragileTouchWiredCount,
			["fragile_broken"] = _blocks.FragileBrokenCount,
			["fragile_blocks_left"] = _blocks.BlockCount,
			["fragile_blink_seen"] = _fragileBlinkSeen,
			["fragile_blink_started"] = _blocks.FragileBlinkStartedCount,
			["fragile_blink_finished"] = _blocks.FragileBlinkFinishedCount,
			["fragile_blink_duration"] = GameDefs.FragileBlinkDuration,
			["fragile_blink_cycles"] = GameDefs.FragileBlinkCycles,
			["fragile_standable_during_blink"] = _fragileStoodDuringBlink,
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
