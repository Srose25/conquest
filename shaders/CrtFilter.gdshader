shader_type canvas_item;
render_mode unshaded;

// Keep the scanline pattern locked to Conquest's native game resolution even
// if the window is resized. Linear screen sampling preserves sub-pixel UI
// details (notably one-pixel health-bar dividers) instead of dropping them
// when the final viewport lands between native pixels.
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
const vec2 GAME_RESOLUTION = vec2(1152.0, 648.0);

void fragment() {
	vec2 native_pixel = floor(SCREEN_UV * GAME_RESOLUTION);
	vec3 color = texture(screen_texture, SCREEN_UV).rgb;

	float scanline = 1.0 - 0.13 * mod(native_pixel.y, 2.0);
	vec2 from_center = SCREEN_UV * 2.0 - vec2(1.0);
	float vignette = 1.0 - 0.14 * dot(from_center, from_center);
	color *= scanline * max(0.76, vignette);

	COLOR = vec4(color, 1.0);
}
