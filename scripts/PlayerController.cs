#nullable enable
using Godot;

/// <summary>Handles player movement, cursor aiming, and firing reusable bullet scenes.</summary>
	public partial class PlayerController : CharacterBody2D
{
	[Signal] public delegate void DamagedEventHandler(float amount, float remainingHealth);
	[Export] public float MoveSpeed { get; set; } = 255.0f;
	[Export] public float FireCooldownSeconds { get; set; } = 0.16f;
	[Export] public float BulletSpeed { get; set; } = 630.0f;
	[Export] public float MuzzleOffset { get; set; } = 26.0f;
	[Export] public PackedScene? BulletScene { get; set; }
	[Export] public float MaxHealth { get; set; } = 20.0f;
	[Export] public float MaxShield { get; set; } = 20.0f;
	[Export] public float ShieldRegenDelaySeconds { get; set; } = 5.0f;
	[Export] public float ShieldRegenDurationSeconds { get; set; } = 5.0f;
	[Export] public float CoastStopSeconds { get; set; } = 10.0f;
	[Export] public float DirectionChangeSeconds { get; set; } = 0.9f;

	public float Health { get; private set; }
	public float Shield { get; private set; }
	public int Resources { get; private set; }
	public bool IsDead => Health <= 0.0f;

	private Timer _fireCooldown = null!;
	private Marker2D _muzzle = null!;
	private ShaderMaterial? _hitFlashMaterial;
	private float _hitFlashSeconds;
	private float _hitFlashHoldSeconds;
	private const float HitFlashDuration = 0.12f;
	// The cyan repair tint fades continuously across the same duration as the
	// 0.25-second health-cell growth animation.
	private const float RepairFlashDuration = 0.25f;
	private float _currentHitFlashDuration = HitFlashDuration;
	private float _secondsSinceShieldHit;

	public override void _Ready()
	{
		AddToGroup("player");
		Health = Mathf.Max(1.0f, MaxHealth);
		Shield = Mathf.Max(0.0f, MaxShield);
		_fireCooldown = GetNode<Timer>("FireCooldown");
		_muzzle = GetNode<Marker2D>("Cursor");
		Sprite2D sprite = GetNode<Sprite2D>("Sprite2D");
		if (sprite.Material is ShaderMaterial material)
		{
			_hitFlashMaterial = (ShaderMaterial)material.Duplicate();
			sprite.Material = _hitFlashMaterial;
			_hitFlashMaterial.SetShaderParameter("flash_amount", 0.0f);
		}
		_fireCooldown.WaitTime = FireCooldownSeconds;
		_fireCooldown.OneShot = true;
		// The ship artwork faces local up; the rotation correction maps this to the aim direction.
		_muzzle.Position = Vector2.Up * MuzzleOffset;

		// Keep the exported reference editor-configurable, but recover if the
		// serialized C# property is missing or stale in the scene file.
		BulletScene ??= GD.Load<PackedScene>("res://scenes/combat/Bullet.tscn");
		if (BulletScene == null)
			GD.PrintErr("PlayerController could not load Bullet.tscn.");
	}

	public void TakeDamage(float amount, bool bypassShield = false)
	{
		if (amount <= 0.0f || Health <= 0.0f)
			return;
		_secondsSinceShieldHit = 0.0f;
		float remainingDamage = amount;
		if (!bypassShield && Shield > 0.0f)
		{
			float shieldDamage = Mathf.Min(Shield, remainingDamage);
			Shield -= shieldDamage;
			remainingDamage -= shieldDamage;
		}
		Health = Mathf.Max(0.0f, Health - remainingDamage);
		FlashHit(new Color("ff2638"));
		EmitSignal(SignalName.Damaged, amount, Health);
		if (Health <= 0.0f)
		{
			DeathExplosionController.Spawn(this, GlobalPosition, GlobalRotation);
			Visible = false;
			CollisionLayer = 0;
			CollisionMask = 0;
			SetPhysicsProcess(false);
		}
	}

	public override void _Process(double delta)
	{
		float step = (float)delta;
		UpdateShieldRegeneration(step);
		if (_hitFlashMaterial == null)
			return;
		if (_hitFlashHoldSeconds > 0.0f)
		{
			_hitFlashHoldSeconds = Mathf.Max(0.0f, _hitFlashHoldSeconds - step);
			_hitFlashMaterial.SetShaderParameter("flash_amount", 1.0f);
			return;
		}
		if (_hitFlashSeconds <= 0.0f)
			return;

		_hitFlashSeconds = Mathf.Max(0.0f, _hitFlashSeconds - step);
		_hitFlashMaterial.SetShaderParameter("flash_amount", _hitFlashSeconds / _currentHitFlashDuration);
	}

	private void UpdateShieldRegeneration(float delta)
	{
		if (IsDead || Shield >= MaxShield || MaxShield <= 0.0f)
			return;
		_secondsSinceShieldHit += delta;
		if (_secondsSinceShieldHit < ShieldRegenDelaySeconds)
			return;

		float regenerationPerSecond = MaxShield / Mathf.Max(0.1f, ShieldRegenDurationSeconds);
		Shield = Mathf.Min(MaxShield, Shield + regenerationPerSecond * delta);
	}

