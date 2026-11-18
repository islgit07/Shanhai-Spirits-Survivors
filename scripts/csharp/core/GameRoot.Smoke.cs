using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// GameRoot 的 --smoke 无头冒烟 partial 部分：固定种子、输入注入时序、传送辅助与 JSON 断言报告。
/// 本文件与 GameRoot.cs 是同一个类（partial），私有成员互通；GameRoot.cs 只保留自举 + 相机 + 场景装配。
/// 注入必须发生在物理帧内（见 _PhysicsProcess 头部注释）；断言只增不减，报告键变更需同步 tools/check_project.py。
/// </summary>
public partial class GameRoot
{
    // ---------------------------------------------------------------- 冒烟状态
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
	// BOUNCE-JELLY 冒烟阶段（7.2~8.0s）：传送到弹跳块上方 → 自由落下 → 断言蹦蹦床动画与弹射都触发
	private bool _bounceTeleported, _bounceSkipped, _bounceJellySeen;
	private int _bounceJellyBefore;
	private float _bounceStartY, _bounceMinY;

	/// <summary>冒烟测试用：把主角放到第一块单向平台的中心上方，供「下 + 跳」下穿验证。</summary>
	private bool SmokeTeleportToPlatform()
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

	/// <summary>BOUNCE-JELLY 冒烟测试用：把主角放到第一块弹跳块上方 0.6 格，靠自由落下触发弹射与动画。</summary>
	private bool SmokeTeleportToBounce()
	{
		if (Player == null || !IsInstanceValid(Player)) return false;
		if (!_blocks.GetFirstBounce(out float x, out float y)) return false;
		Player.GlobalPosition = new Vector2(x, y - GameDefs.MasterSize.Y * 0.5f - 0.6f);
		Player.Velocity = Vector2.Zero;
		Player.ResetMover();
		return true;
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

		// @trace BOUNCE-JELLY：传送到弹跳块上方 → 真实落下触发 Jelly + 弹射，采样上升高度。
		if (_smokeTimer > 7.2f && !_bounceTeleported)
		{
			_bounceTeleported = true;
			_bounceJellyBefore = _blocks.BounceJellyStartedCount;
			if (SmokeTeleportToBounce())
			{
				// 基准取传送之后：否则会把「瞬移上升」误算成弹射高度
				_bounceStartY = Player != null && IsInstanceValid(Player) ? Player.GlobalPosition.Y : 0f;
				_bounceMinY = _bounceStartY;
				GD.Print("[SMOKE] 传送到弹跳块上方 + 自由落下");
			}
			else
			{
				_bounceSkipped = true;
				GD.Print("[SMOKE] 弹跳块验证跳过（传送失败）");
			}
		}
		if (_bounceTeleported && !_bounceSkipped && Player != null && IsInstanceValid(Player))
			_bounceMinY = Mathf.Min(_bounceMinY, Player.GlobalPosition.Y);
		if (!_bounceSkipped && !_bounceJellySeen && _blocks.BounceJellyStartedCount > _bounceJellyBefore)
			_bounceJellySeen = true;

		// 8.0s 收尾：闪烁与弹跳两段运行时证据都留足时间
		if (_smokeTimer > 8.0f) FinishSmoke();
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
			["monster_field_ready"] = _monsterField.PoolSize == GameDefs.MonsterPoolSize && _monsterField.Active.Count == 0,
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
			// BOUNCE-JELLY：真实落下触发蹦蹦床动画，且玩家确实被弹起（跳过传送时不假阴性）
			["bounce_jelly_ok"] = _bounceSkipped
								  || (_bounceJellySeen && _bounceStartY - _bounceMinY > 1.0f),
			// 随从系统第 1 块：目录含烛龙/饕餮/穷奇三张定义卡，Id 唯一且有序、身份字段非空
			["spirits_catalog_ok"] = _spirits.Count >= 3
									 && SpiritCatalog.Find("zhulong") != null
									 && SpiritCatalog.Find("taotie") != null
									 && SpiritCatalog.Find("qiongqi") != null
									 && _spirits.All(s => !string.IsNullOrWhiteSpace(s.Id)
														  && !string.IsNullOrWhiteSpace(s.DisplayName)
														  && !string.IsNullOrWhiteSpace(s.Lore))
									 && _spirits.Select(s => s.Id).Distinct().Count() == _spirits.Count
									 && _spirits.Select(s => s.Id)
												 .SequenceEqual(_spirits.Select(s => s.Id)
																		.OrderBy(id => id, StringComparer.Ordinal)),
			// 随从系统第 2 块：三张卡各有互异的占位色（换色区分随从）
			["spirits_tints_ok"] = _spirits.Count >= 3
								   && _spirits.All(s => s.Tint.A > 0.99f)
								   && _spirits.Select(s => s.Tint.ToHtml(false))
											  .Distinct().Count() == _spirits.Count,
			// 选中本命后，实体从 tscn 实例化，贴图 = pet.png，且 modulate 套上了该随从的身份色
			["fate_companion_ok"] = _chosenSpirit != null
									&& _companion != null && IsInstanceValid(_companion)
									&& _companion.SpiritId == _chosenSpirit.Id
									&& _companion.TexturePath.EndsWith("/pet.png")
									&& _companion.AppliedTint == _chosenSpirit.Tint,
			// 随从系统第 3 块：存档已加载（单槽 JSON + 版本号）+ 三本命树结构合法且互不相同
			["save_loaded_ok"] = _save != null && _save.Version == SaveGame.SaveVersion
								 && SaveGame.HasSave,
			["upgrade_trees_ok"] = _spirits.Count >= 3 && UpgradeCatalog.All.Count >= 3
								   && _spirits.All(s => UpgradeCatalog.Tree(s.Id).IsValid(out _)),
			// SPIRIT-FOLLOW：本命实体在场上、有真实碰撞体、层规则正确；且实体永远不与主角同位
			["follow_entity_ok"] = _spiritGroup != null && _spiritGroup.Primary != null
								   && _chosenSpirit != null
								   && _spiritGroup.Primary.SpiritId == _chosenSpirit.Id
								   && _spiritGroup.Primary.GetNodeOrNull<CollisionShape2D>("CollisionShape2D")?.Shape != null
								   && _spiritGroup.Primary.CollisionLayer == GameDefs.LayerSpirit
								   && (_spiritGroup.Primary.CollisionMask & GameDefs.LayerActor) != 0u,
			["follow_no_overlap_ok"] = _spiritGroup != null && _spiritGroup.Pets.Count > 0
									   && _spiritGroup.Pets.All(p =>
										   !SpiritFollowGroup.Overlaps(p.GlobalPosition, Player!.GlobalPosition)),
		};
		bool ok = assertions.Values.All(v => v);
		var report = new Dictionary<string, object>
		{
			["project"] = "山海幻灵",
			["version"] = ProjectSettings.GetSetting("application/config/version", "dev").AsString(),
			["mode"] = "headless-smoke",
			["result"] = ok ? "PASS" : "FAIL",
			["platforms"] = PlatformCount,
		["monsters_active"] = _monsterField.Active.Count,
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
			// BOUNCE-JELLY 蹦蹦床动画证据
			["bounce_jelly_started"] = _blocks.BounceJellyStartedCount,
			["bounce_jelly_seen"] = _bounceJellySeen,
			["bounce_skipped"] = _bounceSkipped,
			["bounce_launch_rise"] = Math.Round(_bounceStartY - _bounceMinY, 3),
			// 随从系统第 1 块证据（主角养的灵兽身份目录，尚无技能/选择/跟随）
			["spirits_count"] = _spirits.Count,
			["spirits_ids"] = string.Join(",", _spirits.Select(s => s.Id)),
			["spirits_names"] = string.Join(",", _spirits.Select(s => s.DisplayName)),
			["spirits_tints"] = string.Join(",", _spirits.Select(s => s.Tint.ToHtml(false))),
			// 随从系统第 2 块证据（本命选择 + tscn 实体）
			["fate_chosen_id"] = _chosenSpirit?.Id ?? "",
			["fate_chosen_name"] = _chosenSpirit?.DisplayName ?? "",
			["fate_companion_id"] = _companion?.SpiritId ?? "",
			["fate_companion_texture"] = _companion?.TexturePath ?? "",
			["fate_companion_tint"] = _companion != null ? _companion.AppliedTint.ToHtml(false) : "",
			// 随从系统第 3 块证据（单槽存档 + 升级树目录）
			["save_souls"] = _save?.Souls ?? -1,
			["save_version"] = _save?.Version ?? -1,
			["upgrade_tree_ids"] = string.Join(",", UpgradeCatalog.All.Keys.OrderBy(k => k, StringComparer.Ordinal)),
			// SPIRIT-FOLLOW 证据（实体数量 / 本命 / 碰撞层）
			["follow_pets"] = _spiritGroup?.Pets.Count ?? -1,
			["follow_fate_id"] = _spiritGroup?.Primary?.SpiritId ?? "",
			["follow_layers"] = _spiritGroup?.Primary != null
				? $"layer={_spiritGroup.Primary.CollisionLayer} mask={_spiritGroup.Primary.CollisionMask}" : "",
			["upgrade_tree_nodes"] = string.Join(",", _spirits.Select(s => $"{s.Id}:{UpgradeCatalog.Tree(s.Id).NodeCount}")),
			["player_grounded"] = PlayerGrounded,
			["player_start"] = _smokeStartPos.ToString(),
			["player_end"] = now.ToString(),
			["player_min_y"] = Math.Round(_smokeMinY, 3),
			["assertions"] = assertions,
		};
		GD.Print("SMOKE_JSON_BEGIN");
		GD.Print(JsonSerializer.Serialize(report, SmokeFlags.IndentedJson));
		GD.Print("SMOKE_JSON_END");
		GetTree().Quit(ok ? 0 : 1);
	}
}
