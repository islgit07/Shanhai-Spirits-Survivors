#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""山海幻灵 工程静态自检（离线，不启动 Godot）

检查：工程配置 / C# 工具链 / 脚本与 UID 配对 / @trace 覆盖率 /
GND-001 第一层全覆盖硬约束 / AXIS-001 y 轴方向 / 美术资源 /
.gitignore / 版本号一致性 / 验证证据齐备性。
输出 docs/self-check.json；全过退出 0，任一失败退出 1。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
VERSION = "0.002"
checks = []


def check(name, ok, detail=""):
    checks.append({"name": name, "ok": bool(ok), "detail": str(detail)})


def read(path):
    return path.read_text(encoding="utf-8") if path.exists() else ""


# ---------------------------------------------------------------- 1. 工程配置
pg = read(ROOT / "project.godot")
check("project.godot 存在", bool(pg))
check("主场景指向 Main.tscn", 'run/main_scene="res://scenes/Main.tscn"' in pg)
check("工程名为山海幻灵", 'config/name="山海幻灵"' in pg)
check("4 个 autoload 齐备", all(
    f'{n}="*res://scripts/core/{n}.gd"' in pg
    for n in ("GameEvents", "RunState", "MetaProgress", "GameManager")
))
check("2D 渲染方法 gl_compatibility", 'renderer/rendering_method="gl_compatibility"' in pg)
check(f"config/version = {VERSION}", f'config/version="{VERSION}"' in pg)

main_tscn = read(ROOT / "scenes" / "Main.tscn")
check("Main.tscn 引用 Bootstrap.gd", "res://scripts/core/Bootstrap.gd" in main_tscn)
check("Main.tscn 头部为 format=3 且无注释",
      main_tscn.startswith("[gd_scene load_steps=2 format=3]") and "#" not in main_tscn and ";" not in main_tscn)

# ---------------------------------------------------------------- 2. C# 工具链 (CSHARP-001)
csproj = ROOT / "ShanHaiHuanLing.csproj"
csproj_text = read(csproj)
check("ShanHaiHuanLing.csproj 存在", csproj.exists())
check("C# 使用 Godot.NET.Sdk/4.7.2", "Godot.NET.Sdk/4.7.2" in csproj_text)
check("C# 目标框架 net8.0", "<TargetFramework>net8.0</TargetFramework>" in csproj_text)
check("C# 程序集名为 ASCII", "<AssemblyName>ShanHaiHuanLing</AssemblyName>" in csproj_text)
check("project.godot 程序集名与 csproj 一致",
      'project/assembly_name="ShanHaiHuanLing"' in pg)
check("C# 运行时探针存在", (ROOT / "tools" / "CSharpToolchainProbe.cs").exists())
check("C# 迁移路线文档存在", (DOCS / "csharp-migration.md").exists())

# ---------------------------------------------------------------- 3. 脚本与 UID
gd_files = sorted((ROOT / "scripts").rglob("*.gd"))
cs_files = sorted((ROOT / "scripts").rglob("*.cs"))
check("GDScript 数量 >= 20", len(gd_files) >= 20, f"{len(gd_files)} 个")
check("C# 玩法组件数量 >= 1", len(cs_files) >= 1, f"{len(cs_files)} 个")
missing_gd_uid = [f.name for f in gd_files if not Path(str(f) + ".uid").exists()]
missing_cs_uid = [f.name for f in cs_files if not Path(str(f) + ".uid").exists()]
check("每个 .gd 都有配对的 .gd.uid", not missing_gd_uid, "缺失: " + ", ".join(missing_gd_uid))
check("每个 .cs 都有配对的 .cs.uid", not missing_cs_uid, "缺失: " + ", ".join(missing_cs_uid))
check("旧 GDScript 地形层已删除", not (ROOT / "scripts" / "gameplay" / "PlatformField.gd").exists())

