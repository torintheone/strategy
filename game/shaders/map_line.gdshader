// Линия постоянной толщины на экране с мягким краем (сглаживание без зубцов при любом масштабе).
// Сетку строит LineMesh: в UV — направление выдавливания вершины (нормаль с поправкой на угол),
// в COLOR.r — сторона линии (0 или 1). Толщина, цвет и масштаб — общие для всего слоя.
shader_type canvas_item;

uniform float zoom = 1.0;
uniform float width = 1.0;          // полная толщина, пикселей экрана
uniform float feather = 1.0;        // ширина мягкого края, пикселей
uniform vec4 line_color : source_color = vec4(1.0);
uniform bool glow = false;          // мягкое свечение вместо чёткой линии
uniform float fade_from = 0.0;      // масштаб, ниже которого линия не видна
uniform float fade_to = 0.0;        // масштаб, с которого линия видна полностью

varying float across;
varying float tint_alpha;

void vertex() {
	tint_alpha = COLOR.a;
	float half_width = width * 0.5 + feather;
	float side = COLOR.r * 2.0 - 1.0;
	VERTEX += UV * side * half_width / zoom;
	across = side * half_width;
}

void fragment() {
	float d = abs(across);
	float alpha;
	if (glow) {
		float t = clamp(d / (width * 0.5 + feather), 0.0, 1.0);
		alpha = (1.0 - t) * (1.0 - t);
	} else {
		float edge = width * 0.5;
		alpha = 1.0 - smoothstep(edge - feather * 0.5, edge + feather * 0.5, d);
	}
	float fade = fade_to > fade_from ? smoothstep(fade_from, fade_to, zoom) : 1.0;
	COLOR = vec4(line_color.rgb, line_color.a * alpha * fade * tint_alpha);
}
