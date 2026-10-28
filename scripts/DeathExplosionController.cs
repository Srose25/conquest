#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>Oriented ship-death pixel burst with hull-colored particle groups.</summary>
public partial class DeathExplosionController : Node2D
{
	private readonly List<Vector2> _positions = new();
	private readonly List<Vector2> _velocities = new();
	private readonly List<Color> _colors = new();
	private float _remaining;
	private const float LifetimeSeconds = 3.0f;
	private const float FadeDurationSeconds = 0.8f;

	public static void Spawn(Node source, Vector2 position, float shipRotation)
	{
		DeathExplosionController effect = new();
		effect.ProcessMode = ProcessModeEnum.Always;
		effect.GlobalPosition = position;
		effect.Initialize(shipRotation);
		Node? parent = source.GetTree().CurrentScene ?? source.GetParent();
		parent?.AddChild(effect);
	}

	private void Initialize(float shipRotation)
	{
		_remaining = LifetimeSeconds;
		RandomNumberGenerator random = new();
		random.Randomize();

		int whiteCount = random.RandiRange(9, 15);
		for (int i = 0; i < whiteCount; i++)
		{
			Vector2 localOffset = RandomEllipseOffset(random, 30.0f, 24.0f);
			AddParticle(localOffset, localOffset.Normalized(), Colors.White, shipRotation, random, 45.0f, 125.0f);
		}

		// The reference ship's windshield is at the pointed/front end of the local-up-facing hull.
		for (int i = 0; i < 5; i++)
			AddParticle(new Vector2(random.RandfRange(-7.0f, 7.0f), random.RandfRange(-20.0f, -8.0f)), Vector2.Up, new Color("2878e8"), shipRotation, random, 55.0f, 120.0f);

		// Red engine/rear pixels originate from the bottom/rear of the local hull.
		for (int i = 0; i < 5; i++)
			AddParticle(new Vector2(random.RandfRange(-13.0f, 13.0f), random.RandfRange(12.0f, 24.0f)), Vector2.Down, new Color("e32d2d"), shipRotation, random, 55.0f, 120.0f);

		// Cyan pixels originate from the forward nose/wing-tip region.
		for (int i = 0; i < 5; i++)
			AddParticle(new Vector2(random.RandfRange(-22.0f, 22.0f), random.RandfRange(-27.0f, -17.0f)), Vector2.Up, new Color("53e4e8"), shipRotation, random, 50.0f, 115.0f);
		QueueRedraw();
	}

	private void AddParticle(Vector2 localOffset, Vector2 localDirection, Color color, float shipRotation, RandomNumberGenerator random, float minimumSpeed, float maximumSpeed)
	{
		_positions.Add(localOffset.Rotated(shipRotation));
		Vector2 direction = (localDirection + new Vector2(random.RandfRange(-0.65f, 0.65f), random.RandfRange(-0.65f, 0.65f))).Normalized();
		_velocities.Add(direction.Rotated(shipRotation) * random.RandfRange(minimumSpeed, maximumSpeed));
		_colors.Add(color);
	}

	private static Vector2 RandomEllipseOffset(RandomNumberGenerator random, float horizontalRadius, float verticalRadius)
	{
		float angle = random.RandfRange(0.0f, Mathf.Tau);
		float radius = Mathf.Sqrt(random.Randf());
		return new Vector2(Mathf.Cos(angle) * horizontalRadius * radius, Mathf.Sin(angle) * verticalRadius * radius);
	}

	public override void _Process(double delta)
	{
		float step = (float)delta;
		_remaining -= step;
		for (int i = 0; i < _positions.Count; i++)
		{
			_positions[i] += _velocities[i] * step;
			_velocities[i] *= Mathf.Pow(0.35f, step);
		}
		QueueRedraw();
		if (_remaining <= 0.0f)
			QueueFree();
	}

	public override void _Draw()
	{
		float alpha = Mathf.Clamp(_remaining / FadeDurationSeconds, 0.0f, 1.0f);
		for (int i = 0; i < _positions.Count; i++)
		{
			Color color = _colors[i];
			color.A = alpha;
			DrawRect(new Rect2(_positions[i] - Vector2.One * 1.5f, Vector2.One * 3.0f), color);
		}
	}
}
