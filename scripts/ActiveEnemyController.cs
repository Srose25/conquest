#nullable enable
using Godot;

/// <summary>Chases the player, attacks at close range, and drops more resources when destroyed.</summary>
public partial class ActiveEnemyController : CharacterBody2D, IDamageable
{
	[Signal] public delegate void DamagedEventHandler(float amount, float remainingHealth);
	[Signal] public delegate void ResourcesDroppedEventHandler(int amount, Vector2 position);
	[Signal] public delegate void DestroyedEventHandler(Vector2 position);

	[Export] public int MinHitsToDestroy { get; set; } = 4;
	[Export] public int MaxHitsToDestroy { get; set; } = 5;
	[Export] public int ResourceYieldMin { get; set; } = 1;
	[Export] public int ResourceYieldMax { get; set; } = 2;
	[Export(PropertyHint.Range, "0,1,0.05")] public float PelletDropChance { get; set; } = 0.20f;
	[Export] public int XpDropAmount { get; set; } = 5;
	[Export] public int BreakPixelCountMin { get; set; } = 7;
	[Export] public int BreakPixelCountMax { get; set; } = 9;
	[Export] public float MoveSpeed { get; set; } = 85.0f;
	[Export] public float TrackingActivationRange { get; set; } = 250.0f;
	[Export] public float AttackRange { get; set; } = 48.0f;
	[Export] public float AttackDamage { get; set; } = 2.0f;

	private float _health;
	private Node2D? _player;
	private Timer _attackTimer = null!;
	private VisibleOnScreenNotifier2D _screenNotifier = null!;
	private Area2D _detectionArea = null!;
	private bool _hasBeenRevealed;
	private bool _hasActivatedTracking;
	private bool _withinPhysicsWindow = true;
	private bool _destroyed;
	private readonly RandomNumberGenerator _random = new();
	private ShaderMaterial? _hitFlashMaterial;
	private float _hitFlashSeconds;
	private const float HitFlashDuration = 0.12f;

	public bool IsEngagingPlayer => _hasActivatedTracking && _hasBeenRevealed && !_destroyed && _screenNotifier.IsOnScreen();

	public override void _Ready()
	{
		CollisionLayer = 1 << 2;
		// Trackers damage the ship through range checks, but collide with smaller asteroids and trackers.
		CollisionMask = (1 << 1) | (1 << 2);
		_random.Randomize();
		int minimumHits = Mathf.Max(1, MinHitsToDestroy);
		int maximumHits = Mathf.Max(minimumHits, MaxHitsToDestroy);
		_health = _random.RandiRange(minimumHits, maximumHits);
		Sprite2D sprite = GetNode<Sprite2D>("Sprite2D");
		if (sprite.Material is ShaderMaterial material)
		{
			_hitFlashMaterial = (ShaderMaterial)material.Duplicate();
			sprite.Material = _hitFlashMaterial;
			_hitFlashMaterial.SetShaderParameter("flash_amount", 0.0f);
		}
		_attackTimer = GetNode<Timer>("AttackTimer");
		_screenNotifier = GetNode<VisibleOnScreenNotifier2D>("VisibleOnScreenNotifier2D");
		_detectionArea = GetNode<Area2D>("DetectionArea");
		_detectionArea.BodyEntered += OnDetectionBodyEntered;
		_screenNotifier.ScreenEntered += OnTrackerScreenEntered;
		_screenNotifier.ScreenExited += OnTrackerScreenExited;
		_attackTimer.WaitTime = 1.0f;
		_attackTimer.OneShot = false;
		_attackTimer.Timeout += OnAttackTimerTimeout;
		_player = GetTree().GetFirstNodeInGroup("player") as Node2D;
		AddToGroup("tracker");
		Velocity = Vector2.Zero;
		SetTrackerCollisionEnabled(IsActuallyOnScreen());
	}

	public override void _Process(double delta)
	{
		if (_hitFlashSeconds <= 0.0f || _hitFlashMaterial == null)
			return;
		_hitFlashSeconds = Mathf.Max(0.0f, _hitFlashSeconds - (float)delta);
		_hitFlashMaterial.SetShaderParameter("flash_amount", _hitFlashSeconds / HitFlashDuration);
	}

