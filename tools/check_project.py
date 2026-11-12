#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""山海幻灵 v0.003 工程静态自检（离线，不启动 Godot）

检查：工程配置 / 零 autoload / C# 工具链 / 脚本与 UID 配对 / @trace 覆盖 /
用户设定（幻灵幻兽同源、幻灵师不攻击）/ BM 机制（三分支、Lv3 材料、cap、灵魄、Lore）/
GND-001 第一层全覆盖 / AXIS-001 y 轴 / 美术 / .gitignore / 版本一致 / 验证证据。
输出 docs/self-check.json；全过退出 0，任一失败退出 1。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
VERSION = "0.003"
EXPECTED_CS = 19
EXPECTED_ITEMS = 31
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
check("主场景指向 Main.tscn", 'run/main_scene="res://scenes/Main.tscn"' in pg)
check("工程名为山海幻灵", 'config/name="山海幻灵"' in pg)
check("已删除全部 autoload", "[autoload]" not in pg and "GameEvents=\"*res://" not in pg)
check("Main.tscn 挂 GameRoot.cs", "res://scripts/csharp/core/GameRoot.cs" in read(ROOT / "scenes" / "Main.tscn"))
check("2D 渲染 gl_compatibility", 'renderer/rendering_method="gl_compatibility"' in pg)
check("features 含 C#", 'PackedStringArray("4.7", "C#", "GL Compatibility")' in pg)
check(f"config/version = {VERSION}", f'config/version="{VERSION}"' in pg)
check("设定写入 config/description", "山海谷" in pg and "幻灵" in pg)

# ---------------------------------------------------------------- 2. C# 工具链
csproj_text = read(ROOT / "ShanHaiHuanLing.csproj")
check("ShanHaiHuanLing.csproj 存在", (ROOT / "ShanHaiHuanLing.csproj").exists())
check("使用 Godot.NET.Sdk/4.7.2", "Godot.NET.Sdk/4.7.2" in csproj_text)
check("目标框架 net8.0", "<TargetFramework>net8.0</TargetFramework>" in csproj_text)
check("程序集名 ASCII", "<AssemblyName>ShanHaiHuanLing</AssemblyName>" in csproj_text)
check("project.godot 程序集名对齐", 'project/assembly_name="ShanHaiHuanLing"' in pg)
check("C# 运行时探针存在", (ROOT / "tools" / "CSharpToolchainProbe.cs").exists())
check("C# 迁移收口文档存在", (DOCS / "csharp-migration.md").exists())

# ---------------------------------------------------------------- 3. 脚本与 UID
cs_files = sorted((ROOT / "scripts").rglob("*.cs"))
gd_files = sorted((ROOT / "scripts").rglob("*.gd"))
check(f"C# 脚本数量 = {EXPECTED_CS}", len(cs_files) == EXPECTED_CS, f"{len(cs_files)} 个")
check("GDScript 数量 = 0", len(gd_files) == 0, f"{len(gd_files)} 个")
missing_uid = [f.name for f in cs_files if not Path(str(f) + ".uid").exists()]
check("每个 .cs 都有配对 .cs.uid", not missing_uid, "缺失: " + ", ".join(missing_uid))
check("无 .gd.uid 残留", not list((ROOT / "scripts").rglob("*.gd.uid")))
check("旧平铺 C# 地形层已删除", not (ROOT / "scripts" / "csharp" / "PlatformField.cs").exists())
check("旧 GDScript 目录已清空", not (ROOT / "scripts" / "core" / "GameManager.gd").exists()
      and not (ROOT / "scripts" / "gameplay" / "WeaponSystem.gd").exists())

# ---------------------------------------------------------------- 4. @trace 覆盖
trace_ids = set()
for f in cs_files:
    for line in read(f).splitlines():
        if "@trace" in line:
            trace_ids.update(re.findall(r"[A-Z]{2,}[0-9]*-[0-9]{3}", line))
freeze = json.loads(read(DOCS / "design-freeze.json")) if (DOCS / "design-freeze.json").exists() else {}
frozen_ids = {it["id"] for it in freeze.get("items", [])}
check("冻结清单可解析", bool(freeze))
check("@trace 覆盖率 100%", not (frozen_ids - trace_ids),
      "未覆盖: " + ", ".join(sorted(frozen_ids - trace_ids)))
check("无未登记的 @trace 编号", not (trace_ids - frozen_ids),
      "未登记: " + ", ".join(sorted(trace_ids - frozen_ids)))
check(f"冻结清单 = {EXPECTED_ITEMS} 条", len(frozen_ids) == EXPECTED_ITEMS, f"{len(frozen_ids)} 条")
check("代码 @trace 编号数与清单一致", len(trace_ids) == EXPECTED_ITEMS, f"{len(trace_ids)} 个")

