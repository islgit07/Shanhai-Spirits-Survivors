using System;
using Godot;

namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 命令行冒烟 / 截图开关的统一查询：--smoke 与各子冒烟（--follow-smoke / --pause-smoke /
/// --offer-smoke）以及 --shot 共用。Godot 把引擎参数与用户参数分开
/// （「--」之后为 user args），两类都要查；此前 StartPage / SpiritSelect / UpgradeScreen /
/// PauseMenu / CompanionOffer / SpiritFollowGroup / GameRoot 各写了一份，收口到本工具。
/// 无状态纯函数，沿用 ArtBank / Rng 的静态工具范式，零 autoload。
/// </summary>
public static class SmokeFlags
{
    public static bool Has(string flag) =>
        Array.IndexOf(OS.GetCmdlineArgs(), flag) >= 0
        || Array.IndexOf(OS.GetCmdlineUserArgs(), flag) >= 0;
}
