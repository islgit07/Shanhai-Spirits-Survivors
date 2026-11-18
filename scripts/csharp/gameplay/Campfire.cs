using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// @trace NIGHT-001 篝火（v0.0008）：昼夜循环的开关，固定在出生点（GND-001 地面连续）。
/// 交互 = 新增 interact action（E 键，用户指定新交互键）：
///   · 白天 + 篝火旁（CampfireRange 内）+ 按下 E → IgniteRequested（GameRoot 转发给 NightDirector 点燃黑夜）
///   · 黑夜 + 篝火旁 + 按住 E 持续 CampfireHoldTime 秒且期间不受伤 → DawnPrayerCompleted（祈祷天亮）
///     受伤（Master.HealthChanged）立即重置进度——「不受伤长按」是设计核心，不是手感装饰。
/// 视觉全程序化零 PNG：木堆 = 两根 ArtBank 棕色方木交叉，火焰 = CpuParticles2D（点燃才发射）。
/// 本节点只管交互与外观；昼夜状态与波次归 NightDirector，信号向上由 GameRoot 裁决。
/// </summary>
public partial class Campfire : Node2D
{
    private Master? _player;
    private bool _playerInZone;
    private bool _lit;
    private float _holdTime;
    private Sprite2D? _logA;
    private Sprite2D? _logB;
    private CpuParticles2D? _flame;

    /// <summary>篝火是否点燃（黑夜进行中的视觉口径）。</summary>
    public bool Lit => _lit;

    /// <summary>祈祷是否进行中（HUD 进度条显隐）。</summary>
    public bool HoldActive { get; private set; }

    /// <summary>祈祷进度（0..1，HUD 进度条）。</summary>
    public float HoldProgress => Mathf.Clamp(_holdTime / GameDefs.CampfireHoldTime, 0f, 1f);

    /// <summary>点燃请求（白天 + 篝火旁 + 按下 E；GameRoot 转发给 NightDirector.Ignite）。</summary>
    [Signal] public delegate void IgniteRequestedEventHandler();

    /// <summary>祈祷完成（黑夜 + 长按不受伤满 CampfireHoldTime；GameRoot 转发给 NightDirector.Dawn）。</summary>
    [Signal] public delegate void DawnPrayerCompletedEventHandler();

    private static readonly StringName InteractAction = new("interact");

    public override void _Ready()
    {
        // 木堆：两根方木交叉（ArtBank 白纹理 + modulate 上色，与全局零 PNG 口径一致）
        _logA = ArtBank.MakeBox(new Vector2(0.55f, 0.12f), new Color(0.42f, 0.27f, 0.14f));
        _logA.RotationDegrees = 24f;
        AddChild(_logA);
        _logB = ArtBank.MakeBox(new Vector2(0.55f, 0.12f), new Color(0.35f, 0.22f, 0.12f));
        _logB.RotationDegrees = -24f;
        AddChild(_logB);

        // 火焰：橙红粒子向上飘（点燃才发射）
        _flame = new CpuParticles2D
        {
            Position = new Vector2(0f, -0.18f),
            Amount = 26,
            Lifetime = 0.6f,
            Emitting = false,
            Direction = new Vector2(0f, -1f),
            Spread = 18f,
            Gravity = new Vector2(0f, -90f),
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 1.1f,
            ScaleAmountMin = 0.05f,
            ScaleAmountMax = 0.12f,
            Color = new Color(1.0f, 0.62f, 0.18f, 0.9f),
            Texture = ArtBank.PlainTexture(),
        };
        AddChild(_flame);

        // 感应区代码构建（Campfire 直接挂在 Main.tscn，无独立场景文件）
        var zone = new Area2D { CollisionLayer = 0, CollisionMask = GameDefs.LayerActor };
        zone.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = GameDefs.CampfireRange } });
        AddChild(zone);
        zone.BodyEntered += OnZoneEntered;
        zone.BodyExited += OnZoneExited;
    }

    /// <summary>绑定交互主体（GameRoot 调用；受伤重置监听接在 Master.HealthChanged 上）。</summary>
    public void Bind(Master player)
    {
        _player = player;
        player.HealthChanged += OnPlayerHealthChanged;
    }

    /// <summary>点燃 / 熄灭火焰与状态（GameRoot 在昼夜状态切换后调用）。</summary>
    public void SetLit(bool lit)
    {
        _lit = lit;
        if (_flame != null) _flame.Emitting = lit;
        if (!lit) _holdTime = 0f;
    }

    /// <summary>重置为白天（每局开始时调用）。</summary>
    public void ResetToDay() => SetLit(false);

    public override void _PhysicsProcess(double delta)
    {
        HoldActive = false;
        if (_player == null || !IsInstanceValid(_player) || _player.Dead)
        {
            _holdTime = 0f;
            return;
        }
        if (!_playerInZone)
        {
            _holdTime = 0f;
            return;
        }

        if (!_lit)
        {
            // 白天：按下即点燃（不需要长按——开战是玩家主动选择）
            if (Input.IsActionJustPressed(InteractAction))
                EmitSignal(SignalName.IgniteRequested);
            return;
        }

        // 黑夜：按住 E 累计祈祷；松手缓慢回落；受伤（HealthChanged）立即清零
        if (Input.IsActionPressed(InteractAction))
        {
            HoldActive = true;
            _holdTime += (float)delta;
            if (_holdTime >= GameDefs.CampfireHoldTime)
            {
                _holdTime = 0f;
                HoldActive = false;
                EmitSignal(SignalName.DawnPrayerCompleted);
            }
        }
        else
        {
            _holdTime = Mathf.Max(0f, _holdTime - (float)delta * 2f);   // 松手快速回落
        }
    }

    private void OnZoneEntered(Node body)
    {
        if (body == _player) _playerInZone = true;
    }

    private void OnZoneExited(Node body)
    {
        if (body == _player)
        {
            _playerInZone = false;
            _holdTime = 0f;   // 离开篝火 = 祈祷中断
        }
    }

    private void OnPlayerHealthChanged(int hp) => _holdTime = 0f;   // 受伤重置祈祷进度（设计核心）
}
