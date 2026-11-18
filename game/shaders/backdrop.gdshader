// Фон за пределами карты мира: почти чёрный с лёгким виньетированием.
shader_type canvas_item;

uniform vec4 inner : source_color = vec4(0.043, 0.055, 0.075, 1.0);
uniform vec4 outer : source_color = vec4(0.02, 0.026, 0.037, 1.0);

void fragment() {
	float r = distance(UV, vec2(0.5, 0.48));
	vec3 col = mix(inner.rgb, outer.rgb, smoothstep(0.2, 0.85, r));
	float n = fract(sin(dot(FRAGCOORD.xy, vec2(12.9898, 78.233))) * 43758.5453);
	col += (n - 0.5) / 255.0 * 2.0;
	COLOR = vec4(col, 1.0);
}