# ---------------------------------------------------------------- 5. 用户设定：同源 / 幻灵师不攻击
spirit, defs = cs("gameplay/Spirit.cs"), cs("core/GameDefs.cs")
master, spawner = cs("gameplay/Master.cs"), cs("gameplay/WildSpawner.cs")
offers, projectile = cs("gameplay/OfferSystem.cs"), cs("gameplay/Projectile.cs")
world, root = cs("gameplay/SpiritWorld.cs"), cs("core/GameRoot.cs")
legion, run, meta = cs("gameplay/Legion.cs"), cs("core/Run.cs"), cs("core/Meta.cs")

check("Spirit 用 Faction 区分敌我", "public Faction Side;" in spirit)
check("敌我共用一张 SpeciesDef", "public SpeciesDef Def = null!;" in spirit and "SpeciesDef[] Species" in defs)
check("敌我共用一张贴图、只改色调", "FactionTint(side)" in spirit and "public static Color FactionTint" in defs)
check("野生刷怪池来自 Species 表", "GameDefs.Unlocked(_day)" in spawner)
check("唤灵候选池来自同一 Species 表", "GameDefs.Unlocked(Run.Day)" in offers)
check("投射物也是敌我共用一个类", 'Side == Faction.Wild ? "master" : "wild"' in projectile)
check("幻灵师完全不攻击（无开火/索敌）",
      "SpawnProjectile" not in master and "AttackWilds" not in master and "TakeDamage(int amount)" in master)
check("敌我结构对称（都可受击死亡）", "if (Side == Faction.Wild)" in spirit and "Legion.Remove(Uid);" in spirit)

# ---------------------------------------------------------------- 6. BM 机制
check("三类唤灵出口 Raise/Bolster/Fuse", all(k in defs for k in ("Raise = 0", "Bolster = 1", "Fuse = 2")))
check("单一资源阀门：灵韵跨阈值排队唤灵", "PendingOffers++" in run and "GameDefs.EssenceCost(Level)" in run)
check("Lv3 是融合材料而非终点", "CostMinLevel=3" in defs and "MaxUnitLevel = 3" in defs)
check("按物种分类 cap", "CountOf(def.Id) < def.Cap" in legion)
check("献祭成本显式写入候选", "SacrificeUids" in offers and "public string CostText" in offers)
check("灵魄跃迁窗口（权重 ×20）", "w *= 20" in offers and "SoulCost > 0" in offers)
check("Lore 解锁候选分支",
      "RequiredLore" in legion and "Meta.HasLore" in offers and "Legion.CanFuse(r)" in offers)
check("Lore 改候选数而非数值", "OptionCount => GameDefs.PickCount" in meta)
check("融合后回调 Legion.Fuse", "Legion.Fuse(Recipe, SacrificeUids)" in offers)
check("唤灵由灵韵即时触发、不必等天亮", "Run.PendingOffers > 0" in root and "EnterSummon()" in root)

# ---------------------------------------------------------------- 7. GND-001 / AXIS-001
terrain = cs("gameplay/PlatformField.cs")
check("第一层唯一一段 / 全覆盖 / 非单向",
      "AddSegment(new Segment(0, -WorldHalfWidth, WorldHalfWidth, LayerY(0), false));" in terrain)
check("第一层刻意不走随机分支", "刻意不走随机分支" in terrain)
check("IsGroundContinuous 运行时不变量",
      "public bool IsGroundContinuous()" in terrain and "return groundCount == 1;" in terrain)
check("typed signal 保留", "GroundBuiltEventHandler(int segmentCount, bool groundContinuous)" in terrain)
check("地形常量从 GameDefs 单点读取", "GameDefs.WorldHalfWidth" in terrain and "GameDefs.LayerSpacing" in terrain)
check("y-down：层号越大 y 越小", "GroundY - Mathf.Clamp(layer, 0, LayerCount - 1) * LayerSpacing" in terrain)
check("y-down：坠落阈值语义", "FallKillY = 8.0f" in defs and "GlobalPosition.Y > GameDefs.FallKillY" in master)
check("出生点在地板上方", "GameDefs.LayerY(0) - GameDefs.SpawnAboveGround" in world)
check("相机 minf 跟随", "Mathf.Min(GameDefs.LayerY(0) - half.Y * 0.4f" in world)
check("水平活动域锁在地板覆盖域", "Mathf.Clamp(GlobalPosition.X, -edge, edge)" in master)

