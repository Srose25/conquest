#nullable enable
using Godot;

/// <summary>
/// Collectible experience pickup. One scene supports all denominations so the
/// drop spawner can compose an award with the largest sprites first.
/// </summary>
public partial class XpDropController : Area2D, IMagnetizableDrop
{
	[Signal] public delegate void CollectedEventHandler(int amount);
	[Export] public int Amount { get; set; } = 1;

	private Vector2 _burstDirection;
	private float _burstTravelDistance;
	private float _burstElapsed;
	private const float BurstDurationSeconds = 5.0f;

	public override void _Ready()
	{
		CollisionLayer = 4;
		CollisionMask = 1;
		BodyEntered += OnBodyEntered;
		GetNode<Sprite2D>("Sprite2D").Texture = GetTextureForAmount(Amount);
	}

	public void InitializeBurst(Vector2 direction, float travelDistance)
	{
		_burstDirection = direction.LengthSquared() > Mathf.Epsilon ? direction.Normalized() : Vector2.Right;
		_burstTravelDistance = Mathf.Max(0.0f, travelDistance);
		_burstElapsed = 0.0f;
	}

	/// <summary>Spawns a greedy 15/9/5/2/1 denomination decomposition.</summary>
	public static void Spawn(Node source, Vector2 position, int amount)
	{
		if (amount <= 0)
			return;
		PackedScene? scene = GD.Load<PackedScene>("res://scenes/resources/XpDrop.tscn");
		Node? container = source.GetTree().CurrentScene?.GetNodeOrNull("World/ResourceDrops");
		Node? parent = container ?? source.GetParent() ?? source.GetTree().CurrentScene;
		if (scene == null || parent == null)
			return;

		RandomNumberGenerator random = new();
		random.Randomize();
		float hitboxRadius = GetSourceHitboxRadius(source);
		int remaining = amount;
		int[] denominations = { 15, 9, 5, 2, 1 };
		foreach (int denomination in denominations)
		{
			while (remaining >= denomination)
			{
				remaining -= denomination;
				XpDropController drop = scene.Instantiate<XpDropController>();
				drop.Amount = denomination;
				Vector2 direction = Vector2.FromAngle(random.RandfRange(0.0f, Mathf.Tau));
				float initialDistance = Mathf.Sqrt(random.Randf()) * hitboxRadius * 0.45f;
				Vector2 dropPosition = position + direction * initialDistance;
				drop.InitializeBurst(direction, random.RandfRange(0.55f, 1.20f) * hitboxRadius);
				if (parent is Node2D parent2D)
					drop.Position = parent2D.ToLocal(dropPosition);
				else
					drop.GlobalPosition = dropPosition;
				parent.AddChild(drop);
			}
		}
	}

	public void PullToward(Vector2 target, float speed, float delta)
	{
		GlobalPosition = GlobalPosition.MoveToward(target, Mathf.Max(0.0f, speed) * delta);
	}

	public override void _Process(double delta)
	{
		if (_burstElapsed >= BurstDurationSeconds || _burstTravelDistance <= 0.0f)
			return;
		float previousProgress = _burstElapsed / BurstDurationSeconds;
		_burstElapsed = Mathf.Min(BurstDurationSeconds, _burstElapsed + (float)delta);
		float currentProgress = _burstElapsed / BurstDurationSeconds;
		float previousDistance = _burstTravelDistance * (2.0f * previousProgress - previousProgress * previousProgress);
		float currentDistance = _burstTravelDistance * (2.0f * currentProgress - currentProgress * currentProgress);
		GlobalPosition += _burstDirection * (currentDistance - previousDistance);
	}

	private void OnBodyEntered(Node body)
	{
		if (body is not PlayerController player)
			return;
		ProgressionManager? progression = GetTree().CurrentScene?.GetNodeOrNull<ProgressionManager>("ProgressionManager");
		progression?.AddExperience(Amount);
		EmitSignal(SignalName.Collected, Amount);
		QueueFree();
	}

	private static Texture2D? GetTextureForAmount(int amount)
	{
		int denomination = amount switch
		{
			>= 15 => 15,
			>= 9 => 9,
			>= 5 => 5,
			>= 2 => 2,
			_ => 1
		};
		return GD.Load<Texture2D>($"res://Sprites/XP_Pellet_{denomination}.png");
	}

	private static float GetSourceHitboxRadius(Node source)
	{
		CollisionShape2D? collision = source.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		return collision?.Shape is CircleShape2D circle ? circle.Radius : 12.0f;
	}
}
