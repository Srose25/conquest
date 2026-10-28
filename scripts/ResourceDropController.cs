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
		ResourceDropController drop = scene.Instantiate<ResourceDropController>();
		drop.Amount = amount;
		Node? container = source.GetTree().CurrentScene?.GetNodeOrNull("World/ResourceDrops");
		(container ?? source.GetParent() ?? source.GetTree().CurrentScene)?.AddChild(drop);
		drop.GlobalPosition = position;
	}

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, 8.0f, new Color("f5c542"));
		DrawCircle(Vector2.Zero, 4.0f, new Color("fff1a8"));
	}

	private void OnBodyEntered(Node body)
	{
		if (body is not PlayerController player)
			return;
		player.AddResources(Amount);
		EmitSignal(SignalName.Collected, Amount);
		QueueFree();
	}
}
