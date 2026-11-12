class_name BuildSystem
extends RefCounted
## @trace BLD-001 建造物差异化：静态计数聚合，天亮由 GameManager 结算
## 迁移自 Unity StructureField.cs；金库产金 / 灯塔减速 / 医疗帐回血 / 拒马增伤。
## 数值与 GameConfig.SHOP_ITEMS 描述一致，属 demo 级线性叠加，未做递减收益。

static func count(kind: String) -> int:
	return int(RunState.structures.get(kind, 0))

static func vault_income() -> int:
	return count("vault") * 5

static func tent_heal() -> int:
	return count("tent") * 12

static func wall_bonus() -> int:
	return count("wall") * 2

## 灯塔：每座减速 15%，上限 45%（防速度归零）
static func beacon_slow() -> float:
	return 1.0 - minf(0.45, float(count("beacon")) * 0.15)

static func summary() -> Dictionary:
	return {
		"turret": count("turret"), "vault": count("vault"),
		"beacon": count("beacon"), "tent": count("tent"), "wall": count("wall"),
		"income": vault_income(), "heal": tent_heal(),
	}