	private void OnTrackerScreenEntered()
	{
		if (!_withinPhysicsWindow || !IsActuallyOnScreen())
			return;
		_hasBeenRevealed = true;
		SetTrackerCollisionEnabled(true);
		SetPhysicsProcess(true);
	}

	private void OnTrackerScreenExited()
	{
		Velocity = Vector2.Zero;
		SetTrackerCollisionEnabled(false);
	}

	private void SetTrackerCollisionEnabled(bool enabled)
	{
		CollisionLayer = enabled ? (uint)(1 << 2) : 0u;
		CollisionMask = enabled ? (uint)((1 << 1) | (1 << 2)) : 0u;
	}

	public void SetWithinPhysicsWindow(bool enabled)
	{
		if (_withinPhysicsWindow == enabled)
			return;
		_withinPhysicsWindow = enabled;
		if (!enabled)
		{
			Velocity = Vector2.Zero;
			SetTrackerCollisionEnabled(false);
			SetPhysicsProcess(false);
		}
		else
		{
			SetPhysicsProcess(true);
		}
	}

	private bool IsActuallyOnScreen()
	{
		Vector2 screenPosition = GetViewport().GetCanvasTransform() * GlobalPosition;
		return new Rect2(Vector2.Zero, GetViewportRect().Size).Grow(2.0f).HasPoint(screenPosition);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_withinPhysicsWindow)
			return;
		if (!_hasBeenRevealed)
		{
			if (_screenNotifier.IsOnScreen() && IsActuallyOnScreen())
				_hasBeenRevealed = true;
			else
			{
				Velocity = Vector2.Zero;
				_attackTimer.Stop();
				return;
			}
		}
		if (!_screenNotifier.IsOnScreen() || !IsActuallyOnScreen())
		{
			Velocity = Vector2.Zero;
			_attackTimer.Stop();
			return;
		}
		SetTrackerCollisionEnabled(true);

		_player ??= GetTree().GetFirstNodeInGroup("player") as Node2D;
		if (!IsInstanceValid(_player))
		{
			Velocity = Vector2.Zero;
			return;
		}
		// Being revealed only makes a tracker eligible. It remains inert until the
		// player actually comes within its 250-unit magnetism range; after that its
		// normal pursuit behavior continues for the life of the encounter.
		if (!_hasActivatedTracking)
		{
			if (GlobalPosition.DistanceTo(_player!.GlobalPosition) > TrackingActivationRange)
			{
				Velocity = Vector2.Zero;
				_attackTimer.Stop();
				return;
			}
			_hasActivatedTracking = true;
		}
		if (_player is PlayerController player && player.IsDead)
		{
			// Death does not freeze the encounter: revealed trackers keep their last
			// heading until they naturally leave the view and are deactivated.
			_attackTimer.Stop();
			if (Velocity.LengthSquared() > Mathf.Epsilon)
			{
				MoveAndSlide();
				DestroyTrackersCollidingAtPlayerPosition();
				Rotation = Velocity.Angle();
			}
			return;
		}

