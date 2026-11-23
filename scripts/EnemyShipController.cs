#nullable enable
using System.Collections.Generic;
using Godot;

/// <summary>Enemy ship that uses player-like inertia, map collisions, and alternating gun volleys.</summary>
public partial class EnemyShipController : CharacterBody2D, IDamageable
{
	[Export] public float MaxHealth { get; set; } = 20.0f;
	[Export] public float MaxShield { get; set; } = 10.0f;
	[Export] public float ShieldRegenDelaySeconds { get; set; } = 5.0f;
	[Export] public float ShieldRegenDurationSeconds { get; set; } = 5.0f;
	[Export] public float MoveSpeed { get; set; } = 200.0f;
	[Export(PropertyHint.Range, "0.1,1,0.01")] public float PlayerSpeedCapFraction { get; set; } = 0.90f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float InitialLockDelayMinimumSeconds { get; set; } = 3.0f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float InitialLockDelayMaximumSeconds { get; set; } = 5.0f;
	// These are full field-of-view widths, not degrees measured out from center.
	[Export(PropertyHint.Range, "0,200,1")] public float CentralVisionConeDegrees { get; set; } = 90.0f;
	[Export(PropertyHint.Range, "0,90,1")] public float DirectLockConeDegrees { get; set; } = 40.0f;
	[Export(PropertyHint.Range, "1,10,0.1")] public float CentralVisionLockSpeedMultiplier { get; set; } = 3.5f;
	[Export(PropertyHint.Range, "1,30,0.5")] public float MaximumDirectLockSpeedMultiplier { get; set; } = 18.0f;
	[Export(PropertyHint.Range, "0,60,0.1")] public float LostSightSecondsToBreakLock { get; set; } = 10.0f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float FleeBreakLockMinimumSeconds { get; set; } = 5.0f;
	[Export(PropertyHint.Range, "0,20,0.1")] public float FleeBreakLockMaximumSeconds { get; set; } = 10.0f;
	[Export] public float DesiredEngagementRange { get; set; } = 300.0f;
	// A combat ship may close from afar, but it should never select a head-on
	// approach that turns the encounter into a nose-to-nose collision.
	[Export] public float MinimumPlayerStandoffDistance { get; set; } = 190.0f;
	[Export(PropertyHint.Range, "1,5,0.1")] public float MaximumEvasiveBoostSeconds { get; set; } = 5.0f;
	[Export(PropertyHint.Range, "1,1.5,0.01")] public float EvasiveBoostMultiplier { get; set; } = 1.20f;
	[Export] public float CoastStopSeconds { get; set; } = 10.0f;
	[Export] public float DirectionChangeSeconds { get; set; } = 0.9f;
	[Export] public float AccelerationSeconds { get; set; } = 1.5f;
	[Export] public float BoltSpeed { get; set; } = 470.0f;
	[Export] public float BoltDamage { get; set; } = 1.0f;
	[Export] public float MaximumLeadSeconds { get; set; } = 1.25f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float LeadVelocityFactor { get; set; } = 1.0f;
	[Export(PropertyHint.Range, "0,25,0.1")] public float AimErrorMinimumDegrees { get; set; } = 0.15f;
	[Export(PropertyHint.Range, "0,25,0.1")] public float AimErrorMaximumDegrees { get; set; } = 3.5f;
	[Export(PropertyHint.Range, "0,1,0.01")] public float BurstAimTrackingSmoothing { get; set; } = 0.75f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float ObstructionClearBurstChance { get; set; } = 0.30f;
	[Export] public float ObstructionProbeDistanceMinimum { get; set; } = 180.0f;
	[Export] public float ObstructionProbeDistanceMaximum { get; set; } = 290.0f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float RetreatHealthFraction { get; set; } = 0.50f;
	[Export] public int SustainedHitsToDisengage { get; set; } = 4;
	[Export(PropertyHint.Range, "0,1,0.05")] public float RepairPelletDropChance { get; set; } = 0.35f;
	[Export] public int RepairPelletMin { get; set; } = 1;
	[Export] public int RepairPelletMax { get; set; } = 2;
	[Export] public int XpDropMin { get; set; } = 9;
	[Export] public int XpDropMax { get; set; } = 15;
	[Export] public float LinkedAllyAlertRange { get; set; } = 520.0f;
	[Export] public float NearbyAllyAlertRange { get; set; } = 600.0f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float NearbyAllyAlertChance { get; set; } = 0.30f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float AllyAssistChance { get; set; } = 0.45f;
	[Export] public PackedScene? EnemyBoltScene { get; set; }
	// Enemy Sprite 1 is 32x34 and rendered at 1.5x. The red gun pixels are
	// centered at source positions (7.5, 4.5) and (23.5, 4.5), which resolves
	// to these exact local ship coordinates from its centered sprite origin.
	[Export] public Vector2 LeftGunOffset { get; set; } = new(-12.0f, -18.0f);
	[Export] public Vector2 RightGunOffset { get; set; } = new(12.0f, -18.0f);
	// Center of the purple windshield: source pixel (15.5, 7.0) in the 32x34
	// sprite, converted through its 1.5x Sprite2D scale and centered origin.
	[Export] public Vector2 WindshieldRayOriginOffset { get; set; } = new(0.0f, -14.25f);
	[Export] public float TurnSpeedDegrees { get; set; } = 180.0f;
	[Export] public float FiringConeDegrees { get; set; } = 28.0f;

	private float _health;
	private float _shield;
	private float _secondsSinceShieldHit;
	private Node2D? _player;
	private VisibleOnScreenNotifier2D _screenNotifier = null!;
	private readonly RandomNumberGenerator _random = new();
	private bool _withinPhysicsWindow = true;
	private bool _hasBeenRevealed;
	private bool _hasTargetLock;
	private float _initialLockSightSeconds;
	private float _requiredInitialLockSightSeconds;
	private Vector2 _lastInvestigationPosition;
	private float _nearMissInvestigationSeconds;
	private ulong _lastNoticedPlayerBulletId;
	private float _sightQueryCacheSeconds;
	private Vector2 _cachedSightTarget;
	private int _cachedClearSightRays;
	private int _cachedTestedSightRays;
	private bool _cachedCenterSightClear;
	private float _lostSightSeconds;
	private float _playerFleeSeconds;
	private float _requiredPlayerFleeSeconds;
	private float _playerHitCombatCueSeconds;
	private bool _destroyed;
	private bool _fireLeftGun = true;
	private int _burstShotsRemaining;
	private float _nextShotSeconds;
	private float _volleyCooldownSeconds;
	private bool _hasBurstAimPoint;
	private Vector2 _burstAimPoint;
	private float _burstAimOffsetRadians;
	private float _maneuverSeconds;
	private float _desiredRange;
	private int _orbitDirection;
	private bool _isCharging;
	private bool _isDodging;
	private bool _hasMadePostDeathDecision;
	private bool _isRetreatingAfterPlayerDeath;
	private float _postDeathDecisionSeconds;
	private Vector2 _playerDeathPosition;
	private Vector2 _retreatDirection;
	private BreakableEntityController? _patrolAnchor;
	private Vector2 _patrolCenter;
	private float _patrolRadius;
	private float _patrolRetargetSeconds;
	private float _ambushResponseSeconds;
	private float _evasiveBoostSeconds;
	private float _evasiveBoostElapsed;
	private const float EvasiveBoostRampSeconds = 2.5f;
	private bool _ambushStrafe;
	private int _ambushStrafeDirection;
	private EnemyShipController? _linkedAlly;
	private int _recentPlayerHits;
	private float _recentHitWindowSeconds;
	private float _tacticalDisengageSeconds;
	private bool _tacticalEscape;
	private int _tacticalDirection;
	private Vector2 _tacticalFlankDirection;
	private float _tacticalFlankRange;
	private bool _tacticalSeekingCover;
	private Vector2 _coverPosition;
	private float _asteroidAvoidanceSeconds;
	private bool _avoidAsteroidByTurning;
	private int _asteroidAvoidanceDirection;
	private bool _pathAvoidanceActive;
	// Retains a chosen side around a nearby obstacle long enough to steer past it
	// instead of making a different turn decision every physics frame.
	private BreakableEntityController? _rememberedObstacle;
	private float _rememberedObstacleSeconds;
	private readonly List<BreakableEntityController> _nearbyAsteroidCache = new();
	private float _nearbyAsteroidCacheSeconds;
	private readonly CircleShape2D _nearbyAsteroidQueryShape = new();
	private readonly PhysicsShapeQueryParameters2D _nearbyAsteroidQuery = new();
	private float _collisionRecoverySeconds;
	private Vector2 _collisionRecoveryDirection;
	private Vector2 _plannedFlightDirection;
	private float _flightPlanSeconds;
	private BreakableEntityController? _obstructionDecisionAsteroid;
	private bool _clearObstructionByShooting;
	private BreakableEntityController? _obstructionBurstTarget;
	private float _playerCollisionDamageCooldown;
	private ShaderMaterial? _hitFlashMaterial;
	private float _hitFlashSeconds;
#if DEBUG
	private readonly List<DebugRaySample> _debugRaySamples = new();
	private float _debugRaySampleRefreshSeconds;
	private const float DebugRaySampleIntervalSeconds = 0.10f;

	private readonly struct DebugRaySample
	{
		public readonly Vector2 From;
		public readonly Vector2 Endpoint;
		public readonly Vector2 HitPoint;
		public readonly bool Blocked;

		public DebugRaySample(Vector2 from, Vector2 endpoint, Vector2 hitPoint, bool blocked)
		{
			From = from;
			Endpoint = endpoint;
			HitPoint = hitPoint;
			Blocked = blocked;
		}
	}
#endif
	private const float HitFlashDuration = 0.12f;
	private const float EnemyHealthBarRange = 250.0f;
	private const float EnemyHealthBarFadeSeconds = 0.18f;
	private const float EnemyHealthBarDamageAnimationSeconds = 0.25f;
	private static readonly float[] SightRayOffsets = { 0.0f, -4.0f, 4.0f, -8.0f, 8.0f, -12.0f, 12.0f, -16.0f, 16.0f, -20.0f, 20.0f };
	private float _healthBarVisibility;
	private int _lastHealthBarSegments = -1;
	private int _healthBarDamageStartSegment;
	private int _healthBarDamageSegmentCount;
	private float _healthBarDamageAnimationElapsed = -1.0f;
	// Only a small wing may press the attack at once. Other alerted ships keep
	// their lock but orbit as reserves, avoiding an unavoidable firing wall.
	private static int _nextEngagementPriority;
	private int _engagementPriority;
	private const int MaxConcurrentAttackers = 2;

	public bool HasTargetLock => _hasTargetLock;
	// A confirmed player bolt hit is also a combat cue, even during the one
	// physics frame before the ship finishes acquiring its lock.
	public bool IsMusicCombatActive => (_hasTargetLock || _playerHitCombatCueSeconds > 0.0f)
		&& _player is PlayerController { IsDead: false };

	public void LinkWith(EnemyShipController ally)
	{
		if (ally != this)
			_linkedAlly = ally;
	}

	public override void _Ready()
	{
		CollisionLayer = 1 << 2;
		CollisionMask = (1 << 0) | (1 << 1) | (1 << 2);
		_health = Mathf.Max(1.0f, MaxHealth);
		_shield = Mathf.Max(0.0f, MaxShield);
		_random.Randomize();
		RollInitialLockDelay();
		RollFleeBreakLockDelay();
		AddToGroup("enemy_ship");
		_player = GetTree().GetFirstNodeInGroup("player") as Node2D;
		_screenNotifier = GetNode<VisibleOnScreenNotifier2D>("VisibleOnScreenNotifier2D");
		Sprite2D sprite = GetNode<Sprite2D>("Sprite2D");
		if (sprite.Material is ShaderMaterial material)
		{
			_hitFlashMaterial = (ShaderMaterial)material.Duplicate();
			sprite.Material = _hitFlashMaterial;
			_hitFlashMaterial.SetShaderParameter("flash_amount", 0.0f);
		}
		_screenNotifier.ScreenEntered += OnScreenEntered;
		_screenNotifier.ScreenExited += OnScreenExited;
		EnemyBoltScene ??= GD.Load<PackedScene>("res://scenes/combat/EnemyBolt.tscn");
		RefreshManeuver();
		_patrolRetargetSeconds = 0.0f;
		_volleyCooldownSeconds = 1.0f;
		SetCollisionEnabled(true);
	}

