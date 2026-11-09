#nullable enable
using Godot;

/// <summary>A collectible resource dropped by a destroyed entity.</summary>
public partial class ResourceDropController : Area2D
{
	[Signal] public delegate void CollectedEventHandler(int amount);
	[Export] public int Amount { get; set; } = 1;

	public override void _Ready()
	{
		CollisionLayer = 4;
		CollisionMask = 1;
		BodyEntered += OnBodyEntered;
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
		for (int i = 0; i < amount; i++)
		{
			ResourceDropController drop = scene.Instantiate<ResourceDropController>();
			drop.Amount = 1;
			float angle = i * Mathf.Tau / Mathf.Max(1, amount);
			Vector2 dropPosition = position + Vector2.FromAngle(angle) * 12.0f;
			if (parent is Node2D parent2D)
				drop.Position = parent2D.ToLocal(dropPosition);
			else
				drop.GlobalPosition = dropPosition;
			parent.AddChild(drop);
		}
	}

	public void PullToward(Vector2 target, float speed, float delta)
	{
		GlobalPosition = GlobalPosition.MoveToward(target, Mathf.Max(0.0f, speed) * delta);
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
