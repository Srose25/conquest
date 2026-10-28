#nullable enable
using Godot;

/// <summary>Centers the camera on its parent while keeping a 1/16-second follow delay.</summary>
public partial class CameraFollowController : Camera2D
{
	[Export] public float FollowDelaySeconds { get; set; } = 1.0f / 8.0f;

	private Node2D _target = null!;

	public override void _Ready()
	{
		ProcessPriority = -100;
		_target = GetParent<Node2D>();
		TopLevel = true;
		Enabled = true;
		GlobalPosition = _target.GlobalPosition;
	}

	public override void _Process(double delta)
	{
		if (!IsInstanceValid(_target))
			return;

		float delay = Mathf.Max(0.001f, FollowDelaySeconds);
		// Exponential smoothing gives a stable, frame-rate-independent delay.
		float blend = 1.0f - Mathf.Exp(-(float)delta / delay);
		GlobalPosition = GlobalPosition.Lerp(_target.GlobalPosition, blend);
	}
}