	public override void _Process(double delta)
	{
		float step = (float)delta;
		// Keep off-window ships coasting without running expensive AI or collision
		// queries. They retain the same ten-second low-friction deceleration used
		// by the player instead of freezing in place outside the physics window.
		if (!_withinPhysicsWindow && !_destroyed)
			CoastOutsidePhysicsWindow(step);
		_playerHitCombatCueSeconds = Mathf.Max(0.0f, _playerHitCombatCueSeconds - step);
		_player ??= GetTree().GetFirstNodeInGroup("player") as Node2D;
		bool showHealthBar = _player is PlayerController player
			&& !player.IsDead
			&& GlobalPosition.DistanceTo(player.GlobalPosition) <= EnemyHealthBarRange;
		float visibilitySpeed = step / EnemyHealthBarFadeSeconds;
		_healthBarVisibility = Mathf.MoveToward(_healthBarVisibility, showHealthBar ? 1.0f : 0.0f, visibilitySpeed);
		UpdateHealthBarDamageAnimation(step);
		if (_healthBarVisibility > 0.0f || _healthBarDamageAnimationElapsed >= 0.0f)
			QueueRedraw();
#if DEBUG
		if (GetNodeOrNull<DeveloperModeController>("/root/DeveloperTools")?.EnemyRaycastVisualizationEnabled == true)
			RefreshDebugRaySamples(step);
#endif

		if (_hitFlashSeconds > 0.0f && _hitFlashMaterial != null)
		{
			_hitFlashSeconds = Mathf.Max(0.0f, _hitFlashSeconds - step);
			_hitFlashMaterial.SetShaderParameter("flash_amount", _hitFlashSeconds / HitFlashDuration);
		}
	}

	private void CoastOutsidePhysicsWindow(float delta)
	{
		if (Velocity.LengthSquared() <= Mathf.Epsilon)
			return;

		float speedCap = GetMovementSpeedCap();
		float coastDeceleration = speedCap / Mathf.Max(0.1f, CoastStopSeconds);
		Velocity = Velocity.MoveToward(Vector2.Zero, coastDeceleration * delta);
		GlobalPosition += Velocity * delta;
	}

	public override void _Draw()
	{
#if DEBUG
		DrawDebugRaycasts();
#endif
		if (_healthBarVisibility <= 0.0f)
			return;

		// Keep the bar beneath the ship in world-space down, rather than rotating
		// it with the hull as the ship turns.
		Vector2 belowShip = Vector2.Down.Rotated(-Rotation) * 30.0f;
		DrawSetTransform(belowShip, -Rotation);
		// Twenty one-pixel cells plus their one-pixel dividers need at least
		// 42 native pixels. Keeping the compact enemy bar on that grid prevents
		// the CRT pass from erasing thin segments.
		const float width = 42.0f;
		const float height = 6.0f;
		const float border = 1.0f;
		int segmentCount = Mathf.Max(1, Mathf.CeilToInt(MaxHealth));
		int filledSegments = Mathf.Clamp(Mathf.CeilToInt(_health), 0, segmentCount);
		const float divider = 1.0f;
		float segmentWidth = Mathf.Max(1.0f, Mathf.Floor((width - border * 2.0f - divider * (segmentCount - 1)) / segmentCount));
		Rect2 barRect = new(-width * 0.5f, -height * 0.5f, width, height);
		DrawRect(barRect, new Color(0.10f, 0.01f, 0.02f, _healthBarVisibility * 0.90f));
		DrawLine(barRect.Position, barRect.Position + Vector2.Right * width, new Color(1.0f, 0.55f, 0.60f, _healthBarVisibility), 1.0f, true);
		DrawLine(barRect.Position + Vector2.Down * height, barRect.End, new Color(0.35f, 0.03f, 0.06f, _healthBarVisibility), 1.0f, true);

		for (int segment = 0; segment < filledSegments; segment++)
		{
			float left = barRect.Position.X + border + segment * (segmentWidth + divider);
			DrawEnemyHealthSegment(new Rect2(left, barRect.Position.Y + border, segmentWidth, height - border * 2.0f), Colors.White, 0.0f);
		}
		DrawHealthBarDamageTransition(barRect, segmentWidth, divider);
		DrawShieldBar(barRect);
		DrawSetTransform(Vector2.Zero);
	}

#if DEBUG
	private void DrawDebugRaycasts()
	{
		if (GetNodeOrNull<DeveloperModeController>("/root/DeveloperTools")?.EnemyRaycastVisualizationEnabled != true)
			return;
		DrawDebugVisionFan();
		Color lockingColor = GetDebugLockProgressColor();
		foreach (DebugRaySample sample in _debugRaySamples)
		{
			Vector2 endpoint = sample.Blocked ? sample.HitPoint : sample.Endpoint;
			Color rayColor = sample.Blocked ? new Color(1.0f, 0.18f, 0.22f, 0.50f) : lockingColor;
			DrawFadedDebugRay(sample.From, endpoint, rayColor);
			if (sample.Blocked)
			{
				Color dotColor = new(1.0f, 0.12f, 0.16f, 0.50f * GetVisionStrength(sample.From, sample.HitPoint));
				DrawCircle(ToLocal(sample.HitPoint), 2.0f, dotColor);
			}
		}
	}

	private void DrawDebugVisionFan()
	{
		// A former camera-sized shader quad evaluated trigonometry over most of the
		// screen for every enemy. These thin canvas lines describe the same 50-ray
		// FOV at a tiny fraction of the GPU fill cost.
		const int rayCount = 50;
		const int gradientSegments = 6;
		Vector2 from = GetWindshieldRayOrigin();
		float radius = GetFieldOfViewRadius();
		Vector2 forward = -GlobalTransform.Y;
		for (int ray = 0; ray < rayCount; ray++)
		{
			float fraction = ray / (float)(rayCount - 1);
			float angle = Mathf.DegToRad(Mathf.Lerp(-100.0f, 100.0f, fraction));
			Vector2 direction = forward.Rotated(angle);
			for (int segment = 0; segment < gradientSegments; segment++)
			{
				float startFraction = segment / (float)gradientSegments;
				float endFraction = (segment + 1) / (float)gradientSegments;
				Vector2 start = from + direction * radius * startFraction;
				Vector2 end = from + direction * radius * endFraction;
				float alpha = 0.50f * GetVisionStrength(from, (start + end) * 0.5f);
				if (alpha > 0.001f)
					DrawLine(ToLocal(start), ToLocal(end), new Color(1.0f, 1.0f, 1.0f, alpha), 1.0f, true);
			}
		}
	}

	private void RefreshDebugRaySamples(float delta)
	{
		_debugRaySampleRefreshSeconds -= delta;
		if (_debugRaySampleRefreshSeconds > 0.0f)
			return;
		_debugRaySampleRefreshSeconds = DebugRaySampleIntervalSeconds;
		_debugRaySamples.Clear();

		if (_player is not PlayerController player || player.IsDead)
			return;
		Vector2 from = GetWindshieldRayOrigin();
		Vector2 to = player.GlobalPosition;
		if (!IsWithinFieldOfView(from, to))
			return;
		Vector2 playerDirection = from.DirectionTo(to);
		if (playerDirection.LengthSquared() <= Mathf.Epsilon)
			return;

		Vector2 lateral = new(-playerDirection.Y, playerDirection.X);
		foreach (float offset in SightRayOffsets)
		{
			Vector2 endpoint = to + lateral * offset;
			if (!IsWithinFieldOfView(from, endpoint))
				continue;
			PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(from, endpoint, (uint)(1 << 1));
			Godot.Collections.Dictionary hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
			Variant hitPosition = default;
			bool blocked = hit.Count > 0 && hit.TryGetValue("position", out hitPosition);
			_debugRaySamples.Add(new DebugRaySample(from, endpoint, blocked ? hitPosition.AsVector2() : Vector2.Zero, blocked));
		}
		QueueRedraw();
	}

	private Color GetDebugLockProgressColor()
	{
		Color visibleGreen = new(0.15f, 1.0f, 0.42f, 0.50f);
		Color lockedGold = new(1.0f, 0.74f, 0.14f, 0.50f);
		float lockProgress = _hasTargetLock
			? 1.0f
			: Mathf.Clamp(_initialLockSightSeconds / Mathf.Max(0.001f, _requiredInitialLockSightSeconds), 0.0f, 1.0f);
		// Central vision intentionally fills the real lock meter faster than the
		// periphery. A gentle visual gamma keeps the green-to-gold transition
		// visible from the first moment that meter starts accumulating rather than
		// appearing to change only during its final fast increment. This changes
		// only the debug visualization; lock timing remains exactly the same.
		float colorProgress = Mathf.Pow(lockProgress, 0.45f);
		return visibleGreen.Lerp(lockedGold, colorProgress);
	}

	private void DrawDebugRay(Vector2 from, Vector2 to, Color clearColor)
	{
		PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(from, to, (uint)(1 << 1));
		Godot.Collections.Dictionary hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
		Vector2 hitPoint = Vector2.Zero;
		bool blocked = false;
		if (hit.Count > 0 && hit.TryGetValue("position", out Variant hitPosition))
		{
			blocked = true;
			hitPoint = hitPosition.AsVector2();
		}
		Vector2 drawEndpoint = blocked ? hitPoint : to;
		Color rayColor = blocked ? new Color(1.0f, 0.18f, 0.22f, 0.50f) : clearColor;
		DrawFadedDebugRay(from, drawEndpoint, rayColor);
		if (blocked)
		{
			Color dotColor = new(1.0f, 0.12f, 0.16f, 0.50f * GetVisionStrength(from, hitPoint));
			DrawCircle(ToLocal(hitPoint), 2.0f, dotColor);
		}
	}

	private void DrawFadedDebugRay(Vector2 from, Vector2 to, Color color)
	{
		const int segmentCount = 16;
		for (int segment = 0; segment < segmentCount; segment++)
		{
			float startProgress = segment / (float)segmentCount;
			float endProgress = (segment + 1) / (float)segmentCount;
			Vector2 segmentStart = from.Lerp(to, startProgress);
			Vector2 segmentEnd = from.Lerp(to, endProgress);
			Color segmentColor = color;
			segmentColor.A *= GetVisionStrength(from, (segmentStart + segmentEnd) * 0.5f);
			if (segmentColor.A > 0.001f)
				DrawLine(ToLocal(segmentStart), ToLocal(segmentEnd), segmentColor, 1.0f, true);
		}
	}
#endif

	private void DrawShieldBar(Rect2 healthBarRect)
	{
		if (MaxShield <= 0.0f)
			return;

		const float shieldHeight = 3.0f;
		Rect2 shieldRect = new(
			healthBarRect.Position.X,
			healthBarRect.End.Y + 1.5f,
			healthBarRect.Size.X,
			shieldHeight);
		DrawRect(shieldRect, new Color(0.01f, 0.06f, 0.13f, _healthBarVisibility * 0.85f));
		float fraction = Mathf.Clamp(_shield / MaxShield, 0.0f, 1.0f);
		if (fraction > 0.0f)
		{
			Rect2 fillRect = new(shieldRect.Position, new Vector2(shieldRect.Size.X * fraction, shieldRect.Size.Y));
			DrawRect(fillRect, new Color(0.08f, 0.70f, 1.0f, _healthBarVisibility));
		}
		DrawLine(shieldRect.Position + Vector2.Down * shieldRect.Size.Y, shieldRect.End, new Color(0.20f, 0.88f, 1.0f, _healthBarVisibility), 0.5f, true);
	}

	private void UpdateHealthBarDamageAnimation(float delta)
	{
		int segmentCount = Mathf.Max(1, Mathf.CeilToInt(MaxHealth));
		int currentSegments = Mathf.Clamp(Mathf.CeilToInt(_health), 0, segmentCount);
		if (_lastHealthBarSegments < 0)
		{
			_lastHealthBarSegments = currentSegments;
			return;
		}
		if (currentSegments < _lastHealthBarSegments)
		{
			_healthBarDamageStartSegment = currentSegments;
			_healthBarDamageSegmentCount = _lastHealthBarSegments - currentSegments;
			_healthBarDamageAnimationElapsed = 0.0f;
		}
		else if (_healthBarDamageAnimationElapsed >= 0.0f)
		{
			_healthBarDamageAnimationElapsed += delta;
			if (_healthBarDamageAnimationElapsed >= EnemyHealthBarDamageAnimationSeconds)
				_healthBarDamageAnimationElapsed = -1.0f;
		}
		_lastHealthBarSegments = currentSegments;
	}