# ---------------------------------------------------------------- 8. 美术 / gitignore
art = sorted((ROOT / "assets" / "art").glob("*.png"))
check("16 张美术 PNG", len(art) == 16, f"{len(art)} 张")
check("无 Unity .meta 残留", not list((ROOT / "assets" / "art").glob("*.meta")))
gi = read(ROOT / ".gitignore")
check(".gitignore 忽略 .godot/", ".godot/" in gi)
check(".gitignore 不忽略 .cs.uid", "*.uid" not in gi)
check(".gitignore 忽略 MSBuild 产物", "bin/" in gi and "obj/" in gi)

# ---------------------------------------------------------------- 9. 版本一致
readme, roadmap = read(ROOT / "README.md"), read(DOCS / "roadmap.md")
check(f"design-freeze version = {VERSION}", freeze.get("version") == VERSION, str(freeze.get("version")))
check(f"README 标题 v{VERSION}", f"# 山海幻灵 · v{VERSION}" in readme[:120])
check("GameRoot 冒烟报告版本一致", '["version"] = "0.003"' in root)
check(f"roadmap 标题 v{VERSION}", f"路线图（v{VERSION}）" in roadmap)
check("冻结清单语言口径为全量 C#", "C#" in freeze.get("language", "") and "0 个 .gd" in freeze.get("language", ""))
check("设定字段进入冻结清单", freeze.get("setting", {}).get("sameSpeciesRule", "").startswith("幻灵与幻兽共用"))

# ---------------------------------------------------------------- 10. 重构边界文档
boundary = freeze.get("refactorBoundary", {})
check("refactor-v0.003.md 存在", (DOCS / "refactor-v0.003.md").exists())
check("边界四类齐备", all(k in boundary for k in ("retained", "replaced", "removed", "proposal", "defectFix")))
check("★助手提案 7 项逐条登记", len(boundary.get("proposal", [])) == 7, str(len(boundary.get("proposal", []))))
check("未做清单非空", len(freeze.get("notImplemented", [])) >= 10)
check("Q6 已标注由 v0.003 关闭", any("v0.003" in q.get("status", "") for q in freeze.get("openQuestions", [])))
check("诚实边界写明 Play Mode 0 次", any("Play Mode" in x for x in freeze.get("knownLimits", [])))

# ---------------------------------------------------------------- 11. 验证证据
for fname in ("compile-evidence.txt", "smoke-evidence.txt", "smoke-report.json"):
    check(f"证据 {fname} 存在", (DOCS / fname).exists())
compile_log = read(DOCS / "compile-evidence.txt")
check("编译证据含 dotnet build 成功", "0 个错误" in compile_log and "已成功生成" in compile_log)
check("编译证据含 Godot 导入", "GODOT_IMPORT" in compile_log)
check("编译证据含 C# 运行时探针", "CSHARP_PROBE" in compile_log and "CSHARP_RUNTIME_PROBE" in compile_log)
smoke_path = DOCS / "smoke-report.json"
if smoke_path.exists():
    smoke = json.loads(read(smoke_path))
    a = smoke.get("assertions", {})
    failed = [k for k, v in a.items() if not v]
    check("冒烟结果 PASS", smoke.get("result") == "PASS", str(smoke.get("result")))
    check("冒烟 24 项断言全过", len(a) == 24 and not failed,
          "失败: " + ", ".join(failed) if failed else f"{len(a)}/24")
    for k in ("ground_continuous", "player_grounded", "fuse_applied", "bolster_applied",
              "raise_applied", "shared_species_table", "cap_respected", "branches_complete"):
        check(f"冒烟断言 {k}", a.get(k) is True)
    check("冒烟报告版本一致", smoke.get("version") == VERSION, str(smoke.get("version")))
    check("冒烟报告语言口径", smoke.get("language") == "C# (full)")
else:
    for i in range(13):
        check(f"冒烟证据检查 {i + 1}", False, "缺少 smoke-report.json")

# ---------------------------------------------------------------- 输出
passed = sum(1 for c in checks if c["ok"])
total = len(checks)
result = {
    "project": "山海幻灵", "version": VERSION,
    "engine": "Godot 4.7.2.stable.mono", "language": "C# (full, zero GDScript, zero autoload)",
    "total": total, "passed": passed, "failed": total - passed,
    "traceIds": sorted(trace_ids),
    "scriptCount": {"csharp": len(cs_files), "gdscript": len(gd_files)},
    "artCount": len(art), "checks": checks,
}
DOCS.mkdir(exist_ok=True)
(DOCS / "self-check.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"山海幻灵 v{VERSION} 静态自检：{passed}/{total} 通过，{total - passed} 失败")
for c in checks:
    if not c["ok"]:
        print(f"  [FAIL] {c['name']} :: {c['detail']}")
print("报告：docs/self-check.json")
sys.exit(0 if passed == total else 1)
