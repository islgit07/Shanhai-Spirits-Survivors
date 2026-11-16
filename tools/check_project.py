#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""山海幻灵 v0.0005 工程静态自检（离线，不启动 Godot）

检查：工程配置 / 零 autoload / C# 工具链 / 脚本与 UID 配对 /
保留项完整（移动手感常量、GND-001 地板硬约束、白箱、冒烟）/
拆除确认（无战斗/成长/UI 残留引用）/ 零 PNG 白箱链路 / 版本一致。
输出 docs/self-check.json；全过退出 0，任一失败退出 1。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
VERSION = "0.0005"
EXPECTED_CS = 14
checks = []


def check(name, ok, detail=""):
    checks.append({"name": name, "ok": bool(ok), "detail": str(detail)})


def read(path):
    return path.read_text(encoding="utf-8") if path.exists() else ""


def cs(rel):
    return read(ROOT / "scripts" / "csharp" / rel)


# ---------------------------------------------------------------- 1. 工程配置
pg = read(ROOT / "project.godot")
check("project.godot 存在", bool(pg))
check("主场景指向 StartPage.tscn", 'run/main_scene="res://scenes/StartPage.tscn"' in pg)
check("工程名为山海幻灵", 'config/name="山海幻灵"' in pg)
check("已删除全部 autoload", "[autoload]" not in pg)
check("Main.tscn 挂 GameRoot.cs", "res://scripts/csharp/core/GameRoot.cs" in read(ROOT / "scenes" / "Main.tscn"))
check("StartPage.tscn 挂 StartPage.cs", "res://scripts/csharp/ui/StartPage.cs" in read(ROOT / "scenes" / "StartPage.tscn"))
check("2D 渲染 gl_compatibility", 'renderer/rendering_method="gl_compatibility"' in pg)
check(f"config/version = {VERSION}", f'config/version="{VERSION}"' in pg)

# ---------------------------------------------------------------- 2. C# 工具链
csproj_text = read(ROOT / "ShanhaiSpiritsSurvivors.csproj")
check("ShanhaiSpiritsSurvivors.csproj 存在", (ROOT / "ShanhaiSpiritsSurvivors.csproj").exists())
check("使用 Godot.NET.Sdk/4.7.2", "Godot.NET.Sdk/4.7.2" in csproj_text)
check("目标框架 net8.0", "<TargetFramework>net8.0</TargetFramework>" in csproj_text)
check("C# 运行时探针存在", (ROOT / "tools" / "CSharpToolchainProbe.cs").exists())

# ---------------------------------------------------------------- 3. 脚本与 UID
cs_files = sorted((ROOT / "scripts").rglob("*.cs"))
gd_files = sorted((ROOT / "scripts").rglob("*.gd"))
check(f"C# 脚本数量 = {EXPECTED_CS}", len(cs_files) == EXPECTED_CS, f"{len(cs_files)} 个")
check("零 GDScript", len(gd_files) == 0, f"{len(gd_files)} 个")
missing_uid = [str(p.relative_to(ROOT)) for p in cs_files
               if not p.with_suffix(p.suffix + ".uid").exists()]
check("全部 .cs 配对 .uid", not missing_uid, ",".join(missing_uid))

# ---------------------------------------------------------------- 4. 保留项：移动手感
defs = cs("core/GameDefs.cs")
for key, label in [
    ("MasterMoveSpeed", "移动速度"),
    ("MasterJumpSpeed", "跳跃初速"),
    ("MasterJumpCutMul", "变高跳截断"),
    ("MasterFallGravityMul", "下落加重"),
    ("MasterApexGravityMul", "顶点滞空"),
    ("MasterMaxFallSpeed", "下落限速"),
    ("MasterCoyoteTime", "土狼时间"),
    ("MasterJumpBuffer", "跳跃缓冲"),
    ("MasterDropThroughTime", "下穿窗口"),
]:
    check(f"手感常量保留：{label}", re.search(rf"public const float {key} =", defs) is not None)