	private void DrawHealthBarDamageTransition(Rect2 barRect, float segmentWidth, float divider)
	{
		if (_healthBarDamageAnimationElapsed < 0.0f || _healthBarDamageSegmentCount <= 0)
			return;
		float progress = Mathf.Clamp(_healthBarDamageAnimationElapsed / EnemyHealthBarDamageAnimationSeconds, 0.0f, 1.0f);
		const float fadeToWhiteEnd = 0.30f;
		float whiteStrength = Mathf.Clamp(progress / fadeToWhiteEnd, 0.0f, 1.0f);
		float shrinkProgress = Mathf.Clamp((progress - fadeToWhiteEnd) / (1.0f - fadeToWhiteEnd), 0.0f, 1.0f);
		float heightFactor = 1.0f - Mathf.Ease(shrinkProgress, -1.8f);
		for (int index = 0; index < _healthBarDamageSegmentCount; index++)
		{
			int segment = _healthBarDamageStartSegment + index;
			float fullHeight = barRect.Size.Y - 2.0f;
			float animatedHeight = fullHeight * heightFactor;
			if (animatedHeight <= 0.0f)
				continue;
			float left = barRect.Position.X + 1.0f + segment * (segmentWidth + divider);
			float top = barRect.Position.Y + 1.0f + (fullHeight - animatedHeight) * 0.5f;
			DrawEnemyHealthSegment(new Rect2(left, top, segmentWidth, animatedHeight), Colors.White, whiteStrength);
		}
	}

	private void DrawEnemyHealthSegment(Rect2 rect, Color transitionColor, float transitionStrength)
	{
		Color ruby = new(0.86f, 0.04f, 0.12f, _healthBarVisibility);
		Color fill = ruby.Lerp(new Color(transitionColor, _healthBarVisibility), transitionStrength);
		DrawRect(rect, fill);
	}

	public void SetWithinPhysicsWindow(bool enabled)
	{
		if (_withinPhysicsWindow == enabled)
			return;
		_withinPhysicsWindow = enabled;
		if (!enabled)
		{
			SetCollisionEnabled(false);
			SetPhysicsProcess(false);
		}
		else
		{
			SetPhysicsProcess(true);
		}
	}

	public void ClearTargetLock()
	{
		EndCombatEngagement();
		_burstShotsRemaining = 0;
		_obstructionBurstTarget = null;
		_hasMadePostDeathDecision = false;
		_patrolRetargetSeconds = 0.0f;
	}

	private void OnScreenEntered()
	{
		if (!_withinPhysicsWindow || !IsActuallyOnScreen())
			return;
		_hasBeenRevealed = true;
		SetCollisionEnabled(true);
	}

	private void OnScreenExited()
	{
		// Patrols, and ships that have acquired a target, both continue naturally
		// beyond the camera edge. Their physics window still caps distant work.
	}

	private void SetCollisionEnabled(bool enabled)
	{
		CollisionLayer = enabled ? (uint)(1 << 2) : 0u;
		CollisionMask = enabled ? (uint)((1 << 0) | (1 << 1) | (1 << 2)) : 0u;
	}

	private bool IsActuallyOnScreen()
	{
		Camera2D? camera = GetViewport().GetCamera2D();
		if (camera == null)
			return false;
		Vector2 visibleSize = GetViewportRect().Size * camera.Zoom;
		Rect2 cameraBounds = new(camera.GetScreenCenterPosition() - visibleSize * 0.5f, visibleSize);
		return cameraBounds.Grow(1.0f).HasPoint(GlobalPosition);
	}

	public override void _PhysicsProcess(double delta)
	{
		_playerCollisionDamageCooldown = Mathf.Max(0.0f, _playerCollisionDamageCooldown - (float)delta);
		if (_evasiveBoostSeconds > 0.0f)
		{
			_evasiveBoostSeconds = Mathf.Max(0.0f, _evasiveBoostSeconds - (float)delta);
			_evasiveBoostElapsed += (float)delta;
		}
		else
		{
			_evasiveBoostElapsed = 0.0f;
		}
		_nearMissInvestigationSeconds = Mathf.Max(0.0f, _nearMissInvestigationSeconds - (float)delta);
		_nearbyAsteroidCacheSeconds = Mathf.Max(0.0f, _nearbyAsteroidCacheSeconds - (float)delta);
		_sightQueryCacheSeconds = Mathf.Max(0.0f, _sightQueryCacheSeconds - (float)delta);
		UpdateShieldRegeneration((float)delta);
		if (!_withinPhysicsWindow)
			return;
		bool onScreen = IsActuallyOnScreen();
		// This is deliberately separate from the lock itself. A ship may patrol
		// unseen, but it cannot ever acquire the player until its center has been
		// visibly inside the camera at least once.
		if (onScreen)
			_hasBeenRevealed = true;
		SetCollisionEnabled(true);

		_player ??= GetTree().GetFirstNodeInGroup("player") as Node2D;
		float step = (float)delta;
		if (_recentHitWindowSeconds > 0.0f)
		{
			_recentHitWindowSeconds -= step;
			if (_recentHitWindowSeconds <= 0.0f)
				_recentPlayerHits = 0;
		}
		if (_player is not PlayerController player)
		{
			MoveWithCollision(Vector2.Zero, 0.0f, step);
			return;
		}
		if (!_hasBeenRevealed)
		{
			// No path—including linked allies or direct-hit reactions—may create a
			// combat lock before this ship has physically appeared in the camera.
			EndCombatEngagement();
			_burstShotsRemaining = 0;
			if (!player.IsDead)
				PatrolAsteroids(step);
			else
				MoveWithCollision(Vector2.Zero, 0.0f, step);
			return;
		}
		if (player.IsDead)
		{
			// A player's death is a world event, not only a response for ships that
			// already fired. Every active ship must choose its orbit-or-departure
			// behavior rather than leaving unalerted patrols frozen in place.
			HandlePlayerDeath(player.GlobalPosition, step);
			return;
		}
		if (_hasTargetLock)
		{
			// Fleeing still influences their maneuvering, but never revokes a combat
			// lock by itself. A lock is broken only after ten sustained seconds
			// outside this ship's own field of view.
			if (IsPlayerFleeingFromThisShip(player))
			{
				_playerFleeSeconds += step;
			}
			else
			{
				// A brief turn or strafe should not erase a nearly successful escape,
				// but returning to attack does steadily rebuild the ship's resolve.
				_playerFleeSeconds = Mathf.Max(0.0f, _playerFleeSeconds - step * 0.75f);
			}

			// A lock must be earned and retained with real line of sight. Asteroids
			// block the same broad five-ray visibility check used for acquisition;
			// after five seconds without sight, the player has successfully escaped.
			// A ship may retain a lock for a short period after leaving the camera,
			// but it cannot pursue forever from outside the player's view.
			if (IsActuallyOnScreen() && CanSeePlayer(player.GlobalPosition))
			{
				_lostSightSeconds = 0.0f;
			}
			else
			{
				_lostSightSeconds += step;
				if (_lostSightSeconds >= Mathf.Max(0.0f, LostSightSecondsToBreakLock))
				{
					ClearTargetLock();
					PatrolAsteroids(step);
					return;
				}
			}
		}
		if (ProcessTacticalDisengage(player.GlobalPosition, step))
		{
			if (onScreen)
				ProcessWeapons(player.GlobalPosition, step);
			return;
		}
		// First contact is a permanent camera-only rule. A ship may patrol outside
		// the view, but it never obtains its first combat lock there.
		if (!_hasTargetLock)
			UpdateInitialLockAcquisition(player.GlobalPosition, step);
		if (!_hasTargetLock)
		{
			// A near miss is suspicious but never counts as visual contact. The ship
			// can investigate its source without receiving any lock-on progress.
			if (_nearMissInvestigationSeconds > 0.0f)
			{
				InvestigateLastSighting(step);
				return;
			}
			if (HasReachedInvestigationThreshold())
				InvestigateLastSighting(step);
			else
				PatrolAsteroids(step);
			return;
		}
		if (!HasEngagementSlot())
		{
			// Keep a reserve present and readable, but never let it add another
			// burst to the player. A wounded front-line ship yields its place to a
			// healthier wingmate, so reserves still have a reason to step in.
			_burstShotsRemaining = 0;
			_volleyCooldownSeconds = 1.0f;
			OrbitAsReserve(player.GlobalPosition, step);
			return;
		}
		if (ProcessAmbushResponse(player.GlobalPosition, step))
		{
			if (onScreen)
				ProcessWeapons(player.GlobalPosition, step);
			return;
		}

		_maneuverSeconds -= step;
		if (_maneuverSeconds <= 0.0f)
			RefreshManeuver();
		MoveLikePlayer(player.GlobalPosition, step);
		if (onScreen)
			ProcessWeapons(player.GlobalPosition, step);
	}

