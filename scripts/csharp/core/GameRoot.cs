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
	// 随从系统第 1 块：启动时加载本命随从目录（主角养的灵兽），只做「游戏知道有哪些随从」。
	// 第 2 块：读选择页写入 SceneTree 的本命 Id，从 spirit_pet.tscn 实例化本命灵兽实体。
	// 技能 / 本命升级 / 跟随效果仍未实现，留待后续逐块加。
	private IReadOnlyList<SpiritDef> _spirits = SpiritCatalog.All;
	public IReadOnlyList<SpiritDef> Spirits => _spirits;
	private const string SpiritPetScenePath = "res://scenes/spirits/spirit_pet.tscn";
	private SpiritDef? _chosenSpirit;
	private SpiritPet? _companion;
	public SpiritDef? ChosenSpirit => _chosenSpirit;
	public SpiritPet? Companion => _companion;
	// SPIRIT-FOLLOW：场上随从实体队伍（本命 + 局内领取的队友），由 SpiritFollowGroup 统一生成与跟随。
	private SpiritFollowGroup _spiritGroup = null!;
	public SpiritFollowGroup SpiritGroup => _spiritGroup;
	private int _currentSeed;
	private Label _seedLabel = null!;
	// 随从系统第 3 块：单槽存档 + 升级树目录（数据底座，冒烟里也要验证）。
	private SaveData _save = null!;
	public SaveData Save => _save;
	// 局内随从界面（第四块 UI）：当局临时队伍，退出即清、绝不写存档；
	// 调试 HUD / 三选一弹窗 / ESC 双栏面板都读这同一份状态。
	public RunRoster Companions { get; } = new();

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
		_spiritGroup = GetNode<SpiritFollowGroup>("SpiritFollowGroup");

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

		GD.Print("[BOOT] 山海幻灵 v0.0005 · 开始页 + 最小骨架（移动手感 + 平台地图生成）");
		// 随从系统第 1 块：打印已发现的本命随从目录（定义卡自动扫描，加卡零代码）。
		_spirits = SpiritCatalog.All;
		GD.Print($"[BOOT] spirits={_spirits.Count} ids=[{string.Join(",", _spirits.Select(s => s.Id))}]");

		// 随从系统第 3 块：读单槽存档 + 校验升级树目录（只读，不在游戏内改魂魄/加点）。
		_save = SaveGame.LoadOrCreate();
		bool treesValid = _spirits.Count >= 3
			&& _spirits.All(s => UpgradeCatalog.Tree(s.Id).IsValid(out _));
		GD.Print($"[BOOT] save souls={_save.Souls} growth=[{string.Join(",", _spirits.Select(s => $"{s.Id}:{SaveGame.Growth(s.Id).Value(GrowthStat.Level)}"))}] trees_valid={treesValid}");

		// 随从系统第 2 块：读取选择页写入的本命 Id，从 tscn 实例化本命灵兽。
		// 直接以 Main.tscn 起（如 --smoke 单场景测试）时无 meta：兜底取目录第一只，保证实体仍存在。
		string chosenId = GetTree().HasMeta(SpiritCatalog.ChosenIdMeta)
			? GetTree().GetMeta(SpiritCatalog.ChosenIdMeta).AsString() : "";
		_chosenSpirit = SpiritCatalog.Find(chosenId) ?? _spirits.FirstOrDefault();
		// SPIRIT-FOLLOW：实体生成与跟随交给 SpiritFollowGroup（本命 = 槽位 0，队友按领取顺序排在后面）。
		_spiritGroup.Bind(Player!, _blocks);   // 地形网格供随从 BFS 防卡死寻路
		_spiritGroup.Refresh();
		_companion = _spiritGroup.Primary;
		if (_chosenSpirit != null && _companion != null)
			GD.Print($"[FATE] companion spawned id={_chosenSpirit.Id} tint={_chosenSpirit.Tint} scene={SpiritPetScenePath} followers={_spiritGroup.Followers}");
		else
			GD.PushError("[FATE] 本命目录为空，无法生成随从实体");

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
	/// <summary>三选一领到新随从 / 合成后调用：同步场上实体（加人、换资质、重排槽位）。</summary>
	public void RefreshCompanions() => _spiritGroup?.Refresh();

	private void BeginRun(int seed)
	{
		_currentSeed = seed;
		Companions.Clear();   // 当局临时：每次开局从空队伍开始，不碰局外存档
		_spiritGroup?.Refresh();   // 重开一局：清掉上一局的队友实体，本命保留
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

}