mover = cs("gameplay/PlatformMover.cs")
check("移动内核保留（PlatformMover + MoveAndSlide + 下穿）",
      "class PlatformMover" in mover and "MoveAndSlide" in mover and "DropThrough" in mover)
check("移动内核：上攀总开关走 MoveTuning（白箱可单独开）",
      "AutoClimbEnabled" in mover and "T.AutoClimbEnabled" in mover)
check("自动上攀：几何台沿探测（Ledge 结构 + ComputeClimb 纯几何方法）",
      "struct Ledge" in mover and "ComputeClimb" in mover)
check("自动上攀：翻越吸附写入移动内核（TryClimb + 移动后判定）",
      "TryClimb" in mover and "onFloorNow" in mover)
check("自动上攀：攀爬参数常量（ClimbReachUp / ClimbEdgeBand / ClimbStep）",
      "MasterClimbReachUp" in defs and "MasterClimbEdgeBand" in defs and "MasterClimbStep" in defs)
check("基础键鼠映射（4 action × 1 键，键盘保留）",
      all(a + "={" in pg for a in ("move_left", "move_right", "move_down", "jump"))
      and pg.count('"physical_keycode"') == 4)
# 手柄：键盘与手柄共用同一 action，游戏代码零改动（input-handling §5 推荐做法）。
# 校验具体枚举值与方向，避免只有字符串「InputEventJoypad」出现的假阳性。
def _action_events(action):
    start = pg.find(action + "={")
    end = pg.find("\n}\n", start)
    return pg[start:end] if start >= 0 and end > start else ""

_left_ev = _action_events("move_left")
_right_ev = _action_events("move_right")
_down_ev = _action_events("move_down")
_jump_ev = _action_events("jump")
check("手柄映射：move_left = 十字键左(13) + 左摇杆左(axis0,-1)",
      '"button_index":13' in _left_ev and '"axis":0' in _left_ev and '"axis_value":-1.0' in _left_ev)
check("手柄映射：move_right = 十字键右(14) + 左摇杆右(axis0,+1)",
      '"button_index":14' in _right_ev and '"axis":0' in _right_ev and '"axis_value":1.0' in _right_ev)
check("手柄映射：move_down = 十字键下(12) + 左摇杆下(axis1,+1)",
      '"button_index":12' in _down_ev and '"axis":1' in _down_ev and '"axis_value":1.0' in _down_ev)
check("手柄映射：jump = A 键(0)",
      '"button_index":0' in _jump_ev)
check("手柄映射：每个 action 的事件数（左/右/下 3 个，跳跃 2 个）",
      _left_ev.count("Object(InputEvent") == 3
      and _right_ev.count("Object(InputEvent") == 3
      and _down_ev.count("Object(InputEvent") == 3
      and _jump_ev.count("Object(InputEvent") == 2)
check("手柄映射：事件类型为 Godot 4 joypad button / motion",
      pg.count("Object(InputEventJoypadButton") == 4
      and pg.count("Object(InputEventJoypadMotion") == 3)

# ---------------------------------------------------------------- 地形类型系统（MAP-TYPE）
cfg_cs = cs("gameplay/MapGenConfig.cs")
igen_cs = cs("gameplay/IMapGenerator.cs")
defgen_cs = cs("gameplay/DefaultMapGenerator.cs")
field = cs("gameplay/BlockField.cs")
check("地形系统：配置卡片类 [GlobalClass] : Resource 且 28 个行为参数可编辑",
      "[GlobalClass]" in cfg_cs and "partial class MapGenConfig : Resource" in cfg_cs
      and cfg_cs.count("[Export] public") >= 28)
check("地形系统：策略接口 IMapGenerator 定义 Generate(field, seed, config)",
      "interface IMapGenerator" in igen_cs and "void Generate" in igen_cs)
check("地形系统：默认生成器实现接口并转交 Build(seed, config)",
      "class DefaultMapGenerator : IMapGenerator" in defgen_cs
      and "field.Build(seed, config)" in defgen_cs)
check("地形系统：默认配置卡片存在且被 Godot 挂上 MapGenConfig 脚本",
      (ROOT / "resources" / "maps" / "default.tres").exists()
      and 'script_class="MapGenConfig"' in read(ROOT / "resources" / "maps" / "default.tres")
      and "MapGenConfig.cs" in read(ROOT / "resources" / "maps" / "default.tres"))
