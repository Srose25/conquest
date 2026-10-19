using Godot;
using System;

public partial class MapGen : Node2D
{
    const float ShipSpeed = 255, ShipRadius = 18, ShotCooldown = .16f;
    readonly RandomNumberGenerator random = new();
    CharacterBody2D ship = null!;
    Label status = null!;
    float nextShot;
    int mined;

    public override void _Ready()
    {
        random.Randomize(); QueueRedraw(); BuildShip(); BuildAsteroidMaze();
        status = new Label { Position = new Vector2(16, 14), Text = "Ore mined: 0    WASD: thrust    Mouse: aim    Left click: mine" };
        status.AddThemeColorOverride("font_color", new Color("b9e5ff")); AddChild(status);
    }
    public override void _Draw()
    {
        var area = GetViewportRect(); DrawRect(area, new Color("07101d"));
        for (var x = 0; x < area.Size.X; x += 64) DrawLine(new Vector2(x, 0), new Vector2(x, area.Size.Y), new Color("10223a"));
        for (var y = 0; y < area.Size.Y; y += 64) DrawLine(new Vector2(0, y), new Vector2(area.Size.X, y), new Color("10223a"));
    }
    public override void _Process(double delta)
    {
        QueueRedraw();
        var movement = new Vector2((Input.IsKeyPressed(Key.D) ? 1 : 0) - (Input.IsKeyPressed(Key.A) ? 1 : 0), (Input.IsKeyPressed(Key.S) ? 1 : 0) - (Input.IsKeyPressed(Key.W) ? 1 : 0));
        ship.Velocity = movement.Normalized() * ShipSpeed; ship.MoveAndSlide();
        ship.LookAt(GetGlobalMousePosition()); ship.Rotation += Mathf.Pi / 2; // art faces up; LookAt faces right
        nextShot -= (float)delta;
        if (Input.IsMouseButtonPressed(MouseButton.Left) && nextShot <= 0) { Shoot(); nextShot = ShotCooldown; }
    }
    void BuildShip()
    {
        ship = new CharacterBody2D { Name = "PlayerShip", Position = GetViewportRect().Size / 2, CollisionLayer = 1, CollisionMask = 2 };
        ship.AddChild(new Sprite2D { Texture = GD.Load<Texture2D>("res://Protagonist_Ship.png"), Scale = Vector2.One * 1.5f });
        ship.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = ShipRadius } }); AddChild(ship);
    }
    void BuildAsteroidMaze()
    {
        var size = GetViewportRect().Size;
        for (int row = 1; row < size.Y / 110; row++) { int gap = random.RandiRange(1, Math.Max(2, (int)size.X / 78 - 2)); for (int col = 0; col < size.X / 78; col++) if (col != gap && col != gap + 1) AddAsteroid(new Vector2(45 + col * 78, 55 + row * 110), row % 3 == 0); }
        for (int col = 1; col < size.X / 155; col++) { int gap = random.RandiRange(1, Math.Max(2, (int)size.Y / 90 - 2)); for (int row = 0; row < size.Y / 90; row++) if (row != gap && row != gap + 1) AddAsteroid(new Vector2(78 + col * 155, 45 + row * 90), false); }
    }
    void AddAsteroid(Vector2 position, bool mayMove)
    {
        if (position.DistanceTo(ship.Position) < 115) return;
        AddChild(new Asteroid { Position = position, Texture = GD.Load<Texture2D>($"res://meteor{random.RandiRange(1, 3)}.png"), IsMoving = mayMove && random.Randf() < .45f, MoveAxis = random.Randf() < .5f ? Vector2.Right : Vector2.Down, MoveDistance = random.RandfRange(18, 42), MoveSpeed = random.RandfRange(.7f, 1.5f) });
    }
    void Shoot()
    {
        var shot = new MiningShot { Position = ship.Position + ship.GlobalTransform.X * 25, Direction = ship.GlobalTransform.X.Normalized() };
        shot.AsteroidMined += asteroid => { if (IsInstanceValid(asteroid)) { asteroid.QueueFree(); status.Text = $"Ore mined: {++mined}    WASD: thrust    Mouse: aim    Left click: mine"; } };
        AddChild(shot);
    }
}

public partial class Asteroid : AnimatableBody2D
{
    public Texture2D Texture = null!; public bool IsMoving; public Vector2 MoveAxis; public float MoveDistance, MoveSpeed;
    Vector2 origin; float time;
    public override void _Ready() { origin = Position; CollisionLayer = 2; AddChild(new Sprite2D { Texture = Texture }); AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = Math.Max(Texture.GetWidth(), Texture.GetHeight()) * .38f } }); }
    public override void _PhysicsProcess(double delta) { if (IsMoving) { time += (float)delta * MoveSpeed; Position = origin + MoveAxis * Mathf.Sin(time) * MoveDistance; } }
}

public partial class MiningShot : Area2D
{
    public Vector2 Direction; public event Action<Asteroid> AsteroidMined; float life = 1.15f;
    public override void _Ready() { CollisionMask = 2; AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 4 } }); BodyEntered += body => { if (body is Asteroid asteroid) { AsteroidMined?.Invoke(asteroid); QueueFree(); } }; QueueRedraw(); }
    public override void _Draw() => DrawCircle(Vector2.Zero, 4, new Color("7fffd4"));
    public override void _PhysicsProcess(double delta) { Position += Direction * 630 * (float)delta; if ((life -= (float)delta) <= 0 || !GetViewportRect().Grow(60).HasPoint(Position)) QueueFree(); }
}
