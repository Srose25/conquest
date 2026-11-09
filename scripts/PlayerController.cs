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
	[Export] public float CoastStopSeconds { get; set; } = 10.0f;
	[Export] public float DirectionChangeSeconds { get; set; } = 0.9f;
	[Export] public float MouseMovementInfluence { get; set; } = 0.35f;
	[Export] public float LowMagnetSpeed { get; set; } = 120.0f;
	[Export] public float HighMagnetSpeed { get; set; } = 300.0f;

	public float Health { get; private set; }
	public int Resources { get; private set; }
	public bool IsDead => Health <= 0.0f;

	private Timer _fireCooldown = null!;
	private Marker2D _muzzle = null!;
	private Area2D _lowMagnetField = null!;
	private Area2D _highMagnetField = null!;
	private Sprite2D _crosshair = null!;

	public override void _Ready()
	{
		AddToGroup("player");
		Health = Mathf.Max(1.0f, MaxHealth);
		_fireCooldown = GetNode<Timer>("FireCooldown");
		_muzzle = GetNode<Marker2D>("Cursor");
		_lowMagnetField = GetNode<Area2D>("LowMagnetField");
		_highMagnetField = GetNode<Area2D>("HighMagnetField");
		_crosshair = GetNode<Sprite2D>("Crosshair");
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

	public override void _Process(double delta)
	{
		_crosshair.GlobalPosition = GetGlobalMousePosition();
	}

	public void TakeDamage(float amount)
	{
		if (amount <= 0.0f || Health <= 0.0f)
			return;
		Health = Mathf.Max(0.0f, Health - amount);
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

	public void AddResources(int amount)
	{
		if (amount > 0)
			Resources += amount;
	}

	public void RecoverHealth(float amount)
	{
		if (amount > 0.0f)
			Health = Mathf.Min(Mathf.Max(1.0f, MaxHealth), Health + amount);
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
			if (GetSlideCollision(i).GetCollider() is not BreakableEntityController asteroid)
				continue;

			hitAsteroid = true;
			float currentSpeed = velocityBeforeContact.Length();
			if (currentSpeed > 0.01f)
				asteroid.ApplyPush(GlobalPosition.DirectionTo(asteroid.GlobalPosition), currentSpeed);
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

	/// <summary>Pulls nearby resource drops through the two player magnet fields.</summary>
	private void HandleMagnetism(float delta)
	{
		var attractedDrops = new HashSet<ResourceDropController>();
		// Process the inner field first so its stronger pull wins in the overlap.
		AttractDropsInField(_highMagnetField, HighMagnetSpeed, delta, attractedDrops);
		AttractDropsInField(_lowMagnetField, LowMagnetSpeed, delta, attractedDrops);
	}

	private void AttractDropsInField(Area2D field, float speed, float delta, HashSet<ResourceDropController> attractedDrops)
	{
		foreach (Area2D area in field.GetOverlappingAreas())
		{
			if (area is not ResourceDropController drop || !attractedDrops.Add(drop))
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
			controller.Initialize(direction, BulletSpeed, this);
		}
		bullet.GlobalPosition = _muzzle.GlobalPosition;
		Node parent = GetTree().CurrentScene ?? GetParent();
		parent.AddChild(bullet);
		_fireCooldown.Start();
	}
}