# 卡片必须显式承载数值（不是 Godot 省略默认值的空壳），且关键项 == GameDefs 口径
_card = read(ROOT / "resources" / "maps" / "default.tres")
check("地形系统：配置卡片显式写入全部 28 个数值（所见即所得）",
      sum(1 for line in _card.splitlines() if "=" in line and not line.startswith("[") and "script" not in line) >= 28)
check("地形系统：卡片默认净空率 0.8 / 路径 2~3 / 粘液权重 0.01 与 GameDefs 一致",
      "BlockVerticalClearRatio = 0.8" in _card and "BackbonePathMin = 2" in _card
      and "BackbonePathMax = 3" in _card and "BlockWeightSlime = 0.01" in _card)
check("地形系统：BlockField 读 config 而非写死 GameDefs 行为参数",
      "MapGenConfig _cfg" in field and "Build(int seedValue, MapGenConfig config)" in field
      and field.count("_cfg.") >= 28)
check("地形系统：保留旧 Build(seed) 入口（探针/既有调用零改动）",
      "Build(int seedValue) => Build(seedValue" in field or "void Build(int seedValue)" in field)
check("地形系统：两个调用方走 IMapGenerator.Generate 且从卡片加载配置",
      "new DefaultMapGenerator()" in cs("core/GameRoot.cs")
      and "new DefaultMapGenerator()" in cs("tools/WhiteboxRoot.cs")
      and "GD.Load<MapGenConfig>(DefaultMapConfigPath)" in cs("core/GameRoot.cs"))
check("地形系统：网格维度/坐标基准不接 config（仍在字段初始化用 GameDefs）",
      "new BlockType[GameDefs.BlockRows, GameDefs.BlockCols]" in field
      and "-GameDefs.WorldHalfWidth" in field)
check("主角只有移动跳跃（Master 无 TakeDamage）",
      "TakeDamage" not in cs("gameplay/Master.cs"))

# ---------------------------------------------------------------- 5. 保留项：地图生成
field = cs("gameplay/BlockField.cs")
check("GND-001 地面层全覆盖硬约束", "BlockType.Normal, false" in field
      and "IsGroundContinuous" in field)
check("种子化地形（Rng.DeriveSeed64）", "DeriveSeed64" in field)
oneway_tscn = read(ROOT / "scenes" / "blocks" / "block_oneway.tscn")
check("单向平台独立碰撞层（DROP-001 下穿）",
      "collision_layer = 2" in oneway_tscn and "one_way_collision = true" in oneway_tscn)
check("方块随机分布（run 长度/空档/类型权重，MAP-003）",
      "BlockRunMax" in field and "BlockGapMax" in field and "PickType" in field)
check("自动上攀：地形导出可攀台沿（BlockField.GetLedges）", "GetLedges" in field)
check("自动上攀：主角接入台沿（Master.SetLedges）", "SetLedges" in cs("gameplay/Master.cs"))
check("MAP-005 上下净空常量（2 行 / 2 列 / 80% 达标 / 20% 破例）",
      all(k in defs for k in ("BlockVerticalClearRows", "BlockVerticalClearCols",
                              "BlockVerticalClearRatio", "BlockVerticalClearBreakChance"))
      and "BlockVerticalClearRatio = 0.80f" in defs)
check("MAP-005 生成期拒绝贴太近的装饰块（骨架豁免）",
      "HasNearVerticalNeighbor" in field
      and "BlockVerticalClearBreakChance" in field and "!purePlatform" in field)
check("MAP-005 末尾统计合规率 + 冒烟断言 >= 80%",
      "MeasureVerticalClearance" in field
      and "vertical_clear_80pct" in cs("core/GameRoot.cs"))
check("MAP-005 骨架层间距 3 行（中间空 2 格）", "LayerStepRows = 3" in defs)
check("MAP-005 不达标时修剪装饰块（骨架 / 地面受保护）",
      "EnforceVerticalClearance" in field and "_backboneCell" in field)
