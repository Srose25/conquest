#nullable enable
using Godot;

/// <summary>Moves a player bullet, reports its first target hit, and despawns it.</summary>
public partial class BulletController : Area2D
{
	[Signal] public delegate void HitEventHandler(Node target);
	[Export] public float LifetimeSeconds { get; set; } = 1.15f;

	private Vector2 _direction = Vector2.Right;
	private float _speed;
	private float _remainingLifetime;
	private Node? _source;
	private bool _hasHit;

	public override void _Ready()
	{
		CollisionLayer = 1 << 3;
		CollisionMask = (1 << 1) | (1 << 2);
		_remainingLifetime = LifetimeSeconds;
		BodyEntered += OnBodyEntered;
		GetNode<VisibleOnScreenNotifier2D>("VisibleOnScreenNotifier2D").ScreenExited += OnScreenExited;
		QueueRedraw();
	}

	public void Initialize(Vector2 direction, float speed, Node? source = null)
	{
		_direction = direction.LengthSquared() > Mathf.Epsilon ? direction.Normalized() : Vector2.Right;
		_speed = Mathf.Max(0.0f, speed);
		_source = source;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_speed <= 0.0f)
			return;
		GlobalPosition += _direction * _speed * (float)delta;
		_remainingLifetime -= (float)delta;
		if (_remainingLifetime <= 0.0f)
			QueueFree();
	}

	public override void _Draw() => DrawCircle(Vector2.Zero, 4.0f, new Color("00ffff"));

	private void OnBodyEntered(Node body)
	{
		if (_hasHit || body == _source || !IsInstanceValid(body))
			return;
		_hasHit = true;
		BulletImpactController.Spawn(this, GlobalPosition);
		if (body is IDamageable damageable)
			damageable.TakeDamage(1.0f, this);
		else if (body.GetParent() is IDamageable parentDamageable)
			parentDamageable.TakeDamage(1.0f, this);
		EmitSignal(SignalName.Hit, body);
		QueueFree();
	}

	private void OnScreenExited()
	{
		if (!_hasHit)
			QueueFree();
	}
}
