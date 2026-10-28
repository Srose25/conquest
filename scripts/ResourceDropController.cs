#nullable enable
using Godot;

/// <summary>A collectible resource dropped by a destroyed entity.</summary>
public partial class ResourceDropController : Area2D
{
	[Signal] public delegate void CollectedEventHandler(int amount);
	[Export] public int Amount { get; set; } = 1;

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = 1;
		BodyEntered += OnBodyEntered;
		QueueRedraw();
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

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, 8.0f, new Color("fff1a8"));
		DrawCircle(Vector2.Zero, 4.0f, new Color("f5c542"));
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