# ---------------------------------------------------------------- 4. @trace 覆盖（GDScript + C#）
trace_ids = set()
source_files = gd_files + cs_files
for f in source_files:
    for line in read(f).splitlines():
        if "@trace" in line:
            trace_ids.update(re.findall(r"[A-Z]{2,}[0-9]*-[0-9]{3}", line))

freeze = json.loads(read(DOCS / "design-freeze.json")) if (DOCS / "design-freeze.json").exists() else {}
frozen_ids = {it["id"] for it in freeze.get("items", [])}
missing_trace = sorted(frozen_ids - trace_ids)
unknown_trace = sorted(trace_ids - frozen_ids)
check("冻结清单可被解析", bool(freeze))
check("@trace 覆盖率 100%（清单条目都有代码标记）", not missing_trace,
      "未覆盖: " + ", ".join(missing_trace))
check("无未登记的 @trace 编号", not unknown_trace,
      "未登记: " + ", ".join(unknown_trace))
check("冻结清单条目数 = 29", len(frozen_ids) == 29, f"{len(frozen_ids)} 条")

# ---------------------------------------------------------------- 5. GND-001 第一层全覆盖
terrain = read(ROOT / "scripts" / "csharp" / "PlatformField.cs")
check("C# 地形层为 [GlobalClass]", "[GlobalClass]" in terrain)
check("第一层是唯一一段、覆盖 [-W,+W]、非单向",
      "AddSegment(new Segment(0, -WorldHalfWidth, WorldHalfWidth, LayerY(0), false));" in terrain)
check("第一层刻意不走随机分支", "刻意不走随机分支" in terrain)
check("存在 IsGroundContinuous() 运行时不变量",
      "public bool IsGroundContinuous()" in terrain and "return groundCount == 1;" in terrain)
check("存在 typed signal（PascalCase + EventHandler）",
      "GroundBuiltEventHandler(int segmentCount, bool groundContinuous)" in terrain)
check("节点清理用 QueueFree 而非 free", "QueueFree()" in terrain and ".Free();" not in terrain)
cfg = read(ROOT / "scripts" / "core" / "GameConfig.gd")
check("GameConfig 声明 GROUND_CONTINUOUS 口径", "const GROUND_CONTINUOUS := true" in cfg)
world = read(ROOT / "scripts" / "gameplay" / "World.gd")
check("World 经 C# 组件读取全覆盖状态", 'call("IsGroundContinuous")' in world)
check("冒烟暴露 ground_continuous 字段", '"ground_continuous": ground_continuous' in read(ROOT / "scripts" / "core" / "GameManager.gd"))

# ---------------------------------------------------------------- 6. AXIS-001 y 轴方向
check("layer_y 已翻转为 y-down（高层 y 更小）", "GROUND_Y - float(clampi(i, 0, LAYER_COUNT - 1)) * LAYER_SPACING" in cfg)
check("坠落阈值改为 y 增大语义", "FALL_KILL_Y := 8.0" in cfg)
player = read(ROOT / "scripts" / "gameplay" / "Player.gd")
check("坠落判定为 y > 阈值", "global_position.y > GameConfig.FALL_KILL_Y" in player)
check("玩家水平边界锁在地板覆盖域内", "global_position.x = clampf(global_position.x, -edge, edge)" in player)
check("出生点在地板上方（y 更小）", "layer_y(0) - GameConfig.SPAWN_ABOVE_GROUND" in world)
check("相机 y-down 跟随用 minf", "minf(GameConfig.layer_y(0) - half.y * 0.4" in world)
check("敌人 y 夹取上下界已互换", "var top_y := GameConfig.layer_y(GameConfig.LAYER_COUNT - 1)" in world)
check("冒烟暴露 player_grounded 字段", '"player_grounded": player_grounded' in read(ROOT / "scripts" / "core" / "GameManager.gd"))