	private void FlashHit(Color color, float duration = HitFlashDuration, float holdDuration = 0.0f)
	{
		if (_hitFlashMaterial == null)
			return;

		_currentHitFlashDuration = Mathf.Max(0.01f, duration);
		_hitFlashSeconds = _currentHitFlashDuration;
		_hitFlashHoldSeconds = Mathf.Max(0.0f, holdDuration);
		_hitFlashMaterial.SetShaderParameter("flash_color", color);
		_hitFlashMaterial.SetShaderParameter("flash_amount", 1.0f);
	}

	public void AddResources(int amount)
	{
		if (amount > 0)
			Resources += amount;
	}

	public void RecoverHealth(float amount)
	{
		if (amount <= 0.0f || IsDead)
			return;

		float previousHealth = Health;
		Health = Mathf.Min(Mathf.Max(1.0f, MaxHealth), Health + amount);
		if (Health > previousHealth)
			FlashHit(new Color("26cfff"), RepairFlashDuration);
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector2 input = GetMovementInput();
		float step = (float)delta;
		if (input == Vector2.Zero)
		{
			float coastDeceleration = MoveSpeed / Mathf.Max(0.1f, CoastStopSeconds);
			Velocity = Velocity.MoveToward(Vector2.Zero, coastDeceleration * step);
		}
		else
		{
			Vector2 targetVelocity = input * MoveSpeed;
			bool changingDirection = Velocity.LengthSquared() > 0.01f && Velocity.Dot(targetVelocity) < 0.0f;
			float acceleration = MoveSpeed / Mathf.Max(0.1f, changingDirection ? DirectionChangeSeconds : 1.5f);
			Velocity = Velocity.MoveToward(targetVelocity, acceleration * step);
		}
		Vector2 velocityBeforeAsteroidContacts = Velocity;
		Vector2 positionBeforeAsteroidContacts = GlobalPosition;
		MoveAndSlide();
		HandleAsteroidContacts(velocityBeforeAsteroidContacts, positionBeforeAsteroidContacts, step);
		Vector2 direction = AimAtCursor();
		if (Input.IsMouseButtonPressed(MouseButton.Left) && _fireCooldown.TimeLeft <= 0.0)
			Fire(direction);
	}

	private void HandleAsteroidContacts(Vector2 velocityBeforeContact, Vector2 positionBeforeContact, float delta)
	{
		bool hitAsteroid = false;
		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			Node? collider = GetSlideCollision(i).GetCollider() as Node;
			if (collider is BreakableEntityController asteroid)
			{
				hitAsteroid = true;
				float currentSpeed = velocityBeforeContact.Length();
				if (currentSpeed > 0.01f)
					asteroid.ApplyPush(GlobalPosition.DirectionTo(asteroid.GlobalPosition), currentSpeed);
			}
			else if (collider is EnemyShipController enemyShip)
			{
				enemyShip.ResolvePlayerCollision(this);
			}
		}
		if (!hitAsteroid)
			return;

		// Keep real collision resolution, but do not accept an impulse that launches the ship.
		Velocity = velocityBeforeContact * 0.5f;
		float maxAllowedDisplacement = velocityBeforeContact.Length() * delta + 4.0f;
		Vector2 displacement = GlobalPosition - positionBeforeContact;
		if (displacement.Length() > maxAllowedDisplacement)
			GlobalPosition = positionBeforeContact + displacement.Normalized() * maxAllowedDisplacement;
	}

	private Vector2 GetMovementInput()
	{
		Vector2 movement = new(
			(Input.IsKeyPressed(Key.D) ? 1.0f : 0.0f) - (Input.IsKeyPressed(Key.A) ? 1.0f : 0.0f),
			(Input.IsKeyPressed(Key.S) ? 1.0f : 0.0f) - (Input.IsKeyPressed(Key.W) ? 1.0f : 0.0f));
		return movement.LengthSquared() > 1.0f ? movement.Normalized() : movement;
	}

	private Vector2 AimAtCursor()
	{
		Vector2 direction = GlobalPosition.DirectionTo(GetGlobalMousePosition());
		if (direction.LengthSquared() <= Mathf.Epsilon)
			return Vector2.Right;
		// The ship artwork faces upward; Godot's zero rotation points right.
		Rotation = direction.Angle() + Mathf.Pi / 2.0f;
		return direction.Normalized();
	}

	private void Fire(Vector2 direction)
	{
		//Debugging line
		if (BulletScene == null)
		{
			GD.Print($"Fire called. BulletScene is null: {BulletScene == null}");
			return;
		}

		//Debugging line
		//if (BulletScene != null)
		//{
		//	GD.Print($"Bullet scene path: {BulletScene.ResourcePath}");
		//	
		//}
		//else
		//{
		//	GD.PrintErr("BulletScene was not assigned.");
		//	return;
		//}

		Area2D bullet = BulletScene.Instantiate<Area2D>();
		if (bullet is BulletController controller)
		{
			controller.Initialize(direction, BulletSpeed, this);
		}
		bullet.GlobalPosition = _muzzle.GlobalPosition;
		Node parent = GetTree().CurrentScene ?? GetParent();
		parent.AddChild(bullet);
		_fireCooldown.Start();
	}
}
