#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>Three-second ship explosion using colors and component positions from the enemy sprite.</summary>
public partial class EnemyShipExplosionController : Node2D
{
	private readonly List<Vector2> _positions = new();
	private readonly List<Vector2> _velocities = new();
	private readonly List<Color> _colors = new();
	private float _remaining;
	private const float LifetimeSeconds = 3.0f;
	private const float FadeDurationSeconds = 0.8f;

	public static void Spawn(Node source, Vector2 position, float shipRotation)
	{
		EnemyShipExplosionController effect = new();
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

		// Grey hull fragments spread from the main body.
		int hullCount = random.RandiRange(10, 14);
		for (int i = 0; i < hullCount; i++)
			AddParticle(new Vector2(random.RandfRange(-18.0f, 18.0f), random.RandfRange(-14.0f, 16.0f)), new Color("8d93a1"), shipRotation, random, 45.0f, 125.0f);

		// Purple windshield fragments come from the forward/top section of the sprite.
		for (int i = 0; i < 5; i++)
			AddParticle(new Vector2(random.RandfRange(-7.0f, 7.0f), random.RandfRange(-17.0f, -7.0f)), new Color("be6cff"), shipRotation, random, 55.0f, 130.0f);

		// Red fragments originate at the left and right gun tips.
		for (int i = 0; i < 4; i++)
		{
			float side = i % 2 == 0 ? -1.0f : 1.0f;
			AddParticle(new Vector2(side * random.RandfRange(16.0f, 23.0f), random.RandfRange(3.0f, 12.0f)), new Color("e33b45"), shipRotation, random, 55.0f, 135.0f);
		}

		// Blue fragments come from the multicolored rear thrusters.
		for (int i = 0; i < 4; i++)
			AddParticle(new Vector2(random.RandfRange(-9.0f, 9.0f), random.RandfRange(12.0f, 20.0f)), new Color("3a9dff"), shipRotation, random, 55.0f, 135.0f);

		// White highlights reflect the bright edges of the ship sprite.
		for (int i = 0; i < 4; i++)
			AddParticle(new Vector2(random.RandfRange(-15.0f, 15.0f), random.RandfRange(-13.0f, 10.0f)), new Color("f2f5ff"), shipRotation, random, 50.0f, 120.0f);
		QueueRedraw();
	}

	private void AddParticle(Vector2 localOffset, Color color, float shipRotation, RandomNumberGenerator random, float minimumSpeed, float maximumSpeed)
	{
		_positions.Add(localOffset.Rotated(shipRotation));
		Vector2 direction = (localOffset.Normalized() + new Vector2(random.RandfRange(-0.65f, 0.65f), random.RandfRange(-0.65f, 0.65f))).Normalized();
		_velocities.Add(direction.Rotated(shipRotation) * random.RandfRange(minimumSpeed, maximumSpeed));
		_colors.Add(color);
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
