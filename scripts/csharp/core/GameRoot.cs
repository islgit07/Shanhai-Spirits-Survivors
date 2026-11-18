using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// v0.0001 自举：注册输入 → 拉起最小世界（相机 + 地形生成 + 主角 + 相机跟随）。
/// 场景结构在 Main.tscn 里摆（节点名即契约：根 WorldRoot/{Camera2D, BlockField, Player, ...}），
/// 主角是独立场景 Player.tscn（Node2D → CharacterBody2D[Master] → CollisionShape2D）。
///
/// @trace JUMP-001 / MAP-001 / GND-001 「移动手感」和「平台地图生成」是后续所有设计的公共底座。
/// @trace MONSTER-001 v0.0007：接入怪物对象池（MonsterField）、玩家血量 HUD 与死亡处理
/// （血量归零 → 定格 DeathRestartDelay 秒 → 回开始页）；刷怪节奏留给 v0.0008 波次系统。
/// --smoke 无头验证：建图 / 落地 / 会走 / 会跳 / 会下穿；怪物链路走 --monster-smoke。
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
	private MapGenConfig _mapConfig = GD.Load<MapGenConfig>(MapGenConfig.DefaultConfigPath) ?? MapGenConfig.CreateDefault();
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

	// MONSTER-001：怪物对象池与刷怪口（预实例化，游玩中零 Instantiate）；波次系统 v0.0008 接此池。
	private MonsterField _monsterField = null!;
	public MonsterField MonsterField => _monsterField;
	// 魂魄拾取物对象池：怪物死亡原地掉落，玩家走过去捡（SaveGame.AddSouls 同一份货币）。
	private SoulField _soulField = null!;
	// 死亡处理：血量归零 → Master 发 Died → 这里定格 DeathRestartDelay 秒后回开始页（冒烟断言锚点）。
	public bool PlayerDiedSeen { get; private set; }
	private const string StartScenePath = "res://scenes/StartPage.tscn";
	private Label _hpLabel = null!;

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

		SmokeMode = SmokeFlags.Has("--smoke");

		// 场景结构在 Main.tscn 摆好（节点名即契约）；Backdrop / Player 的贴图与节点都在场景里摆
		_camera = GetNode<Camera2D>("Camera2D");
		_blocks = GetNode<BlockField>("BlockField");
		Player = GetNode<Master>("Player/CharacterBody2D");
		_spiritGroup = GetNode<SpiritFollowGroup>("SpiritFollowGroup");
		_monsterField = GetNode<MonsterField>("MonsterField");
		_soulField = GetNode<SoulField>("SoulField");
		_soulField.Bind(Player!);   // 魂魄拾取判定需要主角引用（Area2D 只认这个 body）

		// MONSTER-001：生命 HUD + 死亡处理（信号向上：Master 只发事件，处理权在根）
		Player.Died += OnPlayerDied;
		Player.HealthChanged += OnHealthChanged;

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
		BuildHpHud();   // MONSTER-001：左上角生命显示（HealthChanged 时重绘）
		GD.Print($"[BOOT] seed_ui=on seed={_currentSeed}");

		GD.Print($"[BOOT] 山海幻灵 v{ProjectSettings.GetSetting("application/config/version", "dev").AsString()} · 开始页 + 最小骨架（移动手感 + 平台地图生成）");
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
		// MONSTER-001：怪物池绑定追击目标与方块几何（怪自动吃粘液 / 弹跳块）。
		_monsterField.Bind(Player!, _blocks, _soulField);
		_companion = _spiritGroup.Primary;
		if (_chosenSpirit != null && _companion != null)
			GD.Print($"[FATE] companion spawned id={_chosenSpirit.Id} tint={_chosenSpirit.Tint} scene={SpiritPetScenePath} followers={_spiritGroup.Followers}");
		else
			GD.PushError("[FATE] 本命目录为空，无法生成随从实体");

		if (SmokeMode) BeginSmoke();

		// 仅截图自检用：--shot 时 1.5 秒后存图并退出
		if (SmokeFlags.Has("--shot"))
			CallDeferred(new StringName(nameof(CaptureShot)));
	}

	/// <summary>仅截图自检用：--shot 时存图并退出（实现与白箱共用 SmokeFlags.Capture）。</summary>
	private void CaptureShot() => SmokeFlags.Capture(this, "user://game_shot.png", "BOOT");

	private static int TimeSeed() => (int)Time.GetUnixTimeFromSystem() & 0x7FFFFFFF;

	/// <summary>种子徽章当前文本（节点可能尚未创建，做显式空值保护）。</summary>
	private string SeedUiText() => _seedLabel != null && IsInstanceValid(_seedLabel)
		? _seedLabel.Text : string.Empty;

	/// <summary>三选一领到新随从 / 合成后调用：同步场上实体（加人、换资质、重排槽位）。</summary>
	public void RefreshCompanions() => _spiritGroup?.Refresh();

	/// <summary>开局：设相机缩放、按种子建地形、把主角复位到出生点。</summary>
	private void BeginRun(int seed)
	{
		_currentSeed = seed;
		Companions.Clear();   // 当局临时：每次开局从空队伍开始，不碰局外存档
		_spiritGroup?.Refresh();   // 重开一局：清掉上一局的队友实体，本命保留
		_monsterField?.ReleaseAll();   // MONSTER-001：每局从空场开始，池实例保留复用
		_soulField?.ReleaseAll();      // 地上魂魄同样清空（捡了的不追回）
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

	// ---------------------------------------------------------------- 生命 HUD 与死亡处理（MONSTER-001）
	/// <summary>左上角生命显示：♥ = 当前血 / ♡ = 已损失血（HealthChanged 时重绘）。</summary>
	private void BuildHpHud()
	{
		var layer = new CanvasLayer { Name = "PlayerHud", Layer = 1 };
		AddChild(layer);
		var back = UiSkin.MakePanel(new Rect2(new Vector2(14f, 14f), new Vector2(150f, 34f)));
		back.Name = "HpBack";
		layer.AddChild(back);
		_hpLabel = UiSkin.MakeLabel(HpHudText(Player!.Hp), 16, UiSkin.Text);
		_hpLabel.Position = new Vector2(24f, 20f);
		layer.AddChild(_hpLabel);
	}

	private static string HpHudText(int hp)
	{
		hp = Mathf.Clamp(hp, 0, GameDefs.MasterMaxHp);
		return "生命 " + new string('♥', hp) + new string('♡', GameDefs.MasterMaxHp - hp);
	}

	private void OnHealthChanged(int newHp) => _hpLabel.Text = HpHudText(newHp);

	/// <summary>
	/// 玩家死亡：先置 PlayerDiedSeen（冒烟断言锚点），定格 DeathRestartDelay 秒让玩家看清自己倒了，
	/// 再切回开始页。定时器回调加存活防护——避免极端时序下打到已释放节点（同 SpawnDust 教训）。
	/// </summary>
	private void OnPlayerDied()
	{
		PlayerDiedSeen = true;
		GD.Print("[GAME] 玩家倒下 → 即将回开始页");
		var tree = GetTree();
		tree.CreateTimer(GameDefs.DeathRestartDelay).Timeout += () =>
		{
			if (IsInstanceValid(this)) tree.ChangeSceneToFile(StartScenePath);
		};
	}

}
