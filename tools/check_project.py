#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""山海幻灵 工程静态自检（离线，不启动 Godot）

检查：工程配置 / 脚本与 .gd.uid 配对 / @trace 覆盖率 / 美术资源 /
.gitignore / 版本号一致性 / 验证证据齐备性。
输出 docs/self-check.json；全过退出 0，任一失败退出 1。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
checks = []


def check(name, ok, detail=""):
    checks.append({"name": name, "ok": bool(ok), "detail": str(detail)})


def read(path):
    return path.read_text(encoding="utf-8") if path.exists() else ""


# ---------------------------------------------------------------- 1. 工程配置
pg_path = ROOT / "project.godot"
pg = read(pg_path)
check("project.godot 存在", pg_path.exists())
check("主场景指向 Main.tscn", 'run/main_scene="res://scenes/Main.tscn"' in pg)
check("工程名为山海幻灵", 'config/name="山海幻灵"' in pg)
check("4 个 autoload 齐备", all(
    f'{n}="*res://scripts/core/{n}.gd"' in pg
    for n in ("GameEvents", "RunState", "MetaProgress", "GameManager")
))
check("2D 渲染方法 gl_compatibility", 'renderer/rendering_method="gl_compatibility"' in pg)
check("config/version = 0.001", 'config/version="0.001"' in pg)

main_tscn = read(ROOT / "scenes" / "Main.tscn")
check("Main.tscn 引用 Bootstrap.gd", "res://scripts/core/Bootstrap.gd" in main_tscn)

# ---------------------------------------------------------------- 2. 脚本与 UID
gd_files = sorted((ROOT / "scripts").rglob("*.gd"))
check("GDScript 数量 >= 20", len(gd_files) >= 20, f"{len(gd_files)} 个")
missing_uid = [f.name for f in gd_files if not Path(str(f) + ".uid").exists()]
check("每个 .gd 都有配对的 .gd.uid", not missing_uid, "缺失: " + ", ".join(missing_uid))

# ---------------------------------------------------------------- 3. @trace 覆盖
trace_ids = set()
for f in gd_files:
    for line in read(f).splitlines():
        if "@trace" in line:
            trace_ids.update(re.findall(r"[A-Z]{2,}[0-9]*-[0-9]{3}", line))

freeze_path = DOCS / "design-freeze.json"
freeze = json.loads(read(freeze_path)) if freeze_path.exists() else {}
frozen_ids = {it["id"] for it in freeze.get("items", [])}
missing_trace = sorted(frozen_ids - trace_ids)
unknown_trace = sorted(trace_ids - frozen_ids)
check("冻结清单可被解析", bool(freeze))
check("@trace 覆盖率 100%（清单条目都有代码标记）", not missing_trace,
      "未覆盖: " + ", ".join(missing_trace))
check("无未登记的 @trace 编号", not unknown_trace,
      "未登记: " + ", ".join(unknown_trace))
check("冻结清单条目数 = 26", len(frozen_ids) == 26, f"{len(frozen_ids)} 条")

# ---------------------------------------------------------------- 4. 美术
art = sorted((ROOT / "assets" / "art").glob("*.png"))
meta = sorted((ROOT / "assets" / "art").glob("*.meta"))
check("16 张美术 PNG", len(art) == 16, f"{len(art)} 张")
check("无 Unity .meta 残留", not meta, f"{len(meta)} 个")

# ---------------------------------------------------------------- 5. .gitignore
gi = read(ROOT / ".gitignore")
check(".gitignore 忽略 .godot/", ".godot/" in gi)
check(".gitignore 不忽略 .gd.uid", "*.uid" not in gi)
check(".gitignore 忽略 *.log", "*.log" in gi)

# ---------------------------------------------------------------- 6. 版本一致
readme = read(ROOT / "README.md")
check("design-freeze version = 0.001", freeze.get("version") == "0.001",
      str(freeze.get("version")))
check("README 标题为 v0.001", "v0.001" in readme[:200])

# ---------------------------------------------------------------- 7. 验证证据
for fname in ("compile-evidence.txt", "smoke-evidence.txt", "smoke-report.json"):
    check(f"证据文件 {fname} 存在", (DOCS / fname).exists())

smoke_path = DOCS / "smoke-report.json"
if smoke_path.exists():
    smoke = json.loads(read(smoke_path))
    check("冒烟结果 PASS", smoke.get("result") == "PASS", str(smoke.get("result")))
    assertions = smoke.get("assertions", {})
    failed = [k for k, v in assertions.items() if not v]
    check("冒烟 10 项断言全过", len(assertions) == 10 and not failed,
          "失败: " + ", ".join(failed) if failed else f"{len(assertions)}/10")
else:
    check("冒烟结果 PASS", False, "缺少 smoke-report.json")
    check("冒烟 10 项断言全过", False, "缺少 smoke-report.json")

# ---------------------------------------------------------------- 输出
passed = sum(1 for c in checks if c["ok"])
total = len(checks)
result = {
    "project": "山海幻灵",
    "version": "0.001",
    "engine": "Godot 4.7.2.stable.mono",
    "total": total,
    "passed": passed,
    "failed": total - passed,
    "warnings": 0,
    "traceIds": sorted(trace_ids),
    "scriptCount": len(gd_files),
    "artCount": len(art),
    "checks": checks,
}
DOCS.mkdir(exist_ok=True)
(DOCS / "self-check.json").write_text(
    json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
)
print(f"山海幻灵 v0.001 静态自检：{passed}/{total} 通过，{total - passed} 失败")
for c in checks:
    if not c["ok"]:
        print(f"  [FAIL] {c['name']} :: {c['detail']}")
print("报告：docs/self-check.json")
sys.exit(0 if passed == total else 1)
