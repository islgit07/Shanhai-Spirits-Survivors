using System;
using System.Collections.Generic;
using Godot;

namespace ShanHaiHuanLing;

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
    private PlatformField _platforms = null!;
    private Camera2D _camera = null!;
    private CanvasLayer _ui = null!;
    private Control _panel = null!;
    private readonly List<Label> _values = new();
    private readonly List<HSlider> _sliders = new();
    private readonly List<float> _sliderDefaults = new();
    private VBoxContainer _vbox = null!;
    private Label _metrics = null!;
    private Label _state = null!;
    private int _seed = DefaultSeed;

    public override void _Ready()
    {
        RegisterInput();      // 白箱是独立场景，没有 GameRoot 帮忙注册按键
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

    /// <summary>与游戏一致的按键映射（白箱单独运行时自行注册），含手柄。</summary>
    private static void RegisterInput()
    {
        Add("move_left", new[] { Key.A, Key.Left });
        Add("move_right", new[] { Key.D, Key.Right });
        Add("move_down", new[] { Key.S, Key.Down });
        Add("jump", new[] { Key.Space, Key.W, Key.Up });
        // 手柄适配：左摇杆 + 十字键移动/下穿，A 键（Xbox A / PS 交叉）跳跃
        AddPad("move_left", JoyAxis.LeftX, -1.0f, JoyButton.DpadLeft);
        AddPad("move_right", JoyAxis.LeftX, 1.0f, JoyButton.DpadRight);
        AddPad("move_down", JoyAxis.LeftY, 1.0f, JoyButton.DpadDown);
        AddPadButton("jump", JoyButton.A);
    }

    private static void Add(string action, Key[] keys)
    {
        var name = new StringName(action);
        if (!InputMap.HasAction(name)) InputMap.AddAction(name, 0.2f);
        foreach (Key k in keys)
        {
            var ev = new InputEventKey { PhysicalKeycode = k };
            if (!InputMap.ActionHasEvent(name, ev)) InputMap.ActionAddEvent(name, ev);
        }
    }

    /// <summary>手柄绑定：摇杆单轴推满方向 + 同向十字键，一并挂到动作上（带去重）。</summary>
    private static void AddPad(string action, JoyAxis axis, float value, JoyButton dpad)
    {
        var name = new StringName(action);
        AddEvent(name, new InputEventJoypadMotion { Axis = axis, AxisValue = value });
        AddEvent(name, new InputEventJoypadButton { ButtonIndex = dpad });
    }

    private static void AddPadButton(string action, JoyButton button)
        => AddEvent(new StringName(action), new InputEventJoypadButton { ButtonIndex = button });

    private static void AddEvent(StringName name, InputEvent ev)
    {
        if (!InputMap.ActionHasEvent(name, ev)) InputMap.ActionAddEvent(name, ev);
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
        var bg = new ColorRect
        {
            Color = new Color(0.10f, 0.11f, 0.16f),
            Position = new Vector2(-2000f, -2000f),
            Size = new Vector2(4000f, 4000f),
            ZIndex = -50,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(bg);

        _platforms = new PlatformField();
        AddChild(_platforms);
        _platforms.Build(_seed);

        _player = new WhiteboxPlayer { Tuning = _tuning };
        AddChild(_player);
        _player.Respawn();


        float h = GetViewportRect().Size.Y <= 0.0f ? 720.0f : GetViewportRect().Size.Y;
        float zoom = h / (2.0f * GameDefs.CameraViewHalfHeight);
        _camera = new Camera2D { Zoom = new Vector2(zoom, zoom), Enabled = true };
        _player.AddChild(_camera);
    }

    private void RebuildPlatforms()
    {
        _platforms.Build(_seed);
        _player.Respawn();
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
        _vbox.AddChild(UiSkin.MakeLabel("手感目标：复刻《终焉之莉莉》——轻盈可控、顶点悬停、短按矮跳",
            12, new Color(0.62f, 0.66f, 0.78f)));

        AddSlider("移动速度", 2.0f, 16.0f, 0.1f, _tuning.MoveSpeed, v => _tuning.MoveSpeed = v);
        AddSlider("跳跃初速", 6.0f, 26.0f, 0.1f, _tuning.JumpSpeed, v => _tuning.JumpSpeed = v);
        AddSlider("重力", 8.0f, 60.0f, 0.5f, _tuning.Gravity, v => _tuning.Gravity = v);
        AddSlider("空中控制", 0.0f, 1.0f, 0.05f, _tuning.AirControl, v => _tuning.AirControl = v);
        AddSlider("土狼时间(秒)", 0.0f, 0.30f, 0.01f, _tuning.CoyoteTime, v => _tuning.CoyoteTime = v);
        AddSlider("跳跃缓冲(秒)", 0.0f, 0.30f, 0.01f, _tuning.JumpBuffer, v => _tuning.JumpBuffer = v);
        AddSlider("变高跳截断", 0.10f, 1.00f, 0.01f, _tuning.JumpCutMul, v => _tuning.JumpCutMul = v);
        AddSlider("下落重力倍率", 1.0f, 2.5f, 0.05f, _tuning.FallGravityMul, v => _tuning.FallGravityMul = v);
        AddSlider("顶点滞空倍率", 0.20f, 1.00f, 0.05f, _tuning.ApexGravityMul, v => _tuning.ApexGravityMul = v);
        AddSlider("顶点阈值", 0.0f, 6.0f, 0.1f, _tuning.ApexThreshold, v => _tuning.ApexThreshold = v);
        AddSlider("最大下落速度", 12.0f, 40.0f, 0.5f, _tuning.MaxFallSpeed, v => _tuning.MaxFallSpeed = v);

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
        btnRow.AddChild(copy); btnRow.AddChild(reset);
        btnRow.AddChild(respawn); btnRow.AddChild(terrain);
        _vbox.AddChild(btnRow);

        _vbox.AddChild(UiSkin.MakeLabel(
            "A/D 走 · Space 跳(短按矮/长按高) · R 复位 · T 换地形 · Tab 隐藏面板\n" +
            "调好后把 user://whitebox_tuning.json 发给助手，或直接念数值。", 12, UiSkin.Jade));
        _vbox.AddChild(UiSkin.MakeLabel("参数每次改动都会自动写入 " + SavePath, 12,
            new Color(0.62f, 0.66f, 0.78f)));
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
        0 => $"{v:F1} 单位/秒",
        1 => $"{v:F1} 单位/秒",
        2 => $"{v:F1} 单位/秒²",
        3 => $"{v:P0}",
        4 => $"{v:F2} 秒",
        5 => $"{v:F2} 秒",
        6 => $"{v:P0}",
        7 => $"{v:F2} ×",
        8 => $"{v:F2} ×",
        9 => $"{v:F1} 单位/秒",
        _ => $"{v:F1} 单位/秒",
    };

    private void RefreshMetrics()
    {
        if (_metrics == null) return;
        float h = _tuning.JumpHeight;
        float t = _tuning.AirTime;
        float span = _tuning.JumpSpan;
        float layers = h / GameDefs.LayerSpacing;
        float shortH = _tuning.ShortHopHeight;
        _metrics.Text =
            $"满跳高度　{h:F2} 单位（层高 {GameDefs.LayerSpacing:F1} → 约 {layers:F2} 层）\n" +
            $"短按高度　{shortH:F2} 单位（变高跳 ×{_tuning.JumpCutMul:F2}²）\n" +
            $"理论滞空　{t:F2} 秒\n" +
            $"水平跨距　{span:F2} 单位（满速起跳）\n" +
            $"单跳能否上一层　{(layers >= 1.0f ? "可以" : "不行 —— 需要加大跳跃初速或减小重力")}";
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
        _tuning.AirControl = d.AirControl;
        _tuning.CoyoteTime = d.CoyoteTime;
        _tuning.JumpBuffer = d.JumpBuffer;
        _tuning.JumpCutMul = d.JumpCutMul;
        _tuning.FallGravityMul = d.FallGravityMul;
        _tuning.ApexGravityMul = d.ApexGravityMul;
        _tuning.ApexThreshold = d.ApexThreshold;
        _tuning.MaxFallSpeed = d.MaxFallSpeed;
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
            ["air_control"] = Math.Round(_tuning.AirControl, 2),
            ["coyote_time"] = Math.Round(_tuning.CoyoteTime, 3),
            ["jump_buffer"] = Math.Round(_tuning.JumpBuffer, 3),
            ["jump_cut_mul"] = Math.Round(_tuning.JumpCutMul, 3),
            ["fall_gravity_mul"] = Math.Round(_tuning.FallGravityMul, 3),
            ["apex_gravity_mul"] = Math.Round(_tuning.ApexGravityMul, 3),
            ["apex_threshold"] = Math.Round(_tuning.ApexThreshold, 3),
            ["max_fall_speed"] = Math.Round(_tuning.MaxFallSpeed, 2),
            ["derived"] = new Godot.Collections.Dictionary
            {
                ["jump_height"] = Math.Round(_tuning.JumpHeight, 2),
                ["short_hop_height"] = Math.Round(_tuning.ShortHopHeight, 2),
                ["air_time"] = Math.Round(_tuning.AirTime, 2),
                ["jump_span"] = Math.Round(_tuning.JumpSpan, 2),
                ["layers"] = Math.Round(_tuning.JumpHeight / GameDefs.LayerSpacing, 2),
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

/// <summary>白箱角色：只做「跑 + 跳」，移动完全走 PlatformMover（与游戏主角同一份内核）。</summary>
public partial class WhiteboxPlayer : CharacterBody2D
{
    public MoveTuning Tuning = MoveTuning.GameDefaults();

    /// <summary>本次起跳后的实测最高点（脱离地面那一刻开始计）。</summary>
    public float MeasuredApex { get; private set; }

    private readonly PlatformMover _mover = null!;
    private Sprite2D _sprite = null!;
    private float _jumpStartY;
    private bool _tracking;

    public bool Grounded => _mover != null && _mover.Grounded;


    // 注意：字段初始化顺序，_mover 依赖 Tuning，因此在 _Ready 里构造
    public WhiteboxPlayer()
    {
        _mover = new PlatformMover(MoveTuning.GameDefaults());
    }

    public override void _Ready()
    {
        CollisionLayer = 1u;
        CollisionMask = GameDefs.LayerSolid | GameDefs.LayerOneWay;
        var shape = new RectangleShape2D { Size = GameDefs.MasterSize };
        AddChild(new CollisionShape2D { Shape = shape });
        _sprite = ArtBank.MakeSprite("player", GameDefs.MasterSize);
        AddChild(_sprite);
        _mover.T = Tuning;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        float dir = Input.GetAxis("move_left", "move_right");
        _mover.Step(this, dir, Input.IsActionJustPressed("jump"), Input.IsActionPressed("jump"),
            Input.IsActionPressed("move_down"), dt);

        if (_mover.JustJumped)
        {
            _tracking = true;
            _jumpStartY = GlobalPosition.Y;
            MeasuredApex = 0.0f;
        }
        else if (_tracking)
        {
            MeasuredApex = Mathf.Max(MeasuredApex, _jumpStartY - GlobalPosition.Y);
            if (_mover.Grounded) _tracking = false;
        }

        if (GlobalPosition.Y > GameDefs.FallKillY) Respawn();
    }

    public void Respawn()
    {
        GlobalPosition = new Vector2(0.0f, GameDefs.LayerY(0) - GameDefs.SpawnAboveGround);
        Velocity = Vector2.Zero;
        MeasuredApex = 0.0f;
        _tracking = false;
        _mover.ResetWindows();
    }
}