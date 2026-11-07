#nullable enable
using Godot;

/// <summary>A collectible resource dropped by a destroyed entity.</summary>
public partial class ResourceDropController : Area2D
{
	[Signal] public delegate void CollectedEventHandler(int amount);
	[Export] public int Amount { get; set; } = 1;

	private Vector2 _burstDirection;
	private float _burstTravelDistance;
	private float _burstElapsed;
	private const float BurstDurationSeconds = 5.0f;

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = 1;
		BodyEntered += OnBodyEntered;
	}

	public void InitializeBurst(Vector2 direction, float travelDistance)
	{
		_burstDirection = direction.LengthSquared() > Mathf.Epsilon ? direction.Normalized() : Vector2.Right;
		_burstTravelDistance = Mathf.Max(0.0f, travelDistance);
		_burstElapsed = 0.0f;
	}

	public override void _Process(double delta)
	{
		if (_burstElapsed >= BurstDurationSeconds || _burstTravelDistance <= 0.0f)
			return;

		float previousProgress = _burstElapsed / BurstDurationSeconds;
		_burstElapsed = Mathf.Min(BurstDurationSeconds, _burstElapsed + (float)delta);
		float currentProgress = _burstElapsed / BurstDurationSeconds;
		// v(t) eases linearly to zero. Integrating 2t - t² produces a subtle
		// outward burst that reaches its exact endpoint after five seconds.
		float previousDistance = _burstTravelDistance * (2.0f * previousProgress - previousProgress * previousProgress);
		float currentDistance = _burstTravelDistance * (2.0f * currentProgress - currentProgress * currentProgress);
		GlobalPosition += _burstDirection * (currentDistance - previousDistance);
	}

	public static void Spawn(Node source, Vector2 position, int amount)
	{
		if (amount <= 0)
			return;
		PackedScene? scene = GD.Load<PackedScene>("res://scenes/resources/ResourceDrop.tscn");
		if (scene == null)
			return;
		Node? container = source.GetTree().CurrentScene?.GetNodeOrNull("World/ResourceDrops");
		Node? parent = container ?? source.GetParent() ?? source.GetTree().CurrentScene;
		if (parent == null)
			return;
		RandomNumberGenerator random = new();
		random.Randomize();
		float hitboxRadius = GetSourceHitboxRadius(source);
		for (int i = 0; i < amount; i++)
		{
			ResourceDropController drop = scene.Instantiate<ResourceDropController>();
			drop.Amount = 1;
			float angle = random.RandfRange(0.0f, Mathf.Tau);
			Vector2 direction = Vector2.FromAngle(angle);
			// Spawn in the source hitbox, then travel no farther than 1.2 radii.
			// Even the farthest pellet remains inside twice the source radius.
			float initialDistance = Mathf.Sqrt(random.Randf()) * hitboxRadius * 0.45f;
			float burstTravel = random.RandfRange(0.55f, 1.20f) * hitboxRadius;
			Vector2 dropPosition = position + direction * initialDistance;
			drop.InitializeBurst(direction, burstTravel);
			if (parent is Node2D parent2D)
				drop.Position = parent2D.ToLocal(dropPosition);
			else
				drop.GlobalPosition = dropPosition;
			parent.AddChild(drop);
		}
	}

	private static float GetSourceHitboxRadius(Node source)
	{
		CollisionShape2D? collision = source.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		if (collision?.Shape is CircleShape2D circle)
			return circle.Radius;
		return 12.0f;
	}

	private void OnBodyEntered(Node body)
	{
		if (body is not PlayerController player)
			return;
		player.AddResources(Amount);
		player.RecoverHealth(1.0f);
		EmitSignal(SignalName.Collected, Amount);
		QueueFree();
	}
}
