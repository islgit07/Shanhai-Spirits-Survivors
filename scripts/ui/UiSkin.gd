class_name UiSkin
extends RefCounted
## @trace UIS-001 统一 UI 皮肤：调色板 + 系统中文字体
## 迁移自 Unity UiSkin.cs 的「统一调色板」思路，但实现完全不同：
## Godot 内置字体不含 CJK，这里用 SystemFont 请求系统中文字体（不分发字体文件）。
## 已知限制：目标系统若无中文字体会回退默认字体显示方块，见 docs/migration-map.md。

const GOLD := Color(1.00, 0.84, 0.46)
const BLUE := Color(0.55, 0.76, 1.00)
const CRIMSON := Color(0.95, 0.42, 0.42)
const JADE := Color(0.48, 0.88, 0.66)
const INK := Color(0.07, 0.08, 0.13)
const PANEL := Color(0.13, 0.15, 0.23, 0.94)
const TEXT := Color(0.92, 0.92, 0.96)

static var _font: SystemFont = null

static func cjk_font() -> SystemFont:
	if _font == null:
		_font = SystemFont.new()
		# 用 set() 而非直接赋值：属性若在某版本缺失也只是静默回退，不会崩溃
		_font.set("font_names", PackedStringArray([
			"Microsoft YaHei", "Microsoft YaHei UI", "Noto Sans CJK SC", "SimHei", "sans-serif",
		]))
	return _font

static func label(text: String, size: int, color: Color) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_override("font", cjk_font())
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	return l

static func button(text: String, size: int = 18) -> Button:
	var b := Button.new()
	b.text = text
	b.add_theme_font_override("font", cjk_font())
	b.add_theme_font_size_override("font_size", size)
	b.add_theme_color_override("font_color", TEXT)
	return b

static func panel(rect: Rect2, color: Color = PANEL) -> ColorRect:
	var p := ColorRect.new()
	p.color = color
	p.position = rect.position
	p.size = rect.size
	p.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return p
