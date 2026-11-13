#nullable enable
using Godot;

/// <summary>Displays the delayed death menu without pausing the world simulation.</summary>
public partial class GameOverController : Control
{
	private const string TitleScenePath = "res://scenes/TitleScreen.tscn";
	private const float GameOverFadeSeconds = 5.0f;
	private const float ButtonStaggerSeconds = 0.5f;
	private const float ButtonPopSeconds = 0.20f;

	private Label _heading = null!;
	private Button _retryButton = null!;
	private Button _quitButton = null!;
	private bool _sequenceStarted;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_heading = GetNode<Label>("Center/Menu/Heading");
		_retryButton = GetNode<Button>("Center/Menu/RetryButton");
		_quitButton = GetNode<Button>("Center/Menu/QuitButton");
		_retryButton.Pressed += Retry;
		_quitButton.Pressed += QuitToTitle;
		ResetPresentation();
		CallDeferred(MethodName.ConnectPlayer);
	}

	private void ConnectPlayer()
	{
		if (GetTree().GetFirstNodeInGroup("player") is not PlayerController player)
			return;
		player.Died += ShowGameOver;
		if (player.IsDead)
			ShowGameOver();
	}

	private void ResetPresentation()
	{
		Hide();
		_heading.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
		PrepareButton(_retryButton);
		PrepareButton(_quitButton);
	}

	private static void PrepareButton(Control button)
	{
		button.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
		button.Scale = new Vector2(0.82f, 0.82f);
	}

	private void ShowGameOver()
	{
		if (_sequenceStarted)
			return;
		_sequenceStarted = true;
		GetMusicController()?.HandlePlayerDeath();
		Input.MouseMode = Input.MouseModeEnum.Visible;
		Show();
		Tween sequence = CreateTween();
		sequence.TweenProperty(_heading, "modulate:a", 1.0f, GameOverFadeSeconds);
		sequence.TweenCallback(Callable.From(() => PopIn(_retryButton)));
		sequence.TweenInterval(ButtonStaggerSeconds);
		sequence.TweenCallback(Callable.From(() => PopIn(_quitButton)));
	}

	private static void PopIn(Control button)
	{
		button.PivotOffset = button.Size * 0.5f;
		Tween pop = button.CreateTween().SetParallel();
		pop.TweenProperty(button, "modulate:a", 1.0f, ButtonPopSeconds);
		pop.TweenProperty(button, "scale", Vector2.One, ButtonPopSeconds).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
	}

	private void Retry()
	{
		Input.MouseMode = Input.MouseModeEnum.Hidden;
		GetMusicController()?.PreservePlaybackForRetry();
		GetTree().ReloadCurrentScene();
	}

	private DynamicMusicController? GetMusicController() => GetTree().GetFirstNodeInGroup("dynamic_music") as DynamicMusicController;

	private void QuitToTitle()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;
		GetTree().ChangeSceneToFile(TitleScenePath);
	}
}
