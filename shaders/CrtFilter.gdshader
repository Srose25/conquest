shader_type canvas_item;
render_mode unshaded;

// Keep the filter locked to Conquest's native game resolution even if the
// window is resized, so the scanline pattern remains part of the pixel art.
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
const vec2 GAME_RESOLUTION = vec2(1152.0, 648.0);

void fragment() {
	vec2 native_pixel = floor(SCREEN_UV * GAME_RESOLUTION);
	vec2 native_uv = (native_pixel + vec2(0.5)) / GAME_RESOLUTION;
	vec3 color = texture(screen_texture, native_uv).rgb;

	float scanline = 1.0 - 0.13 * mod(native_pixel.y, 2.0);
	vec2 from_center = SCREEN_UV * 2.0 - vec2(1.0);
	float vignette = 1.0 - 0.14 * dot(from_center, from_center);
	color *= scanline * max(0.76, vignette);

	COLOR = vec4(color, 1.0);
}
