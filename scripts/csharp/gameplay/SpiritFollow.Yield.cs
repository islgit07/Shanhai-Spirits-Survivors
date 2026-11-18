using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// SpiritFollow 的「让路 v2 + 落点朝向」partial 部分（2026-11-16 让路系统）：
/// 主角前进方向的通道让位（让到身后 1.25 格）+ 贴太近时径向兜底退开 +
/// 落点整体镜像到主角「身后」的朝向滞回（防左右横跳）。
/// 方法体逐字沿用原单文件实现，仅从 _PhysicsProcess 提为方法（行为零改动）。
/// </summary>
public partial class SpiritFollow
{
    private float _lastDirX = 1f;        // 主角最近一次有效移动方向（+1 右 / -1 左）
    private float _facingX = 1f;         // 落点镜像基准：落点整体放到主角「身后」
    private float _facingHoldT;          // 朝向翻转滞回倒计时

    /// <summary>
    /// 主角速度判断（在往哪走——让路必须让到身后，否则会越让越挡）+ 落点朝向滞回。
    /// 返回本帧落点：横向随朝向镜像，纵向夹紧在「站同一地面就够得到」的范围内
    /// （否则会一直被判定为要跨层 → 原地连跳）。
    /// </summary>
    private Vector2 UpdateFacing(float dt, out bool playerMoving)
    {
        Vector2 playerVel = _target is CharacterBody2D cb ? cb.Velocity : Vector2.Zero;
        playerMoving = Mathf.Abs(playerVel.X) > GameDefs.SpiritYieldMinSpeed;
        if (playerMoving) _lastDirX = Mathf.Sign(playerVel.X);

        _facingHoldT = Mathf.Max(0f, _facingHoldT - dt);
        if (playerMoving && _lastDirX != _facingX && _facingHoldT <= 0f)
        {
            _facingX = _lastDirX;
            _facingHoldT = GameDefs.SpiritFacingHoldTime;
        }
        return new Vector2(
            _slotOffset.X * -_facingX,
            Mathf.Clamp(_slotOffset.Y, -GameDefs.SpiritSlotLiftMax, GameDefs.SpiritSlotDropMax));
    }

    /// <summary>
    /// 让路 v2：清空主角前进方向的「通道」。
    /// 旧版是「从主角径向退开」——主角向左走、随从也在左边时，径向退同样是向左，于是它一路退、
    /// 一路挡在主角前面。新版改成：主角前方通道内 → 让到身后；贴太近 → 径向兜底退开。
    /// </summary>
    private Vector2 ApplyYield(Vector2 pos, Vector2 playerPos, Vector2 wish, bool playerMoving,
        out bool yielding, out float ahead)
    {
        Vector2 relNow = pos - playerPos;
        ahead = relNow.X * _lastDirX;   // > 0 表示位于主角前进方向那一侧
        bool inLane = playerMoving
                      && ahead > -0.15f && ahead < GameDefs.SpiritYieldLaneLength
                      && Mathf.Abs(relNow.Y) < GameDefs.SpiritYieldLaneHalfHeight;
        yielding = false;
        if (inLane)
        {
            wish = playerPos + new Vector2(-_lastDirX * 1.25f, -0.15f);   // 身后 1.25 格，略抬高
            yielding = true;
        }
        else if (relNow.LengthSquared() < GameDefs.SpiritAvoidRadius * GameDefs.SpiritAvoidRadius
                 && relNow.LengthSquared() > 0.0001f)
        {
            wish = pos + relNow.Normalized() * GameDefs.SpiritAvoidRadius;
            yielding = true;
        }
        return wish;
    }
}
