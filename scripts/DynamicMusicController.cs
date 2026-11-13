#nullable enable
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Two synchronized decks for explicitly paired exploration/combat OST tracks.
/// The pair list is Inspector-driven so matching filenames and folder names are
/// never needed.
/// </summary>
[Tool]
public partial class DynamicMusicController : Node
{
	public enum MusicState { OutOfCombat, Combat }

	[Signal] public delegate void TransitionQueuedEventHandler(int state, double cueTime);
	[Signal] public delegate void TransitionStartedEventHandler(int state);
	[Signal] public delegate void BossThemeQueuedEventHandler(double cueTime);

	[ExportCategory("Normal OST pairs")]
	[Export] public Godot.Collections.Array<DynamicMusicTrackPair> TrackPairs { get; set; } = new();
	[Export]
	public int PairCount
	{
		get => TrackPairs.Count;
		set
		{
			ResizeTrackPairs(Mathf.Max(0, value));
			NotifyPropertyListChanged();
		}
	}
	[ExportToolButton("Add Pair", Icon = "Add")]
	public Callable AddPairButton => Callable.From(AddPair);

	[ExportGroup("Automatic game states")]
	[Export(PropertyHint.Range, "0,20,0.1")] public float CombatReleaseCushionSeconds { get; set; } = 4.0f;

	[ExportGroup("Crossfade")]
	[Export(PropertyHint.Range, "0,4,0.01")] public float FadeInSeconds { get; set; } = 0.40f;
	[Export(PropertyHint.Range, "0,4,0.01")] public float FadeOutSeconds { get; set; } = 0.40f;

	[ExportGroup("Cue timing")]
	[Export(PropertyHint.Range, "30,300,0.01")] public float BeatsPerMinute { get; set; } = 135.0f;
	[Export(PropertyHint.Range, "1,16,1")] public int BeatsPerBar { get; set; } = 4;
	[Export] public float BeatOffsetSeconds { get; set; }

	private const float SilenceDb = -80.0f;
	private readonly RandomNumberGenerator _random = new();
	private readonly List<DynamicMusicTrackPair> _validPairs = new();
	private AudioStreamPlayer _playerA = null!;
	private AudioStreamPlayer _playerB = null!;
	private AudioStreamPlayer _activePlayer = null!;
	private AudioStreamPlayer _inactivePlayer = null!;
	private AudioStreamPlayer? _fadingOut;
	private AudioStreamPlayer? _fadingIn;
	private DynamicMusicTrackPair? _currentPair;
	private DynamicMusicTrackPair? _previousPair;
	private DynamicMusicTrackPair? _pendingPair;
	private AudioStream? _pendingBossTrack;
	private MusicState _currentState = MusicState.OutOfCombat;
	private MusicState? _pendingState;
	private double _pendingCueTime;
	private double _pairStartTransportSeconds;
	private double _transportSeconds;
	private float _fadeElapsed;
	private bool _isLayerMixing;
	private float _outOfCombatStartGain;
	private float _combatStartGain;
	private float _outOfCombatTargetGain;
	private float _combatTargetGain;
	private float _combatReleaseCushionRemaining;
	private bool _bossThemeActive;
	private bool _holdSilenceForDeath;

