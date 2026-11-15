using System;
using System.Collections.Generic;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 平台跳跃白箱（调参工具，不是游戏机制）。
///
/// 目的：让「移动速度 / 跳跃初速 / 重力 / 空中控制 / 土狼时间 / 跳跃缓冲」可以在真实地形上
/// 边跑边调，并把数值落成 user://whitebox_tuning.json，再写回 GameDefs 供正式关卡使用。
///
/// 关键约束：白箱角色与游戏主角共用 PlatformMover（同一个移动内核），
/// 所以白箱里调出的手感 = 游戏里的手感，不会出现「白箱好、进游戏变样」。
///
/// 运行：Godot 打开 scenes/Whitebox.tscn 按 F6，或
///   godot --path &lt;项目&gt; res://scenes/Whitebox.tscn
/// </summary>
public partial class WhiteboxRoot : Node2D
{
    private const string SavePath = "user://whitebox_tuning.json";
    private const int DefaultSeed = 20261112;

    private readonly MoveTuning _tuning = MoveTuning.GameDefaults();
    private WhiteboxPlayer _player = null!;
    private BlockField _blocks = null!;
    private Camera2D _camera = null!;
    private CanvasLayer _ui = null!;
    private Control _panel = null!;
    private readonly List<Label> _values = new();
    private readonly List<HSlider> _sliders = new();
    private readonly List<float> _sliderDefaults = new();
    private VBoxContainer _vbox = null!;
    private Label _metrics = null!;
    private Label _state = null!;
    private Label _seedLabel = null!;
    private int _seed = DefaultSeed;

