// «Стеклянная» панель: размытая карта под панелью, тёмный оттенок, скруглённые углы,
// тонкая светлая кромка и едва заметный блик сверху.
shader_type canvas_item;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform vec4 tint : source_color = vec4(0.055, 0.067, 0.086, 0.8);
uniform vec4 border : source_color = vec4(1.0, 1.0, 1.0, 0.08);
uniform float sheen = 0.035;
uniform float radius = 14.0;
uniform vec2 size = vec2(100.0, 100.0);
uniform float blur = 2.5;

float rounded_box(vec2 p, vec2 b, float r) {
	vec2 q = abs(p) - b + r;
	return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

void fragment() {
	vec2 p = (UV - 0.5) * size;
	float d = rounded_box(p, size * 0.5, radius);
	float inside = 1.0 - smoothstep(-0.75, 0.75, d);

	vec2 px = SCREEN_PIXEL_SIZE;
	vec3 bg = textureLod(screen_texture, SCREEN_UV, blur).rgb * 0.2;
	bg += textureLod(screen_texture, SCREEN_UV + vec2(6.0, 0.0) * px, blur).rgb * 0.1;
	bg += textureLod(screen_texture, SCREEN_UV - vec2(6.0, 0.0) * px, blur).rgb * 0.1;
	bg += textureLod(screen_texture, SCREEN_UV + vec2(0.0, 6.0) * px, blur).rgb * 0.1;
	bg += textureLod(screen_texture, SCREEN_UV - vec2(0.0, 6.0) * px, blur).rgb * 0.1;
	bg += textureLod(screen_texture, SCREEN_UV + vec2(5.0, 5.0) * px, blur).rgb * 0.1;
	bg += textureLod(screen_texture, SCREEN_UV - vec2(5.0, 5.0) * px, blur).rgb * 0.1;
	bg += textureLod(screen_texture, SCREEN_UV + vec2(5.0, -5.0) * px, blur).rgb * 0.1;
	bg += textureLod(screen_texture, SCREEN_UV - vec2(5.0, -5.0) * px, blur).rgb * 0.1;

	vec3 col = mix(bg, tint.rgb, tint.a);
	col += vec3(sheen) * (1.0 - smoothstep(0.0, 0.5, UV.y));
	float edge = 1.0 - smoothstep(0.0, 1.2, abs(d + 0.6));
	col = mix(col, border.rgb, border.a * edge);
	COLOR = vec4(col, inside);
}
