#nullable enable
using Godot;

/// <summary>Draws the player's health as twenty red segments at the top-left of the screen.</summary>
public partial class HealthBarController : Control
{
	[Export] public int SegmentCount { get; set; } = 20;

	private PlayerController? _player;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		MouseFilter = MouseFilterEnum.Ignore;
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		QueueRedraw();
	}

	public override void _Draw()
	{
		const float border = 2.0f;
		float width = Size.X;
		float height = Size.Y;
		float healthRatio = _player == null || _player.MaxHealth <= 0.0f
			? 1.0f
			: Mathf.Clamp(_player.Health / _player.MaxHealth, 0.0f, 1.0f);

		DrawRect(new Rect2(Vector2.Zero, Size), new Color("260b0b"));
		DrawRect(new Rect2(border, border, (width - border * 2.0f) * healthRatio, height - border * 2.0f), new Color("d82020"));
		DrawRect(new Rect2(Vector2.Zero, Size), new Color("f0f0f0"), false, border);

		for (int i = 1; i < SegmentCount; i++)
		{
			float x = width * i / SegmentCount;
			DrawLine(new Vector2(x, 0.0f), new Vector2(x, height), new Color("f0f0f0"), 1.0f);
		}
	}
}