# ---------------------------------------------------------------- 7. 美术
art = sorted((ROOT / "assets" / "art").glob("*.png"))
check("16 张美术 PNG", len(art) == 16, f"{len(art)} 张")
check("无 Unity .meta 残留", not list((ROOT / "assets" / "art").glob("*.meta")))

# ---------------------------------------------------------------- 8. .gitignore
gi = read(ROOT / ".gitignore")
check(".gitignore 忽略 .godot/", ".godot/" in gi)
check(".gitignore 不忽略 .gd.uid / .cs.uid", "*.uid" not in gi)
check(".gitignore 忽略 *.log", "*.log" in gi)
check(".gitignore 忽略 MSBuild 产物", "bin/" in gi and "obj/" in gi)

# ---------------------------------------------------------------- 9. 版本一致
readme = read(ROOT / "README.md")
check(f"design-freeze version = {VERSION}", freeze.get("version") == VERSION, str(freeze.get("version")))
check(f"README 标题为 v{VERSION}", f"v{VERSION}" in readme[:200])
check("GameManager 冒烟报告版本一致", f'"version": "{VERSION}"' in read(ROOT / "scripts" / "core" / "GameManager.gd"))
check("roadmap 标题为 v0.002", "# 山海幻灵 · 商业完成度路线图（v0.002）" in read(DOCS / "roadmap.md"))

# ---------------------------------------------------------------- 10. 验证证据
for fname in ("compile-evidence.txt", "smoke-evidence.txt", "smoke-report.json"):
    check(f"证据文件 {fname} 存在", (DOCS / fname).exists())
compile_log = read(DOCS / "compile-evidence.txt")
check("编译证据含 dotnet build 成功", "已成功生成" in compile_log and "0 个错误" in compile_log)
check("编译证据含 Godot 导入", "CSHARP_DOTNET_BUILD" in compile_log and "GODOT_IMPORT" in compile_log)

smoke_path = DOCS / "smoke-report.json"
if smoke_path.exists():
    smoke = json.loads(read(smoke_path))
    check("冒烟结果 PASS", smoke.get("result") == "PASS", str(smoke.get("result")))
    assertions = smoke.get("assertions", {})
    failed = [k for k, v in assertions.items() if not v]
    check("冒烟 12 项断言全过", len(assertions) == 12 and not failed,
          "失败: " + ", ".join(failed) if failed else f"{len(assertions)}/12")
    check("冒烟证实第一层全覆盖", smoke.get("ground_continuous") is True)
    check("冒烟证实玩家站在地板上", smoke.get("player_grounded") is True)
    check("冒烟报告版本一致", smoke.get("version") == VERSION, str(smoke.get("version")))
else:
    for name in ("冒烟结果 PASS", "冒烟 12 项断言全过", "冒烟证实第一层全覆盖",
                 "冒烟证实玩家站在地板上", "冒烟报告版本一致"):
        check(name, False, "缺少 smoke-report.json")

# ---------------------------------------------------------------- 输出
passed = sum(1 for c in checks if c["ok"])
total = len(checks)
result = {
    "project": "山海幻灵",
    "version": VERSION,
    "engine": "Godot 4.7.2.stable.mono",
    "language": "GDScript + C# (mixed, migrating)",
    "total": total,
    "passed": passed,
    "failed": total - passed,
    "warnings": 0,
    "traceIds": sorted(trace_ids),
    "scriptCount": {"gdscript": len(gd_files), "csharp": len(cs_files)},
    "artCount": len(art),
    "checks": checks,
}
DOCS.mkdir(exist_ok=True)
(DOCS / "self-check.json").write_text(
    json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
)
print(f"山海幻灵 v{VERSION} 静态自检：{passed}/{total} 通过，{total - passed} 失败")
for c in checks:
    if not c["ok"]:
        print(f"  [FAIL] {c['name']} :: {c['detail']}")
print("报告：docs/self-check.json")
sys.exit(0 if passed == total else 1)