check("MAP-006 尽量少出现单个方块（BlockRunMin=2 + PruneIsolatedSingles + 断言）",
      "BlockRunMin = 2" in defs and "PruneIsolatedSingles" in field
      and "few_single_blocks" in cs("core/GameRoot.cs"))
check("MAP-007 地面到顶层完整路径验证与兜底（EnsureFullPathToTop + ComputeTrueReachability）",
      "EnsureFullPathToTop" in field and "ComputeTrueReachability" in field
      and "path_to_top_ok" in cs("core/GameRoot.cs"))
check("MAP-008 生成 2~3 条独立骨架路径（列带隔离 + 多路径断言）",
      "BackbonePathMin = 2" in defs and "BackbonePathMax = 3" in defs
      and "_paths" in field and "bandLeft" in field
      and "multi_path_ok" in cs("core/GameRoot.cs"))
check("开发用地图种子徽章（UiSkin + 游戏 / 白箱挂载 + 换种子刷新）",
      "MakeSeedBadge" in cs("ui/UiSkin.cs")
      and "MakeSeedBadge" in cs("core/GameRoot.cs")
      and "RefreshSeed" in cs("tools/WhiteboxRoot.cs")
      and "seed_ui_ok" in cs("core/GameRoot.cs"))
# ---------------------------------------------------------------- 5b. MAP-009 粘液块
root_cs = cs("core/GameRoot.cs")
probe_cs = read(ROOT / "tools" / "MapSeedProbe.cs")
slime_tscn = read(ROOT / "scenes" / "blocks" / "block_slime.tscn")
check("MAP-009 粘液块常量（1% / 组名 / 上限 3 / 减速 / 黏跳 / 黏回）",
      all(k in defs for k in ("BlockWeightSlime", "SlimeBlockGroup", "BlockSlimeMaxCount",
                              "SlimeSpeedMul", "SlimeAccelMul", "SlimeHopSpeed",
                              "SlimeHopGravity", "SlimeSnapGravity", "SlimeSnapTime",
                              "SlimeHopApexMax"))
      and "BlockWeightSlime = 0.01f" in defs
      and "BlockSlimeMaxCount = 3" in defs)
check("MAP-009 粘液块预制场景（零 PNG / 实体碰撞层 / 运行时纯色）",
      (ROOT / "scenes" / "blocks" / "block_slime.tscn").exists()
      and ".png" not in slime_tscn
      and "collision_layer = 1" in slime_tscn
      and "modulate = Color(0.36, 0.78, 0.42, 1)" in slime_tscn)
check("MAP-009 生成端：极低权重 + 硬上限 + 每图保底 1 块 + 最终统计",
      "BlockType.Slime" in field and "BlockWeightSlime" in field
      and "BlockSlimeMaxCount" in field and "EnsureSlimePresence" in field
      and "CountSlimeBlocks" in field)
check("MAP-009 实例化端：粘液碰撞体打组并计数（分组数 == 类型数）",
      "AddToGroup(SlimeGroup)" in field and "SlimeGroupedCount++" in field
      and "SlimeGroupedCount == field.SlimeCount" in probe_cs)
check("MAP-009 移动内核：脚下组识别 + 减速 + 极小起跳 + 黏回重力",
      "ComputeSlimeVertical" in mover and "ComputeSlimeTargetSpeed" in mover
      and "SlimeSnapGravity" in mover and "_slimeSnap" in mover
      and "IsInGroup(SlimeGroup)" in mover)
check("MAP-009 冒烟：粘液块传送 + 数量/减速/黏跳/黏回四条断言",
      "GetFirstSlime" in field and "SmokeTeleportToSlime" in root_cs
      and all(k in root_cs for k in ("slime_blocks_ok", "slime_slow_ok",
                                     "slime_no_jump_ok", "slime_snap_ok")))
check("MAP-009 黏跳模型单一来源（移动内核供冒烟 / 探针共用）",
      "SimulateSlimeHop" in mover and "SimulateSlimeHop" in root_cs
      and "SimulateSlimeHop" in probe_cs)