		float distance = GlobalPosition.DistanceTo(_player.GlobalPosition);
		if (distance > AttackRange)
		{
			Vector2 chaseDirection = GlobalPosition.DirectionTo(_player.GlobalPosition);
			Vector2 positionBeforeMove = GlobalPosition;
			float maxStep = MoveSpeed * (float)delta;
			Velocity = chaseDirection * MoveSpeed;
			Velocity = Velocity.LimitLength(Mathf.Max(0.0f, MoveSpeed));
			MoveAndSlide();
			if (GlobalPosition.DistanceTo(positionBeforeMove) > maxStep + 2.0f)
				GlobalPosition = positionBeforeMove + chaseDirection * maxStep;
			Velocity = chaseDirection * MoveSpeed;
			Velocity = Velocity.LimitLength(Mathf.Max(0.0f, MoveSpeed));
			PushAsteroidsFromCollisions();
		}
		else
		{
			AttackPlayer(_player);
		}
		Rotation = GlobalPosition.DirectionTo(_player.GlobalPosition).Angle();
	}

	public void TakeDamage(float amount, Node? source = null)
	{
		if (_destroyed || amount <= 0.0f)
			return;
		_health = Mathf.Max(0.0f, _health - amount);
		FlashHit();
		EmitSignal(SignalName.Damaged, amount, _health);
		if (_health <= 0.0f)
			Destroy(true);
	}

	private void FlashHit()
	{
		if (_hitFlashMaterial == null)
			return;
		_hitFlashSeconds = HitFlashDuration;
		_hitFlashMaterial.SetShaderParameter("flash_color", Colors.White);
		_hitFlashMaterial.SetShaderParameter("flash_amount", 1.0f);
	}

	private void OnAttackTimerTimeout()
	{
		if (_destroyed || !IsInstanceValid(_player) || GlobalPosition.DistanceTo(_player!.GlobalPosition) > AttackRange)
			return;
		AttackPlayer(_player);
	}

	private void OnDetectionBodyEntered(Node body)
	{
		if (_hasBeenRevealed && _hasActivatedTracking && body is PlayerController player)
			AttackPlayer(player);
	}

	private void AttackPlayer(Node playerNode)
	{
		if (_destroyed || playerNode is not PlayerController player)
			return;
		if (player.IsDead)
			return;
		player.TakeDamage(AttackDamage, bypassShield: true);
		Destroy(false);
	}

	private void PushAsteroidsFromCollisions()
	{
		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			KinematicCollision2D collision = GetSlideCollision(i);
			Node? collider = collision.GetCollider() as Node;
			if (collider is BreakableEntityController asteroid)
				asteroid.ApplyPush(GlobalPosition.DirectionTo(asteroid.GlobalPosition), MoveSpeed * 1.5f);
			else if (collider is ActiveEnemyController tracker && tracker != this && !tracker._destroyed && IsAtPlayerPosition(collision.GetPosition()))
			{
				// Only a collision at the ship's position destroys trackers.
				tracker.Destroy(false);
				Destroy(false);
				return;
			}
		}
	}

	private void DestroyTrackersCollidingAtPlayerPosition()
	{
		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			KinematicCollision2D collision = GetSlideCollision(i);
			if (collision.GetCollider() is ActiveEnemyController tracker && tracker != this && !tracker._destroyed && IsAtPlayerPosition(collision.GetPosition()))
			{
				tracker.Destroy(false);
				Destroy(false);
				return;
			}
		}
	}

	private bool IsAtPlayerPosition(Vector2 position)
	{
		// The hidden player remains at the death location, making this valid after death too.
		return IsInstanceValid(_player) && position.DistanceSquaredTo(_player!.GlobalPosition) <= 24.0f * 24.0f;
	}

	private void Destroy(bool canDropPellets)
	{
		if (_destroyed)
			return;
		_destroyed = true;
		int minimum = Mathf.Max(0, ResourceYieldMin);
		int maximum = Mathf.Max(minimum, ResourceYieldMax);
		int yield = _random.RandiRange(minimum, maximum);
		// Medium asteroids have a fixed five-point XP award, independent of repair drops.
		XpDropController.Spawn(this, GlobalPosition, Mathf.Max(1, XpDropAmount));
		int pixelMinimum = Mathf.Max(0, BreakPixelCountMin);
		int pixelMaximum = Mathf.Max(pixelMinimum, BreakPixelCountMax);
		BreakEffectController.Spawn(this, GlobalPosition, _random.RandiRange(pixelMinimum, pixelMaximum));
		if (canDropPellets && _random.Randf() <= PelletDropChance)
		{
			ResourceDropController.Spawn(this, GlobalPosition, yield);
			EmitSignal(SignalName.ResourcesDropped, yield, GlobalPosition);
		}
		EmitSignal(SignalName.Destroyed, GlobalPosition);
		QueueFree();
	}
}
