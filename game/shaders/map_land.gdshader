// Заливка регионов: цвет режима карты (через modulate) с лёгкой зернистостью, как на бумажной карте,
// и мягким светом с северо-запада. Подсвеченный регион светлее.
shader_type canvas_item;

uniform float grain = 0.07;
uniform float light = 0.06;
uniform float map_height = 3990.0;

varying vec2 world;

void vertex() {
	world = VERTEX;
}

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void fragment() {
	float n = noise(world * 0.045) * 0.55 + noise(world * 0.17) * 0.3 + noise(world * 0.6) * 0.15;
	float shade = 1.0 + grain * (n - 0.5) + light * (0.5 - (world.y / map_height + 0.5) * 0.6 - world.x / 16384.0);
	COLOR = vec4(COLOR.rgb * shade, COLOR.a);
}
