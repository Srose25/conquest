#nullable enable
using Godot;

/// <summary>Short-lived white pixel burst used when an asteroid breaks.</summary>
public partial class BreakEffectController : Node2D
{
	[Export] public float LifetimeSeconds { get; set; } = 3.0f;

	private Vector2[] _positions = System.Array.Empty<Vector2>();
	private Vector2[] _velocities = System.Array.Empty<Vector2>();
	private float _remaining;
	private const float FadeDurationSeconds = 0.7f;

	public static void Spawn(Node source, Vector2 position, int pixelCount)
	{
		if (pixelCount <= 0)
			return;
		BreakEffectController effect = new();
		effect.GlobalPosition = position;
		effect.Initialize(pixelCount);
		Node? parent = source.GetTree().CurrentScene ?? source.GetParent();
		parent?.AddChild(effect);
	}

	private void Initialize(int pixelCount)
	{
		_remaining = Mathf.Max(0.1f, LifetimeSeconds);
		_positions = new Vector2[pixelCount];
		_velocities = new Vector2[pixelCount];
		RandomNumberGenerator random = new();
		random.Randomize();
		for (int i = 0; i < pixelCount; i++)
		{
			_positions[i] = Vector2.Zero;
			_velocities[i] = Vector2.FromAngle(random.RandfRange(0.0f, Mathf.Tau)) * random.RandfRange(45.0f, 115.0f);
		}
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		float step = (float)delta;
		_remaining -= step;
		for (int i = 0; i < _positions.Length; i++)
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
		Color pixelColor = new(1.0f, 1.0f, 1.0f, alpha);
		foreach (Vector2 position in _positions)
			DrawRect(new Rect2(position - Vector2.One * 1.5f, Vector2.One * 3.0f), pixelColor);
	}
}