check("MAP-009 多种子探针校验粘液数量与分组 + 黏跳纯逻辑",
      "field.SlimeCount" in probe_cs and "field.SlimeGroupedCount" in probe_cs
      and "[SLIME_PROBE]" in probe_cs)
check("MAP-009 白箱图例列出粘液块与出现概率",
      "BlockField.Tint(BlockType.Slime)" in cs("tools/WhiteboxRoot.cs")
      and "粘液 1%" in cs("tools/WhiteboxRoot.cs"))
check("MAP-005 多种子探针存在", (ROOT / "tools" / "MapSeedProbe.cs").exists())

# ---------------------------------------------------------------- 5c. BUGFIX-FRAGILE 脆弱木板
fragile_tscn = read(ROOT / "scenes" / "blocks" / "block_fragile.tscn")

def fragile_touch_crosses_top(tscn):
    # 方块 1×1、中心 (0,0) → 顶面 y = -0.5。感应区必须「跨过」-0.5：
    # 完全埋在方块内部时，站在顶面的玩家碰撞体全在上方，BodyEntered 永不触发（踩不碎）。
    # 这里算几何而不是锁死具体数值，后续调整感应区尺寸只要仍跨顶面就保持 PASS。
    try:
        seg = tscn.split('id="touch_rect"')[1]
        sy = float(seg.split("size = Vector2(1, ")[1].split(")")[0])
        py = float(seg.split("position = Vector2(0, ")[1].split(")")[0])
    except (IndexError, ValueError):
        return False
    return (py - sy * 0.5) < -0.5 < (py + sy * 0.5)

check("BUGFIX-FRAGILE 感应区跨过方块顶面（几何行为断言，非锁死数值）",
      bool(fragile_tscn) and fragile_touch_crosses_top(fragile_tscn)
      and "[gd_scene load_steps=3 format=3" in fragile_tscn
      and ".png" not in fragile_tscn and "collision_mask = 1" in fragile_tscn)
check("BUGFIX-FRAGILE 生成 / 接线 / 碎块三项计数可观测",
      all(k in field for k in ("FragileCount", "FragileTouchWiredCount", "FragileBrokenCount"))
      and "FragileTouchWiredCount++" in field and "FragileBrokenCount++" in field)
def fragile_method_body(src, name, next_name):
    a = src.find(f"private void {name}")
    b = src.find(f"private void {next_name}", a)
    # 截掉后一个方法的 XML 注释，避免注释里的字样误判为方法体
    return src[a:b].split("/// <summary>")[0] if a >= 0 and b > a else ""


_start_body = fragile_method_body(field, "StartFragileBlink", "FinishFragileBreak")
_finish_body = fragile_method_body(field, "FinishFragileBreak", "BlinkThenFree")
check("BUGFIX-FRAGILE 物理回调内不做同步 RemoveChild（延迟释放统一收口）",
      bool(_start_body)
      and all(op not in _start_body for op in ("RemoveChild", "QueueFree", "DeferredRemove"))
      and "DeferredRemove(n)" in field and "DeferredRemove(body)" in field
      and "{ RemoveChild(n); n.QueueFree(); }" not in field)
check("BUGFIX-FRAGILE 冒烟用真实自由落下验证踩碎（传送 + 断言 + 报告字段）",
      "GetFirstFragile" in field and "SmokeTeleportToFragile" in root_cs
      and "fragile_break_ok" in root_cs and "fragile_broken" in root_cs
      and "_fragileBrokenBefore" in root_cs)
check("BUGFIX-FRAGILE 多种子探针校验感应区接线完整",
      "field.FragileTouchWiredCount == field.FragileCount" in probe_cs)
check("BUGFIX-FRAGILE 感应区只保留预制体单份实现（动态 AddTouchArea 死代码已删）",
      "AddTouchArea" not in field and 'GetNodeOrNull<Area2D>("Touch")' in field)

