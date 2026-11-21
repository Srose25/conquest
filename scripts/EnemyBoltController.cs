#nullable enable
using Godot;

/// <summary>Red enemy bolt that damages only the player and despawns outside the camera view.</summary>
public partial class EnemyBoltController : Area2D
{
	[Export] public float LifetimeSeconds { get; set; } = 1.8f;
	private Vector2 _direction = Vector2.Right;
	private float _speed;
	private float _damage = 1.0f;
	private float _remainingLifetime;
	private Node? _source;
	private bool _hasHit;

	public override void _Ready()
	{
		CollisionLayer = 1 << 4;
		// Enemy shots normally target the player, but can also clear a passive
		// asteroid that has repeatedly blocked their route.
		CollisionMask = (1 << 0) | (1 << 1);
		_remainingLifetime = LifetimeSeconds;
		BodyEntered += OnBodyEntered;
		GetNode<VisibleOnScreenNotifier2D>("VisibleOnScreenNotifier2D").ScreenExited += OnScreenExited;
		QueueRedraw();
	}

	public void Initialize(Vector2 direction, float speed, float damage, Node? source)
	{
		_direction = direction.LengthSquared() > Mathf.Epsilon ? direction.Normalized() : Vector2.Right;
		_speed = Mathf.Max(0.0f, speed);
		_damage = Mathf.Max(0.0f, damage);
		_source = source;
		Rotation = _direction.Angle();
	}

	public override void _PhysicsProcess(double delta)
	{
		GlobalPosition += _direction * _speed * (float)delta;
		_remainingLifetime -= (float)delta;
		if (_remainingLifetime <= 0.0f)
			QueueFree();
	}

	public override void _Draw()
	{
		DrawRect(new Rect2(-7.0f, -2.0f, 14.0f, 4.0f), new Color("7d0b16"));
		DrawRect(new Rect2(-6.0f, -1.0f, 12.0f, 2.0f), new Color("ff4a58"));
	}

	private void OnBodyEntered(Node body)
	{
		if (_hasHit || body == _source)
			return;
		_hasHit = true;
		EnemyBoltImpactController.Spawn(this, GlobalPosition);
		if (body is PlayerController player)
		{
			player.TakeDamage(_damage);
			SoundEffectsController.PlayOneShot(this, SoundEffectsController.Effect.ShipHit);
		}
		else if (body is BreakableEntityController asteroid)
		{
			asteroid.TakeDamage(_damage, this);
			SoundEffectsController.PlayOneShot(this, SoundEffectsController.Effect.AsteroidHit);
		}
		else
		{
			_hasHit = false;
			return;
		}
		QueueFree();
	}

	private void OnScreenExited()
	{
		if (!_hasHit)
			QueueFree();
	}
}
