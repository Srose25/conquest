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
	[Export] public float DesiredEngagementRange { get; set; } = 300.0f;
	[Export] public float CoastStopSeconds { get; set; } = 10.0f;
	[Export] public float DirectionChangeSeconds { get; set; } = 0.9f;
	[Export] public float AccelerationSeconds { get; set; } = 1.5f;
	[Export] public float BoltSpeed { get; set; } = 470.0f;
	[Export] public float BoltDamage { get; set; } = 1.0f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float RetreatHealthFraction { get; set; } = 0.50f;
	[Export] public int SustainedHitsToDisengage { get; set; } = 4;
	[Export(PropertyHint.Range, "0,1,0.05")] public float RepairPelletDropChance { get; set; } = 0.35f;
	[Export] public int RepairPelletMin { get; set; } = 1;
	[Export] public int RepairPelletMax { get; set; } = 2;
	[Export] public float LinkedAllyAlertRange { get; set; } = 520.0f;
	[Export] public PackedScene? EnemyBoltScene { get; set; }
	[Export] public Vector2 LeftGunOffset { get; set; } = new(-8.0f, -17.0f);
	[Export] public Vector2 RightGunOffset { get; set; } = new(8.0f, -17.0f);
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
	private bool _destroyed;
	private bool _fireLeftGun = true;
	private int _burstShotsRemaining;
	private float _nextShotSeconds;
	private float _volleyCooldownSeconds;
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
	private float _collisionRecoverySeconds;
	private Vector2 _collisionRecoveryDirection;
	private float _playerCollisionDamageCooldown;
	private ShaderMaterial? _hitFlashMaterial;
	private float _hitFlashSeconds;
	private const float HitFlashDuration = 0.12f;
	private const float EnemyHealthBarRange = 250.0f;
	private const float EnemyHealthBarFadeSeconds = 0.18f;
	private const float EnemyHealthBarDamageAnimationSeconds = 0.25f;
	private static readonly float[] SightRayOffsets = { 0.0f, -8.0f, 8.0f, -16.0f, 16.0f };
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
		_player ??= GetTree().GetFirstNodeInGroup("player") as Node2D;
		bool showHealthBar = _player is PlayerController player
			&& !player.IsDead
			&& GlobalPosition.DistanceTo(player.GlobalPosition) <= EnemyHealthBarRange;
		float visibilitySpeed = step / EnemyHealthBarFadeSeconds;
		_healthBarVisibility = Mathf.MoveToward(_healthBarVisibility, showHealthBar ? 1.0f : 0.0f, visibilitySpeed);
		UpdateHealthBarDamageAnimation(step);
		if (_healthBarVisibility > 0.0f || _healthBarDamageAnimationElapsed >= 0.0f)
			QueueRedraw();

		if (_hitFlashSeconds > 0.0f && _hitFlashMaterial != null)
		{
			_hitFlashSeconds = Mathf.Max(0.0f, _hitFlashSeconds - step);
			_hitFlashMaterial.SetShaderParameter("flash_amount", _hitFlashSeconds / HitFlashDuration);
		}
	}

	public override void _Draw()
	{
		if (_healthBarVisibility <= 0.0f)
			return;

		// Keep the bar beneath the ship in world-space down, rather than rotating
		// it with the hull as the ship turns.
		Vector2 belowShip = Vector2.Down.Rotated(-Rotation) * 30.0f;
		DrawSetTransform(belowShip, -Rotation);
		const float width = 38.0f;
		const float height = 6.0f;
		const float border = 1.0f;
		int segmentCount = Mathf.Max(1, Mathf.CeilToInt(MaxHealth));
		int filledSegments = Mathf.Clamp(Mathf.CeilToInt(_health), 0, segmentCount);
		float divider = 0.5f;
		float segmentWidth = (width - border * 2.0f - divider * (segmentCount - 1)) / segmentCount;
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
			Velocity = Vector2.Zero;
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
		_hasTargetLock = false;
		_burstShotsRemaining = 0;
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
			_hasTargetLock = false;
			_burstShotsRemaining = 0;
			if (!player.IsDead)
				PatrolAsteroids(step);
			else
				MoveWithCollision(Vector2.Zero, 0.0f, step);
			return;
		}
		if (player.IsDead)
		{
			if (_hasTargetLock)
				HandlePlayerDeath(player.GlobalPosition, step);
			else
				MoveWithCollision(Vector2.Zero, 0.0f, step);
			return;
		}
		if (ProcessTacticalDisengage(player.GlobalPosition, step))
			return;
		// First contact is a permanent camera-only rule. A ship may patrol outside
		// the view, but it never obtains its first combat lock there.
		if (!_hasTargetLock && CanAcquireInitialTarget() && CanSeePlayer(GlobalPosition, player.GlobalPosition))
			BeginPlayerAlert();
		if (!_hasTargetLock)
		{
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
			return;

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
		Vector2 desiredDirection;
		if (_tacticalEscape)
		{
			desiredDirection = (-toPlayer + tangent * 0.28f).Normalized();
		}
		else
		{
			// A tactical ship may hide on the far side of a suitable asteroid. If no
			// cover was selected, it instead uses a wide 180-degree flank point.
			Vector2 tacticalPoint = _tacticalSeekingCover
				? _coverPosition
				: playerPosition + _tacticalFlankDirection * _tacticalFlankRange;
			desiredDirection = GlobalPosition.DirectionTo(tacticalPoint);
			if (desiredDirection.LengthSquared() <= Mathf.Epsilon)
				desiredDirection = tangent;
		}
		float targetRotation = desiredDirection.Angle() + Mathf.Pi / 2.0f;
		Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * 1.35f * delta);
		MoveWithCollision(desiredDirection, MoveSpeed * (_tacticalEscape ? 1.12f : 1.04f), delta);
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
			Vector2 newApproachAngle = (tangent * 0.88f + toPlayer * 0.12f).Normalized();
			float targetRotation = newApproachAngle.Angle() + Mathf.Pi / 2.0f;
			Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * 1.7f * delta);
			MoveWithCollision(newApproachAngle, MoveSpeed * 0.82f, delta);
		}
		else
		{
			// A direct hit from behind can provoke a quick, readable turn, while
			// RotateToward keeps the hull rotation continuous rather than instant.
			float targetRotation = toPlayer.Angle() + Mathf.Pi / 2.0f;
			Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * 2.2f * delta);
			MoveWithCollision(toPlayer, MoveSpeed * 0.72f, delta);
		}
		return true;
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
		float targetRotation = toPlayer.Angle() + Mathf.Pi / 2.0f;
		Rotation = Mathf.RotateToward(Rotation, targetRotation, Mathf.DegToRad(TurnSpeedDegrees) * delta);
		float distance = GlobalPosition.DistanceTo(playerPosition);
		Vector2 tangent = new Vector2(-toPlayer.Y, toPlayer.X) * _orbitDirection;
		Vector2 desiredDirection = _isCharging && distance > 85.0f
			? toPlayer
			: _isDodging
				? (tangent * 0.92f + toPlayer * 0.08f).Normalized()
			: distance > _desiredRange + 65.0f
				? (toPlayer + tangent * 0.28f).Normalized()
				: distance < _desiredRange - 65.0f
					? (-toPlayer + tangent * 0.52f).Normalized()
					: (tangent * 0.78f + toPlayer * 0.22f).Normalized();

		float maneuverSpeed = _isCharging ? MoveSpeed * 1.15f : MoveSpeed;
		MoveWithCollision(desiredDirection, maneuverSpeed, delta);
	}

	private bool HasEngagementSlot()
	{
		List<EnemyShipController> engagedShips = new();
		foreach (Node node in GetTree().GetNodesInGroup("enemy_ship"))
		{
			if (node is EnemyShipController ship
				&& IsInstanceValid(ship)
				&& !ship._destroyed
				&& ship._withinPhysicsWindow
				&& ship._hasTargetLock)
			{
				engagedShips.Add(ship);
			}
		}

		engagedShips.Sort((left, right) => left.GetEngagementScore().CompareTo(right.GetEngagementScore()));
		int slotCount = Mathf.Min(MaxConcurrentAttackers, engagedShips.Count);
		for (int index = 0; index < slotCount; index++)
		{
			if (engagedShips[index] == this)
				return true;
		}
		return false;
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
		Vector2 velocityBeforeCollision = Velocity;
		Vector2 positionBeforeCollision = GlobalPosition;
		if (desiredDirection.LengthSquared() <= Mathf.Epsilon || speed <= 0.0f)
		{
			float coastDeceleration = MoveSpeed / Mathf.Max(0.1f, CoastStopSeconds);
			Velocity = Velocity.MoveToward(Vector2.Zero, coastDeceleration * delta);
		}
		else
		{
			desiredDirection = SteerAroundPlayer(desiredDirection);
			desiredDirection = EvadePlayerBullets(desiredDirection);
			if (_collisionRecoverySeconds > 0.0f)
			{
				_collisionRecoverySeconds -= delta;
				Vector2 tangent = new Vector2(-_collisionRecoveryDirection.Y, _collisionRecoveryDirection.X) * _asteroidAvoidanceDirection;
				desiredDirection = (_collisionRecoveryDirection * 0.78f + tangent * 0.62f).Normalized();
			}
			else
			{
				desiredDirection = SteerAroundAsteroids(desiredDirection, speed, delta);
			}
			Vector2 currentHeading = Velocity.LengthSquared() > 0.01f ? Velocity.Normalized() : desiredDirection;
			Vector2 shipForward = -GlobalTransform.Y;
			// Ships can strafe or reverse, but their acceleration retains a deliberate
			// preference for where the hull is facing and for their existing momentum.
			float forwardAlignment = (shipForward.Dot(desiredDirection) + 1.0f) * 0.5f;
			desiredDirection = desiredDirection.Lerp(currentHeading, 0.18f).Lerp(shipForward, 0.52f).Normalized();
			float driveSpeed = speed * Mathf.Lerp(0.62f, 1.22f, forwardAlignment);
			Vector2 targetVelocity = desiredDirection * driveSpeed;
			bool reversing = Velocity.LengthSquared() > 0.01f && Velocity.Dot(targetVelocity) < 0.0f;
			float acceleration = MoveSpeed / Mathf.Max(0.1f, reversing ? DirectionChangeSeconds : AccelerationSeconds);
			Velocity = Velocity.MoveToward(targetVelocity, acceleration * delta);
		}
		MoveAndSlide();
		HandleAsteroidContacts(velocityBeforeCollision, positionBeforeCollision, delta);
	}

	private Vector2 SteerAroundPlayer(Vector2 desiredDirection)
	{
		if (_player is not PlayerController player || player.IsDead)
			return desiredDirection;

		float distance = GlobalPosition.DistanceTo(player.GlobalPosition);
		const float avoidanceRange = 88.0f;
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
			side = _orbitDirection;
		Vector2 tangent = new Vector2(-toPlayer.Y, toPlayer.X) * Mathf.Sign(side);
		return (desiredDirection * (1.0f - proximity * 0.80f) + awayFromPlayer * proximity * 0.90f + tangent * proximity * 0.72f).Normalized();
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

			float side = bullet.Direction.Cross(relative);
			if (Mathf.Abs(side) <= 0.01f)
				side = _random.Randf() < 0.5f ? -1.0f : 1.0f;
			else
				side = Mathf.Sign(side);
			Vector2 dodgeDirection = new Vector2(-bullet.Direction.Y, bullet.Direction.X) * side;
			return (desiredDirection * 0.30f + dodgeDirection * 0.90f).Normalized();
		}
		return desiredDirection;
	}

	private Vector2 SteerAroundAsteroids(Vector2 desiredDirection, float speed, float delta)
	{
		Vector2 probeDirection = desiredDirection.Normalized();
		float probeDistance = Mathf.Lerp(56.0f, 96.0f, Mathf.Clamp(speed / Mathf.Max(1.0f, MoveSpeed), 0.0f, 1.25f));
		PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(
			GlobalPosition,
			GlobalPosition + probeDirection * probeDistance,
			(uint)(1 << 1));
		Godot.Collections.Dictionary hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
		if (hit.Count == 0 || !hit.TryGetValue("position", out Variant hitPositionVariant))
		{
			_asteroidAvoidanceSeconds = Mathf.Max(0.0f, _asteroidAvoidanceSeconds - delta);
			return desiredDirection;
		}

		Vector2 hitPosition = hitPositionVariant.AsVector2();
		Vector2 awayFromAsteroid = hitPosition.DirectionTo(GlobalPosition);
		if (awayFromAsteroid.LengthSquared() <= Mathf.Epsilon)
			awayFromAsteroid = -probeDirection;
		float sideSign = probeDirection.Cross(awayFromAsteroid);
		if (_asteroidAvoidanceSeconds <= 0.0f)
		{
			_asteroidAvoidanceSeconds = _random.RandfRange(0.35f, 0.70f);
			_avoidAsteroidByTurning = _random.Randf() < 0.5f;
			_asteroidAvoidanceDirection = Mathf.Abs(sideSign) <= 0.05f
				? _orbitDirection
				: Mathf.Sign(sideSign) > 0.0f ? 1 : -1;
		}
		Vector2 tangent = new Vector2(-awayFromAsteroid.Y, awayFromAsteroid.X) * _asteroidAvoidanceDirection;
		if (_avoidAsteroidByTurning)
		{
			// Turn forward around the obstacle, preserving momentum and hull direction.
			return (probeDirection * 0.62f + tangent * 0.72f + awayFromAsteroid * 0.32f).Normalized();
		}
		// Or make a more pronounced lateral strafe to find a clear firing angle.
		return (probeDirection * 0.22f + tangent * 0.88f + awayFromAsteroid * 0.68f).Normalized();
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
			float speed = velocityBeforeCollision.Length();
			if (speed > 0.01f)
				asteroid.ApplyPush(GlobalPosition.DirectionTo(asteroid.GlobalPosition), speed);
		}
		if (!hitAsteroid)
			return;

		if (recoveryDirection.LengthSquared() <= Mathf.Epsilon)
			recoveryDirection = -GlobalTransform.Y;
		_collisionRecoveryDirection = recoveryDirection;
		_collisionRecoverySeconds = 0.55f;
		_asteroidAvoidanceDirection = _random.Randf() < 0.5f ? -1 : 1;
		Vector2 recoveryTangent = new Vector2(-recoveryDirection.Y, recoveryDirection.X) * _asteroidAvoidanceDirection;
		Velocity = (recoveryDirection * MoveSpeed * 0.34f + recoveryTangent * MoveSpeed * 0.16f).LimitLength(MoveSpeed * 0.45f);
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
			if (_nextShotSeconds <= 0.0f)
			{
				FireBolt(playerPosition);
				_burstShotsRemaining--;
				_nextShotSeconds = 0.12f;
				if (_burstShotsRemaining == 0)
					_volleyCooldownSeconds = 1.0f;
			}
			return;
		}

		_volleyCooldownSeconds -= delta;
		if (_volleyCooldownSeconds <= 0.0f)
		{
			_burstShotsRemaining = 6;
			_nextShotSeconds = 0.0f;
		}
	}

	private void FireBolt(Vector2 playerPosition)
	{
		if (EnemyBoltScene == null)
			return;
		Vector2 spawnPosition = ToGlobal(_fireLeftGun ? LeftGunOffset : RightGunOffset);
		if (!HasClearShot(spawnPosition, playerPosition))
			return;
		_fireLeftGun = !_fireLeftGun;
		EnemyBoltController bolt = EnemyBoltScene.Instantiate<EnemyBoltController>();
		bolt.Initialize(spawnPosition.DirectionTo(playerPosition), BoltSpeed, BoltDamage, this);
		bolt.GlobalPosition = spawnPosition;
		Node parent = GetTree().CurrentScene ?? GetParent();
		parent.AddChild(bolt);
	}

	private bool HasClearShot(Vector2 from, Vector2 to)
	{
		if (!CanSeePlayer(from, to))
			return false;
		Vector2 playerDirection = from.DirectionTo(to);
		Vector2 forward = -GlobalTransform.Y;
		float firingDot = Mathf.Cos(Mathf.DegToRad(FiringConeDegrees * 0.5f));
		if (forward.Dot(playerDirection) < firingDot)
			return false;
		return true;
	}

	private bool CanSeePlayer(Vector2 from, Vector2 to)
	{
		Vector2 playerDirection = from.DirectionTo(to);
		Vector2 forward = -GlobalTransform.Y;
		if (forward.Dot(playerDirection) < Mathf.Cos(Mathf.DegToRad(100.0f)))
			return false;

		// Looking through a pinhole between asteroids is not a meaningful visual
		// lock. Test the player across most of its collision width: the center ray
		// and at least two more rays must remain clear.
		Vector2 lateral = new(-playerDirection.Y, playerDirection.X);
		int clearRayCount = 0;
		bool centerRayClear = false;
		foreach (float offset in SightRayOffsets)
		{
			PhysicsRayQueryParameters2D query = PhysicsRayQueryParameters2D.Create(
				from,
				to + lateral * offset,
				(uint)(1 << 1));
			Godot.Collections.Dictionary hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
			if (hit.Count != 0)
				continue;
			clearRayCount++;
			if (Mathf.IsZeroApprox(offset))
				centerRayClear = true;
		}
		return centerRayClear && clearRayCount >= 3;
	}

	private void RefreshManeuver()
	{
		_isCharging = _random.Randf() < 0.15f;
		_isDodging = !_isCharging && _random.Randf() < 0.60f;
		_maneuverSeconds = _isCharging ? _random.RandfRange(0.55f, 1.00f) : _isDodging ? _random.RandfRange(0.55f, 1.20f) : _random.RandfRange(0.80f, 1.80f);
		_desiredRange = DesiredEngagementRange + _random.RandfRange(-55.0f, 55.0f);
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
			AlertFromPlayerFire();
			RegisterPlayerHit();
		}
		else if (source is PlayerController)
		{
			FlashHit(Colors.White);
		}
		if (_health <= 0.0f)
		{
			_destroyed = true;
			EnemyShipExplosionController.Spawn(this, GlobalPosition, GlobalRotation);
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

		// MoveAndSlide already provides the solid no-pass-through contact. Do not
		// alter either body's position here: doing so mid-physics step can make the
		// engine's next collision recovery look like a launch.
		player.Velocity = Vector2.Zero;
		Velocity = Vector2.Zero;

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

		// Even under sustained pressure, a ship only sometimes breaks off; most
		// will keep fighting rather than becoming predictable cowards.
		if (_random.Randf() > 0.40f)
			return;

		// Drop the combat lock deliberately: this prevents firing while the ship
		// turns away to escape or choose a new approach angle.
		_hasTargetLock = false;
		_burstShotsRemaining = 0;
		_ambushResponseSeconds = 0.0f;
		// A tactical disengage usually means a lateral dodge and a new angle;
		// fully running away is the rarer 10% response.
		_tacticalEscape = _random.Randf() < 0.10f;
		_tacticalDirection = _random.Randf() < 0.5f ? -1 : 1;
		Vector2 fromPlayer = -GlobalPosition.DirectionTo(_player?.GlobalPosition ?? GlobalPosition + Vector2.Up);
		if (fromPlayer.LengthSquared() <= Mathf.Epsilon)
			fromPlayer = -GlobalTransform.Y;
		_tacticalFlankDirection = fromPlayer.Rotated(_random.RandfRange(-Mathf.Pi * 0.5f, Mathf.Pi * 0.5f)).Normalized();
		_tacticalFlankRange = _random.RandfRange(190.0f, 330.0f);
		_tacticalSeekingCover = !_tacticalEscape && _random.Randf() < 0.35f && TryChooseCoverPosition(_player?.GlobalPosition ?? GlobalPosition);
		_tacticalDisengageSeconds = _tacticalEscape
			? _random.RandfRange(1.35f, 2.30f)
			: _random.RandfRange(1.80f, 3.40f);
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
		BeginPlayerAlert();
		// A pair is not a guaranteed instant alarm network. A surprised ship only
		// has a small chance to warn its partner, and that partner still has to be
		// on camera before it can establish its first lock.
		if (caughtOffGuard
			&& _random.Randf() <= 0.20f
			&& IsInstanceValid(_linkedAlly)
			&& GlobalPosition.DistanceTo(_linkedAlly!.GlobalPosition) <= LinkedAllyAlertRange)
			_linkedAlly.AlertFromLinkedAlly();
	}

	private void AlertFromLinkedAlly()
	{
		if (!_destroyed && (_hasTargetLock || CanAcquireInitialTarget()))
			BeginPlayerAlert();
	}

	private bool CanAcquireInitialTarget()
	{
		return !_destroyed
			&& _withinPhysicsWindow
			&& _hasBeenRevealed
			&& IsActuallyOnScreen();
	}

	private void BeginPlayerAlert()
	{
		// Keep this guard in the one method that creates a lock so future AI
		// reactions cannot accidentally reintroduce off-screen first engagement.
		if (_hasTargetLock || !CanAcquireInitialTarget())
			return;

		_hasTargetLock = true;
		_engagementPriority = ++_nextEngagementPriority;
		_burstShotsRemaining = 0;
		_volleyCooldownSeconds = 1.0f;
		_ambushStrafe = _random.Randf() < 0.5f;
		_ambushStrafeDirection = _random.Randf() < 0.5f ? -1 : 1;
		_ambushResponseSeconds = _random.RandfRange(0.45f, 0.85f);
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