# ---------------------------------------------------------------- 5d. FRAGILE-BLINK 闪时可站 / 闪完才碎
_gamedefs = cs("core/GameDefs.cs")
_blink_body = read(ROOT / "scripts" / "csharp" / "gameplay" / "BlockField.cs")
_blink_root = cs("core/GameRoot.cs")
check("FRAGILE-BLINK 时长 1.5 秒 + 3 次白闪（GameDefs 常量）",
      "FragileBlinkDuration = 1.5f" in _gamedefs
      and "FragileBlinkCycles = 3" in _gamedefs)
check("FRAGILE-BLINK 视觉节点绑定 Sprite 的 Tween，闪烁结束回调才判碎",
      "BlinkThenFree(r, c, _nodes[r, c], _visuals[r, c])" in _start_body
      and "visual.CreateTween()" in _blink_body
      and "Colors.White" in _blink_body and "警示红" not in _blink_body
      and '"modulate", flashTint' in _blink_body
      and '"modulate", baseTint' in _blink_body
      and "FinishFragileBreak(r, c, body)" in _blink_body)
_blink_method = fragile_method_body(field, "BlinkThenFree", "Jelly")
check("FRAGILE-BLINK 只闪颜色、绝不缩放（无 scale / parallel 补间）",
      bool(_blink_method)
      and '"scale"' not in _blink_method
      and "Parallel" not in _blink_method
      and _blink_method.count('"modulate"') == 2)
check("FRAGILE-BLINK 闪烁期间方块仍是实体、可站立（不动碰撞 / 不计数 / 不摘 _alive）",
      bool(_start_body) and bool(_finish_body)
      and all(op not in _start_body for op in (
          "CollisionLayer", "CollisionMask", "Monitoring",
          "_alive[r, c] = false", "BlockCount--", "FragileBrokenCount++"))
      and "_blinking[r, c] = true" in _start_body
      and "if (!_alive[r, c] || _blinking[r, c]) return;" in _start_body
      and all(op in _finish_body for op in (
          "_alive[r, c] = false", "BlockCount--", "FragileBrokenCount++",
          "FragileBlinkFinishedCount++", "DeferredRemove(body)")))
check("FRAGILE-BLINK 起止计数可观测 + 冒烟验证闪时可站 / 闪完才碎",
      all(k in field for k in ("FragileBlinkStartedCount", "FragileBlinkFinishedCount"))
      and "FragileBlinkStartedCount++" in field and "FragileBlinkFinishedCount++" in field
      and "fragile_blink_ok" in _blink_root
      and "fragile_blink_finished_ok" in _blink_root
      and "fragile_standable_during_blink" in _blink_root
      and "_fragileStoodDuringBlink" in _blink_root
      and "_fragileBlocksBefore" in _blink_root
      and "fragile_blink_duration" in _blink_root)
check("FRAGILE-BLINK 白箱图例同步为闪时可站 / 1.5 秒后才碎",
      "白闪 3 次 / 1.5 秒后才碎（闪时可站立）" in cs("tools/WhiteboxRoot.cs"))

# ---------------------------------------------------------------- 5e. BOUNCE-JELLY 蹦蹦床动画
_jelly_body = fragile_method_body(field, "Jelly", "SpawnBounceRing")
_ring_body = fragile_method_body(field, "SpawnBounceRing", "OpenTreasure")
check("BOUNCE-JELLY 三段式参数集中在 GameDefs（压缩 / 回弹 / 余震 / 冲击环）",
      all(k in _gamedefs for k in (
          "BounceSquashTime", "BounceReleaseTime", "BounceSettleTime",
          "BounceSquashX", "BounceSquashY", "BounceStretchX", "BounceStretchY",
          "BounceDipY", "BounceLiftY", "BounceRingStart", "BounceRingEnd", "BounceRingTime")))
check("BOUNCE-JELLY 三段式：压扁 → 过冲回弹 → Elastic 余震 + 高亮回落",
      bool(_jelly_body)
      and all(k in _jelly_body for k in (
          "BounceSquashTime", "BounceReleaseTime", "BounceSettleTime",
          "Tween.TransitionType.Back", "Tween.TransitionType.Elastic",
          '"position:y"', '"modulate"', "flashTint", "SpawnBounceRing(r, c)", "BounceJellyStartedCount++"))
      and "if (visual == null || _jellyBusy[r, c]) return;" in _jelly_body)
