class_name ArtBank
extends RefCounted
## @trace ART-001 美术资源加载与 Sprite 规格化
## 迁移自 Unity ArtBank.cs：源工程把非方形图填充成正方形纹理再创建 1×1 世界单位 Sprite，
## 使 localScale=size 语义成立。Godot 里改用 Sprite2D.scale = 目标世界尺寸 / 纹理像素尺寸，
## 效果等价且不需要改动纹理本身。缺图自动回退纯白 1×1 纹理。

const ART_PATH := "res://assets/art/"

static var _cache: Dictionary = {}
static var _fallback: Texture2D = null

static func tex(key: String) -> Texture2D:
	if _cache.has(key):
		return _cache[key]
	var path: String = ART_PATH + key + ".png"
	var t: Texture2D = null
	if ResourceLoader.exists(path):
		t = load(path) as Texture2D
	if t == null:
		t = _make_fallback()
	_cache[key] = t
	return t

static func _make_fallback() -> Texture2D:
	if _fallback != null:
		return _fallback
	var img := Image.create_empty(2, 2, false, Image.FORMAT_RGBA8)
	img.fill(Color(1.0, 1.0, 1.0, 1.0))
	_fallback = ImageTexture.create_from_image(img)
	return _fallback

## 生成一个显示为 size.x × size.y 世界单位的 Sprite2D。
static func make_sprite(key: String, size: Vector2) -> Sprite2D:
	var s := Sprite2D.new()
	var t := tex(key)
	s.texture = t
	var tw := maxf(1.0, float(t.get_width()))
	var th := maxf(1.0, float(t.get_height()))
	s.scale = Vector2(size.x / tw, size.y / th)
	return s

## 纯色块（平台 / 血条 / 光环等刻意不用美术图）。
static func make_block(size: Vector2, color: Color) -> ColorRect:
	var r := ColorRect.new()
	r.color = color
	r.size = Vector2(size.x, size.y)
	r.position = Vector2(-size.x * 0.5, -size.y * 0.5)
	return r
