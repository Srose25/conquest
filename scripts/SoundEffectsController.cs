#nullable enable
using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Plays short gameplay effects as independent SFX-bus voices. Every request
/// creates its own AudioStreamPlayer, so rapid gunfire and simultaneous impact
/// sounds overlap instead of cutting off an earlier one-shot.
/// </summary>
public partial class SoundEffectsController : Node
{
	public enum Effect
	{
		MainGun,
		AsteroidHit,
		ShipHit,
		AsteroidExplode,
		ShipExplode,
		XpCollect,
		RepairPelletCollect,
		RepairPelletCollectAndRepair,
		ShieldRecharge,
		ShieldAlarm,
		HealthAlarm,
	}

	private static readonly IReadOnlyDictionary<Effect, string> SoundPaths = new Dictionary<Effect, string>
	{
		[Effect.MainGun] = "res://SFX/MainGun.wav",
		[Effect.AsteroidHit] = "res://SFX/AsteroidHit.wav",
		[Effect.ShipHit] = "res://SFX/ShipHit.wav",
		[Effect.AsteroidExplode] = "res://SFX/AsteroidExplode.wav",
		[Effect.ShipExplode] = "res://SFX/ShipExplode.wav",
		[Effect.XpCollect] = "res://SFX/XPCollect.wav",
		[Effect.RepairPelletCollect] = "res://SFX/RPPCollect.wav",
		[Effect.RepairPelletCollectAndRepair] = "res://SFX/RPPCollect+Repair.wav",
		[Effect.ShieldRecharge] = "res://SFX/ShieldRecharge.wav",
		[Effect.ShieldAlarm] = "res://SFX/ShieldAlarm.wav",
		[Effect.HealthAlarm] = "res://SFX/HealthAlarm.wav",
	};

	private readonly Dictionary<Effect, AudioStream> _sounds = new();
	private readonly RandomNumberGenerator _random = new();
	private AudioStreamPlayer? _shieldRechargeVoice;
	private AudioStreamPlayer? _shieldAlarmVoice;
	private Timer? _shieldAlarmLoopTimer;
	private bool _shieldAlarmActive;
	private float _shieldAlarmFadeElapsed = -1.0f;
	private float _shieldAlarmFadeStartGain = 1.0f;
	private const float ShieldAlarmFadeOutSeconds = 1.0f;
	private const float ShieldContinuousSoundGainDb = 8.0f;

	public override void _Ready()
	{
		_random.Randomize();
		foreach ((Effect effect, string path) in SoundPaths)
		{
			AudioStream? stream = GD.Load<AudioStream>(path);
			if (stream != null)
				_sounds[effect] = stream;
			else
				GD.PrintErr($"Missing sound effect: {path}");
		}
		InitializeShieldAlarmLoop();
	}

	public override void _Process(double delta)
	{
		if (_shieldAlarmVoice == null || _shieldAlarmFadeElapsed < 0.0f)
			return;

		_shieldAlarmFadeElapsed += (float)delta;
		float progress = Mathf.Clamp(_shieldAlarmFadeElapsed / ShieldAlarmFadeOutSeconds, 0.0f, 1.0f);
		SetVoiceLinearGain(_shieldAlarmVoice, Mathf.Lerp(_shieldAlarmFadeStartGain, 0.0f, progress));
		if (progress >= 1.0f)
			StopShieldAlarmVoice();
	}

	public static void PlayOneShot(Node source, Effect effect, float pitchCents = 0.0f, float gainDb = 0.0f)
	{
		SoundEffectsController? controller = source.GetTree().Root.GetNodeOrNull<SoundEffectsController>("SoundEffects");
		controller?.Play(effect, pitchCents, gainDb);
	}

	public static void PlayOneShotWithRandomPitch(Node source, Effect effect, float minimumCents, float maximumCents)
	{
		SoundEffectsController? controller = source.GetTree().Root.GetNodeOrNull<SoundEffectsController>("SoundEffects");
		if (controller == null)
			return;
		float minimum = Mathf.Min(minimumCents, maximumCents);
		float maximum = Mathf.Max(minimumCents, maximumCents);
		controller.Play(effect, controller._random.RandfRange(minimum, maximum));
	}

	/// <summary>
	/// Starts the player-shield recharge sound at its authored playback rate.
	/// PlayerController explicitly cuts it off if regeneration is interrupted or
	/// completes before this one-shot reaches its natural end.
	/// </summary>
	public static void StartShieldRecharge(Node source)
	{
		SoundEffectsController? controller = source.GetTree().Root.GetNodeOrNull<SoundEffectsController>("SoundEffects");
		controller?.CrossfadeShieldAlarmToRecharge();
	}

	public static void StopShieldRecharge(Node source)
	{
		SoundEffectsController? controller = source.GetTree().Root.GetNodeOrNull<SoundEffectsController>("SoundEffects");
		controller?.StopShieldRechargeVoice();
	}

	public static void StartShieldAlarm(Node source)
	{
		SoundEffectsController? controller = source.GetTree().Root.GetNodeOrNull<SoundEffectsController>("SoundEffects");
		controller?.PlayShieldAlarm();
	}

	public static void FadeOutShieldAlarm(Node source)
	{
		SoundEffectsController? controller = source.GetTree().Root.GetNodeOrNull<SoundEffectsController>("SoundEffects");
		controller?.FadeShieldAlarmOut();
	}

