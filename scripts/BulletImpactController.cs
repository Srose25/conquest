#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>Short cyan particle burst emitted where a bullet hits an entity.</summary>
public partial class BulletImpactController : Node2D
{
	private readonly List<Vector2> _positions = new();
	private readonly List<Vector2> _velocities = new();
	private float _remaining = 0.45f;

	public static void Spawn(Node source, Vector2 position)
	{
		BulletImpactController effect = new();
		effect.GlobalPosition = position;
		effect.Initialize();
		Node? parent = source.GetTree().CurrentScene ?? source.GetParent();
		parent?.AddChild(effect);
	}

	private void Initialize()
	{
		RandomNumberGenerator random = new();
		random.Randomize();
		int particleCount = random.RandiRange(2, 3);
		for (int i = 0; i < particleCount; i++)
		{
			_positions.Add(Vector2.Zero);
			_velocities.Add(Vector2.FromAngle(random.RandfRange(0.0f, Mathf.Tau)) * random.RandfRange(40.0f, 95.0f));
		}
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		float step = (float)delta;
		_remaining -= step;
		for (int i = 0; i < _positions.Count; i++)
		{
			_positions[i] += _velocities[i] * step;
			_velocities[i] *= Mathf.Pow(0.2f, step);
		}
		QueueRedraw();
		if (_remaining <= 0.0f)
			QueueFree();
	}

	public override void _Draw()
	{
		Color color = new(0.0f, 1.0f, 1.0f, Mathf.Clamp(_remaining / 0.45f, 0.0f, 1.0f));
		foreach (Vector2 position in _positions)
			DrawRect(new Rect2(position - Vector2.One * 1.5f, Vector2.One * 3.0f), color);
	}
}
