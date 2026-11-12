class_name RunSeed
extends RefCounted
## @trace SEED-001 一局一种子 + 分域随机流隔离 + 无状态确定性查询
## 迁移自 Unity RunSeed.cs。GDScript int 为 64 位有符号，全部用 & 0xFFFFFFFF 截到 32 位，
## 避免 FNV-1a / 雪崩混淆的乘法溢出影响低 32 位结果。

static func hash_str(s: String) -> int:
	var h: int = 2166136261
	for i in range(s.length()):
		h = (h ^ (s.unicode_at(i) & 0xFF)) & 0xFFFFFFFF
		h = (h * 16777619) & 0xFFFFFFFF
	return h

## 每个域一条独立随机流，互不干扰（刷怪 / 平台 / 商店 / 卡牌分开）。
static func make_rng(seed_value: int, domain: String) -> RandomNumberGenerator:
	var rng := RandomNumberGenerator.new()
	rng.seed = (seed_value ^ hash_str(domain)) & 0x7FFFFFFF
	return rng

## 查询型随机：同一 (seed, domain, index) 恒返回同一 [0,1)，不推进任何状态。
static func deterministic(seed_value: int, domain: String, index: int) -> float:
	var x: int = (seed_value ^ hash_str(domain) ^ ((index * 0x9E3779B1) & 0xFFFFFFFF)) & 0xFFFFFFFF
	x = ((x ^ (x >> 16)) * 0x7FEB352D) & 0xFFFFFFFF
	x = ((x ^ (x >> 15)) * 0x846CA68B) & 0xFFFFFFFF
	x = (x ^ (x >> 16)) & 0xFFFFFFFF
	return float(x) / 4294967296.0
