#nullable enable
using Godot;

/// <summary>Controls the title menu and transitions into the game scene.</summary>
public partial class TitleScreen : Control
{
	private const string GameScenePath = "res://scenes/game/Game.tscn";

	private Button _playButton = null!;
	private Button _quitButton = null!;
	private bool _transitionStarted;

	public override void _Ready()
	{
		_playButton = GetNode<Button>("MenuPanel/Menu/PlayButton");
		_quitButton = GetNode<Button>("MenuPanel/Menu/QuitButton");

		_playButton.Pressed += OnPlayPressed;
		_quitButton.Pressed += OnQuitPressed;

		// Settings is intentionally disabled until its screen is implemented.
		GetNode<Button>("MenuPanel/Menu/SettingsButton").Disabled = true;
	}

	private void OnPlayPressed()
	{
		if (_transitionStarted)
			return;

		_transitionStarted = true;
		_playButton.Disabled = true;
		_quitButton.Disabled = true;
		StartSceneTransition();
	}

	private void OnQuitPressed()
	{
		var confirmation = new ConfirmationDialog
		{
			Title = "Quit Conquest?",
			DialogText = "Are you sure?",
			OkButtonText = "Quit",
			CancelButtonText = "Cancel"
		};

		AddChild(confirmation);
		confirmation.Confirmed += () => GetTree().Quit();
		confirmation.PopupCentered();
	}

	private void StartSceneTransition()
	{
		// Keep the fade overlay on the root viewport so it survives the scene swap.
		var transitionLayer = new CanvasLayer { Layer = 100 };
		var overlay = new ColorRect
		{
			Color = new Color(0, 0, 0, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		transitionLayer.AddChild(overlay);
		GetTree().Root.AddChild(transitionLayer);

		// Use the SceneTree tween so it remains alive after this title-screen node
		// is freed by ChangeSceneToFile.
		var fade = GetTree().CreateTween();
		fade.TweenProperty(overlay, "color:a", 1.0f, 0.45f);
		fade.TweenCallback(Callable.From(() => GetTree().ChangeSceneToFile(GameScenePath)));
		fade.TweenInterval(0.05f);
		fade.TweenProperty(overlay, "color:a", 0.0f, 0.45f);
		fade.TweenCallback(Callable.From(transitionLayer.QueueFree));
	}
}
