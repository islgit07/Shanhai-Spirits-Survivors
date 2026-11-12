class_name UpgradeSystem
extends RefCounted
## @trace ROGUE-001 天亮三选一：卡池抽取
## 源工程 41 张卡，本垂直切片精简迁移 14 张（GameConfig.UPGRADES），见 docs/migration-map.md。
## 抽卡用种子分域随机流，与刷怪/平台/商店互不干扰。

static func pick(seed_value: int, day: int, count: int) -> Array:
	var rng := RunSeed.make_rng(seed_value, "upgrade-%d" % day)
	var pool: Array = GameConfig.UPGRADES.duplicate()
	var out: Array = []
	for i in range(mini(count, pool.size())):
		var idx := rng.randi_range(0, pool.size() - 1)
		out.append(pool[idx])
		pool.remove_at(idx)
	return out