check("BOUNCE-JELLY 只动 Visual 子节点，碰撞 / 弹射 / 地图几何零改动",
      bool(_jelly_body)
      and "visual.CreateTween()" in _jelly_body
      and all(op not in _jelly_body for op in (
          "CollisionLayer", "CollisionMask", "GlobalPosition", "Velocity", "RemoveChild", "QueueFree")))
check("BOUNCE-JELLY 冲击环是程序化 Line2D，零 PNG / 零新资源 / 播完自释放",
      bool(_ring_body)
      and "new Line2D" in _ring_body
      and "ring.Points = points" in _ring_body
      and '"modulate:a"' in _ring_body
      and "SetParallel(true)" in _ring_body
      and "Chain().TweenCallback" in _ring_body
      and "ring.QueueFree" in _ring_body
      and ".png" not in _ring_body)
check("BOUNCE-JELLY 冒烟：真实落下触发动画 + 玩家被弹起（含跳过兜底）",
      "GetFirstBounce" in field and "SmokeTeleportToBounce" in _blink_root
      and "bounce_jelly_ok" in _blink_root
      and "bounce_jelly_seen" in _blink_root
      and "bounce_launch_rise" in _blink_root)
check("BOUNCE-JELLY 白箱图例同步为蹦蹦床三段动画",
      "蹦蹦床压扁→回弹→余震 + 冲击环" in cs("tools/WhiteboxRoot.cs"))
_bounce_tscn = read(ROOT / "scenes" / "blocks" / "block_bounce.tscn")
check("BOUNCE-JELLY 感应区跨过方块顶面（否则站在顶面永远触发不了弹跳）",
      fragile_touch_crosses_top(_bounce_tscn)
      and "collision_mask = 1" in _bounce_tscn and ".png" not in _bounce_tscn)
_mover_cs = cs("gameplay/PlatformMover.cs")
check("BOUNCE-JELLY 弹射不再被贴地清零：内核一次性空中标记 + 两个玩家统一走内核",
      "ApplyExternalLaunch" in _mover_cs
      and "_externalLaunch" in _mover_cs
      and "Grounded = body.IsOnFloor() && !launched" in _mover_cs
      and all("_mover.ApplyExternalLaunch(this, GameDefs.BounceSpeed)" in p
              for p in (cs("gameplay/Master.cs"), cs("tools/WhiteboxPlayer.cs"))))

check("Build 可重复调用时清空净空标记 _reserved", "Array.Clear(_reserved)" in field)
check("世界根保留相机跟随", "Lerp" in cs("core/GameRoot.cs"))
check("白箱保留（与游戏共用移动内核）",
      (ROOT / "scenes" / "Whitebox.tscn").exists()
      and "PlatformMover" in cs("tools/WhiteboxRoot.cs"))

# ---------------------------------------------------------------- 6. 零 PNG 白箱链路 / 开始页
whitebox = read(ROOT / "scenes" / "Whitebox.tscn")
block_scenes = "".join(read(p) for p in sorted((ROOT / "scenes" / "blocks").glob("*.tscn")))
loot_scene = read(ROOT / "scenes" / "fx" / "loot_drop.tscn")
check("白箱零 PNG：Whitebox.tscn 无贴图引用",
      ".png" not in whitebox and "Texture2D" not in whitebox)
check("白箱零 PNG：共用方块 / 掉落预制体无贴图引用",
      ".png" not in (block_scenes + loot_scene))
check("纯色纹理由 ArtBank 运行时生成（不依赖资源文件）",
      "PlainTexture" in cs("core/ArtBank.cs") and "PlainTexture" in field)
start = cs("ui/StartPage.cs")
start_tscn = read(ROOT / "scenes" / "StartPage.tscn")
check("开始页键盘导航（grab_focus + 上下焦点链）",
      "GrabFocus" in start and "FocusNeighbor" in start)
check("开始页 Help 节点保留（按键说明另处实现，不锁死文案）",
      'node name="Help" type="Label"' in start_tscn)
