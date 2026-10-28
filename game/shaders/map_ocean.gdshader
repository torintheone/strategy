// Океан внутри контура проекции: светлее у экватора, темнее к полюсам и краям, без полос на градиенте.
shader_type canvas_item;

uniform vec4 center_color : source_color = vec4(0.075, 0.105, 0.145, 1.0);
uniform vec4 edge_color : source_color = vec4(0.04, 0.058, 0.085, 1.0);
uniform vec2 half_size = vec2(4096.0, 1995.0);

varying vec2 world;

void vertex() {
	world = VERTEX;
}

void fragment() {
	vec2 p = world / half_size;
	float r = length(vec2(p.x * 0.75, p.y * 1.1));
	vec3 col = mix(center_color.rgb, edge_color.rgb, smoothstep(0.15, 1.05, r));
	float n = fract(sin(dot(FRAGCOORD.xy, vec2(12.9898, 78.233))) * 43758.5453);
	col += (n - 0.5) / 255.0 * 2.0;
	COLOR = vec4(col, 1.0);
}
