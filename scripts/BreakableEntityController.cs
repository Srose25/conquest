#nullable enable
using Godot;

/// <summary>Stationary breakable entity that converts bullet damage into resource yield.</summary>
public partial class BreakableEntityController : StaticBody2D, IDamageable
{
	[Signal] public delegate void DamagedEventHandler(float amount, float remainingHealth);
	[Signal] public delegate void ResourcesDroppedEventHandler(int amount, Vector2 position);
	[Signal] public delegate void DestroyedEventHandler(Vector2 position);

	[Export] public float MaxHealth { get; set; } = 2.0f;
	[Export] public int ResourceYieldMin { get; set; } = 1;
	[Export] public int ResourceYieldMax { get; set; } = 2;

	private float _health;
	private bool _destroyed;
	private readonly RandomNumberGenerator _random = new();

	public override void _Ready()
	{
		CollisionLayer = 1 << 1;
		CollisionMask = 1 << 3;
		_health = Mathf.Max(1.0f, MaxHealth);
		_random.Randomize();
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
		ResourceDropController.Spawn(this, GlobalPosition, yield);
		EmitSignal(SignalName.ResourcesDropped, yield, GlobalPosition);
		EmitSignal(SignalName.Destroyed, GlobalPosition);
		QueueFree();
	}
}
