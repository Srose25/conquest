shader_type canvas_item;
render_mode unshaded;

uniform float half_fov_degrees = 100.0;
uniform float ray_count = 50.0;

void fragment()
{
	// The one-pixel Sprite2D is scaled to the FOV radius. Its UV therefore
	// provides a cheap, per-fragment polar view of the enemy sensor cone.
	vec2 point = (UV - vec2(0.5)) * 2.0;
	float distance_fraction = length(point);
	float view_angle = degrees(atan(point.x, -point.y));
	if (distance_fraction > 1.0 || abs(view_angle) > half_fov_degrees)
		discard;

	// Full vision through the inner half of the radius, then fade smoothly out.
	float range_strength = distance_fraction <= 0.5
		? 1.0
		: 1.0 - smoothstep(0.0, 1.0, (distance_fraction - 0.5) * 2.0);
	// Preserve the strong central cone while keeping peripheral vision visible.
	float angle_fraction = abs(view_angle);
	float direction_strength = angle_fraction <= 20.0
		? 1.0
		: angle_fraction <= 45.0
			? mix(1.0, 0.65, smoothstep(0.0, 1.0, (angle_fraction - 20.0) / 25.0))
			: mix(0.65, 0.12, smoothstep(0.0, 1.0, (angle_fraction - 45.0) / 55.0));

	// Thin, discrete white spokes: fifty FOV rays without fifty CPU draw calls.
	float ray_coordinate = (view_angle + half_fov_degrees) / (half_fov_degrees * 2.0) * (ray_count - 1.0);
	float ray_distance = abs(fract(ray_coordinate + 0.5) - 0.5);
	float ray_strength = 1.0 - smoothstep(0.035, 0.080, ray_distance);
	COLOR = vec4(vec3(1.0), 0.50 * range_strength * direction_strength * ray_strength);
}
