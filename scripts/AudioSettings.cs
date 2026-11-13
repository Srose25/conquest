#nullable enable
using Godot;

/// <summary>Persists and applies the player-facing BGM and SFX volume settings.</summary>
public partial class AudioSettings : Node
{
	public const float DefaultVolume = 0.5f;
	private const string SettingsPath = "user://settings.cfg";
	private const string SettingsSection = "audio";
	private const string BgmKey = "bgm_volume";
	private const string SfxKey = "sfx_volume";
	private const string BgmBusName = "BGM";
	private const string SfxBusName = "SFX";

	private readonly ConfigFile _settingsFile = new();
	public float BgmVolume { get; private set; } = DefaultVolume;
	public float SfxVolume { get; private set; } = DefaultVolume;

	public override void _Ready()
	{
		Load();
		CallDeferred(MethodName.ApplyVolumes);
	}

	public void SetBgmVolume(float value)
	{
		BgmVolume = Mathf.Clamp(value, 0.0f, 1.0f);
		ApplyBusVolume(BgmBusName, BgmVolume);
		Save();
	}

	public void SetSfxVolume(float value)
	{
		SfxVolume = Mathf.Clamp(value, 0.0f, 1.0f);
		ApplyBusVolume(SfxBusName, SfxVolume);
		Save();
	}

	private void Load()
	{
		if (_settingsFile.Load(SettingsPath) != Error.Ok)
			return;
		BgmVolume = Mathf.Clamp(_settingsFile.GetValue(SettingsSection, BgmKey, DefaultVolume).AsSingle(), 0.0f, 1.0f);
		SfxVolume = Mathf.Clamp(_settingsFile.GetValue(SettingsSection, SfxKey, DefaultVolume).AsSingle(), 0.0f, 1.0f);
	}

	private void Save()
	{
		_settingsFile.SetValue(SettingsSection, BgmKey, BgmVolume);
		_settingsFile.SetValue(SettingsSection, SfxKey, SfxVolume);
		_settingsFile.Save(SettingsPath);
	}

	private void ApplyVolumes()
	{
		ApplyBusVolume(BgmBusName, BgmVolume);
		ApplyBusVolume(SfxBusName, SfxVolume);
	}

	private static void ApplyBusVolume(string busName, float volume)
	{
		int busIndex = AudioServer.GetBusIndex(busName);
		if (busIndex < 0)
			return;
		AudioServer.SetBusMute(busIndex, volume <= 0.0001f);
		AudioServer.SetBusVolumeDb(busIndex, volume <= 0.0001f ? -80.0f : Mathf.LinearToDb(volume));
	}
}
