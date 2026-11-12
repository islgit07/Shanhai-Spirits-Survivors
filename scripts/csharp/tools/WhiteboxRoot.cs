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

    /// <summary>与游戏一致的按键映射（白箱单独运行时自行注册）。</summary>
    private static void RegisterInput()
    {
        Add("move_left", new[] { Key.A, Key.Left });
        Add("move_right", new[] { Key.D, Key.Right });
        Add("jump", new[] { Key.Space, Key.W, Key.Up });
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
            Size = new Vector2(412f, 688f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panel.AddChild(back);

        _panel.AddChild(Place(UiSkin.MakeLabel("跳跃白箱 · 参数调节", 22, UiSkin.Gold), 868, 26));
        _panel.AddChild(Place(UiSkin.MakeLabel("白箱与游戏共用同一移动内核，调好即生效", 13, UiSkin.Jade), 868, 56));

        int y = 88;
        y = AddSlider("移动速度", 2.0f, 16.0f, 0.1f, _tuning.MoveSpeed, v => _tuning.MoveSpeed = v, y);
        y = AddSlider("跳跃初速", 6.0f, 26.0f, 0.1f, _tuning.JumpSpeed, v => _tuning.JumpSpeed = v, y);
        y = AddSlider("重力", 8.0f, 60.0f, 0.5f, _tuning.Gravity, v => _tuning.Gravity = v, y);
        y = AddSlider("空中控制", 0.0f, 1.0f, 0.05f, _tuning.AirControl, v => _tuning.AirControl = v, y);
        y = AddSlider("土狼时间(秒)", 0.0f, 0.30f, 0.01f, _tuning.CoyoteTime, v => _tuning.CoyoteTime = v, y);
        y = AddSlider("跳跃缓冲(秒)", 0.0f, 0.30f, 0.01f, _tuning.JumpBuffer, v => _tuning.JumpBuffer = v, y);

        _metrics = Place(UiSkin.MakeLabel("", 14, UiSkin.Text), 868, y + 6);
        _panel.AddChild(_metrics);

        _state = Place(UiSkin.MakeLabel("", 13, UiSkin.Blue), 868, y + 132);
        _panel.AddChild(_state);

        var copy = UiSkin.MakeButton("复制参数到剪贴板", 15);
        copy.Position = new Vector2(868f, y + 166f);
        copy.Size = new Vector2(188f, 38f);
        copy.Pressed += () =>
        {
            DisplayServer.ClipboardSet(SnapshotJson());
            Signals.EmitHint("参数已复制");
            GD.Print("[WHITEBOX] copied: " + SnapshotJson());
        };
        _panel.AddChild(copy);

        var reset = UiSkin.MakeButton("恢复默认", 15);
        reset.Position = new Vector2(1064f, y + 166f);
        reset.Size = new Vector2(184f, 38f);
        reset.Pressed += ResetDefaults;
        _panel.AddChild(reset);

        var respawn = UiSkin.MakeButton("复位位置 (R)", 15);
        respawn.Position = new Vector2(868f, y + 210f);
        respawn.Size = new Vector2(188f, 38f);
        respawn.Pressed += () => _player.Respawn();
        _panel.AddChild(respawn);

        var terrain = UiSkin.MakeButton("换一张地形 (T)", 15);
        terrain.Position = new Vector2(1064f, y + 210f);
        terrain.Size = new Vector2(184f, 38f);
        terrain.Pressed += () =>
        {
            _seed = (int)((ulong)Time.GetUnixTimeFromSystem() & 0x7FFFFFFFu);
            RebuildPlatforms();
        };
        _panel.AddChild(terrain);

        var tip = Place(UiSkin.MakeLabel(
            "A/D 走 · Space 跳 · R 复位 · T 换地形 · Tab 隐藏面板\n" +
            "调好后把 user://whitebox_tuning.json 发给助手，或直接念数值。", 13, UiSkin.Jade),
            868, y + 258);
        _panel.AddChild(tip);

        var saved = Place(UiSkin.MakeLabel("参数每次改动都会自动写入 " + SavePath, 12,
            new Color(0.62f, 0.66f, 0.78f)), 868, y + 306);
        _panel.AddChild(saved);
    }

    private static Label Place(Label l, float x, float y)
    {
        l.Position = new Vector2(x, y);
        return l;
    }

    /// <summary>一行：标题 + 数值 + 滑条。返回下一行的 y。</summary>
    private int AddSlider(string title, float min, float max, float step, float value,
        Action<float> apply, int y)
    {
        var label = UiSkin.MakeLabel(title, 15, UiSkin.Text);
        label.Position = new Vector2(868f, y);
        _panel.AddChild(label);

        var readout = UiSkin.MakeLabel("", 15, UiSkin.Gold);
        readout.Position = new Vector2(1150f, y);
        _panel.AddChild(readout);
        _values.Add(readout);

        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            Position = new Vector2(868f, y + 22f),
            Size = new Vector2(380f, 18f),
        };
        slider.ValueChanged += v =>
        {
            apply((float)v);
            int i = _values.IndexOf(readout);
            readout.Text = FormatValue(i, (float)v);
            RefreshMetrics();
            SaveTuning();
        };
        readout.Text = FormatValue(_values.Count - 1, value);
        _panel.AddChild(slider);
        return y + 54;
    }

    private static string FormatValue(int index, float v) => index switch
    {
        0 => $"{v:F1} 单位/秒",
        1 => $"{v:F1} 单位/秒",
        2 => $"{v:F1} 单位/秒²",
        3 => $"{v:P0}",
        4 => $"{v:F2} 秒",
        _ => $"{v:F2} 秒",
    };

    private void RefreshMetrics()
    {
        if (_metrics == null) return;
        float h = _tuning.JumpHeight;
        float t = _tuning.AirTime;
        float span = _tuning.JumpSpan;
        float layers = h / GameDefs.LayerSpacing;
        _metrics.Text =
            $"理论跳跃高度　{h:F2} 单位（层高 {GameDefs.LayerSpacing:F1} → 约 {layers:F2} 层）\n" +
            $"理论滞空　　　{t:F2} 秒\n" +
            $"水平跨距　　　{span:F2} 单位（满速起跳）\n" +
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
        // 重建 UI 读数：直接还原滑条值
        int i = 0;
        foreach (Node n in _panel.GetChildren())
        {
            if (n is not HSlider s) continue;
            s.Value = i switch
            {
                0 => _tuning.MoveSpeed, 1 => _tuning.JumpSpeed, 2 => _tuning.Gravity,
                3 => _tuning.AirControl, 4 => _tuning.CoyoteTime, _ => _tuning.JumpBuffer,
            };
            i++;
        }
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
            ["derived"] = new Godot.Collections.Dictionary
            {
                ["jump_height"] = Math.Round(_tuning.JumpHeight, 2),
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
        CollisionMask = 1u;
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
        _mover.Step(this, dir, Input.IsActionJustPressed("jump"), dt);

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