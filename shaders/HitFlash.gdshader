shader_type canvas_item;

// Kept on the sprite material so each ship can flash without changing the
// color of every instance that shares the source texture.
uniform vec4 flash_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float flash_amount : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec4 base_color = texture(TEXTURE, UV);
	COLOR = vec4(mix(base_color.rgb, flash_color.rgb, flash_amount), base_color.a);
}
