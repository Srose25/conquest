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
	private Button _crtFilterOnButton = null!;
	private Button _crtFilterOffButton = null!;
	private CheckButton _developerModeToggle = null!;
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
		_crtFilterOnButton = GetNode<Button>("PausePanel/SettingsMenu/CrtFilterRow/CrtFilterOnButton");
		_crtFilterOffButton = GetNode<Button>("PausePanel/SettingsMenu/CrtFilterRow/CrtFilterOffButton");
		_developerModeToggle = GetNode<CheckButton>("PausePanel/SettingsMenu/DevModeRow/DevModeToggle");
		_bgmValue = GetNode<Label>("PausePanel/SettingsMenu/BgmVolumeValue");
		_sfxValue = GetNode<Label>("PausePanel/SettingsMenu/SfxVolumeValue");
		_audioSettings = GetNode<AudioSettings>("/root/AudioSettings");

		GetNode<Button>("PausePanel/MainMenu/ResumeButton").Pressed += Resume;
		GetNode<Button>("PausePanel/MainMenu/SettingsButton").Pressed += OpenSettings;
		GetNode<Button>("PausePanel/MainMenu/QuitToTitleButton").Pressed += QuitToTitle;
		GetNode<Button>("PausePanel/SettingsMenu/BackButton").Pressed += ShowMainMenu;
		_bgmSlider.Value = _audioSettings.BgmVolume * 100.0f;
		_sfxSlider.Value = _audioSettings.SfxVolume * 100.0f;
		UpdateCrtFilterButtons(_audioSettings.CrtEnabled);
		_bgmSlider.ValueChanged += OnBgmVolumeChanged;
		_sfxSlider.ValueChanged += OnSfxVolumeChanged;
		_crtFilterOnButton.Pressed += () => SetCrtFilterEnabled(true);
		_crtFilterOffButton.Pressed += () => SetCrtFilterEnabled(false);
		DeveloperModeController developerTools = GetNode<DeveloperModeController>("/root/DeveloperTools");
		_developerModeToggle.Visible = DeveloperModeController.IsAvailable;
		_developerModeToggle.ButtonPressed = developerTools.Enabled;
		_developerModeToggle.Toggled += developerTools.SetEnabled;
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

	private void SetCrtFilterEnabled(bool enabled)
	{
		_audioSettings.SetCrtEnabled(enabled);
		UpdateCrtFilterButtons(enabled);
	}

	private void UpdateCrtFilterButtons(bool enabled)
	{
		_crtFilterOnButton.ButtonPressed = enabled;
		_crtFilterOffButton.ButtonPressed = !enabled;
		SetCrtButtonOpacity(_crtFilterOnButton, enabled);
		SetCrtButtonOpacity(_crtFilterOffButton, !enabled);
	}

	private static void SetCrtButtonOpacity(Button button, bool selected)
	{
		Color color = new(1.0f, 1.0f, 1.0f, selected ? 1.0f : 0.5f);
		button.AddThemeColorOverride("font_color", color);
		button.AddThemeColorOverride("font_hover_color", color);
		button.AddThemeColorOverride("font_pressed_color", color);
	}

	private void UpdateVolumeLabels()
	{
		_bgmValue.Text = $"{Mathf.RoundToInt((float)_bgmSlider.Value)}%";
		_sfxValue.Text = $"{Mathf.RoundToInt((float)_sfxSlider.Value)}%";
	}
}