	private bool ProcessTacticalDisengage(Vector2 playerPosition, float delta)
	{
		if (_tacticalDisengageSeconds <= 0.0f)
			return false;

		_tacticalDisengageSeconds -= delta;
		Vector2 toPlayer = GlobalPosition.DirectionTo(playerPosition);
		if (toPlayer.LengthSquared() <= Mathf.Epsilon)
			toPlayer = GlobalTransform.Y;
		Vector2 tangent = new Vector2(-toPlayer.Y, toPlayer.X) * _tacticalDirection;
		// A pressured ship commits to a single 90-degree flank/strafe boost. It
		// does not choose a point behind the player or randomly turn and flee.
		Vector2 desiredDirection = GetStandoffArcDirection(
			toPlayer,
			GlobalPosition.DistanceTo(playerPosition),
			tangent,
			false);
		float targetRotation = desiredDirection.Angle() + Mathf.Pi / 2.0f;
		Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * 1.35f * delta);
		MoveWithCollision(desiredDirection, MoveSpeed * GetEvasiveBoostMultiplier(), delta);
		return true;
	}

	private bool ProcessAmbushResponse(Vector2 playerPosition, float delta)
	{
		if (_ambushResponseSeconds <= 0.0f)
			return false;

		_ambushResponseSeconds -= delta;
		Vector2 toPlayer = GlobalPosition.DirectionTo(playerPosition);
		if (toPlayer.LengthSquared() <= Mathf.Epsilon)
			toPlayer = -GlobalTransform.Y;
		if (_ambushStrafe)
		{
			Vector2 tangent = new Vector2(-toPlayer.Y, toPlayer.X) * _ambushStrafeDirection;
			// Respond to an ambush by carving sideways around the player, never by
			// driving directly at their nose.
			Vector2 newApproachAngle = GetStandoffArcDirection(toPlayer, GlobalPosition.DistanceTo(playerPosition), tangent, false);
			float targetRotation = newApproachAngle.Angle() + Mathf.Pi / 2.0f;
			Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * 1.7f * delta);
			// A confirmed hit starts an evasive reorientation rather than a snap turn.
			// The brief boost keeps the response decisive without outrunning the player.
			MoveWithCollision(newApproachAngle, MoveSpeed * GetEvasiveBoostMultiplier(), delta);
		}
		else
		{
			// A direct hit from behind can provoke a quick, readable turn, while
			// RotateToward keeps the hull rotation continuous rather than instant.
			// It still travels through a lateral arc while turning instead of charging
			// straight at the player.
			float targetRotation = toPlayer.Angle() + Mathf.Pi / 2.0f;
			Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * 2.2f * delta);
			Vector2 tangent = new Vector2(-toPlayer.Y, toPlayer.X) * _ambushStrafeDirection;
			MoveWithCollision(GetStandoffArcDirection(toPlayer, GlobalPosition.DistanceTo(playerPosition), tangent, false), MoveSpeed * 0.72f, delta);
		}
		return true;
	}

	private bool HasReachedInvestigationThreshold()
	{
		if (_requiredInitialLockSightSeconds <= 0.001f)
			return false;
		return _initialLockSightSeconds / _requiredInitialLockSightSeconds >= 0.40f;
	}

	private void InvestigateLastSighting(float delta)
	{
		Vector2 toSighting = GlobalPosition.DirectionTo(_lastInvestigationPosition);
		if (toSighting.LengthSquared() <= Mathf.Epsilon)
		{
			PatrolAsteroids(delta);
			return;
		}

		// Investigation is intentional but non-hostile: approach the remembered
		// sighting along a broad arc and retain the normal standoff distance. It
		// cannot fire until the remaining 60% has earned a true combat lock.
		Vector2 tangent = new Vector2(-toSighting.Y, toSighting.X) * (_orbitDirection == 0 ? 1 : _orbitDirection);
		float distance = GlobalPosition.DistanceTo(_lastInvestigationPosition);
		GetCombatRangeBand(out _, out float maximumRange);
		Vector2 desiredDirection = distance > maximumRange
			? (toSighting * 0.48f + tangent * 0.86f).Normalized()
			: (tangent * 0.96f + toSighting * 0.08f).Normalized();
		float targetRotation = desiredDirection.Angle() + Mathf.Pi / 2.0f;
		Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * delta);
		MoveWithCollision(desiredDirection, MoveSpeed * 0.72f, delta);
	}

	private void StartEvasiveBoost(float requestedSeconds)
	{
		float duration = Mathf.Min(
			Mathf.Max(0.0f, MaximumEvasiveBoostSeconds),
			Mathf.Max(EvasiveBoostRampSeconds, requestedSeconds));
		if (_evasiveBoostSeconds <= 0.0f)
			_evasiveBoostElapsed = 0.0f;
		_evasiveBoostSeconds = Mathf.Max(_evasiveBoostSeconds, duration);
	}

	private float GetEvasiveBoostMultiplier()
	{
		if (_evasiveBoostSeconds <= 0.0f)
			return 1.0f;
		float ramp = Mathf.Clamp(_evasiveBoostElapsed / EvasiveBoostRampSeconds, 0.0f, 1.0f);
		return Mathf.Lerp(1.0f, Mathf.Max(1.0f, EvasiveBoostMultiplier), ramp);
	}

	private void PatrolAsteroids(float delta)
	{
		_patrolRetargetSeconds -= delta;
		if (_patrolRetargetSeconds <= 0.0f || !IsInstanceValid(_patrolAnchor))
			SelectPatrolTarget();

		Vector2 toPatrolCenter = GlobalPosition.DirectionTo(_patrolCenter);
		if (toPatrolCenter.LengthSquared() <= Mathf.Epsilon)
			toPatrolCenter = -GlobalTransform.Y;
		float distance = GlobalPosition.DistanceTo(_patrolCenter);
		Vector2 tangent = new Vector2(-toPatrolCenter.Y, toPatrolCenter.X) * _orbitDirection;
		Vector2 desiredDirection = distance > _patrolRadius + 35.0f
			? (toPatrolCenter + tangent * 0.32f).Normalized()
			: distance < _patrolRadius - 35.0f
				? (-toPatrolCenter + tangent * 0.58f).Normalized()
				: tangent;
		float targetRotation = desiredDirection.Angle() + Mathf.Pi / 2.0f;
		Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * delta);
		MoveWithCollision(desiredDirection, MoveSpeed * 0.48f, delta);
	}

	private void SelectPatrolTarget()
	{
		List<BreakableEntityController> nearbyAsteroids = new();
		foreach (Node node in GetTree().GetNodesInGroup("breakable"))
		{
			if (node is BreakableEntityController asteroid
				&& IsInstanceValid(asteroid)
				&& GlobalPosition.DistanceTo(asteroid.GlobalPosition) <= 850.0f)
			{
				nearbyAsteroids.Add(asteroid);
			}
		}

		_patrolRetargetSeconds = _random.RandfRange(5.0f, 9.0f);
		_patrolRadius = _random.RandfRange(100.0f, 185.0f);
		_orbitDirection = _random.Randf() < 0.5f ? -1 : 1;
		if (nearbyAsteroids.Count == 0)
		{
			_patrolAnchor = null;
			_patrolCenter = GlobalPosition + (-GlobalTransform.Y * _random.RandfRange(80.0f, 150.0f));
			return;
		}

		_patrolAnchor = nearbyAsteroids[_random.RandiRange(0, nearbyAsteroids.Count - 1)];
		// Most patrols circle one asteroid. Some use the centroid of a small random
		// collection, which creates wider, less uniform asteroid-field movement.
		if (_random.Randf() < 0.40f && nearbyAsteroids.Count > 1)
		{
			int count = Mathf.Min(nearbyAsteroids.Count, _random.RandiRange(2, 4));
			Vector2 total = Vector2.Zero;
			for (int i = 0; i < count; i++)
				total += nearbyAsteroids[_random.RandiRange(0, nearbyAsteroids.Count - 1)].GlobalPosition;
			_patrolCenter = total / count;
			_patrolRadius += 45.0f;
		}
		else
		{
			_patrolCenter = _patrolAnchor.GlobalPosition;
		}
	}

	private void MoveLikePlayer(Vector2 playerPosition, float delta)
	{
		Vector2 toPlayer = GlobalPosition.DirectionTo(playerPosition);
		float distance = GlobalPosition.DistanceTo(playerPosition);
		GetCombatRangeBand(out float minimumRange, out float maximumRange);
		Vector2 tangent = new Vector2(-toPlayer.Y, toPlayer.X) * _orbitDirection;
		bool tooClose = distance < minimumRange;
		bool tooFar = distance > maximumRange;
		Vector2 desiredDirection;
		if (tooClose)
		{
			// Recover range through a lateral arc. This keeps its guns trained on the
			// player while explicitly increasing separation instead of drifting into a
			// face-to-face collision.
			desiredDirection = (tangent * 0.88f - toPlayer * 0.62f).Normalized();
		}
		else if (tooFar)
		{
			// Even when closing a long gap, enter on a broad arc rather than making a
			// direct beeline. The engagement ring handles the final distance.
			desiredDirection = _isCharging
				? (toPlayer * 0.68f + tangent * 0.65f).Normalized()
				: (toPlayer * 0.50f + tangent * 0.86f).Normalized();
		}
		else if (_isDodging)
		{
			desiredDirection = (tangent * 0.98f + toPlayer * 0.04f).Normalized();
		}
		else
		{
			desiredDirection = (tangent * 0.95f + toPlayer * 0.10f).Normalized();
		}

		// Ordinary combat always keeps the guns trained on the player. The hull is
		// only allowed to face along its movement path during a dedicated evasive
		// boost, not merely because it has completed a volley or drifted too close.
		float targetRotation = toPlayer.Angle() + Mathf.Pi / 2.0f;
		Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * delta);
		float maneuverSpeed = tooClose
			? MoveSpeed * 1.08f
			: _isCharging && tooFar ? MoveSpeed * 1.15f : MoveSpeed;
		MoveWithCollision(desiredDirection, maneuverSpeed, delta);
	}

	private Vector2 GetStandoffArcDirection(Vector2 toPlayer, float distance, Vector2 tangent, bool allowStrongClose)
	{
		GetCombatRangeBand(out float minimumRange, out float maximumRange);
		if (distance < minimumRange)
			return (tangent * 0.88f - toPlayer * (allowStrongClose ? 0.42f : 0.62f)).Normalized();
		if (distance > maximumRange)
			return (tangent * 0.84f + toPlayer * 0.52f).Normalized();
		return (tangent * 0.95f + toPlayer * 0.08f).Normalized();
	}

	private void GetCombatRangeBand(out float minimumRange, out float maximumRange)
	{
		// Keep combat comfortably inside the useful middle of the visual cone.
		// The cone radius is based on the current camera diagonal, so 50–75% stays
		// readable on-screen instead of letting ships hover at the sight limit.
		float visionRadius = Mathf.Max(1.0f, GetFieldOfViewRadius());
		minimumRange = visionRadius * 0.50f;
		maximumRange = visionRadius * 0.75f;
	}

	private bool HasEngagementSlot()
	{
		// There are only two firing slots. Count ships that outrank this one rather
		// than allocating and sorting a List every physics tick on every ship.
		int higherPriorityShips = 0;
		int ownScore = GetEngagementScore();
		foreach (Node node in GetTree().GetNodesInGroup("enemy_ship"))
		{
			if (node is EnemyShipController ship
				&& ship != this
				&& IsInstanceValid(ship)
				&& !ship._destroyed
				&& ship._withinPhysicsWindow
				&& ship._hasTargetLock)
			{
				int candidateScore = ship.GetEngagementScore();
				if (candidateScore < ownScore
					|| (candidateScore == ownScore && ship._engagementPriority < _engagementPriority))
				{
					higherPriorityShips++;
					if (higherPriorityShips >= MaxConcurrentAttackers)
						return false;
				}
			}
		}
		return true;
	}

	private int GetEngagementScore()
	{
		// A damaged front-line ship falls back into the wing. This lets an orbiting
		// reserve rotate into the two-ship firing line without ever exceeding it.
		bool needsRelief = _health <= MaxHealth * 0.50f;
		return _engagementPriority + (needsRelief ? 1_000_000 : 0);
	}

	private void OrbitAsReserve(Vector2 playerPosition, float delta)
	{
		Vector2 toPlayer = GlobalPosition.DirectionTo(playerPosition);
		if (toPlayer.LengthSquared() <= Mathf.Epsilon)
			toPlayer = -GlobalTransform.Y;
		float distance = GlobalPosition.DistanceTo(playerPosition);
		Vector2 tangent = new Vector2(-toPlayer.Y, toPlayer.X) * _orbitDirection;
		const float reserveRange = 440.0f;
		Vector2 desiredDirection = distance > reserveRange + 55.0f
			? (toPlayer + tangent * 0.36f).Normalized()
			: distance < reserveRange - 55.0f
				? (-toPlayer + tangent * 0.60f).Normalized()
				: tangent;
		float targetRotation = desiredDirection.Angle() + Mathf.Pi / 2.0f;
		Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * delta);
		MoveWithCollision(desiredDirection, MoveSpeed * 0.62f, delta);
	}

	private void HandlePlayerDeath(Vector2 deathPosition, float delta)
	{
		if (!_hasMadePostDeathDecision)
			ChoosePostDeathBehavior(deathPosition);
		else if (!_isRetreatingAfterPlayerDeath)
		{
			_postDeathDecisionSeconds -= delta;
			if (_postDeathDecisionSeconds <= 0.0f)
				ChoosePostDeathBehavior(deathPosition);
		}

		if (_isRetreatingAfterPlayerDeath)
		{
			float targetRotation = _retreatDirection.Angle() + Mathf.Pi / 2.0f;
			Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * delta);
			MoveWithCollision(_retreatDirection, MoveSpeed, delta);
			return;
		}

		Vector2 toDeathPosition = GlobalPosition.DirectionTo(_playerDeathPosition);
		float distance = GlobalPosition.DistanceTo(_playerDeathPosition);
		Vector2 tangent = new Vector2(-toDeathPosition.Y, toDeathPosition.X) * _orbitDirection;
		Vector2 orbitDirection = distance > _desiredRange + 45.0f
			? (toDeathPosition + tangent * 0.32f).Normalized()
			: distance < _desiredRange - 45.0f
				? (-toDeathPosition + tangent * 0.62f).Normalized()
				: tangent;
		// Face along the orbital path instead of looking inward at the death point.
		float orbitRotation = orbitDirection.Angle() + Mathf.Pi / 2.0f;
		Rotation = Mathf.RotateToward(Rotation, orbitRotation, Mathf.DegToRad(TurnSpeedDegrees) * delta);
		MoveWithCollision(orbitDirection, MoveSpeed * 0.78f, delta);
	}

	private void ChoosePostDeathBehavior(Vector2 deathPosition)
	{
		_hasMadePostDeathDecision = true;
		_playerDeathPosition = deathPosition;
		_isRetreatingAfterPlayerDeath = _random.Randf() < 0.5f;
		_postDeathDecisionSeconds = _isRetreatingAfterPlayerDeath ? 0.0f : _random.RandfRange(2.5f, 5.0f);
		_burstShotsRemaining = 0;
		_volleyCooldownSeconds = 2.0f;
		_isCharging = false;
		_isDodging = false;
		if (_isRetreatingAfterPlayerDeath)
		{
			_retreatDirection = deathPosition.DirectionTo(GlobalPosition);
			if (_retreatDirection.LengthSquared() <= Mathf.Epsilon)
				_retreatDirection = -GlobalTransform.Y;
			_retreatDirection = _retreatDirection.Rotated(_random.RandfRange(-0.32f, 0.32f)).Normalized();
		}
		else
		{
			_desiredRange = _random.RandfRange(90.0f, 145.0f);
			_orbitDirection = _random.Randf() < 0.5f ? -1 : 1;
		}
	}

	private void MoveWithCollision(Vector2 desiredDirection, float speed, float delta)
	{
		float speedCap = GetMovementSpeedCap();
		speed = Mathf.Min(speed, speedCap);
		Vector2 velocityBeforeCollision = Velocity;
		Vector2 positionBeforeCollision = GlobalPosition;
		if (desiredDirection.LengthSquared() <= Mathf.Epsilon || speed <= 0.0f)
		{
			_flightPlanSeconds = 0.0f;
			float coastDeceleration = speedCap / Mathf.Max(0.1f, CoastStopSeconds);
			Velocity = Velocity.MoveToward(Vector2.Zero, coastDeceleration * delta);
		}
		else
		{
			desiredDirection = GetPlannedFlightDirection(desiredDirection, speed, delta);
			Vector2 currentHeading = Velocity.LengthSquared() > 0.01f ? Velocity.Normalized() : desiredDirection;
			Vector2 shipForward = -GlobalTransform.Y;
			// Ships can strafe or reverse, but their acceleration retains a deliberate
			// preference for where the hull is facing and for their existing momentum.
			float forwardAlignment = (shipForward.Dot(desiredDirection) + 1.0f) * 0.5f;
			float hullPreference = _pathAvoidanceActive ? 0.18f : 0.52f;
			desiredDirection = desiredDirection.Lerp(currentHeading, 0.18f).Lerp(shipForward, hullPreference).Normalized();
			float driveSpeed = Mathf.Min(speedCap, speed * Mathf.Lerp(0.62f, 1.22f, forwardAlignment));
			Vector2 targetVelocity = desiredDirection * driveSpeed;
			bool reversing = Velocity.LengthSquared() > 0.01f && Velocity.Dot(targetVelocity) < 0.0f;
			// A flank boost must actually travel during its short decision window.
			// It therefore reaches its chosen arc quickly, then returns to the same
			// low-friction acceleration and coasting behavior as normal flight.
			float accelerationWindow = _evasiveBoostSeconds > 0.0f
				? 0.30f
				: reversing ? DirectionChangeSeconds : AccelerationSeconds;
			float acceleration = speedCap / Mathf.Max(0.1f, accelerationWindow);
			Velocity = Velocity.MoveToward(targetVelocity, acceleration * delta);
		}
		MoveAndSlide();
		HandleAsteroidContacts(velocityBeforeCollision, positionBeforeCollision, delta);
	}

	private Vector2 GetPlannedFlightDirection(Vector2 intendedDirection, float speed, float delta)
	{
		_flightPlanSeconds = Mathf.Max(0.0f, _flightPlanSeconds - delta);
		bool mustReplan = _flightPlanSeconds <= 0.0f
			|| _plannedFlightDirection.LengthSquared() <= Mathf.Epsilon
			|| _collisionRecoverySeconds > 0.0f;
		if (mustReplan)
		{
			_pathAvoidanceActive = false;
			Vector2 plannedDirection = SteerAroundPlayer(intendedDirection);
			plannedDirection = SteerAroundOtherShips(plannedDirection);
			if (_collisionRecoverySeconds > 0.0f)
			{
				_pathAvoidanceActive = true;
				_collisionRecoverySeconds = Mathf.Max(0.0f, _collisionRecoverySeconds - delta);
				Vector2 tangent = new Vector2(-_collisionRecoveryDirection.Y, _collisionRecoveryDirection.X) * _asteroidAvoidanceDirection;
				plannedDirection = (_collisionRecoveryDirection * 0.78f + tangent * 0.62f).Normalized();
				_flightPlanSeconds = 0.08f;
			}
			else
			{
				plannedDirection = SteerAroundAsteroids(plannedDirection, speed, delta);
				// Hold ordinary routes briefly. This is long enough to eliminate
				// frame-by-frame indecision while retaining player-like responsiveness.
				_flightPlanSeconds = 0.18f;
			}
			_plannedFlightDirection = plannedDirection;
		}

		// Bullets are the sole immediate exception: a new near hit may bend the
		// current route, but that evasive route is then held for a short interval
		// rather than being re-rolled every physics frame.
		Vector2 bulletAdjustedDirection = EvadePlayerBullets(_plannedFlightDirection);
		if (!bulletAdjustedDirection.IsEqualApprox(_plannedFlightDirection))
		{
			_plannedFlightDirection = bulletAdjustedDirection;
			_flightPlanSeconds = Mathf.Min(_flightPlanSeconds, 0.10f);
		}
		return _plannedFlightDirection;
	}

	private float GetMovementSpeedCap()
	{
		float playerSpeedCap = _player is PlayerController player
			? player.MoveSpeed * Mathf.Clamp(PlayerSpeedCapFraction, 0.0f, 1.0f)
			: MoveSpeed;
		return Mathf.Max(0.0f, playerSpeedCap);
	}

	private Vector2 SteerAroundPlayer(Vector2 desiredDirection)
	{
		if (_player is not PlayerController player || player.IsDead)
			return desiredDirection;

		float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
		float avoidanceRange = Mathf.Max(88.0f, MinimumPlayerStandoffDistance);
		if (distance >= avoidanceRange)
			return desiredDirection;
		Vector2 toPlayer = GlobalPosition.DirectionTo(player.GlobalPosition);
		float closingIntent = Mathf.Max(0.0f, desiredDirection.Dot(toPlayer));
		if (Velocity.LengthSquared() > 0.01f)
			closingIntent = Mathf.Max(closingIntent, Velocity.Normalized().Dot(toPlayer));
		if (closingIntent <= 0.05f)
			return desiredDirection;

		float proximity = 1.0f - distance / avoidanceRange;
		Vector2 awayFromPlayer = -toPlayer;
		float side = desiredDirection.Cross(toPlayer);
		if (Mathf.Abs(side) <= 0.05f)
			side = _orbitDirection == 0 ? 1 : _orbitDirection;
		Vector2 tangent = new Vector2(-toPlayer.Y, toPlayer.X) * Mathf.Sign(side);
		_pathAvoidanceActive = true;
		return (desiredDirection * (1.0f - proximity * 0.80f) + awayFromPlayer * proximity * 0.90f + tangent * proximity * 0.72f).Normalized();
	}

	private Vector2 SteerAroundOtherShips(Vector2 desiredDirection)
	{
		Vector2 direction = desiredDirection.Normalized();
		EnemyShipController? closestShip = null;
		Vector2 closestRelative = Vector2.Zero;
		float closestForwardDistance = 230.0f;
		foreach (Node node in GetTree().GetNodesInGroup("enemy_ship"))
		{
			if (node is not EnemyShipController other || other == this || !IsInstanceValid(other) || other._destroyed)
				continue;
			// Predict the other ship's short-term drift so two ships choose a route
			// before their collision circles overlap.
			Vector2 predictedPosition = other.GlobalPosition + other.Velocity * 0.70f;
			Vector2 relative = predictedPosition - GlobalPosition;
			float forwardDistance = relative.Dot(direction);
			if (forwardDistance <= 0.0f || forwardDistance >= closestForwardDistance)
				continue;
			if (Mathf.Abs(direction.Cross(relative)) > 72.0f)
				continue;
			closestShip = other;
			closestRelative = relative;
			closestForwardDistance = forwardDistance;
		}

		if (closestShip == null)
			return desiredDirection;

		Vector2 toShip = closestRelative.Normalized();
		float side = direction.Cross(toShip);
		if (Mathf.Abs(side) <= 0.05f)
			side = _orbitDirection == 0 ? 1 : _orbitDirection;
		Vector2 tangent = new Vector2(-toShip.Y, toShip.X) * Mathf.Sign(side);
		_pathAvoidanceActive = true;
		return (desiredDirection * 0.34f + tangent * 0.90f - toShip * 0.58f).Normalized();
	}

	private Vector2 EvadePlayerBullets(Vector2 desiredDirection)
	{
		foreach (Node node in GetTree().GetNodesInGroup("player_bullet"))
		{
			if (node is not BulletController bullet || !IsInstanceValid(bullet))
				continue;
			Vector2 relative = GlobalPosition - bullet.GlobalPosition;
			float forwardDistance = relative.Dot(bullet.Direction);
			if (forwardDistance <= 0.0f || forwardDistance > 190.0f)
				continue;
			float lateralDistance = Mathf.Abs(bullet.Direction.Cross(relative));
			if (lateralDistance > 46.0f)
				continue;

			if (!_hasTargetLock && _hasBeenRevealed)
				RegisterPassingPlayerBolt(bullet);

			float side = bullet.Direction.Cross(relative);
			if (Mathf.Abs(side) <= 0.01f)
				side = _orbitDirection == 0 ? 1.0f : _orbitDirection;
			else
				side = Mathf.Sign(side);
			Vector2 dodgeDirection = new Vector2(-bullet.Direction.Y, bullet.Direction.X) * side;
			_pathAvoidanceActive = true;
			return (desiredDirection * 0.30f + dodgeDirection * 0.90f).Normalized();
		}
		return desiredDirection;
	}

	private void RegisterPassingPlayerBolt(BulletController bullet)
	{
		ulong bulletId = bullet.GetInstanceId();
		if (bulletId == _lastNoticedPlayerBulletId)
			return;

		_lastNoticedPlayerBulletId = bulletId;
		// Look back down the projectile's path rather than pretending the bolt
		// itself revealed the player. This remains an investigation only: it does
		// not modify the sight/lock meter or bypass the visibility rules.
		_lastInvestigationPosition = bullet.GlobalPosition - bullet.Direction * 180.0f;
		_nearMissInvestigationSeconds = 2.25f;
	}

	private Vector2 SteerAroundAsteroids(Vector2 desiredDirection, float speed, float delta)
	{
		// Probe the course the inertial ship will actually travel, not merely the
		// newly requested steering vector. This catches a rock that enters while
		// the hull is still carrying momentum from its prior heading.
		Vector2 currentCourse = Velocity.LengthSquared() > 0.01f ? Velocity.Normalized() : -GlobalTransform.Y;
		Vector2 probeDirection = desiredDirection.Lerp(currentCourse, 0.42f).Normalized();
		// Plan against a full ship-width corridor, not a single pencil-thin ray.
		// This catches an asteroid as soon as it enters the committed flight path,
		// long before the collision hull can touch it.
		float speedFraction = Mathf.Clamp(speed / Mathf.Max(1.0f, MoveSpeed), 0.0f, 1.0f);
		float probeDistance = Mathf.Lerp(
			Mathf.Max(24.0f, ObstructionProbeDistanceMinimum),
			Mathf.Max(ObstructionProbeDistanceMinimum, ObstructionProbeDistanceMaximum),
			speedFraction);
		BreakableEntityController? asteroid = FindAsteroidInFlightCorridor(probeDirection, probeDistance);
		if (!IsInstanceValid(asteroid))
		{
			_asteroidAvoidanceSeconds = Mathf.Max(0.0f, _asteroidAvoidanceSeconds - delta);
			ResetObstructionDecision();
			return ApplyRememberedObstacleSteering(desiredDirection, probeDistance, delta);
		}
		UpdateBlockingAsteroid(asteroid, probeDirection);
		RememberObstacle(asteroid, probeDirection);

		Vector2 awayFromAsteroid = asteroid.GlobalPosition.DirectionTo(GlobalPosition);
		if (awayFromAsteroid.LengthSquared() <= Mathf.Epsilon)
			awayFromAsteroid = -probeDirection;
		float sideSign = probeDirection.Cross(awayFromAsteroid);
		if (_asteroidAvoidanceSeconds <= 0.0f)
		{
			_asteroidAvoidanceSeconds = 1.8f;
			_avoidAsteroidByTurning = true;
			_asteroidAvoidanceDirection = Mathf.Abs(sideSign) <= 0.05f
				? (_orbitDirection == 0 ? 1 : _orbitDirection)
				: Mathf.Sign(sideSign) > 0.0f ? 1 : -1;
		}
		_pathAvoidanceActive = true;
		Vector2 tangent = new Vector2(-awayFromAsteroid.Y, awayFromAsteroid.X) * _asteroidAvoidanceDirection;
		// Commit to the selected side rather than repeatedly choosing a new turn.
		return (probeDirection * 0.38f + tangent * 0.88f + awayFromAsteroid * 0.72f).Normalized();
	}

	private BreakableEntityController? FindAsteroidInFlightCorridor(Vector2 direction, float probeDistance)
	{
		RefreshNearbyAsteroidCache();
		BreakableEntityController? closest = null;
		float closestDistance = probeDistance;
		// Ship radius (18) + asteroid radius (22) + a generous navigation buffer.
		const float corridorHalfWidth = 58.0f;
		foreach (BreakableEntityController asteroid in _nearbyAsteroidCache)
		{
			if (!IsInstanceValid(asteroid))
				continue;
			Vector2 relative = asteroid.GlobalPosition - GlobalPosition;
			float forwardDistance = relative.Dot(direction);
			if (forwardDistance <= 0.0f || forwardDistance >= closestDistance)
				continue;
			float lateralDistance = Mathf.Abs(direction.Cross(relative));
			if (lateralDistance > corridorHalfWidth)
				continue;
			closest = asteroid;
			closestDistance = forwardDistance;
		}
		return closest;
	}

	private void RefreshNearbyAsteroidCache()
	{
		if (_nearbyAsteroidCacheSeconds > 0.0f)
			return;
		_nearbyAsteroidCacheSeconds = 0.25f;
		_nearbyAsteroidCache.Clear();
		float searchRadius = Mathf.Max(ObstructionProbeDistanceMaximum, 360.0f) + 96.0f;
		// Query only the nearby physics bodies instead of making every active ship
		// iterate the full generated asteroid list four times per second. This is a
		// major cost reduction once several ships are present.
		_nearbyAsteroidQueryShape.Radius = searchRadius;
		_nearbyAsteroidQuery.Shape = _nearbyAsteroidQueryShape;
		_nearbyAsteroidQuery.Transform = new Transform2D(0.0f, GlobalPosition);
		_nearbyAsteroidQuery.CollisionMask = 1u << 1;
		_nearbyAsteroidQuery.CollideWithBodies = true;
		_nearbyAsteroidQuery.CollideWithAreas = false;
		foreach (Godot.Collections.Dictionary result in GetWorld2D().DirectSpaceState.IntersectShape(_nearbyAsteroidQuery, 64))
		{
			if (result.TryGetValue("collider", out Variant collider)
				&& collider.AsGodotObject() is BreakableEntityController asteroid
				&& IsInstanceValid(asteroid))
				_nearbyAsteroidCache.Add(asteroid);
		}
	}

	private void RememberObstacle(BreakableEntityController asteroid, Vector2 probeDirection)
	{
		if (!IsInstanceValid(asteroid))
			return;

		if (_rememberedObstacle != asteroid)
		{
			if (_rememberedObstacleSeconds <= 0.0f)
			{
				Vector2 awayFromAsteroid = asteroid.GlobalPosition.DirectionTo(GlobalPosition);
				float side = probeDirection.Cross(awayFromAsteroid);
				_asteroidAvoidanceDirection = Mathf.Abs(side) <= 0.05f
					? (_orbitDirection == 0 ? 1 : _orbitDirection)
					: Mathf.Sign(side) > 0.0f ? 1 : -1;
			}
			_rememberedObstacle = asteroid;
		}
		_rememberedObstacleSeconds = 2.0f;
	}

	private Vector2 ApplyRememberedObstacleSteering(Vector2 desiredDirection, float probeDistance, float delta)
	{
		if (!IsInstanceValid(_rememberedObstacle))
		{
			_rememberedObstacle = null;
			_rememberedObstacleSeconds = 0.0f;
			return desiredDirection;
		}

		_rememberedObstacleSeconds = Mathf.Max(0.0f, _rememberedObstacleSeconds - delta);
		Vector2 toObstacle = GlobalPosition.DirectionTo(_rememberedObstacle!.GlobalPosition);
		float distance = GlobalPosition.DistanceTo(_rememberedObstacle.GlobalPosition);
		if (_rememberedObstacleSeconds <= 0.0f
			|| distance > probeDistance * 1.45f
			|| desiredDirection.Dot(toObstacle) < -0.15f)
		{
			_rememberedObstacle = null;
			return desiredDirection;
		}

		Vector2 awayFromAsteroid = -toObstacle;
		Vector2 tangent = new Vector2(-awayFromAsteroid.Y, awayFromAsteroid.X) * _asteroidAvoidanceDirection;
		_pathAvoidanceActive = true;
		return (desiredDirection * 0.42f + tangent * 0.78f + awayFromAsteroid * 0.54f).Normalized();
	}

	private void UpdateBlockingAsteroid(BreakableEntityController asteroid, Vector2 probeDirection)
	{
		if (!IsInstanceValid(asteroid))
		{
			ResetObstructionDecision();
			return;
		}

		if (_obstructionDecisionAsteroid != asteroid)
		{
			_obstructionDecisionAsteroid = asteroid;
			// This asteroid is directly inside the route the ship has committed to.
			// Clearing it is a navigation decision, so it cannot depend on whether
			// the ship has already acquired a player target.
			_clearObstructionByShooting = true;
		}
		if (!_clearObstructionByShooting
			|| _burstShotsRemaining > 0)
			return;

		// This is a long-range forward-ray decision, never a collision response.
		// Use the same six-round rhythm as a player volley so a chosen clear is
		// decisive rather than a stream of distracting single shots.
		StartObstructionBurst(asteroid);
	}

	private void ResetObstructionDecision()
	{
		_obstructionDecisionAsteroid = null;
		_clearObstructionByShooting = false;
	}

	private void HandleAsteroidContacts(Vector2 velocityBeforeCollision, Vector2 positionBeforeCollision, float delta)
	{
		bool hitAsteroid = false;
		Vector2 recoveryDirection = Vector2.Zero;
		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			if (GetSlideCollision(i).GetCollider() is not BreakableEntityController asteroid)
				continue;
			hitAsteroid = true;
			recoveryDirection = asteroid.GlobalPosition.DirectionTo(GlobalPosition);
		}
		if (!hitAsteroid)
			return;

		if (recoveryDirection.LengthSquared() <= Mathf.Epsilon)
			recoveryDirection = -GlobalTransform.Y;
		_collisionRecoveryDirection = recoveryDirection;
		_collisionRecoverySeconds = 0.55f;
		_asteroidAvoidanceDirection = _orbitDirection == 0 ? 1 : _orbitDirection;
		Vector2 recoveryTangent = new Vector2(-recoveryDirection.Y, recoveryDirection.X) * _asteroidAvoidanceDirection;
		float speedCap = GetMovementSpeedCap();
		Velocity = (recoveryDirection * speedCap * 0.34f + recoveryTangent * speedCap * 0.16f).LimitLength(speedCap * 0.45f);
		float maxDisplacement = velocityBeforeCollision.Length() * delta + 4.0f;
		Vector2 displacement = GlobalPosition - positionBeforeCollision;
		if (displacement.Length() > maxDisplacement)
			GlobalPosition = positionBeforeCollision + displacement.Normalized() * maxDisplacement;
	}

	private void ProcessWeapons(Vector2 playerPosition, float delta)
	{
		if (_burstShotsRemaining > 0)
		{
			_nextShotSeconds -= delta;
			if (_nextShotSeconds > 0.0f)
				return;

			bool fired;
			if (IsInstanceValid(_obstructionBurstTarget))
			{
				// A flank route may identify its obstacle before the hull has finished
				// turning toward that leg. Retain the burst until the guns can genuinely
				// bear on it instead of cancelling the route-clear decision.
				if (!CanFireAtBlockingAsteroid(_obstructionBurstTarget!))
				{
					_nextShotSeconds = 0.08f;
					return;
				}
				ClearBurstAim();
				fired = FireAtBlockingAsteroid(_obstructionBurstTarget!);
			}
			else
			{
				_obstructionBurstTarget = null;
				fired = FireBolt(playerPosition);
			}

			// Do not keep emptying a six-shot counter after a target moved behind
			// cover. The next available volley should immediately return to a valid
			// player shot rather than treating lost sight as completed fire.
			if (!fired)
			{
				_burstShotsRemaining = 0;
				_obstructionBurstTarget = null;
				ClearBurstAim();
				_volleyCooldownSeconds = 0.18f;
				return;
			}

			_burstShotsRemaining--;
			_nextShotSeconds = 0.12f;
			if (_burstShotsRemaining == 0)
			{
				_obstructionBurstTarget = null;
				ClearBurstAim();
				_volleyCooldownSeconds = 1.0f;
			}
			return;
		}

		_volleyCooldownSeconds -= delta;
		if (_volleyCooldownSeconds <= 0.0f
			&& HasClearShot(ToGlobal(_fireLeftGun ? LeftGunOffset : RightGunOffset), playerPosition))
		{
			StartPlayerBurst();
		}
	}

	private void StartObstructionBurst(BreakableEntityController asteroid)
	{
		if (!IsInstanceValid(asteroid))
			return;
		_obstructionBurstTarget = asteroid;
		_burstShotsRemaining = 6;
		_nextShotSeconds = 0.0f;
		_volleyCooldownSeconds = 0.0f;
		ClearBurstAim();
	}

	private void StartPlayerBurst()
	{
		_burstShotsRemaining = 6;
		_nextShotSeconds = 0.0f;
		ClearBurstAim();
	}

	private void ClearBurstAim()
	{
		_hasBurstAimPoint = false;
		_burstAimPoint = Vector2.Zero;
		_burstAimOffsetRadians = 0.0f;
	}

	private bool FireBolt(Vector2 playerPosition)
	{
		if (EnemyBoltScene == null || _player is not PlayerController player)
			return false;
		Vector2 spawnPosition = ToGlobal(_fireLeftGun ? LeftGunOffset : RightGunOffset);
		if (!HasClearShot(spawnPosition, playerPosition))
			return false;
		Vector2 aimPoint = GetSmoothedBurstAimPoint(spawnPosition, player);
		_fireLeftGun = !_fireLeftGun;
		EnemyBoltController bolt = EnemyBoltScene.Instantiate<EnemyBoltController>();
		bolt.Initialize(spawnPosition.DirectionTo(aimPoint), BoltSpeed, BoltDamage, this);
		bolt.GlobalPosition = spawnPosition;
		Node parent = GetTree().CurrentScene ?? GetParent();
		parent.AddChild(bolt);
		// Keep enemy volleys audibly distinct from the player's gun when both are
		// firing at once, without changing the player's unpitched one-shot.
		SoundEffectsController.PlayOneShot(this, SoundEffectsController.Effect.MainGun, -100.0f);
		return true;
	}

	private Vector2 GetSmoothedBurstAimPoint(Vector2 spawnPosition, PlayerController player)
	{
		Vector2 relativePosition = player.GlobalPosition - spawnPosition;
		if (!_hasBurstAimPoint)
		{
			_burstAimOffsetRadians = GetBurstAimErrorRadians(relativePosition, player);
			_hasBurstAimPoint = true;
		}

		Vector2 desiredAimPoint = GetLeadAimPoint(spawnPosition, player, _burstAimOffsetRadians);
		if (_burstAimPoint == Vector2.Zero)
		{
			_burstAimPoint = desiredAimPoint;
		}
		else
		{
			// Preserve a single burst's imperfect lead identity, then gradually move
			// it as the player changes course. Each bolt stays near its neighbors
			// without the burst becoming a frozen snapshot or aimbot tracking.
			_burstAimPoint = _burstAimPoint.Lerp(
				desiredAimPoint,
				Mathf.Clamp(BurstAimTrackingSmoothing, 0.0f, 1.0f));
		}
		return _burstAimPoint;
	}

	private float GetBurstAimErrorRadians(Vector2 relativePosition, PlayerController player)
	{
		float distanceFactor = Mathf.Clamp(relativePosition.Length() / 800.0f, 0.0f, 1.0f);
		Vector2 targetDirection = relativePosition.Normalized();
		// Lateral movement is the difficult part of an intercept. A player flying
		// directly toward or away from the enemy should receive an almost centered
		// burst, not a large side-to-side miss purely because they are moving fast.
		float lateralSpeed = Mathf.Abs(player.Velocity.Cross(targetDirection));
		float lateralMotionFactor = Mathf.Clamp(lateralSpeed / Mathf.Max(1.0f, player.MoveSpeed), 0.0f, 1.0f);
		float minimumError = Mathf.Max(0.0f, AimErrorMinimumDegrees);
		float maximumError = Mathf.Max(minimumError, AimErrorMaximumDegrees);
		float errorFactor = Mathf.Max(lateralMotionFactor, distanceFactor * 0.20f);
		float errorDegrees = Mathf.Lerp(minimumError, maximumError, errorFactor);
		return Mathf.DegToRad(_random.RandfRange(-errorDegrees, errorDegrees));
	}

	private Vector2 GetLeadAimPoint(Vector2 spawnPosition, PlayerController player, float errorRadians)
	{
		Vector2 relativePosition = player.GlobalPosition - spawnPosition;
		Vector2 targetVelocity = player.Velocity * Mathf.Clamp(LeadVelocityFactor, 0.0f, 1.0f);
		float projectileSpeed = Mathf.Max(1.0f, BoltSpeed);
		float travelTime = CalculateInterceptTime(relativePosition, targetVelocity, projectileSpeed);
		travelTime = Mathf.Clamp(travelTime, 0.0f, Mathf.Max(0.0f, MaximumLeadSeconds));

		Vector2 predictedPoint = player.GlobalPosition + targetVelocity * travelTime;
		Vector2 aimVector = predictedPoint - spawnPosition;
		if (aimVector.LengthSquared() <= Mathf.Epsilon)
			return player.GlobalPosition;

		return spawnPosition + aimVector.Rotated(errorRadians);
	}

	private static float CalculateInterceptTime(Vector2 relativePosition, Vector2 targetVelocity, float projectileSpeed)
	{
		float a = targetVelocity.LengthSquared() - projectileSpeed * projectileSpeed;
		float b = 2.0f * relativePosition.Dot(targetVelocity);
		float c = relativePosition.LengthSquared();
		if (c <= Mathf.Epsilon)
			return 0.0f;

		if (Mathf.Abs(a) <= 0.0001f)
		{
			if (Mathf.Abs(b) <= 0.0001f)
				return Mathf.Sqrt(c) / projectileSpeed;
			float linearTime = -c / b;
			return linearTime > 0.0f ? linearTime : Mathf.Sqrt(c) / projectileSpeed;
		}

		float discriminant = b * b - 4.0f * a * c;
		if (discriminant < 0.0f)
			return Mathf.Sqrt(c) / projectileSpeed;

		float root = Mathf.Sqrt(discriminant);
		float firstTime = (-b - root) / (2.0f * a);
		float secondTime = (-b + root) / (2.0f * a);
		float interceptTime = float.MaxValue;
		if (firstTime > 0.0f)
			interceptTime = firstTime;
		if (secondTime > 0.0f)
			interceptTime = Mathf.Min(interceptTime, secondTime);
		return interceptTime < float.MaxValue ? interceptTime : Mathf.Sqrt(c) / projectileSpeed;
	}

	private bool FireAtBlockingAsteroid(BreakableEntityController asteroid)
	{
		if (EnemyBoltScene == null || !IsInstanceValid(asteroid))
			return false;

		if (!CanFireAtBlockingAsteroid(asteroid))
			return false;
		Vector2 spawnPosition = ToGlobal(_fireLeftGun ? LeftGunOffset : RightGunOffset);
		Vector2 direction = spawnPosition.DirectionTo(asteroid.GlobalPosition);

		_fireLeftGun = !_fireLeftGun;
		EnemyBoltController bolt = EnemyBoltScene.Instantiate<EnemyBoltController>();
		bolt.Initialize(direction, BoltSpeed, BoltDamage, this);
		bolt.GlobalPosition = spawnPosition;
		Node parent = GetTree().CurrentScene ?? GetParent();
		parent.AddChild(bolt);
		SoundEffectsController.PlayOneShot(this, SoundEffectsController.Effect.MainGun, -100.0f);
		return true;
	}

	private bool CanFireAtBlockingAsteroid(BreakableEntityController asteroid)
	{
		if (!IsInstanceValid(asteroid))
			return false;
		Vector2 spawnPosition = ToGlobal(_fireLeftGun ? LeftGunOffset : RightGunOffset);
		Vector2 direction = spawnPosition.DirectionTo(asteroid.GlobalPosition);
		return direction.LengthSquared() > Mathf.Epsilon
			&& (-GlobalTransform.Y).Dot(direction) >= 0.72f;
	}

	private bool HasClearShot(Vector2 from, Vector2 to)
	{
		if (!CanSeePlayer(to))
			return false;
		Vector2 playerDirection = from.DirectionTo(to);
		Vector2 forward = -GlobalTransform.Y;
		float firingDot = Mathf.Cos(Mathf.DegToRad(FiringConeDegrees * 0.5f));
		if (forward.Dot(playerDirection) < firingDot)
			return false;
		return true;
	}

	private bool CanSeePlayer(Vector2 to)
	{
		GetPlayerSightRayCoverage(to, out int clearRayCount, out _, out bool centerRayClear);
		return centerRayClear && clearRayCount >= 6;
	}

	private void GetPlayerSightRayCoverage(Vector2 to, out int clearRayCount, out int testedRayCount, out bool centerRayClear)
	{
		// Several behaviors can ask the same eleven-ray question during a single
		// frame. Reuse a very short sample while the player remains nearby; at
		// 20 Hz it is still much faster than the close-range half-second lock.
		if (_sightQueryCacheSeconds > 0.0f && _cachedSightTarget.DistanceSquaredTo(to) <= 576.0f)
		{
			clearRayCount = _cachedClearSightRays;
			testedRayCount = _cachedTestedSightRays;
			centerRayClear = _cachedCenterSightClear;
			return;
		}

		clearRayCount = 0;
		testedRayCount = 0;
		centerRayClear = false;
		Vector2 from = GetWindshieldRayOrigin();
		if (!IsWithinFieldOfView(from, to))
		{
			CacheSightRayCoverage(to, clearRayCount, testedRayCount, centerRayClear);
			return;
		}
		Vector2 playerDirection = from.DirectionTo(to);

		// Looking through a pinhole between asteroids is not a meaningful visual
		// lock. Test the player across most of its collision width with eleven
		// rays: the center ray and at least five more rays must remain clear.
		Vector2 lateral = new(-playerDirection.Y, playerDirection.X);
		foreach (float offset in SightRayOffsets)
		{
			Vector2 endpoint = to + lateral * offset;
			if (!IsWithinFieldOfView(from, endpoint))
				continue;
			testedRayCount++;
			PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(
				from,
				endpoint,
				(uint)(1 << 1));
			Godot.Collections.Dictionary hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
			if (hit.Count != 0)
				continue;
			clearRayCount++;
			if (Mathf.IsZeroApprox(offset))
				centerRayClear = true;
		}
		CacheSightRayCoverage(to, clearRayCount, testedRayCount, centerRayClear);
	}

	private void CacheSightRayCoverage(Vector2 target, int clearRayCount, int testedRayCount, bool centerRayClear)
	{
		_cachedSightTarget = target;
		_cachedClearSightRays = clearRayCount;
		_cachedTestedSightRays = testedRayCount;
		_cachedCenterSightClear = centerRayClear;
		_sightQueryCacheSeconds = 0.05f;
	}

	private Vector2 GetWindshieldRayOrigin()
	{
		return ToGlobal(WindshieldRayOriginOffset);
	}

	private float GetFieldOfViewRadius()
	{
		Camera2D? camera = GetViewport().GetCamera2D();
		Vector2 viewportSize = GetViewportRect().Size * (camera?.Zoom ?? Vector2.One);
		return viewportSize.Length() * 0.5f;
	}

	private bool IsWithinFieldOfView(Vector2 origin, Vector2 target)
	{
		Vector2 direction = origin.DirectionTo(target);
		if (direction.LengthSquared() <= Mathf.Epsilon || origin.DistanceTo(target) > GetFieldOfViewRadius())
			return false;
		float fieldThreshold = Mathf.Cos(Mathf.DegToRad(100.0f));
		return (-GlobalTransform.Y).Dot(direction) >= fieldThreshold;
	}

	private float GetVisionStrength(Vector2 origin, Vector2 target)
	{
		float distance = origin.DistanceTo(target);
		float radius = Mathf.Max(1.0f, GetFieldOfViewRadius());
		float normalizedDistance = Mathf.Clamp(distance / radius, 0.0f, 1.0f);
		float rangeStrength = normalizedDistance <= 0.50f
			? 1.0f
			: 1.0f - Mathf.SmoothStep(0.0f, 1.0f, (normalizedDistance - 0.50f) * 2.0f);

		Vector2 toTarget = origin.DirectionTo(target);
		if (toTarget.LengthSquared() <= Mathf.Epsilon)
			return rangeStrength;
		float viewAngle = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp((-GlobalTransform.Y).Dot(toTarget), -1.0f, 1.0f)));
		const float directHalfAngle = 20.0f;
		const float centralHalfAngle = 45.0f;
		const float peripheralHalfAngle = 100.0f;
		float directionStrength;
		if (viewAngle <= directHalfAngle)
		{
			directionStrength = 1.0f;
		}
		else if (viewAngle <= centralHalfAngle)
		{
			float blend = (viewAngle - directHalfAngle) / (centralHalfAngle - directHalfAngle);
			directionStrength = Mathf.Lerp(1.0f, 0.65f, Mathf.SmoothStep(0.0f, 1.0f, blend));
		}
		else
		{
			float blend = Mathf.Clamp((viewAngle - centralHalfAngle) / (peripheralHalfAngle - centralHalfAngle), 0.0f, 1.0f);
			directionStrength = Mathf.Lerp(0.65f, 0.12f, Mathf.SmoothStep(0.0f, 1.0f, blend));
		}
		return rangeStrength * directionStrength;
	}

	private void RefreshManeuver()
	{
		_isCharging = _random.Randf() < 0.08f;
		_isDodging = !_isCharging && _random.Randf() < 0.20f;
		// Combat intent is deliberately long-lived. A target lock means the ship
		// pursues and kills; it does not constantly re-roll its entire flight plan.
		_maneuverSeconds = _isCharging ? _random.RandfRange(2.20f, 3.10f) : _isDodging ? _random.RandfRange(1.70f, 2.40f) : _random.RandfRange(2.60f, 3.80f);
		GetCombatRangeBand(out float minimumRange, out float maximumRange);
		_desiredRange = _random.RandfRange(
			Mathf.Lerp(minimumRange, maximumRange, 0.35f),
			Mathf.Lerp(minimumRange, maximumRange, 0.65f));
		if (_orbitDirection == 0)
			_orbitDirection = _random.Randf() < 0.5f ? -1 : 1;
	}

	public void TakeDamage(float amount, Node? source = null)
	{
		if (_destroyed || amount <= 0.0f)
			return;
		_secondsSinceShieldHit = 0.0f;
		float remainingDamage = amount;
		if (source is BulletController && _shield > 0.0f)
		{
			float shieldDamage = Mathf.Min(_shield, remainingDamage);
			_shield -= shieldDamage;
			remainingDamage -= shieldDamage;
		}
		_health = Mathf.Max(0.0f, _health - remainingDamage);
		if (source is BulletController)
		{
			FlashHit(Colors.White);
			// Keep a short cue alive even if an off-screen impact cannot become an
			// AI lock; the controller's release cushion handles the musical tail.
			_playerHitCombatCueSeconds = 0.25f;
			AlertFromPlayerFire();
			SignalNearbyAllies();
			RegisterPlayerHit();
		}
		else if (source is PlayerController)
		{
			FlashHit(Colors.White);
		}
		if (_health <= 0.0f)
		{
			_destroyed = true;
			SoundEffectsController.PlayOneShot(this, SoundEffectsController.Effect.ShipExplode);
			EnemyShipExplosionController.Spawn(this, GlobalPosition, GlobalRotation);
			// Enemy ships always award a random 9-15 XP, while repair pellets keep
			// their existing independent chance and healing behavior.
			int xpMinimum = Mathf.Max(9, XpDropMin);
			int xpMaximum = Mathf.Max(xpMinimum, XpDropMax);
			XpDropController.Spawn(this, GlobalPosition, _random.RandiRange(xpMinimum, xpMaximum));
			if (_random.Randf() <= RepairPelletDropChance)
			{
				int minimum = Mathf.Max(1, RepairPelletMin);
				int maximum = Mathf.Max(minimum, RepairPelletMax);
				ResourceDropController.Spawn(this, GlobalPosition, _random.RandiRange(minimum, maximum));
			}
			QueueFree();
		}
	}

	private void UpdateShieldRegeneration(float delta)
	{
		if (_shield >= MaxShield || MaxShield <= 0.0f)
			return;
		_secondsSinceShieldHit += delta;
		if (_secondsSinceShieldHit < ShieldRegenDelaySeconds)
			return;

		float regenerationPerSecond = MaxShield / Mathf.Max(0.1f, ShieldRegenDurationSeconds);
		_shield = Mathf.Min(MaxShield, _shield + regenerationPerSecond * delta);
	}

	public void ResolvePlayerCollision(PlayerController player)
	{
		if (_destroyed || player.IsDead)
			return;

		// MoveAndSlide already provides solid no-pass-through contact. Do not zero
		// either velocity here: stopping a moving body on contact is unlike space
		// flight and was the cause of ships visibly freezing in place. CharacterBody2D
		// recovery keeps the hulls apart without adding an impulse.

		if (_playerCollisionDamageCooldown > 0.0f)
			return;
		_playerCollisionDamageCooldown = 0.60f;
		TakeDamage(1.0f, player);
		player.TakeDamage(1.0f, bypassShield: true);
	}

	private void RegisterPlayerHit()
	{
		_recentPlayerHits++;
		_recentHitWindowSeconds = 1.2f;
		if (!_hasTargetLock || _tacticalDisengageSeconds > 0.0f)
			return;

		bool lowHealth = _health <= Mathf.Max(1.0f, MaxHealth * RetreatHealthFraction);
		bool sustainedFire = _recentPlayerHits >= Mathf.Max(1, SustainedHitsToDisengage);
		if (!lowHealth && !sustainedFire)
			return;

		// Most ships hold their course. Under real pressure, a minority commits to
		// one readable lateral boost—not an indecisive series of reversals.
		if (_random.Randf() > 0.40f)
			return;

		// Retain the earned lock throughout a tactical reposition. Losing it here
		// previously forced the ship through another 3–5 second acquisition delay
		// immediately after a volley, which read as a random turn-and-flee.
		_burstShotsRemaining = 0;
		_ambushResponseSeconds = 0.0f;
		_tacticalEscape = false;
		_tacticalSeekingCover = false;
		_tacticalDirection = _orbitDirection == 0 ? 1 : _orbitDirection;
		_tacticalDisengageSeconds = _random.RandfRange(1.15f, 1.85f);
		StartEvasiveBoost(_tacticalDisengageSeconds);
	}

	private bool TryChooseCoverPosition(Vector2 playerPosition)
	{
		BreakableEntityController? bestCover = null;
		float bestScore = float.MaxValue;
		Vector2 playerToShip = playerPosition.DirectionTo(GlobalPosition);
		float playerToShipDistance = playerPosition.DistanceTo(GlobalPosition);
		foreach (Node node in GetTree().GetNodesInGroup("breakable"))
		{
			if (node is not BreakableEntityController asteroid || !IsInstanceValid(asteroid))
				continue;
			float shipDistance = GlobalPosition.DistanceTo(asteroid.GlobalPosition);
			if (shipDistance < 55.0f || shipDistance > 520.0f)
				continue;
			Vector2 fromPlayer = asteroid.GlobalPosition - playerPosition;
			float projection = fromPlayer.Dot(playerToShip);
			if (projection < 35.0f || projection > playerToShipDistance + 70.0f)
				continue;
			Vector2 closestLinePoint = playerPosition + playerToShip * projection;
			float lateralDistance = asteroid.GlobalPosition.DistanceTo(closestLinePoint);
			float score = lateralDistance * 2.0f + shipDistance;
			if (score < bestScore)
			{
				bestScore = score;
				bestCover = asteroid;
			}
		}
		if (bestCover == null)
			return false;

		Vector2 awayFromPlayer = playerPosition.DirectionTo(bestCover.GlobalPosition);
		_coverPosition = bestCover.GlobalPosition + awayFromPlayer * 48.0f;
		return true;
	}

	private void AlertFromPlayerFire()
	{
		if (!_hasTargetLock && !CanAcquireInitialTarget())
			return;

		bool caughtOffGuard = !_hasTargetLock;
		// A direct hit is an intentional player action, so it can wake the struck
		// ship immediately. Passive line-of-sight acquisition still needs its
		// full 3â€“5 second build-up below.
		BeginPlayerAlert(true, confirmedPlayerHit: true);
		// A pair is not a guaranteed instant alarm network. A surprised ship only
		// has a small chance to warn its partner, and that partner still has to be
		// on camera before it can establish its first lock.
		if (caughtOffGuard
			&& _random.Randf() <= 0.20f
			&& IsInstanceValid(_linkedAlly)
			&& GlobalPosition.DistanceTo(_linkedAlly!.GlobalPosition) <= LinkedAllyAlertRange)
			_linkedAlly.AlertFromLinkedAlly();
	}

	private void SignalNearbyAllies()
	{
		if (_random.Randf() > Mathf.Clamp(NearbyAllyAlertChance, 0.0f, 1.0f))
			return;

		bool requestAssist = _random.Randf() <= Mathf.Clamp(AllyAssistChance, 0.0f, 1.0f);
		float alertRange = Mathf.Max(0.0f, NearbyAllyAlertRange);
		foreach (Node node in GetTree().GetNodesInGroup("enemy_ship"))
		{
			if (node is not EnemyShipController ally
				|| ally == this
				|| ally == _linkedAlly
				|| !IsInstanceValid(ally)
				|| GlobalPosition.DistanceTo(ally.GlobalPosition) > alertRange)
				continue;

			ally.ReceiveAllyAlert(requestAssist);
		}
	}

	private void EndCombatEngagement()
	{
		_hasTargetLock = false;
		_initialLockSightSeconds = 0.0f;
		_obstructionBurstTarget = null;
		_playerFleeSeconds = 0.0f;
		RollInitialLockDelay();
		RollFleeBreakLockDelay();
		_lostSightSeconds = 0.0f;
		_playerHitCombatCueSeconds = 0.0f;
	}

	private void AlertFromLinkedAlly()
	{
		// The caller already applied the 20% paired-wingmate chance. Receiving a
		// warning only moves this ship into a readied approach; it never bypasses
		// the first on-screen, clear-sight lock rule.
		ReceiveAllyAlert(_random.Randf() <= Mathf.Clamp(AllyAssistChance, 0.0f, 1.0f));
	}

	private void ReceiveAllyAlert(bool requestAssist)
	{
		// Signals are useful tactical information, not telepathy. A nearby ship
		// only moves from patrol toward standby/assist if it has already appeared
		// on camera and can see the player through the asteroid field.
		if (_destroyed || _hasTargetLock || !CanAcquireInitialTarget()
			|| _player is not PlayerController player
			|| player.IsDead
			|| !CanSeePlayer(player.GlobalPosition))
			return;

		float readiness = requestAssist ? 0.75f : 0.45f;
		_initialLockSightSeconds = Mathf.Max(
			_initialLockSightSeconds,
			_requiredInitialLockSightSeconds * readiness);
		// An alerted reserve quickly picks a new approach instead of mindlessly
		// continuing its previous asteroid patrol. Engagement slots still limit
		// the actual firing line to two ships.
		_patrolRetargetSeconds = 0.0f;
	}

	private bool CanAcquireInitialTarget()
	{
		MapGen? mapGenerator = GetTree().GetFirstNodeInGroup("map_generator") as MapGen;
		return !_destroyed
			&& _withinPhysicsWindow
			&& _hasBeenRevealed
			&& IsActuallyOnScreen()
			&& (mapGenerator == null || mapGenerator.CanEnemyShipAcquireTargetLock(this));
	}

	private void UpdateInitialLockAcquisition(Vector2 playerPosition, float delta)
	{
		if (!CanAcquireInitialTarget())
		{
			_initialLockSightSeconds = 0.0f;
			return;
		}

		GetPlayerSightRayCoverage(playerPosition, out int clearRayCount, out int testedRayCount, out bool centerRayClear);
		if (!centerRayClear || clearRayCount < 6 || testedRayCount <= 0)
		{
			// The windshield's direct sight is interrupted. A ship has to reacquire
			// rather than completing a lock through an asteroid sliver.
			_initialLockSightSeconds = 0.0f;
			return;
		}

		// Peripheral vision can begin an ordinary lock-on, while central vision
		// accelerates it sharply. CanSeePlayer above is an eleven-ray asteroid
		// occlusion test. Partially blocked player rays now explicitly slow the
		// very same lock meter that drives the green-to-orange debug feedback.
		float visionStrength = GetVisionStrength(GetWindshieldRayOrigin(), playerPosition);
		float unobscuredFraction = clearRayCount / (float)testedRayCount;
		_lastInvestigationPosition = playerPosition;
		float closeRangeMultiplier = GetWindshieldRayOrigin().DistanceTo(playerPosition) <= GetFieldOfViewRadius() * 0.25f
			? Mathf.Max(1.0f, _requiredInitialLockSightSeconds / 0.5f)
			: 1.0f;
		// The closest quarter of a ship's visual cone is an immediate danger zone:
		// with clear sight it completes even a five-second ordinary lock in at most
		// half a second. Partial asteroid occlusion still slows it proportionally.
		_initialLockSightSeconds += delta * GetInitialLockProgressMultiplier(playerPosition) * visionStrength * unobscuredFraction * closeRangeMultiplier;
		if (_initialLockSightSeconds >= _requiredInitialLockSightSeconds)
			BeginPlayerAlert();
	}

	private float GetInitialLockProgressMultiplier(Vector2 playerPosition)
	{
		Vector2 toPlayer = GetWindshieldRayOrigin().DirectionTo(playerPosition);
		if (toPlayer.LengthSquared() <= Mathf.Epsilon)
			return 1.0f;

		Vector2 forward = -GlobalTransform.Y;
		float viewAngleFromCenter = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(forward.Dot(toPlayer), -1.0f, 1.0f)));
		float centralHalfAngle = Mathf.Clamp(CentralVisionConeDegrees, 0.0f, 200.0f) * 0.5f;
		if (viewAngleFromCenter > centralHalfAngle)
			return 1.0f;

		float directHalfAngle = Mathf.Min(
			centralHalfAngle,
			Mathf.Clamp(DirectLockConeDegrees, 0.0f, 90.0f) * 0.5f);
		if (viewAngleFromCenter <= directHalfAngle)
			return Mathf.Max(1.0f, MaximumDirectLockSpeedMultiplier);

		float blend = (centralHalfAngle - viewAngleFromCenter) / Mathf.Max(0.001f, centralHalfAngle - directHalfAngle);
		return Mathf.Lerp(
			Mathf.Max(1.0f, CentralVisionLockSpeedMultiplier),
			Mathf.Max(1.0f, MaximumDirectLockSpeedMultiplier),
			Mathf.SmoothStep(0.0f, 1.0f, blend));
	}

	private void RollInitialLockDelay()
	{
		float minimum = Mathf.Max(0.0f, InitialLockDelayMinimumSeconds);
		float maximum = Mathf.Max(minimum, InitialLockDelayMaximumSeconds);
		_requiredInitialLockSightSeconds = _random.RandfRange(minimum, maximum);
	}

	private void RollFleeBreakLockDelay()
	{
		float minimum = Mathf.Max(0.0f, FleeBreakLockMinimumSeconds);
		float maximum = Mathf.Max(minimum, FleeBreakLockMaximumSeconds);
		_requiredPlayerFleeSeconds = _random.RandfRange(minimum, maximum);
	}

	private bool IsPlayerFleeingFromThisShip(PlayerController player)
	{
		Vector2 velocity = player.Velocity;
		if (velocity.LengthSquared() < 25.0f)
			return false;
		Vector2 awayFromShip = GlobalPosition.DirectionTo(player.GlobalPosition);
		return awayFromShip.LengthSquared() > Mathf.Epsilon
			&& velocity.Normalized().Dot(awayFromShip) >= 0.55f;
	}

	private void BeginPlayerAlert(bool bypassInitialDelay = false, bool confirmedPlayerHit = false)
	{
		// Keep this guard in the one method that creates a lock so future AI
		// reactions cannot accidentally reintroduce off-screen first engagement.
		if (_hasTargetLock || !CanAcquireInitialTarget())
			return;
		if (!bypassInitialDelay && _initialLockSightSeconds < _requiredInitialLockSightSeconds)
			return;

		_hasTargetLock = true;
		_initialLockSightSeconds = 0.0f;
		_playerFleeSeconds = 0.0f;
		RollFleeBreakLockDelay();
		_lostSightSeconds = 0.0f;
		_engagementPriority = ++_nextEngagementPriority;
		_burstShotsRemaining = 0;
		// A ship that was actually hit should react promptly, but it still has to
		// fly a readable arc and bring its guns around before it can return fire.
		// That avoids both the old multi-second hesitation and an unnatural 180
		// degree snap-turn when the player catches it from behind.
		_volleyCooldownSeconds = confirmedPlayerHit ? 0.25f : 1.0f;
		if (confirmedPlayerHit)
		{
			_ambushStrafe = true;
			Vector2 toPlayer = GlobalPosition.DirectionTo(_player?.GlobalPosition ?? GlobalPosition + -GlobalTransform.Y);
			Vector2 shipForward = -GlobalTransform.Y;
			float turnSide = shipForward.Cross(toPlayer);
			_ambushStrafeDirection = Mathf.Abs(turnSide) <= 0.08f
				? (_random.Randf() < 0.5f ? -1 : 1)
				: (turnSide >= 0.0f ? 1 : -1);
			_ambushResponseSeconds = _random.RandfRange(0.38f, 0.62f);
			StartEvasiveBoost(_ambushResponseSeconds);
		}
		else
		{
			_ambushStrafe = _random.Randf() < 0.5f;
			_ambushStrafeDirection = _random.Randf() < 0.5f ? -1 : 1;
			_ambushResponseSeconds = _random.RandfRange(0.45f, 0.85f);
		}
		// The next normal maneuver is independently selected after the reaction.
		RefreshManeuver();
	}

	private void FlashHit(Color color)
	{
		if (_hitFlashMaterial == null)
			return;

		_hitFlashSeconds = HitFlashDuration;
		_hitFlashMaterial.SetShaderParameter("flash_color", color);
		_hitFlashMaterial.SetShaderParameter("flash_amount", 1.0f);
	}
}
