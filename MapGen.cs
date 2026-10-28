#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>Continuously generates reusable entity scenes in chunks around the player.</summary>
public partial class MapGen : Node2D
{
	[Export] public PackedScene? PassiveEntityScene { get; set; }
	[Export] public PackedScene? ActiveEnemyScene { get; set; }
	[Export] public float ChunkSize { get; set; } = 480.0f;
	[Export] public int GenerationRadius { get; set; } = 2;
	[Export] public int PassiveEntitiesPerChunk { get; set; } = 5;
	[Export] public int ActiveEnemiesPerChunk { get; set; } = 1;
	[Export] public float PlayerClearanceRadius { get; set; } = 180.0f;
	[Export] public float ActiveEnemySpawnClearanceRadius { get; set; } = 700.0f;

	private readonly HashSet<Vector2I> _generatedChunks = new();
	private readonly HashSet<Vector2I> _trackerChunksGenerated = new();
	private Node2D _passiveEntities = null!;
	private Node2D _activeEntities = null!;
	private Node2D? _player;
	private Vector2I _lastPlayerChunk = new(int.MinValue, int.MinValue);
	private bool _initialGenerationComplete;

	public override void _Ready()
	{
		PassiveEntityScene ??= GD.Load<PackedScene>("res://scenes/actors/PassiveEntity.tscn");
		ActiveEnemyScene ??= GD.Load<PackedScene>("res://scenes/actors/ActiveEnemy.tscn");
		_passiveEntities = GetNode<Node2D>("PassiveEntities");
		_activeEntities = GetNode<Node2D>("ActiveEntities");
	}

	public override void _Process(double delta)
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as Node2D;
		if (!IsInstanceValid(_player))
			return;

		Vector2I playerChunk = GetChunk(_player!.GlobalPosition);
		if (playerChunk != _lastPlayerChunk)
		{
			GenerateChunksAround(playerChunk);
			_lastPlayerChunk = playerChunk;
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		Vector2 center = _player?.GlobalPosition ?? Vector2.Zero;
		const float drawRange = 1800.0f;
		DrawRect(new Rect2(center - Vector2.One * drawRange, Vector2.One * drawRange * 2.0f), Colors.Black);
		float firstX = Mathf.Floor(center.X / 64.0f) * 64.0f - drawRange;
		float firstY = Mathf.Floor(center.Y / 64.0f) * 64.0f - drawRange;
		Color gridColor = new("10223a");
		for (float x = firstX; x <= center.X + drawRange; x += 64.0f)
			DrawLine(new Vector2(x, center.Y - drawRange), new Vector2(x, center.Y + drawRange), gridColor);
		for (float y = firstY; y <= center.Y + drawRange; y += 64.0f)
			DrawLine(new Vector2(center.X - drawRange, y), new Vector2(center.X + drawRange, y), gridColor);
	}

	private void GenerateChunksAround(Vector2I playerChunk)
	{
		int radius = Mathf.Max(0, GenerationRadius);
		for (int y = -radius; y <= radius; y++)
		{
			for (int x = -radius; x <= radius; x++)
				GenerateChunk(new Vector2I(playerChunk.X + x, playerChunk.Y + y));
		}
		_initialGenerationComplete = true;
	}

	private void GenerateChunk(Vector2I chunk)
	{
		if (PassiveEntityScene == null || ActiveEnemyScene == null)
		{
			GD.PrintErr("MapGen could not load the entity scenes.");
			return;
		}

		RandomNumberGenerator random = new();
		random.Seed = GetChunkSeed(chunk);
		if (_generatedChunks.Add(chunk))
		{
			for (int i = 0; i < Mathf.Max(0, PassiveEntitiesPerChunk); i++)
				SpawnEntity(PassiveEntityScene, _passiveEntities, $"Passive_{chunk.X}_{chunk.Y}_{i}", chunk, random, PlayerClearanceRadius);
		}
		if (_initialGenerationComplete && _trackerChunksGenerated.Add(chunk))
		{
			float trackerClearance = Mathf.Max(ActiveEnemySpawnClearanceRadius, GetViewportClearance());
			for (int i = 0; i < Mathf.Max(0, ActiveEnemiesPerChunk); i++)
				SpawnEntity(ActiveEnemyScene, _activeEntities, $"Active_{chunk.X}_{chunk.Y}_{i}", chunk, random, trackerClearance);
		}
	}

	private float GetViewportClearance()
	{
		return GetViewportRect().Size.Length() * 0.5f + 128.0f;
	}

	private void SpawnEntity(PackedScene scene, Node2D container, string entityName, Vector2I chunk, RandomNumberGenerator random, float playerClearance)
	{
		Node2D entity = scene.Instantiate<Node2D>();
		entity.Name = entityName;
		container.AddChild(entity);
		for (int attempt = 0; attempt < 30; attempt++)
		{
			Vector2 position = new(
				chunk.X * ChunkSize + _randomChunkCoordinate(random),
				chunk.Y * ChunkSize + _randomChunkCoordinate(random));
			if (_player != null && position.DistanceTo(_player.GlobalPosition) < playerClearance)
				position = _player.GlobalPosition + Vector2.FromAngle(random.RandfRange(0.0f, Mathf.Tau)) * playerClearance;
			if (IsSpawnPositionFree(container, entity, position))
			{
				entity.GlobalPosition = position;
				entity.Rotation = random.RandiRange(0, 3) * Mathf.Pi / 2.0f;
				if (random.Randf() < 0.5f)
					entity.Scale = new Vector2(-1.0f, 1.0f);
				return;
			}
		}
		entity.GlobalPosition = _player?.GlobalPosition + Vector2.Right * playerClearance ?? Vector2.Zero;
		entity.Rotation = random.RandiRange(0, 3) * Mathf.Pi / 2.0f;
		if (random.Randf() < 0.5f)
			entity.Scale = new Vector2(-1.0f, 1.0f);
	}

	private static bool IsSpawnPositionFree(Node2D container, Node2D candidate, Vector2 position)
	{
		foreach (Node child in container.GetChildren())
		{
			if (child != candidate && child is Node2D other && other.GlobalPosition.DistanceTo(position) < 72.0f)
				return false;
		}
		return true;
	}

	private float _randomChunkCoordinate(RandomNumberGenerator random)
	{
		return random.RandfRange(32.0f, ChunkSize - 32.0f);
	}

	private Vector2I GetChunk(Vector2 position)
	{
		float size = Mathf.Max(64.0f, ChunkSize);
		return new Vector2I(Mathf.FloorToInt(position.X / size), Mathf.FloorToInt(position.Y / size));
	}

	private static ulong GetChunkSeed(Vector2I chunk)
	{
		ulong x = unchecked((ulong)(long)chunk.X);
		ulong y = unchecked((ulong)(long)chunk.Y);
		ulong seed = (x * 73856093UL) ^ (y * 19349663UL) ^ 0x9E3779B97F4A7C15UL;
		return seed == 0 ? 1UL : seed;
	}
}
