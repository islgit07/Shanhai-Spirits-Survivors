#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""山海幻灵 v0.0003 工程静态自检（离线，不启动 Godot）

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
VERSION = "0.0003"
EXPECTED_CS = 11
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
check("基础键鼠映射（4 action × 1 键，无手柄 / 无方向键）",
      all(a + "={" in pg for a in ("move_left", "move_right", "move_down", "jump"))
      and "InputEventJoypad" not in pg
      and pg.count('"physical_keycode"') == 4)
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
check("开始页 Help 节点保留（按键说明文案允许在编辑器里自由修改）",
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
check("README 记录找回路径", "5978a58" in readme and "docs/archive/v0.003" in readme)
check("overview 全景文档在 docs 根", (DOCS / "overview-v0.003.md").exists())
check("旧文档已归档", (DOCS / "archive" / "v0.003" / "refactor-v0.003.md").exists())

# ---------------------------------------------------------------- 输出
ok_all = all(c["ok"] for c in checks)
report = {"project": "山海幻灵", "version": VERSION, "result": "PASS" if ok_all else "FAIL",
          "checks": checks}
(DOCS / "self-check.json").write_text(json.dumps(report, ensure_ascii=False, indent=2),
                                      encoding="utf-8")
for c in checks:
    print(("PASS " if c["ok"] else "FAIL ") + c["name"] + ("  | " + c["detail"] if c["detail"] else ""))
print(f"\n{sum(c['ok'] for c in checks)}/{len(checks)} checks passed -> docs/self-check.json")
sys.exit(0 if ok_all else 1)
