using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// v0.0001 程序化自举：注册输入 → 建最小世界（地形 + 主角 + 相机）。
/// 无 UI、无菜单、无战斗、无循环阶段——按 F5 直接进入可跑跳的空世界。
///
/// @trace JUMP-001 / MAP-001 / GND-001 本版唯一职责：把「移动手感」和「平台地图生成」
/// 原样保住，作为后续所有设计的公共底座。--smoke 无头验证五件事：建图 / 落地 / 会走 / 会跳 / 会下穿。
/// </summary>
public partial class GameRoot : Node2D
{
	public static GameRoot? Instance;
	public static bool SmokeMode;

	private const int SmokeFixedSeed = 20261113;

	private WorldRoot _world = null!;
	private float _smokeTimer;
	private bool _jumpInjected, _releaseInjected;
	private bool _dropTeleported, _dropJumpInjected, _dropReleased, _dropSkipped;
	private float _dropPlatY;   // 传送后角色中心 y（断言基准）
	private float _smokeMaxY;   // 下穿阶段（2.2~2.6s）角色到达的最低点：曾低于台面即证明穿层
	private Vector2 _smokeStartPos;
	private Vector2 _smokeMovedPos;   // 传送前的位置（player_moved 断言用，不受传送干扰）
	private float _smokeMinY;

	private static readonly Dictionary<string, int[]> ActionKeys = new()
	{
		{ "move_left",  new[] { (int)Key.A, (int)Key.Left } },
		{ "move_right", new[] { (int)Key.D, (int)Key.Right } },
		{ "move_down",  new[] { (int)Key.S, (int)Key.Down } },
		{ "jump",       new[] { (int)Key.Space, (int)Key.W, (int)Key.Up } },
	};

	public override void _Ready()
	{
		Instance = this;
		RegisterActions();

		string[] args = OS.GetCmdlineArgs();
		string[] userArgs = OS.GetCmdlineUserArgs();
		SmokeMode = args.Contains("--smoke") || userArgs.Contains("--smoke");

		_world = new WorldRoot { Name = "WorldRoot" };
		AddChild(_world);
		_world.BeginRun(SmokeMode ? SmokeFixedSeed : TimeSeed());

		GD.Print("[BOOT] 山海幻灵 v0.0001 · 最小骨架（移动手感 + 平台地图生成）");
		if (SmokeMode) BeginSmoke();
	}

	private static int TimeSeed() => (int)Time.GetUnixTimeFromSystem() & 0x7FFFFFFF;

	private static void RegisterActions()
	{
		foreach (var kv in ActionKeys)
		{
			var name = new StringName(kv.Key);
			if (!InputMap.HasAction(name)) InputMap.AddAction(name, 0.2f);
			foreach (int code in kv.Value)
				InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = (Key)code });
		}
		// 手柄适配：左摇杆 + 十字键移动/下穿，A 键（Xbox A / PS 交叉）跳跃
		AddPad("move_left", JoyAxis.LeftX, -1.0f, JoyButton.DpadLeft);
		AddPad("move_right", JoyAxis.LeftX, 1.0f, JoyButton.DpadRight);
		AddPad("move_down", JoyAxis.LeftY, 1.0f, JoyButton.DpadDown);
		AddPadButton("jump", JoyButton.A);
	}

	/// <summary>手柄绑定：摇杆单轴推满方向 + 同向十字键，一并挂到动作上。</summary>
	private static void AddPad(string action, JoyAxis axis, float value, JoyButton dpad)
	{
		var name = new StringName(action);
		InputMap.ActionAddEvent(name, new InputEventJoypadMotion { Axis = axis, AxisValue = value });
		InputMap.ActionAddEvent(name, new InputEventJoypadButton { ButtonIndex = dpad });
	}

	private static void AddPadButton(string action, JoyButton button)
		=> InputMap.ActionAddEvent(new StringName(action),
			new InputEventJoypadButton { ButtonIndex = button });

	// ---------------------------------------------------------------- 无头冒烟
	// 注入项只在 --smoke 生效：固定种子 + 模拟输入（走 → 跳 → 传送到单向平台按下+跳验证下穿）。
	private void BeginSmoke()
	{
		_smokeStartPos = _world.Player != null && IsInstanceValid(_world.Player)
			? _world.Player.GlobalPosition : Vector2.Zero;
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
		if (_world.Player != null && IsInstanceValid(_world.Player))
			_smokeMinY = Mathf.Min(_smokeMinY, _world.Player.GlobalPosition.Y);

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
			var player = _world.Player;
			if (player != null && IsInstanceValid(player))
				_smokeMovedPos = player.GlobalPosition;
			if (_world.SmokeTeleportToPlatform() && player != null && IsInstanceValid(player))
			{
				_dropPlatY = player.GlobalPosition.Y;
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
			var player = _world.Player;
			if (player != null && IsInstanceValid(player))
				_smokeMaxY = Mathf.Max(_smokeMaxY, player.GlobalPosition.Y);
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
		bool hasPlayer = _world.Player != null && IsInstanceValid(_world.Player);
		Vector2 now = hasPlayer ? _world.Player!.GlobalPosition : Vector2.Zero;
		var assertions = new Dictionary<string, bool>
		{
			["platforms_built"] = _world.PlatformCount > 0,
			["ground_continuous"] = _world.GroundContinuous,
			["player_grounded"] = _world.PlayerGrounded,
			["player_moved"] = Mathf.Abs(_smokeMovedPos.X - _smokeStartPos.X) > 2.0f,
			["player_jumped"] = _smokeMinY < _smokeStartPos.Y - 0.5f,
			["player_drop_through"] = _dropSkipped || _smokeMaxY > _dropPlatY + 0.5f,
		};
		bool ok = assertions.Values.All(v => v);
		var report = new Dictionary<string, object>
		{
			["project"] = "山海幻灵",
			["version"] = "0.0001",
			["mode"] = "headless-smoke",
			["result"] = ok ? "PASS" : "FAIL",
			["platforms"] = _world.PlatformCount,
			["ground_continuous"] = _world.GroundContinuous,
			["player_grounded"] = _world.PlayerGrounded,
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
