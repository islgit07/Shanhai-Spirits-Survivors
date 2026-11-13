#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""山海幻灵 v0.0002 工程静态自检（离线，不启动 Godot）

检查：工程配置 / 零 autoload / C# 工具链 / 脚本与 UID 配对 /
保留项完整（移动手感常量、GND-001 地板硬约束、白箱、冒烟）/
拆除确认（无战斗/成长/UI 残留引用）/ 版本一致。
输出 docs/self-check.json；全过退出 0，任一失败退出 1。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
VERSION = "0.0002"
EXPECTED_CS = 10
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
csproj_text = read(ROOT / "ShanHaiHuanLing.csproj")
check("ShanHaiHuanLing.csproj 存在", (ROOT / "ShanHaiHuanLing.csproj").exists())
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
boot = cs("core/GameRoot.cs")
check("手柄输入（摇杆 / 十字键 / A 键跳跃）",
      "JoyAxis.LeftX" in boot and "JoyButton.DpadLeft" in boot and "JoyButton.A" in boot)
check("主角只有移动跳跃（Master 无 TakeDamage）",
      "TakeDamage" not in cs("gameplay/Master.cs"))

# ---------------------------------------------------------------- 5. 保留项：地图生成
field = cs("gameplay/PlatformField.cs")
check("GND-001 第一层全覆盖硬约束", "AddSegment(new Segment(0, -WorldHalfWidth, WorldHalfWidth" in field
      and "IsGroundContinuous" in field)
check("种子化地形（Rng.DeriveSeed64）", "DeriveSeed64" in field)
check("单向平台独立碰撞层（DROP-001 下穿）", "LayerOneWay" in field and "OneWayCollision" in field)
check("平台随机分布（台数/位置/层高浮动，MAP-002）",
      "RandiRange" in field and "LayerJitterY" in field and "MinGap" in field)
check("世界根保留相机跟随", "Lerp" in cs("core/GameRoot.cs"))
check("白箱保留（与游戏共用移动内核）",
      (ROOT / "scenes" / "Whitebox.tscn").exists()
      and "PlatformMover" in cs("tools/WhiteboxRoot.cs"))

# ---------------------------------------------------------------- 6. 拆除确认
all_cs = "".join(read(p) for p in cs_files)
for gone in ["SpeciesDef", "OfferKind", "LegionUnit", "WildSpawner", "EssencePickup",
             "Projectile", "RunPhase", "SpiritTier", "class Hud", "class MainMenu",
             "static class Meta", "static class Run ", "class Signals"]:
    check(f"战斗/成长/UI 已拆除：{gone}", gone not in all_cs)

# ---------------------------------------------------------------- 7. 版本一致
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
