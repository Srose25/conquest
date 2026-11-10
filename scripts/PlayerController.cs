#nullable enable
using System.Collections.Generic;
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
	[Export] public float MouseMovementInfluence { get; set; } = 0.35f;
	[Export] public float LowMagnetSpeed { get; set; } = 120.0f;
	[Export] public float HighMagnetSpeed { get; set; } = 300.0f;
	[Export] public float BulletDamage { get; private set; } = 1.0f;

	public float Health { get; private set; }
	public float Shield { get; private set; }
	public int Resources { get; private set; }
	public bool IsDead => Health <= 0.0f;

	private Timer _fireCooldown = null!;
	private Marker2D _muzzle = null!;
	private Area2D _lowMagnetField = null!;
	private Area2D _highMagnetField = null!;
	private Sprite2D _crosshair = null!;
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
		_lowMagnetField = GetNode<Area2D>("LowMagnetField");
		_highMagnetField = GetNode<Area2D>("HighMagnetField");
		_crosshair = GetNode<Sprite2D>("Crosshair");
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

		Input.MouseMode = Input.MouseModeEnum.Hidden;
	}

	public override void _ExitTree()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;
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
		_crosshair.GlobalPosition = GetGlobalMousePosition();
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

	/// <summary>Applies the automatic utility-point reward from a level-up.</summary>
	public void ApplyLevelUpUpgrade(float healthIncrease, float shieldIncrease, float damageIncrease)
	{
		MaxHealth += Mathf.Max(0.0f, healthIncrease);
		Health = Mathf.Min(MaxHealth, Health + Mathf.Max(0.0f, healthIncrease));
		MaxShield += Mathf.Max(0.0f, shieldIncrease);
		Shield = Mathf.Min(MaxShield, Shield + Mathf.Max(0.0f, shieldIncrease));
		BulletDamage += Mathf.Max(0.0f, damageIncrease);
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector2 input = GetMovementInput();
		float step = (float)delta;
		HandleMagnetism(step);
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
		if (movement == Vector2.Zero)
			return movement;

		// Quantize the mouse direction to one of eight compass directions. When the
		// keyboard is pointed into that octant, blend toward it for more intentional
		// movement while retaining direct WASD control.
		Vector2 mouseOctant = GetMouseOctantDirection();
		if (movement.Dot(mouseOctant) > 0.0f)
			movement = movement.Lerp(mouseOctant, Mathf.Clamp(MouseMovementInfluence, 0.0f, 1.0f));
		return movement.LengthSquared() > 1.0f ? movement.Normalized() : movement;
	}

	private Vector2 GetMouseOctantDirection()
	{
		Vector2 aim = GlobalPosition.DirectionTo(GetGlobalMousePosition());
		if (aim.LengthSquared() <= Mathf.Epsilon)
			return Vector2.Right;
		float octantAngle = Mathf.Round(aim.Angle() / (Mathf.Pi / 4.0f)) * (Mathf.Pi / 4.0f);
		return Vector2.FromAngle(octantAngle);
	}

	/// <summary>Pulls nearby repair and XP drops through the two player magnet fields.</summary>
	private void HandleMagnetism(float delta)
	{
		// Keep the drops as separate entities, but deduplicate them through their
		// shared magnetic movement contract so the stronger inner field wins.
		var attractedDrops = new HashSet<IMagnetizableDrop>();
		// Process the inner field first so its stronger pull wins in the overlap.
		AttractDropsInField(_highMagnetField, HighMagnetSpeed, delta, attractedDrops);
		AttractDropsInField(_lowMagnetField, LowMagnetSpeed, delta, attractedDrops);
	}

	private void AttractDropsInField(Area2D field, float speed, float delta, HashSet<IMagnetizableDrop> attractedDrops)
	{
		foreach (Area2D area in field.GetOverlappingAreas())
		{
			if (area is not IMagnetizableDrop drop || !attractedDrops.Add(drop))
				continue;
			drop.PullToward(GlobalPosition, speed, delta);
		}
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
			controller.Initialize(direction, BulletSpeed, this, BulletDamage);
		}
		bullet.GlobalPosition = _muzzle.GlobalPosition;
		Node parent = GetTree().CurrentScene ?? GetParent();
		parent.AddChild(bullet);
		_fireCooldown.Start();
	}
}