	public void Play(Effect effect, float pitchCents = 0.0f, float gainDb = 0.0f)
	{
		AudioStream? stream = GetSound(effect);
		if (stream == null)
			return;

		AudioStreamPlayer voice = new()
		{
			Stream = stream,
			Bus = new StringName("SFX"),
			PitchScale = Mathf.Pow(2.0f, pitchCents / 1200.0f),
			VolumeDb = gainDb,
		};
		GetTree().Root.AddChild(voice);
		voice.Finished += voice.QueueFree;
		voice.Play();
	}

	private void PlayShieldRecharge()
	{
		if (_shieldRechargeVoice != null)
			return;
		AudioStream? stream = GetSound(Effect.ShieldRecharge);
		if (stream == null)
			return;

		AudioStreamPlayer voice = new()
		{
			Stream = stream,
			Bus = new StringName("SFX"),
			VolumeDb = ShieldContinuousSoundGainDb,
		};
		_shieldRechargeVoice = voice;
		GetTree().Root.AddChild(voice);
		voice.Finished += () =>
		{
			if (_shieldRechargeVoice == voice)
				_shieldRechargeVoice = null;
			voice.QueueFree();
		};
		voice.Play();
	}

	private void CrossfadeShieldAlarmToRecharge()
	{
		// Both operations begin in this same frame: the recharge voice starts
		// immediately while the alarm begins its one-second linear fade.
		PlayShieldRecharge();
		FadeShieldAlarmOut();
	}

	private void StopShieldRechargeVoice()
	{
		if (_shieldRechargeVoice == null)
			return;
		AudioStreamPlayer voice = _shieldRechargeVoice;
		_shieldRechargeVoice = null;
		voice.Stop();
		voice.QueueFree();
	}

	private void PlayShieldAlarm()
	{
		if (_shieldAlarmActive)
			return;
		InitializeShieldAlarmLoop();
		_shieldAlarmActive = true;
		PlayShieldAlarmPass();
		_shieldAlarmLoopTimer?.Start();
	}

	private void FadeShieldAlarmOut()
	{
		if (!_shieldAlarmActive || _shieldAlarmFadeElapsed >= 0.0f)
			return;
		_shieldAlarmActive = false;
		_shieldAlarmLoopTimer?.Stop();
		if (_shieldAlarmVoice == null || !_shieldAlarmVoice.Playing)
			return;
		_shieldAlarmFadeStartGain = _shieldAlarmVoice.VolumeDb <= -79.0f
			? 0.0f
			: Mathf.DbToLinear(_shieldAlarmVoice.VolumeDb);
		_shieldAlarmFadeElapsed = 0.0f;
	}

	private void StopShieldAlarmVoice()
	{
		_shieldAlarmActive = false;
		_shieldAlarmLoopTimer?.Stop();
		_shieldAlarmFadeElapsed = -1.0f;
		if (_shieldAlarmVoice == null)
			return;
		AudioStreamPlayer voice = _shieldAlarmVoice;
		_shieldAlarmVoice = null;
		voice.Stop();
		voice.QueueFree();
	}

	private void InitializeShieldAlarmLoop()
	{
		if (_shieldAlarmLoopTimer != null)
			return;
		AudioStream? stream = GetSound(Effect.ShieldAlarm);
		if (stream == null)
			return;
		_shieldAlarmLoopTimer = new Timer
		{
			WaitTime = Mathf.Max(0.1f, stream.GetLength()),
			OneShot = false,
			ProcessMode = ProcessModeEnum.Always,
		};
		_shieldAlarmLoopTimer.Timeout += PlayShieldAlarmPass;
		AddChild(_shieldAlarmLoopTimer);
	}

	private void PlayShieldAlarmPass()
	{
		if (!_shieldAlarmActive)
			return;
		AudioStream? source = GetSound(Effect.ShieldAlarm);
		if (source is not AudioStreamWav wav)
			return;
		// Do not use the WAV's importer loop for this effect. A fresh ordinary
		// one-shot voice is the same reliable path used by the other SFX.
		AudioStreamWav stream = (AudioStreamWav)wav.Duplicate();
		stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
		AudioStreamPlayer voice = new()
		{
			Stream = stream,
			Bus = new StringName("SFX"),
			VolumeDb = ShieldContinuousSoundGainDb,
		};
		if (_shieldAlarmVoice != null)
		{
			_shieldAlarmVoice.Stop();
			_shieldAlarmVoice.QueueFree();
		}
		_shieldAlarmVoice = voice;
		_shieldAlarmFadeElapsed = -1.0f;
		GetTree().Root.AddChild(voice);
		voice.Finished += () =>
		{
			if (_shieldAlarmVoice == voice)
				_shieldAlarmVoice = null;
			voice.QueueFree();
		};
		voice.Play();
	}

	private static void SetVoiceLinearGain(AudioStreamPlayer voice, float gain)
	{
		voice.VolumeDb = gain <= 0.0001f ? -80.0f : Mathf.LinearToDb(gain);
	}

	private AudioStream? GetSound(Effect effect)
	{
		if (_sounds.TryGetValue(effect, out AudioStream? cached))
			return cached;
		if (!SoundPaths.TryGetValue(effect, out string? path))
			return null;

		AudioStream? loaded = GD.Load<AudioStream>(path);
		if (loaded != null)
			_sounds[effect] = loaded;
		else
			GD.PrintErr($"Missing sound effect: {path}");
		return loaded;
	}
}
