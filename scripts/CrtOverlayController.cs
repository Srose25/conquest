#nullable enable
using Godot;

/// <summary>Shows the native-resolution CRT post-process according to Settings.</summary>
public partial class CrtOverlayController : ColorRect
{
	private AudioSettings? _settings;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		_settings = GetNodeOrNull<AudioSettings>("/root/AudioSettings");
		Visible = _settings?.CrtEnabled ?? false;
	}

	public override void _Process(double delta)
	{
		bool enabled = _settings?.CrtEnabled ?? false;
		if (Visible != enabled)
			Visible = enabled;
	}
}