check("--smoke 从开始页自动进游戏（不挂死）",
      "IsSmokeRequested" in start and "ChangeSceneToFile" in start)

# ---------------------------------------------------------------- 7. 拆除确认
all_cs = "".join(read(p) for p in cs_files)
for gone in ["SpeciesDef", "OfferKind", "LegionUnit", "WildSpawner", "EssencePickup",
             "Projectile", "RunPhase", "SpiritTier", "class Hud", "class MainMenu",
             "static class Meta", "static class Run ", "class Signals"]:
    check(f"战斗/成长/UI 已拆除：{gone}", gone not in all_cs)

# ---------------------------------------------------------------- 8. 版本一致
readme = read(ROOT / "README.md")
check("README 版本一致", f"v{VERSION}" in readme)
# 注：原「README 记录找回路径（5978a58 / docs/archive/v0.003）」检查已移除——
# 远程提交 40c9ce1 简化了 README 更新说明并删掉 History 行，用户拍板按远程 README 为准，
# 故不再要求 README 保留找回路径；归档事实仍在 docs/overview-v0.003.md 中记录。
overview = read(DOCS / "overview-v0.003.md")
# 不只验「文件存在」：旧战斗版 overview 也曾存在，必须验内容确实是 0.0003 回退后基线。
check("overview 全景文档反映 0.0003 回退后基线（非旧战斗版）",
      bool(overview)
      and "0.0003 回退后基线" in overview
      and "ShanhaiSpiritsSurvivors" in overview
      and "PlatformMover" in overview
      and "MAP-009" in overview
      and "无静态状态类" in overview
      and "两个工程是什么关系" not in overview
      and "灵韵 → 唤灵三分支" not in overview)
check("旧文档已归档", (DOCS / "archive" / "v0.003" / "refactor-v0.003.md").exists())

# ---------------------------------------------------------------- 9. 工程元配置
# 本机 core.autocrlf=true 曾让 .tscn / .cs 在工作区出现 CRLF 与 LF 混用，
# git 还会反复告警 "LF will be replaced by CRLF"。规则见 .gitattributes。
ga = read(ROOT / ".gitattributes")
check(".gitattributes 存在且统一 LF 行尾",
      bool(ga) and "* text=auto eol=lf" in ga and "*.tscn text eol=lf" in ga)
check(".gitattributes 把图片标为二进制（不参与行尾转换）", "*.png binary" in ga)
text_exts = {".cs", ".tscn", ".tres", ".godot", ".cfg", ".import", ".uid",
             ".py", ".json", ".md", ".txt", ".csproj", ".gd", ".gdshader"}
crlf_files = []
for p in ROOT.rglob("*"):
    if not p.is_file() or any(s in p.parts for s in (".git", ".godot", "bin", "obj")):
        continue
    if p.suffix not in text_exts and p.name not in (".gitattributes", ".gitignore", ".gdignore"):
        continue
    if b"\r\n" in p.read_bytes():
        crlf_files.append(str(p.relative_to(ROOT)))
check("工作区文本文件无 CRLF 残留", not crlf_files, ",".join(crlf_files[:3]))

# ---------------------------------------------------------------- 输出
ok_all = all(c["ok"] for c in checks)
report = {"project": "山海幻灵", "version": VERSION, "result": "PASS" if ok_all else "FAIL",
          "checks": checks}
# 显式 newline="\n"：Windows 上 write_text/open 默认会把 \n 翻成 \r\n，
# 让本脚本自己产出的 docs/self-check.json 触发上面「无 CRLF 残留」检查（自检自伤）。
with (DOCS / "self-check.json").open("w", encoding="utf-8", newline="\n") as f:
    f.write(json.dumps(report, ensure_ascii=False, indent=2))
for c in checks:
    print(("PASS " if c["ok"] else "FAIL ") + c["name"] + ("  | " + c["detail"] if c["detail"] else ""))
print(f"\n{sum(c['ok'] for c in checks)}/{len(checks)} checks passed -> docs/self-check.json")
sys.exit(0 if ok_all else 1)
