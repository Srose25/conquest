shader_type canvas_item;
render_mode blend_add;

// The title screen has no moving camera, so the star layers scroll themselves.
// These values match the Parallax2D scroll_scale values used by World.tscn.
uniform float scroll_scale = 0.16;
uniform float tile_scale = 2.0;

void fragment()
{
	// Sampling slightly higher in the source texture makes the visible stars move
	// down the screen, which reads as the player moving upward.
	vec2 animated_uv = vec2(UV.x, UV.y - TIME * scroll_scale * 0.10);
	vec3 star_color = texture(TEXTURE, fract(animated_uv * tile_scale)).rgb;

	// The source PNGs have opaque black backgrounds. Treat those pixels as
	// transparent so the far, middle, and near fields can all be seen together.
	// The binary mask prevents the background from introducing a fade as it moves.
	float star_present = step(0.001, max(max(star_color.r, star_color.g), star_color.b));
	COLOR = vec4(star_color, star_present);
}