	public MusicState CurrentState => _currentState;
	public bool IsBossThemeActive => _bossThemeActive || _pendingBossTrack != null;

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
			return;
		ProcessMode = ProcessModeEnum.Always;
		AddToGroup("dynamic_music");
		bool carryingRetryMusic = TryAdoptRetryPlayers(out AudioStreamPlayer? retryPlayerA, out AudioStreamPlayer? retryPlayerB);
		_playerA = retryPlayerA ?? AdoptTitleMusicPlayer() ?? GetNode<AudioStreamPlayer>("PlayerA");
		_playerB = retryPlayerB ?? GetNode<AudioStreamPlayer>("PlayerB");
		_playerA.ProcessMode = ProcessModeEnum.Always;
		_playerB.ProcessMode = ProcessModeEnum.Always;
		_activePlayer = _playerA;
		_inactivePlayer = _playerB;
		if (!carryingRetryMusic)
			_playerB.VolumeDb = SilenceDb;
		_random.Randomize();
		ReloadTrackPairs();
		bool continuingExistingMusic = carryingRetryMusic ? ContinueRetryTrack() : ContinueTitleTrack();
		if (!continuingExistingMusic)
			StartFirstPairImmediately();
	}

	private bool TryAdoptRetryPlayers(out AudioStreamPlayer? retryPlayerA, out AudioStreamPlayer? retryPlayerB)
	{
		AudioStreamPlayer? carriedOut = GetTree().Root.GetNodeOrNull<AudioStreamPlayer>("RetryMusicOut");
		AudioStreamPlayer? carriedCombat = GetTree().Root.GetNodeOrNull<AudioStreamPlayer>("RetryMusicCombat");
		if (carriedOut == null || carriedCombat == null)
		{
			retryPlayerA = null;
			retryPlayerB = null;
			return false;
		}
		retryPlayerA = carriedOut;
		retryPlayerB = carriedCombat;

		GetNode<AudioStreamPlayer>("PlayerA").QueueFree();
		GetNode<AudioStreamPlayer>("PlayerB").QueueFree();
		retryPlayerA.Reparent(this);
		retryPlayerB.Reparent(this);
		retryPlayerA.Name = "PlayerA";
		retryPlayerB.Name = "PlayerB";
		return true;
	}

	private AudioStreamPlayer? AdoptTitleMusicPlayer()
	{
		AudioStreamPlayer? titlePlayer = GetTree().Root.GetNodeOrNull<AudioStreamPlayer>("MainMenuMusic");
		if (titlePlayer == null)
			return null;

		AudioStreamPlayer scenePlayer = GetNode<AudioStreamPlayer>("PlayerA");
		scenePlayer.Name = "RetiredPlayerA";
		scenePlayer.QueueFree();
		titlePlayer.Reparent(this);
		titlePlayer.Name = "PlayerA";
		return titlePlayer;
	}

	private bool ContinueTitleTrack()
	{
		foreach (DynamicMusicTrackPair? pair in TrackPairs)
		{
			if (pair?.AlwaysLoadFirst != true)
				continue;

			AudioStream? openingTrack = GetTrack(pair, MusicState.OutOfCombat);
			if (openingTrack == null || _playerA.Stream != openingTrack || !_playerA.Playing)
				return false;

			_currentPair = pair;
			_currentState = MusicState.OutOfCombat;
			float phase = _playerA.GetPlaybackPosition();
			_pairStartTransportSeconds = _transportSeconds - phase;
			StartCompanionTrack(pair, phase);
			// The title player may still be in its linear startup fade. Preserve its
			// current gain exactly when Play hands it to this controller instead of
			// forcing it to full volume for a frame.
			return true;
		}
		return false;
	}

	private bool ContinueRetryTrack()
	{
		foreach (DynamicMusicTrackPair? pair in TrackPairs)
		{
			if (pair == null || !SameTrack(pair.OutOfCombatTrack, _playerA.Stream) || !SameTrack(pair.CombatTrack, _playerB.Stream))
				continue;
			if (!_playerA.Playing && !_playerB.Playing)
				return false;

			_currentPair = pair;
			_currentState = GetLinearGain(_playerB) > GetLinearGain(_playerA)
				? MusicState.Combat
				: MusicState.OutOfCombat;
			float phase = _playerA.Playing ? _playerA.GetPlaybackPosition() : _playerB.GetPlaybackPosition();
			_pairStartTransportSeconds = _transportSeconds - phase;
			return true;
		}
		return false;
	}

	public override void _Process(double delta)
	{
		_transportSeconds += delta;
		UpdateCrossfade((float)delta);
		if (_holdSilenceForDeath)
			return;
		if (_pendingBossTrack != null && _transportSeconds >= _pendingCueTime)
			BeginBossTheme(_pendingBossTrack);
		if (_bossThemeActive)
		{
			// Imported boss tracks should loop. This fallback restarts a non-looping
			// stream while the boss remains active.
			if (_fadingIn == null && !_activePlayer.Playing && _activePlayer.Stream != null)
				_activePlayer.Play();
			return;
		}

		if (_pendingBossTrack == null)
			UpdateAutomaticState((float)delta);
		if (_pendingState != null && _transportSeconds >= _pendingCueTime)
			BeginStateTransition(_pendingState.Value);
		else if (_pendingPair != null && _transportSeconds >= _pendingCueTime)
			BeginPairTransition(_pendingPair, _currentState);
		else
			QueueNextPairAtTrackEnd();
	}

	/// <summary>Refreshes the complete manually assigned pairs from the Inspector.</summary>
	public void ReloadTrackPairs()
	{
		_validPairs.Clear();
		foreach (DynamicMusicTrackPair pair in TrackPairs)
		{
			if (pair.IsComplete)
				_validPairs.Add(pair);
		}
	}

	/// <summary>Queues the current pair's matching state version at the next bar.</summary>
	public void RequestState(MusicState state)
	{
		if (_bossThemeActive || _pendingBossTrack != null || (_currentState == state && _pendingState == null) || _pendingState == state)
			return;
		_pendingPair = null;
		_pendingState = state;
		_pendingCueTime = GetNextBarTime();
		EmitSignal(SignalName.TransitionQueued, (int)state, _pendingCueTime);
	}

	/// <summary>Queues a boss theme from 0:00 at the next completed bar.</summary>
	public void RequestBossTheme(AudioStream bossTrack)
	{
		if (_bossThemeActive && _activePlayer.Stream == bossTrack)
			return;
		_pendingPair = null;
		_pendingState = null;
		_pendingBossTrack = bossTrack;
		_pendingCueTime = GetNextBarTime();
		EmitSignal(SignalName.BossThemeQueued, _pendingCueTime);
	}

	/// <summary>Call when a boss is defeated or abandoned.</summary>
	public void EndBossTheme()
	{
		_pendingBossTrack = null;
		if (!_bossThemeActive)
			return;
		_bossThemeActive = false;
		QueueNextPair(MusicState.OutOfCombat);
	}

	public void OnBossDefeated() => EndBossTheme();
	public void OnBossAbandoned() => EndBossTheme();

	/// <summary>Called by the death screen before a scene reload.</summary>
	public void HandlePlayerDeath()
	{
		if (!IsBossThemeActive)
			return;

		_pendingBossTrack = null;
		_pendingState = null;
		_pendingPair = null;
		_bossThemeActive = false;
		_holdSilenceForDeath = true;
		_isLayerMixing = false;
		_fadingOut = null;
		_fadingIn = null;
		Tween fade = CreateTween().SetParallel();
		fade.TweenProperty(_playerA, "volume_db", SilenceDb, FadeOutSeconds);
		fade.TweenProperty(_playerB, "volume_db", SilenceDb, FadeOutSeconds);
	}

	/// <summary>
	/// Keeps normal exploration/combat playback alive across Retry. Boss-death
	/// music deliberately returns false so the fresh scene starts its opening pair.
	/// </summary>
	public bool PreservePlaybackForRetry()
	{
		if (IsBossThemeActive || _holdSilenceForDeath)
			return false;

		_fadingOut = null;
		_fadingIn = null;
		_isLayerMixing = false;
		_playerA.Reparent(GetTree().Root);
		_playerB.Reparent(GetTree().Root);
		_playerA.Name = "RetryMusicOut";
		_playerB.Name = "RetryMusicCombat";
		return true;
	}

	private void UpdateAutomaticState(float delta)
	{
		bool combatTriggered = false;
		foreach (Node node in GetTree().GetNodesInGroup("tracker"))
		{
			if (node is ActiveEnemyController tracker && tracker.IsChargingPlayer)
			{
				combatTriggered = true;
				break;
			}
		}

		if (!combatTriggered)
		foreach (Node node in GetTree().GetNodesInGroup("enemy_ship"))
		{
			if (node is EnemyShipController ship && ship.IsMusicCombatActive)
			{
				combatTriggered = true;
				break;
			}
		}

		_combatReleaseCushionRemaining = combatTriggered
			? CombatReleaseCushionSeconds
			: Mathf.Max(0.0f, _combatReleaseCushionRemaining - delta);
		RequestState(_combatReleaseCushionRemaining > 0.0f ? MusicState.Combat : MusicState.OutOfCombat);
	}

	private void BeginBossTheme(AudioStream track)
	{
		_pendingBossTrack = null;
		BeginTrackTransition(track, MusicState.Combat, 0.0f);
		_bossThemeActive = true;
	}

	private void BeginStateTransition(MusicState state)
	{
		_pendingState = null;
		_currentState = state;
		if (_currentPair == null)
		{
			StartNextPairImmediately(state);
			return;
		}
		// The two versions of this pair are already running from the same source
		// timestamp. State changes only alter their volume mix—nothing is started
		// late in an attempt to catch up.
		BeginLayerMix(state);
		EmitSignal(SignalName.TransitionStarted, (int)state);
	}

	private void QueueNextPairAtTrackEnd()
	{
		if (_currentPair == null || _pendingPair != null || _pendingState != null || _fadingIn != null || _isLayerMixing)
			return;

		// Both layers of a pair must run to completion. Using a predicted stream
		// length (and especially subtracting fade time) could cue the next pair
		// before the audible song had actually ended.
		bool explorationFinished = _playerA.Stream == null || !_playerA.Playing;
		bool combatFinished = _playerB.Stream == null || !_playerB.Playing;
		if (!explorationFinished || !combatFinished)
			return;
		QueueNextPair(_currentState, _transportSeconds);
	}

	private void QueueNextPair(MusicState state, double? cueTime = null)
	{
		DynamicMusicTrackPair? nextPair = ChooseNextPair();
		if (nextPair == null)
		{
			FadeToSilence(state, true);
			return;
		}
		_pendingState = null;
		_pendingPair = nextPair;
		_pendingCueTime = cueTime ?? GetNextBarTime();
		EmitSignal(SignalName.TransitionQueued, (int)state, _pendingCueTime);
	}

	private void BeginPairTransition(DynamicMusicTrackPair pair, MusicState state)
	{
		_pendingPair = null;
		_previousPair = _currentPair;
		_currentPair = pair;
		_currentState = state;
		_pairStartTransportSeconds = _transportSeconds;
		StartPairPlayback(pair, state, 0.0f, fadeIn: true);
	}

	private void StartNextPairImmediately(MusicState state)
	{
		DynamicMusicTrackPair? pair = ChooseNextPair();
		if (pair == null)
		{
			FadeToSilence(state, true);
			return;
		}
		_currentPair = pair;
		_currentState = state;
		_pairStartTransportSeconds = _transportSeconds;
		StartPairPlayback(pair, state, 0.0f, fadeIn: true);
	}

	private void StartFirstPairImmediately()
	{
		DynamicMusicTrackPair? firstPair = null;
		foreach (DynamicMusicTrackPair pair in _validPairs)
		{
			if (pair.AlwaysLoadFirst)
			{
				firstPair = pair;
				break;
			}
		}
		if (firstPair == null)
		{
			StartNextPairImmediately(MusicState.OutOfCombat);
			return;
		}
		_currentPair = firstPair;
		_currentState = MusicState.OutOfCombat;
		_pairStartTransportSeconds = _transportSeconds;
		StartPairPlayback(firstPair, _currentState, 0.0f, fadeIn: true);
	}

	private void FadeToSilence(MusicState state, bool clearCurrentPair = false)
	{
		_currentState = state;
		if (clearCurrentPair)
			_currentPair = null;
		BeginLayerMix(state, clearCurrentPair);
	}

	private void StartPairPlayback(DynamicMusicTrackPair pair, MusicState state, float phase, bool fadeIn)
	{
		// Player A permanently carries the exploration version and Player B the
		// combat version for the selected pair. Starting both here keeps their
		// decoders advancing together for the entire song.
		_isLayerMixing = false;
		_fadingOut = null;
		_fadingIn = null;
		_playerA.Stop();
		_playerB.Stop();
		_playerA.Stream = pair.OutOfCombatTrack;
		_playerB.Stream = pair.CombatTrack;
		if (_playerA.Stream != null)
			_playerA.Play(NormalizePhase(phase, _playerA.Stream));
		if (_playerB.Stream != null)
			_playerB.Play(NormalizePhase(phase, _playerB.Stream));
		_activePlayer = state == MusicState.Combat ? _playerB : _playerA;
		_inactivePlayer = state == MusicState.Combat ? _playerA : _playerB;
		if (fadeIn)
		{
			_playerA.VolumeDb = SilenceDb;
			_playerB.VolumeDb = SilenceDb;
			BeginLayerMix(state);
		}
		else
		{
			SetLayerGainsImmediately(state);
		}
	}

	private void StartCompanionTrack(DynamicMusicTrackPair pair, float phase)
	{
		_isLayerMixing = false;
		_playerB.Stop();
		_playerB.Stream = pair.CombatTrack;
		if (_playerB.Stream != null)
			_playerB.Play(NormalizePhase(phase, _playerB.Stream));
		_activePlayer = _playerA;
		_inactivePlayer = _playerB;
	}

	private void BeginLayerMix(MusicState state, bool forceSilence = false)
	{
		_fadeElapsed = 0.0f;
		_outOfCombatStartGain = GetLinearGain(_playerA);
		_combatStartGain = GetLinearGain(_playerB);
		_outOfCombatTargetGain = !forceSilence && state == MusicState.OutOfCombat && _playerA.Stream != null ? 1.0f : 0.0f;
		_combatTargetGain = !forceSilence && state == MusicState.Combat && _playerB.Stream != null ? 1.0f : 0.0f;
		_isLayerMixing = true;
		_activePlayer = state == MusicState.Combat ? _playerB : _playerA;
		_inactivePlayer = state == MusicState.Combat ? _playerA : _playerB;
	}

	private void SetLayerGainsImmediately(MusicState state)
	{
		_playerA.VolumeDb = state == MusicState.OutOfCombat && _playerA.Stream != null ? 0.0f : SilenceDb;
		_playerB.VolumeDb = state == MusicState.Combat && _playerB.Stream != null ? 0.0f : SilenceDb;
	}

	private void BeginTrackTransition(AudioStream track, MusicState state, float phase)
	{
		// Keep the outgoing deck running while the incoming deck starts at the
		// exact same musical timestamp. UpdateCrossfade drives both decks from
		// one linear-amplitude progress value until the fade has completed.
		_isLayerMixing = false;
		_fadeElapsed = 0.0f;
		_fadingOut = _activePlayer.Stream == null ? null : _activePlayer;
		_inactivePlayer.Stop();
		_inactivePlayer.Stream = track;
		_inactivePlayer.VolumeDb = SilenceDb;
		_inactivePlayer.Play(phase);
		_fadingIn = _inactivePlayer;
		_currentState = state;
		EmitSignal(SignalName.TransitionStarted, (int)state);
	}

	private void UpdateCrossfade(float delta)
	{
		if (_fadingOut == null && _fadingIn == null)
		{
			UpdateLayerMix(delta);
			return;
		}
		_fadeElapsed += delta;
		// Both decks begin on the same musical cue, but the two linear amplitude
		// ramps may have different durations. Neither deck is stopped until its
		// own ramp has reached its endpoint.
		float fadeInAmount = FadeInSeconds <= 0.0f ? 1.0f : Mathf.Clamp(_fadeElapsed / FadeInSeconds, 0.0f, 1.0f);
		float fadeOutAmount = FadeOutSeconds <= 0.0f ? 1.0f : Mathf.Clamp(_fadeElapsed / FadeOutSeconds, 0.0f, 1.0f);
		if (_fadingOut != null)
			_fadingOut.VolumeDb = LinearToDb(1.0f - fadeOutAmount);
		if (_fadingIn != null)
			_fadingIn.VolumeDb = LinearToDb(fadeInAmount);
		if (fadeInAmount < 1.0f || fadeOutAmount < 1.0f)
			return;
		AudioStreamPlayer? previous = _fadingOut;
		if (previous != null)
		{
			previous.Stop();
			previous.VolumeDb = SilenceDb;
		}
		if (_fadingIn != null)
		{
			_activePlayer = _fadingIn;
			_inactivePlayer = previous ?? (_activePlayer == _playerA ? _playerB : _playerA);
		}
		else
		{
			_activePlayer = previous == _playerA ? _playerB : _playerA;
			_activePlayer.Stop();
			_activePlayer.Stream = null;
			_activePlayer.VolumeDb = SilenceDb;
			_inactivePlayer = previous ?? (_activePlayer == _playerA ? _playerB : _playerA);
		}
		_fadingOut = null;
		_fadingIn = null;
	}

	private void UpdateLayerMix(float delta)
	{
		if (!_isLayerMixing)
			return;

		_fadeElapsed += delta;
		float outOfCombatProgress = GetMixProgress(_outOfCombatStartGain, _outOfCombatTargetGain);
		float combatProgress = GetMixProgress(_combatStartGain, _combatTargetGain);
		_playerA.VolumeDb = LinearToDb(Mathf.Lerp(_outOfCombatStartGain, _outOfCombatTargetGain, outOfCombatProgress));
		_playerB.VolumeDb = LinearToDb(Mathf.Lerp(_combatStartGain, _combatTargetGain, combatProgress));
		if (outOfCombatProgress >= 1.0f && combatProgress >= 1.0f)
			_isLayerMixing = false;
	}

	private float GetMixProgress(float startGain, float targetGain)
	{
		if (Mathf.IsEqualApprox(startGain, targetGain))
			return 1.0f;
		float duration = targetGain > startGain ? FadeInSeconds : FadeOutSeconds;
		return duration <= 0.0f ? 1.0f : Mathf.Clamp(_fadeElapsed / duration, 0.0f, 1.0f);
	}

	private DynamicMusicTrackPair? ChooseNextPair()
	{
		if (_validPairs.Count == 0)
			return null;
		if (_validPairs.Count == 1)
			return _validPairs[0];
		List<DynamicMusicTrackPair> candidates = new();
		foreach (DynamicMusicTrackPair pair in _validPairs)
		{
			if (pair != _currentPair && pair != _previousPair)
				candidates.Add(pair);
		}
		// One or two configured pairs cannot satisfy a two-pair exclusion. Fall
		// back to excluding only the current song so a two-pair setup alternates.
		if (candidates.Count == 0)
		{
			foreach (DynamicMusicTrackPair pair in _validPairs)
			{
				if (pair != _currentPair)
					candidates.Add(pair);
			}
		}
		return candidates[_random.RandiRange(0, candidates.Count - 1)];
	}

	private float GetSynchronizedPhase(AudioStream track)
	{
		float length = (float)track.GetLength();
		if (length <= 0.0f)
			return 0.0f;

		// A pair is rendered from the same source zero. The controller's monotonic
		// transport is therefore the single authority for both tracks' timestamp.
		float phase = (float)Math.Max(0.0, _transportSeconds - _pairStartTransportSeconds);
		return Mathf.PosMod(phase, length);
	}

	private double GetNextBarTime()
	{
		double beatsPerSecond = Mathf.Max(1.0f, BeatsPerMinute) / 60.0;
		int beatsPerBar = Mathf.Max(1, BeatsPerBar);
		// The transport is anchored to the common 0:00 of the current pair, so
		// bar cues and the incoming track use the exact same clock.
		double musicalTime = Math.Max(0.0, _transportSeconds - _pairStartTransportSeconds);
		double beatPosition = (musicalTime - BeatOffsetSeconds) * beatsPerSecond;
		double beatInBar = PositiveModulo(beatPosition, beatsPerBar);
		double beatsRemaining = beatsPerBar - beatInBar;
		if (beatsRemaining <= 0.0001)
			beatsRemaining = beatsPerBar;
		return _transportSeconds + beatsRemaining / beatsPerSecond;
	}

	private static double PositiveModulo(double value, double divisor)
	{
		double remainder = value % divisor;
		return remainder < 0.0 ? remainder + divisor : remainder;
	}

	private void AddPair()
	{
		TrackPairs.Add(new DynamicMusicTrackPair());
		NotifyPropertyListChanged();
	}

	private void ResizeTrackPairs(int count)
	{
		while (TrackPairs.Count < count)
			TrackPairs.Add(new DynamicMusicTrackPair());
		while (TrackPairs.Count > count)
			TrackPairs.RemoveAt(TrackPairs.Count - 1);
	}

	private static AudioStream? GetTrack(DynamicMusicTrackPair pair, MusicState state) => state == MusicState.Combat ? pair.CombatTrack : pair.OutOfCombatTrack;
	private static bool SameTrack(AudioStream? first, AudioStream? second) => first == second
		|| (first != null && second != null && !string.IsNullOrEmpty(first.ResourcePath) && first.ResourcePath == second.ResourcePath);
	private static float NormalizePhase(float phase, AudioStream stream)
	{
		float length = (float)stream.GetLength();
		return length > 0.0f ? Mathf.PosMod(phase, length) : 0.0f;
	}
	private static float GetLinearGain(AudioStreamPlayer player) => player.VolumeDb <= SilenceDb + 0.001f ? 0.0f : Mathf.DbToLinear(player.VolumeDb);
	private static float LinearToDb(float amount) => amount <= 0.0001f ? SilenceDb : Mathf.LinearToDb(amount);
}
