#nullable enable
using Godot;

/// <summary>Owns the in-game pause and audio settings menus.</summary>
public partial class PauseMenuController : Control
{
	private const string TitleScenePath = "res://scenes/TitleScreen.tscn";

	private Control _mainMenu = null!;
	private Control _settingsMenu = null!;
	private HSlider _bgmSlider = null!;
	private HSlider _sfxSlider = null!;
	private Label _bgmValue = null!;
	private Label _sfxValue = null!;
	private AudioSettings _audioSettings = null!;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_mainMenu = GetNode<Control>("PausePanel/MainMenu");
		_settingsMenu = GetNode<Control>("PausePanel/SettingsMenu");
		_bgmSlider = GetNode<HSlider>("PausePanel/SettingsMenu/BgmVolumeSlider");
		_sfxSlider = GetNode<HSlider>("PausePanel/SettingsMenu/SfxVolumeSlider");
		_bgmValue = GetNode<Label>("PausePanel/SettingsMenu/BgmVolumeValue");
		_sfxValue = GetNode<Label>("PausePanel/SettingsMenu/SfxVolumeValue");
		_audioSettings = GetNode<AudioSettings>("/root/AudioSettings");

		GetNode<Button>("PausePanel/MainMenu/ResumeButton").Pressed += Resume;
		GetNode<Button>("PausePanel/MainMenu/SettingsButton").Pressed += OpenSettings;
		GetNode<Button>("PausePanel/MainMenu/QuitToTitleButton").Pressed += QuitToTitle;
		GetNode<Button>("PausePanel/SettingsMenu/BackButton").Pressed += ShowMainMenu;
		_bgmSlider.Value = _audioSettings.BgmVolume * 100.0f;
		_sfxSlider.Value = _audioSettings.SfxVolume * 100.0f;
		_bgmSlider.ValueChanged += OnBgmVolumeChanged;
		_sfxSlider.ValueChanged += OnSfxVolumeChanged;
		UpdateVolumeLabels();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!@event.IsActionPressed("ui_cancel") || @event.IsEcho())
			return;
		if (!Visible)
			Pause();
		else if (_settingsMenu.Visible)
			ShowMainMenu();
		else
			Resume();
		GetViewport().SetInputAsHandled();
	}

	private void Pause()
	{
		Show();
		ShowMainMenu();
		GetTree().Paused = true;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void Resume()
	{
		GetTree().Paused = false;
		Hide();
		Input.MouseMode = Input.MouseModeEnum.Hidden;
	}

	private void OpenSettings()
	{
		_mainMenu.Hide();
		_settingsMenu.Show();
	}

	private void ShowMainMenu()
	{
		_settingsMenu.Hide();
		_mainMenu.Show();
	}

	private void QuitToTitle()
	{
		GetTree().Paused = false;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		GetTree().ChangeSceneToFile(TitleScenePath);
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
		_bgmValue.Text = $"{Mathf.RoundToInt((float)_bgmSlider.Value)}%";
		_sfxValue.Text = $"{Mathf.RoundToInt((float)_sfxSlider.Value)}%";
	}
}
