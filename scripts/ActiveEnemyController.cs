#nullable enable
using Godot;

/// <summary>Chases the player, attacks at close range, and drops more resources when destroyed.</summary>
public partial class ActiveEnemyController : CharacterBody2D, IDamageable
{
	[Signal] public delegate void DamagedEventHandler(float amount, float remainingHealth);
	[Signal] public delegate void ResourcesDroppedEventHandler(int amount, Vector2 position);
	[Signal] public delegate void DestroyedEventHandler(Vector2 position);

	[Export] public float MaxHealth { get; set; } = 4.0f;
	[Export] public int ResourceYieldMin { get; set; } = 3;
	[Export] public int ResourceYieldMax { get; set; } = 6;
	[Export] public float MoveSpeed { get; set; } = 85.0f;
	[Export] public float AttackRange { get; set; } = 48.0f;
	[Export] public float AttackDamage { get; set; } = 1.0f;

	private float _health;
	private Node2D? _player;
	private Timer _attackTimer = null!;
	private bool _destroyed;
	private readonly RandomNumberGenerator _random = new();

	public override void _Ready()
	{
		CollisionLayer = 1 << 2;
		CollisionMask = 1;
		_health = Mathf.Max(1.0f, MaxHealth);
		_random.Randomize();
		_attackTimer = GetNode<Timer>("AttackTimer");
		_attackTimer.WaitTime = 1.0f;
		_attackTimer.OneShot = false;
		_attackTimer.Timeout += OnAttackTimerTimeout;
		_player = GetTree().GetFirstNodeInGroup("player") as Node2D;
	}

	public override void _PhysicsProcess(double delta)
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as Node2D;
		if (!IsInstanceValid(_player))
		{
			Velocity = Vector2.Zero;
			return;
		}

		float distance = GlobalPosition.DistanceTo(_player.GlobalPosition);
		if (distance > AttackRange)
		{
			Velocity = GlobalPosition.DirectionTo(_player.GlobalPosition) * MoveSpeed;
			MoveAndSlide();
		}
		else
		{
			Velocity = Vector2.Zero;
			if (_attackTimer.IsStopped())
				_attackTimer.Start();
		}
		Rotation = GlobalPosition.DirectionTo(_player.GlobalPosition).Angle();
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

	private void OnAttackTimerTimeout()
	{
		if (_destroyed || !IsInstanceValid(_player) || GlobalPosition.DistanceTo(_player!.GlobalPosition) > AttackRange)
			return;
		if (_player is PlayerController player)
			player.TakeDamage(AttackDamage);
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