    public override void _Ready()
    {
        // 输入映射在 project.godot [input] 里统一配置（move_left / move_right / move_down / jump）
        BuildWorld();
        BuildUi();
        RefreshMetrics();
        GD.Print("[WHITEBOX] 就绪：A/D 移动 · Space 跳 · R 复位 · T 换地形 · Tab 隐藏面板");

        // 仅截图自检用：--shot 时 1.5 秒后存图并退出
        if (OS.GetCmdlineArgs().Length > 0 && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--shot") >= 0)
            CallDeferred(new StringName(nameof(CaptureShot)));
    }

    private async void CaptureShot()
    {
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng("user://whitebox_shot.png");
        GD.Print($"[WHITEBOX] shot err={err}");
        GetTree().Quit(0);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        switch (key.Keycode)
        {
            case Key.R:
                _player.Respawn();
                break;
            case Key.T:
                _seed = (int)((ulong)Time.GetUnixTimeFromSystem() & 0x7FFFFFFFu);
                RebuildPlatforms();
                break;
            case Key.Tab:
                _panel.Visible = !_panel.Visible;
                break;
        }
    }

    // ---------------------------------------------------------------- 世界
    private void BuildWorld()
    {
        // 场景节点在 Whitebox.tscn 里摆（Backdrop / BlockField / Player[CharacterBody2D+Visual+Camera2D]）
        _blocks = GetNodeOrNull<BlockField>("BlockField");
        if (_blocks == null) { _blocks = new BlockField(); AddChild(_blocks); }
        _blocks.Build(_seed);

        _player = GetNodeOrNull<WhiteboxPlayer>("Player");
        if (_player == null) { _player = new WhiteboxPlayer { Tuning = _tuning }; AddChild(_player); }
        else { _player.Tuning = _tuning; }
        _player.Respawn();
        _player.SetLedges(_blocks.GetLedges());   // CLIMB-001 自动上攀：白箱也能实机体验翻台沿
        _player.SetGeometry(_blocks);             // 蔚蓝式转角修正：注入方块几何

        _camera = _player.GetNodeOrNull<Camera2D>("Camera2D");
        float h2 = GetViewportRect().Size.Y <= 0.0f ? 720.0f : GetViewportRect().Size.Y;
        float zoom2 = h2 / (2.0f * GameDefs.CameraViewHalfHeight);
        if (_camera == null)
        {
            _camera = new Camera2D { Zoom = new Vector2(zoom2, zoom2), Enabled = true };
            _player.AddChild(_camera);
        }
        else { _camera.Zoom = new Vector2(zoom2, zoom2); _camera.Enabled = true; }
    }

    private void RebuildPlatforms()
    {
        _blocks.Build(_seed);
        _player.Respawn();
        _player.SetLedges(_blocks.GetLedges());   // CLIMB-001 换地形后刷新台沿
        UiSkin.RefreshSeed(_seedLabel, _seed);    // 开发用：换地形后同步显示新种子
        RefreshMetrics();
    }

    // ---------------------------------------------------------------- UI
    private void BuildUi()
    {
        _ui = new CanvasLayer { Name = "WhiteboxUi" };
        AddChild(_ui);

        _panel = new Control();
        _ui.AddChild(_panel);

        var back = new ColorRect
        {
            Color = new Color(0.07f, 0.08f, 0.13f, 0.93f),
            Position = new Vector2(852f, 16f),
            Size = new Vector2(420f, 688f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panel.AddChild(back);

        // 滚动容器：参数较多，保证在 1280×720 下不溢出
        var scroll = new ScrollContainer
        {
            Position = new Vector2(860f, 24f),
            Size = new Vector2(404f, 672f),
        };
        _panel.AddChild(scroll);

        _vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _vbox.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_vbox);

        _vbox.AddChild(UiSkin.MakeLabel("跳跃白箱 · 参数调节", 22, UiSkin.Gold));
        _vbox.AddChild(UiSkin.MakeLabel("白箱与游戏共用同一移动内核，调好即生效", 13, UiSkin.Jade));
        _vbox.AddChild(UiSkin.MakeLabel("手感目标：蔚蓝（Celeste）式响应——起停跟手、弧线干脆、零浮空",
            12, new Color(0.62f, 0.66f, 0.78f)));

        AddSlider("移动速度", 2.0f, 16.0f, 0.1f, _tuning.MoveSpeed, v => _tuning.MoveSpeed = v);
        AddSlider("跳跃初速", 6.0f, 26.0f, 0.1f, _tuning.JumpSpeed, v => _tuning.JumpSpeed = v);
        AddSlider("重力", 8.0f, 60.0f, 0.5f, _tuning.Gravity, v => _tuning.Gravity = v);
        AddSlider("地面加速", 20.0f, 200.0f, 5.0f, _tuning.GroundAccel, v => _tuning.GroundAccel = v);
        AddSlider("地面刹车", 20.0f, 200.0f, 5.0f, _tuning.GroundDecel, v => _tuning.GroundDecel = v);
        AddSlider("空中加速", 20.0f, 200.0f, 5.0f, _tuning.AirAccel, v => _tuning.AirAccel = v);
        AddSlider("急转身倍率", 1.0f, 4.0f, 0.1f, _tuning.TurnAccelMul, v => _tuning.TurnAccelMul = v);
        AddSlider("空中滑行", 0.0f, 1.0f, 0.02f, _tuning.EndTraction, v => _tuning.EndTraction = v);
        AddSlider("土狼时间(秒)", 0.0f, 0.30f, 0.01f, _tuning.CoyoteTime, v => _tuning.CoyoteTime = v);
        AddSlider("跳跃缓冲(秒)", 0.0f, 0.30f, 0.01f, _tuning.JumpBuffer, v => _tuning.JumpBuffer = v);
        AddSlider("变高跳截断", 0.10f, 1.00f, 0.01f, _tuning.JumpCutMul, v => _tuning.JumpCutMul = v);
        AddSlider("下落重力倍率", 1.0f, 2.5f, 0.05f, _tuning.FallGravityMul, v => _tuning.FallGravityMul = v);
        AddSlider("顶点滞空倍率", 0.20f, 1.00f, 0.05f, _tuning.ApexGravityMul, v => _tuning.ApexGravityMul = v);
        AddSlider("顶点阈值", 0.0f, 6.0f, 0.1f, _tuning.ApexThreshold, v => _tuning.ApexThreshold = v);
        AddSlider("最大下落速度", 12.0f, 40.0f, 0.5f, _tuning.MaxFallSpeed, v => _tuning.MaxFallSpeed = v);
        AddSlider("下穿窗口(秒)", 0.0f, 0.50f, 0.01f, _tuning.DropThroughTime, v => _tuning.DropThroughTime = v);
        AddSlider("上攀探测高度", 0.0f, 1.20f, 0.05f, _tuning.ClimbReachUp, v => _tuning.ClimbReachUp = v);
        AddSlider("上攀探测带宽", 0.0f, 1.00f, 0.05f, _tuning.ClimbEdgeBand, v => _tuning.ClimbEdgeBand = v);
        AddSlider("上攀推进距离", 0.05f, 0.80f, 0.05f, _tuning.ClimbStep, v => _tuning.ClimbStep = v);
        AddSlider("上攀冷却(秒)", 0.0f, 0.50f, 0.01f, _tuning.ClimbCooldown, v => _tuning.ClimbCooldown = v);
        AddSlider("转角修正", 0.0f, 2.0f, 0.05f, _tuning.CornerCorrectionMax, v => _tuning.CornerCorrectionMax = v);

        _metrics = UiSkin.MakeLabel("", 14, UiSkin.Text);
        _vbox.AddChild(_metrics);

        _state = UiSkin.MakeLabel("", 13, UiSkin.Blue);
        _vbox.AddChild(_state);

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        var copy = UiSkin.MakeButton("复制参数", 14);
        copy.Pressed += () =>
        {
            DisplayServer.ClipboardSet(SnapshotJson());
            GD.Print("[WHITEBOX] copied: " + SnapshotJson());
        };
        var reset = UiSkin.MakeButton("恢复默认", 14);
        reset.Pressed += ResetDefaults;
        var respawn = UiSkin.MakeButton("复位(R)", 14);
        respawn.Pressed += () => _player.Respawn();
        var terrain = UiSkin.MakeButton("换地形(T)", 14);
        terrain.Pressed += () =>
        {
            _seed = (int)((ulong)Time.GetUnixTimeFromSystem() & 0x7FFFFFFFu);
            RebuildPlatforms();
        };
        var climb = UiSkin.MakeButton($"自动上攀:{(_tuning.AutoClimbEnabled ? "开" : "关")}", 14);
        climb.Pressed += () =>
        {
            _tuning.AutoClimbEnabled = !_tuning.AutoClimbEnabled;
            climb.Text = $"自动上攀:{(_tuning.AutoClimbEnabled ? "开" : "关")}";
            GD.Print($"[WHITEBOX] 自动上攀 = {_tuning.AutoClimbEnabled}");
        };
        btnRow.AddChild(copy); btnRow.AddChild(reset);
        btnRow.AddChild(respawn); btnRow.AddChild(terrain);
        btnRow.AddChild(climb);
        _vbox.AddChild(btnRow);

        _vbox.AddChild(UiSkin.MakeLabel(
            "A/D 走 · Space 跳(短按矮/长按高) · R 复位 · T 换地形 · Tab 隐藏面板\n" +
            "调好后把 user://whitebox_tuning.json 发给助手，或直接念数值。", 12, UiSkin.Jade));
        _vbox.AddChild(UiSkin.MakeLabel("参数每次改动都会自动写入 " + SavePath, 12,
            new Color(0.62f, 0.66f, 0.78f)));

        BuildLegend();

        // 开发用：常驻显示当前地图种子（放在图例下方，避开右侧参数面板与左上图例）
        _seedLabel = UiSkin.MakeSeedBadge(this, _seed, new Vector2(16f, 270f));
    }

    // ---------------------------------------------------------------- 方块图例（常驻左上角）
    /// <summary>左上角常驻图例：方块颜色 → 名称 / 行为 / 生成规则，与 BlockField.Tint 同源配色。</summary>
    private void BuildLegend()
    {
        var panel = new ColorRect
        {
            Color = new Color(0.07f, 0.08f, 0.13f, 0.88f),
            Position = new Vector2(16f, 16f),
            Size = new Vector2(344f, 246f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _ui.AddChild(panel);

        var box = new VBoxContainer
        {
            Position = new Vector2(12f, 10f),
            Size = new Vector2(320f, 226f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 3);
        panel.AddChild(box);

        box.AddChild(UiSkin.MakeLabel("方块图例", 15, UiSkin.Gold));
        AddLegendRow(box, BlockField.Tint(BlockType.Normal), "普通实心块 —— 四面实体");
        AddLegendRow(box, BlockField.Tint(BlockType.Normal, true), "单向平台 —— 下方可跳穿 · 站上 S+跳 下穿");
        AddLegendRow(box, BlockField.Tint(BlockType.Fragile), "脆弱木板 —— 踩上即碎（本局不恢复）");
        AddLegendRow(box, BlockField.Tint(BlockType.Bounce), "弹跳块 —— 弹射 ≈4.8 格（果冻动画）");
        AddLegendRow(box, BlockField.Tint(BlockType.Buildable), "可建筑块 —— 占位（未来塔防改造点）");
        AddLegendRow(box, BlockField.Tint(BlockType.Treasure), "宝箱块 —— 触碰打开 · 掉占位宝物");
        box.AddChild(UiSkin.MakeLabel("单向连排：2~3 常见 · 1/4 少见 · 5 极少见 · >5 不出现", 11, UiSkin.Jade));
        box.AddChild(UiSkin.MakeLabel("特殊块/run：脆弱 7% · 弹跳 6% · 建筑 2% · 宝箱 2%", 11, UiSkin.Jade));
    }

    /// <summary>图例一行：色块 + 说明文字。</summary>
    private static void AddLegendRow(VBoxContainer box, Color color, string text)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new ColorRect
        {
            Color = color,
            CustomMinimumSize = new Vector2(16f, 16f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        row.AddChild(UiSkin.MakeLabel(text, 12, UiSkin.Text));
        box.AddChild(row);
    }

    /// <summary>一行：标题 + 滑条 + 读数，加入 VBox。滑条值变化时即时写回调参对象。</summary>
    private void AddSlider(string title, float min, float max, float step, float value,
        Action<float> apply)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var label = UiSkin.MakeLabel(title, 14, UiSkin.Text);
        label.CustomMinimumSize = new Vector2(118f, 0f);
        var readout = UiSkin.MakeLabel("", 14, UiSkin.Gold);
        readout.CustomMinimumSize = new Vector2(96f, 0f);
        readout.HorizontalAlignment = HorizontalAlignment.Right;

        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        int idx = _values.Count;
        slider.ValueChanged += v =>
        {
            apply((float)v);
            readout.Text = FormatValue(idx, (float)v);
            RefreshMetrics();
            SaveTuning();
        };
        readout.Text = FormatValue(idx, value);

        row.AddChild(label);
        row.AddChild(slider);
        row.AddChild(readout);
        _vbox.AddChild(row);

        _values.Add(readout);
        _sliders.Add(slider);
        _sliderDefaults.Add(value);
    }

    private static string FormatValue(int index, float v) => index switch
    {
        0 => $"{v:F1} 格/秒",
        1 => $"{v:F1} 格/秒",
        2 => $"{v:F1} 格/秒²",
        3 => $"{v:F0} 格/秒²",
        4 => $"{v:F0} 格/秒²",
        5 => $"{v:F0} 格/秒²",
        6 => $"{v:F1} ×",
        7 => $"{v:P0}",
        8 => $"{v:F2} 秒",
        9 => $"{v:F2} 秒",
        10 => $"{v:P0}",
        11 => $"{v:F2} ×",
        12 => $"{v:F2} ×",
        13 => $"{v:F1} 格/秒",
        14 => $"{v:F2} 格/秒",
        15 => $"{v:F2} 秒",
        16 => $"{v:F2} 格",
        17 => $"{v:F2} 格",
        18 => $"{v:F2} 格",
        19 => $"{v:F2} 秒",
        20 => $"{v:F2} 格",
        _ => $"{v:F1}",
    };

    private void RefreshMetrics()
    {
        if (_metrics == null) return;
        float h = _tuning.JumpHeight;
        float t = _tuning.AirTime;
        float span = _tuning.JumpSpan;
        float cells = h;   // 方块地图：1 世界单位 = 1 格，跳高格数 = 跳高数值
        float shortH = _tuning.ShortHopHeight;
        _metrics.Text =
            $"满跳高度　{h:F2} 格（1 格 = 1 世界单位）\n" +
            $"短按高度　{shortH:F2} 单位（变高跳 ×{_tuning.JumpCutMul:F2}²）\n" +
            $"理论滞空　{t:F2} 秒\n" +
            $"水平跨距　{span:F2} 单位（满速起跳）\n" +
            $"单跳能否上一格　{(cells >= 1.0f ? "可以" : "不行 —— 需要加大跳跃初速或减小重力")}";
    }

    public override void _Process(double delta)
    {
        if (_state == null || _player == null) return;
        _state.Text =
            $"实测最高点　{_player.MeasuredApex:F2} 单位\n" +
            $"{( _player.Grounded ? "在地面" : "空中")}　速度 ({_player.Velocity.X:F1}, {_player.Velocity.Y:F1})\n" +
            $"地形种子 {_seed}";
    }

    // ---------------------------------------------------------------- 持久化
    private void ResetDefaults()
    {
        MoveTuning d = MoveTuning.GameDefaults();
        _tuning.MoveSpeed = d.MoveSpeed;
        _tuning.JumpSpeed = d.JumpSpeed;
        _tuning.Gravity = d.Gravity;
        _tuning.GroundAccel = d.GroundAccel;
        _tuning.GroundDecel = d.GroundDecel;
        _tuning.AirAccel = d.AirAccel;
        _tuning.TurnAccelMul = d.TurnAccelMul;
        _tuning.EndTraction = d.EndTraction;
        _tuning.CoyoteTime = d.CoyoteTime;
        _tuning.JumpBuffer = d.JumpBuffer;
        _tuning.JumpCutMul = d.JumpCutMul;
        _tuning.FallGravityMul = d.FallGravityMul;
        _tuning.ApexGravityMul = d.ApexGravityMul;
        _tuning.ApexThreshold = d.ApexThreshold;
        _tuning.MaxFallSpeed = d.MaxFallSpeed;
        _tuning.CornerCorrectionMax = d.CornerCorrectionMax;
        _tuning.DropThroughTime = d.DropThroughTime;
        _tuning.ClimbReachUp = d.ClimbReachUp;
        _tuning.ClimbEdgeBand = d.ClimbEdgeBand;
        _tuning.ClimbStep = d.ClimbStep;
        _tuning.ClimbCooldown = d.ClimbCooldown;
        // 重建 UI 读数：直接还原滑条值（触发 ValueChanged 即同步 _tuning 与读数）
        for (int i = 0; i < _sliders.Count; i++)
            _sliders[i].Value = _sliderDefaults[i];
        RefreshMetrics();
        SaveTuning();
    }

    private string SnapshotJson()
    {
        var d = new Godot.Collections.Dictionary
        {
            ["move_speed"] = Math.Round(_tuning.MoveSpeed, 2),
            ["jump_speed"] = Math.Round(_tuning.JumpSpeed, 2),
            ["gravity"] = Math.Round(_tuning.Gravity, 2),
            ["ground_accel"] = Math.Round(_tuning.GroundAccel, 1),
            ["ground_decel"] = Math.Round(_tuning.GroundDecel, 1),
            ["air_accel"] = Math.Round(_tuning.AirAccel, 1),
            ["turn_accel_mul"] = Math.Round(_tuning.TurnAccelMul, 2),
            ["end_traction"] = Math.Round(_tuning.EndTraction, 2),
            ["coyote_time"] = Math.Round(_tuning.CoyoteTime, 3),
            ["jump_buffer"] = Math.Round(_tuning.JumpBuffer, 3),
            ["jump_cut_mul"] = Math.Round(_tuning.JumpCutMul, 3),
            ["fall_gravity_mul"] = Math.Round(_tuning.FallGravityMul, 3),
            ["apex_gravity_mul"] = Math.Round(_tuning.ApexGravityMul, 3),
            ["apex_threshold"] = Math.Round(_tuning.ApexThreshold, 3),
            ["max_fall_speed"] = Math.Round(_tuning.MaxFallSpeed, 2),
            ["corner_correction_max"] = Math.Round(_tuning.CornerCorrectionMax, 2),
            ["derived"] = new Godot.Collections.Dictionary
            {
                ["jump_height"] = Math.Round(_tuning.JumpHeight, 2),
                ["short_hop_height"] = Math.Round(_tuning.ShortHopHeight, 2),
                ["air_time"] = Math.Round(_tuning.AirTime, 2),
                ["jump_span"] = Math.Round(_tuning.JumpSpan, 2),
            ["cells"] = Math.Round(_tuning.JumpHeight, 2),
            },
            ["terrain_seed"] = _seed,
        };
        return Json.Stringify(d, "  ");
    }

    private void SaveTuning()
    {
        using FileAccess f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (f == null) return;
        f.StoreString(SnapshotJson());
    }
}
