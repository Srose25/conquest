#nullable enable
using Godot;

/// <summary>Stationary breakable entity that converts bullet damage into resource yield.</summary>
public partial class BreakableEntityController : RigidBody2D, IDamageable
{
	[Signal] public delegate void DamagedEventHandler(float amount, float remainingHealth);
	[Signal] public delegate void ResourcesDroppedEventHandler(int amount, Vector2 position);
	[Signal] public delegate void DestroyedEventHandler(Vector2 position);

	[Export] public float MaxHealth { get; set; } = 2.0f;
	[Export] public int ResourceYieldMin { get; set; } = 1;
	[Export] public int ResourceYieldMax { get; set; } = 2;
	[Export(PropertyHint.Range, "0,1,0.05")] public float PelletDropChance { get; set; } = 0.35f;
	[Export] public int BreakPixelCountMin { get; set; } = 3;
	[Export] public int BreakPixelCountMax { get; set; } = 5;
	[Export] public float PushSlowdownDurationSeconds { get; set; } = 10.0f;

	private float _health;
	private bool _destroyed;
	private bool _wasPushed;
	private float _secondsSincePush;
	private float _pushSpeedAtStart;
	private readonly RandomNumberGenerator _random = new();

	public override void _Ready()
	{
		AddToGroup("breakable");
		CollisionLayer = 1 << 1;
		// Real collisions with the player, other asteroids, and bullets.
		CollisionMask = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3);
		GravityScale = 0.0f;
		LockRotation = true;
		LinearDamp = 0.0f;
		ContactMonitor = true;
		MaxContactsReported = 4;
		_health = Mathf.Max(1.0f, MaxHealth);
		_random.Randomize();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (GetContactCount() > 0 && LinearVelocity.LengthSquared() > 0.01f)
		{
			_wasPushed = true;
			_secondsSincePush = 0.0f;
			if (_pushSpeedAtStart <= 0.0f)
				_pushSpeedAtStart = LinearVelocity.Length();
		}
		else if (_wasPushed)
		{
			_secondsSincePush += (float)delta;
			float duration = Mathf.Max(0.1f, PushSlowdownDurationSeconds);
			if (_secondsSincePush >= duration)
			{
				LinearVelocity = Vector2.Zero;
				AngularVelocity = 0.0f;
				_pushSpeedAtStart = 0.0f;
				_wasPushed = false;
				return;
			}
			float remainingFraction = 1.0f - Mathf.Clamp(_secondsSincePush / duration, 0.0f, 1.0f);
			if (_pushSpeedAtStart > 0.0f)
				LinearVelocity = LinearVelocity.Normalized() * (_pushSpeedAtStart * remainingFraction);
		}
	}

	public void ApplyPush(Vector2 direction, float strength)
	{
		if (direction.LengthSquared() <= Mathf.Epsilon || strength <= 0.0f)
			return;
		// A pushed asteroid receives exactly half of the pushing object's current speed.
		LinearVelocity = direction.Normalized() * (strength * 0.5f);
		_wasPushed = true;
		_secondsSincePush = 0.0f;
		_pushSpeedAtStart = LinearVelocity.Length();
	}

	public void TakeDamage(float amount, Node? source = null)
	{
		if (_destroyed || amount <= 0.0f)
			return;

		_health = Mathf.Max(0.0f, _health - amount);
		EmitSignal(SignalName.Damaged, amount, _health);
		if (_health <= 0.0f)
			Destroy();
	}

	private void Destroy()
	{
		if (_destroyed)
			return;
		_destroyed = true;
		int minimum = Mathf.Max(0, ResourceYieldMin);
		int maximum = Mathf.Max(minimum, ResourceYieldMax);
		int yield = _random.RandiRange(minimum, maximum);
		int pixelMinimum = Mathf.Max(0, BreakPixelCountMin);
		int pixelMaximum = Mathf.Max(pixelMinimum, BreakPixelCountMax);
		BreakEffectController.Spawn(this, GlobalPosition, _random.RandiRange(pixelMinimum, pixelMaximum));
		if (_random.Randf() <= PelletDropChance)
		{
			ResourceDropController.Spawn(this, GlobalPosition, yield);
			EmitSignal(SignalName.ResourcesDropped, yield, GlobalPosition);
		}
		EmitSignal(SignalName.Destroyed, GlobalPosition);
		QueueFree();
	}
}
