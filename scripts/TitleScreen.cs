#nullable enable
using Godot;

/// <summary>Controls the title menu and transitions into the game scene.</summary>
public partial class TitleScreen : Control
{
	private const string GameScenePath = "res://scenes/game/Game.tscn";
	private const float SilenceDb = -80.0f;

	private Button _playButton = null!;
	private Button _settingsButton = null!;
	private Button _quitButton = null!;
	private Control _menuPanel = null!;
	private Control _settingsPanel = null!;
	private HSlider _bgmVolumeSlider = null!;
	private HSlider _sfxVolumeSlider = null!;
	private Label _bgmVolumeValue = null!;
	private Label _sfxVolumeValue = null!;
	private MainMenuMusicController _mainMenuMusic = null!;
	private AudioSettings _audioSettings = null!;
	private bool _transitionStarted;

	public override void _Ready()
	{
		_playButton = GetNode<Button>("MenuPanel/Menu/PlayButton");
		_settingsButton = GetNode<Button>("MenuPanel/Menu/SettingsButton");
		_quitButton = GetNode<Button>("MenuPanel/Menu/QuitButton");
		_menuPanel = GetNode<Control>("MenuPanel");
		_settingsPanel = GetNode<Control>("SettingsPanel");
		_bgmVolumeSlider = GetNode<HSlider>("SettingsPanel/Settings/BgmVolumeSlider");
		_sfxVolumeSlider = GetNode<HSlider>("SettingsPanel/Settings/SfxVolumeSlider");
		_bgmVolumeValue = GetNode<Label>("SettingsPanel/Settings/BgmVolumeValue");
		_sfxVolumeValue = GetNode<Label>("SettingsPanel/Settings/SfxVolumeValue");
		_mainMenuMusic = GetNode<MainMenuMusicController>("MainMenuMusic");
		_audioSettings = GetNode<AudioSettings>("/root/AudioSettings");

		_playButton.Pressed += OnPlayPressed;
		_settingsButton.Pressed += OpenSettings;
		_quitButton.Pressed += OnQuitPressed;
		GetNode<Button>("SettingsPanel/Settings/BackButton").Pressed += CloseSettings;
		_bgmVolumeSlider.Value = _audioSettings.BgmVolume * 100.0f;
		_sfxVolumeSlider.Value = _audioSettings.SfxVolume * 100.0f;
		_bgmVolumeSlider.ValueChanged += OnBgmVolumeChanged;
		_sfxVolumeSlider.ValueChanged += OnSfxVolumeChanged;
		UpdateVolumeLabels();
		StartMainMenuMusic();
	}

	private void StartMainMenuMusic()
	{
		AudioStream? track = _mainMenuMusic.MenuTrack ?? GetFirstMarkedTrack();
		if (track == null)
			return;

		_mainMenuMusic.Stream = track;
		_mainMenuMusic.VolumeDb = SilenceDb;
		_mainMenuMusic.Play();
		if (_mainMenuMusic.FadeInSeconds <= 0.0f)
		{
			_mainMenuMusic.VolumeDb = 0.0f;
			return;
		}
		_mainMenuMusic.CreateTween()
			.SetTrans(Tween.TransitionType.Linear)
			.SetEase(Tween.EaseType.InOut)
			.TweenProperty(_mainMenuMusic, "volume_db", 0.0f, _mainMenuMusic.FadeInSeconds);
	}

	private static AudioStream? GetFirstMarkedTrack()
	{
		PackedScene? gameScene = ResourceLoader.Load<PackedScene>(GameScenePath);
		Node? game = gameScene?.Instantiate();
		DynamicMusicController? music = game?.GetNodeOrNull<DynamicMusicController>("DynamicMusic");
		if (music != null)
		{
			foreach (DynamicMusicTrackPair? pair in music.TrackPairs)
			{
				if (pair?.AlwaysLoadFirst == true)
				{
					AudioStream? track = pair.OutOfCombatTrack ?? pair.CombatTrack;
					game?.QueueFree();
					return track;
				}
			}
		}

		game?.QueueFree();
		return null;
	}

	private void OpenSettings()
	{
		_menuPanel.Hide();
		_settingsPanel.Show();
	}

	private void CloseSettings()
	{
		_settingsPanel.Hide();
		_menuPanel.Show();
	}

	private void OnBgmVolumeChanged(double value)
	{
		_audioSettings.SetBgmVolume((float)value / 100.0f);
		UpdateVolumeLabels();
	}

	private void OnSfxVolumeChanged(double value)
	{
		_audioSettings.SetSfxVolume((float)value / 100.0f);
		UpdateVolumeLabels();
	}

	private void UpdateVolumeLabels()
	{
		_bgmVolumeValue.Text = $"{Mathf.RoundToInt((float)_bgmVolumeSlider.Value)}%";
		_sfxVolumeValue.Text = $"{Mathf.RoundToInt((float)_sfxVolumeSlider.Value)}%";
	}

	private void OnPlayPressed()
	{
		if (_transitionStarted)
			return;

		_transitionStarted = true;
		_playButton.Disabled = true;
		_quitButton.Disabled = true;
		PreserveMenuMusicForGame();
		StartSceneTransition();
	}

	private void PreserveMenuMusicForGame()
	{
		// The game controller adopts this player only when it is playing the
		// Always Load First pair. Reparenting keeps its playback position and
		// the node-bound linear fade alive through the scene transition.
		if (_mainMenuMusic.Stream != null && _mainMenuMusic.GetParent() == this)
			_mainMenuMusic.Reparent(GetTree().Root);
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
