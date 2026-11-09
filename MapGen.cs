#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>Continuously generates reusable entity scenes in chunks around the player.</summary>
public partial class MapGen : Node2D
{
	[Export] public PackedScene? PassiveEntityScene { get; set; }
	[Export] public PackedScene? ActiveEnemyScene { get; set; }
	[Export] public PackedScene? EnemyShipScene { get; set; }
	[Export] public Vector2 ChunkSize { get; set; } = Vector2.Zero;
	[Export] public int GenerationRadius { get; set; } = 2;
	[Export] public int PassiveEntitiesPerChunk { get; set; } = 5;
	[Export] public int ActiveEnemiesMinPerChunk { get; set; } = 1;
	[Export] public int ActiveEnemiesMaxPerChunk { get; set; } = 5;
	[Export(PropertyHint.Range, "0,1,0.05")] public float EnemyShipSpawnChance { get; set; } = 0.2f;
	[Export] public int EnemyShipsMinPerChunk { get; set; } = 1;
	[Export] public int EnemyShipsMaxPerChunk { get; set; } = 2;
	[Export] public float ReferenceChunkSize { get; set; } = 480.0f;
	[Export] public float PassiveDensityMultiplier { get; set; } = 1.25f;
	[Export] public float PlayerClearanceRadius { get; set; } = 180.0f;
	[Export] public float ActiveEnemySpawnClearanceRadius { get; set; } = 700.0f;
	[Export(PropertyHint.Range, "0,1,0.05")] public float SafeChunkChance { get; set; } = 0.3f;

	private readonly HashSet<Vector2I> _generatedChunks = new();
	private readonly HashSet<Vector2I> _queuedChunks = new();
	private readonly Queue<ChunkRequest> _pendingChunks = new();
	private readonly Dictionary<Vector2I, int> _chunkVisitCounts = new();
	private readonly Dictionary<Vector2I, int> _enemyShipSpawnAttempts = new();
	private readonly RandomNumberGenerator _random = new();
	private Node2D _passiveEntities = null!;
	private Node2D _activeEntities = null!;
	private Node2D? _player;
	private Vector2I _lastPlayerChunk = new(int.MinValue, int.MinValue);

	private readonly struct ChunkRequest
	{
		public readonly Vector2I Coordinate;
		public readonly bool IsInitialGeneration;

		public ChunkRequest(Vector2I coordinate, bool isInitialGeneration)
		{
			Coordinate = coordinate;
			IsInitialGeneration = isInitialGeneration;
		}
	}

	public override void _Ready()
	{
		_random.Randomize();
		// A chunk is one complete viewport resolution, not an arbitrary fixed square.
		ChunkSize = GetViewportRect().Size;
		PassiveEntityScene ??= GD.Load<PackedScene>("res://scenes/actors/PassiveEntity.tscn");
		ActiveEnemyScene ??= GD.Load<PackedScene>("res://scenes/actors/ActiveEnemy.tscn");
		EnemyShipScene ??= GD.Load<PackedScene>("res://scenes/actors/EnemyShip.tscn");
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
			bool firstChunkEntry = _lastPlayerChunk.X == int.MinValue;
			if (firstChunkEntry)
				QueueInitialChunkWindow(playerChunk);
			else
				QueueEnteredChunkEdges(_lastPlayerChunk, playerChunk);
			if (firstChunkEntry)
				SpawnTrackersForInitialOuterChunks(playerChunk);
			else
				SpawnHostilesForChunksLeavingProtectionBox(_lastPlayerChunk, playerChunk);
			_lastPlayerChunk = playerChunk;
		}
		ProcessGenerationQueue(playerChunk);
		UpdatePhysicsWindow(playerChunk);
		RemoveTrackersOutsideActiveRadius(playerChunk);
		RemovePassiveAsteroidsOutsideActiveRadius(playerChunk);
		QueueRedraw();
	}

	public override void _Draw()
	{
		Camera2D? camera = GetViewport().GetCamera2D();
		Vector2 center = camera?.GlobalPosition ?? _player?.GlobalPosition ?? Vector2.Zero;
		const float drawRange = 1800.0f;
		float firstX = Mathf.Floor(center.X / 64.0f) * 64.0f - drawRange;
		float firstY = Mathf.Floor(center.Y / 64.0f) * 64.0f - drawRange;
		Color gridColor = new(56.0f / 255.0f, 217.0f / 255.0f, 255.0f / 255.0f, 0.25f);
		for (float x = firstX; x <= center.X + drawRange; x += 64.0f)
			DrawLine(new Vector2(x, center.Y - drawRange), new Vector2(x, center.Y + drawRange), gridColor, 1.5f, true);
		for (float y = firstY; y <= center.Y + drawRange; y += 64.0f)
			DrawLine(new Vector2(center.X - drawRange, y), new Vector2(center.X + drawRange, y), gridColor, 1.5f, true);
	}

	private void QueueInitialChunkWindow(Vector2I playerChunk)
	{
		int radius = Mathf.Max(0, GenerationRadius);
		for (int y = -radius; y <= radius; y++)
		{
			for (int x = -radius; x <= radius; x++)
				QueueChunk(new Vector2I(playerChunk.X + x, playerChunk.Y + y), true);
		}
	}

	private void QueueEnteredChunkEdges(Vector2I previousChunk, Vector2I currentChunk)
	{
		int radius = Mathf.Max(0, GenerationRadius);
		int deltaX = currentChunk.X - previousChunk.X;
		int deltaY = currentChunk.Y - previousChunk.Y;
		if (Mathf.Abs(deltaX) > 1 || Mathf.Abs(deltaY) > 1)
		{
			QueueInitialChunkWindow(currentChunk);
			return;
		}

		if (deltaX != 0)
		{
			int edgeX = currentChunk.X + Mathf.Sign(deltaX) * radius;
			for (int y = -radius; y <= radius; y++)
				QueueChunk(new Vector2I(edgeX, currentChunk.Y + y), false);
		}
		if (deltaY != 0)
		{
			int edgeY = currentChunk.Y + Mathf.Sign(deltaY) * radius;
			for (int x = -radius; x <= radius; x++)
				QueueChunk(new Vector2I(currentChunk.X + x, edgeY), false);
		}
	}

	private void QueueChunk(Vector2I chunk, bool isInitialGeneration)
	{
		if (_generatedChunks.Contains(chunk) || !_queuedChunks.Add(chunk))
			return;
		_pendingChunks.Enqueue(new ChunkRequest(chunk, isInitialGeneration));
	}

	private void ProcessGenerationQueue(Vector2I playerChunk)
	{
		int radius = Mathf.Max(0, GenerationRadius);
		while (_pendingChunks.Count > 0)
		{
			ChunkRequest request = _pendingChunks.Dequeue();
			_queuedChunks.Remove(request.Coordinate);
			if (Mathf.Abs(request.Coordinate.X - playerChunk.X) > radius || Mathf.Abs(request.Coordinate.Y - playerChunk.Y) > radius)
				continue;
			GenerateChunk(request.Coordinate, request.IsInitialGeneration);
			return;
		}
	}

	private void SpawnTrackersForChunkVisit(Vector2I chunk)
	{
		int visitNumber = _chunkVisitCounts.GetValueOrDefault(chunk) + 1;
		_chunkVisitCounts[chunk] = visitNumber;
		SpawnTrackersForChunk(chunk, visitNumber, $"visit{visitNumber}");
	}

	private void SpawnTrackersForInitialOuterChunks(Vector2I centerChunk)
	{
		int radius = Mathf.Max(0, GenerationRadius);
		for (int y = -radius; y <= radius; y++)
		{
			for (int x = -radius; x <= radius; x++)
			{
				if (Mathf.Abs(x) <= 1 && Mathf.Abs(y) <= 1)
					continue;
				Vector2I chunk = new(centerChunk.X + x, centerChunk.Y + y);
				SpawnTrackersForChunk(chunk, 0, "initial");
			}
		}
	}

	private void SpawnHostilesForChunksLeavingProtectionBox(Vector2I previousPlayerChunk, Vector2I currentPlayerChunk)
	{
		int deltaX = currentPlayerChunk.X - previousPlayerChunk.X;
		int deltaY = currentPlayerChunk.Y - previousPlayerChunk.Y;
		if (Mathf.Abs(deltaX) > 1 || Mathf.Abs(deltaY) > 1)
		{
			SpawnTrackersForInitialOuterChunks(currentPlayerChunk);
			SpawnEnemyShipsForOuterRing(currentPlayerChunk);
			return;
		}

		// Only chunks that have just crossed from the surrounding 3x3 into the
		// outer ring become eligible. A cleared nearby chunk therefore cannot
		// respawn threats until it has genuinely left the protection box.
		for (int y = -1; y <= 1; y++)
		{
			for (int x = -1; x <= 1; x++)
			{
				Vector2I chunk = new(previousPlayerChunk.X + x, previousPlayerChunk.Y + y);
				if (GetChunkDistance(chunk, currentPlayerChunk) != 2)
					continue;
				SpawnTrackersForChunkVisit(chunk);
				SpawnEnemyShipsForChunk(chunk, false);
			}
		}
	}

	private void SpawnEnemyShipsForOuterRing(Vector2I centerChunk)
	{
		for (int y = -2; y <= 2; y++)
		{
			for (int x = -2; x <= 2; x++)
			{
				if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) == 2)
					SpawnEnemyShipsForChunk(new Vector2I(centerChunk.X + x, centerChunk.Y + y), false);
			}
		}
	}

	private void SpawnTrackersForChunk(Vector2I chunk, int seedVisit, string nameSuffix)
	{
		if (HasTrackerInChunk(chunk) || _random.Randf() < Mathf.Clamp(SafeChunkChance, 0.0f, 1.0f) || ActiveEnemyScene == null)
			return;

		RandomNumberGenerator random = new();
		random.Seed = GetChunkSeed(chunk) ^ (ulong)(seedVisit + 1) * 83492791UL;
		float trackerClearance = Mathf.Max(ActiveEnemySpawnClearanceRadius, GetChunkHalfClearance());
		int trackerCount = random.RandiRange(Mathf.Max(1, ActiveEnemiesMinPerChunk), Mathf.Max(1, ActiveEnemiesMaxPerChunk));
		for (int i = 0; i < trackerCount; i++)
			SpawnEntity(ActiveEnemyScene, _activeEntities, $"Active_{chunk.X}_{chunk.Y}_{nameSuffix}_{i}", chunk, random, trackerClearance);
	}

	private bool HasTrackerInChunk(Vector2I chunk)
	{
		foreach (Node child in _activeEntities.GetChildren())
		{
			if (child is ActiveEnemyController tracker && GetChunk(tracker.GlobalPosition) == chunk)
				return true;
		}
		return false;
	}

	private void RemoveTrackersOutsideActiveRadius(Vector2I playerChunk)
	{
		int radius = Mathf.Max(0, GenerationRadius);
		foreach (Node child in _activeEntities.GetChildren())
		{
			// Tracker asteroids are recycled outside the active window. Enemy ships
			// persist so an off-screen ship remains part of the generated world.
			if (child is not ActiveEnemyController tracker)
				continue;
			Vector2I trackerChunk = GetChunk(tracker.GlobalPosition);
			if (Mathf.Abs(trackerChunk.X - playerChunk.X) > radius || Mathf.Abs(trackerChunk.Y - playerChunk.Y) > radius)
				tracker.QueueFree();
		}
	}

	private void UpdatePhysicsWindow(Vector2I playerChunk)
	{
		const int physicsRadius = 1;
		foreach (Node child in _passiveEntities.GetChildren())
		{
			if (child is BreakableEntityController asteroid)
			{
				Vector2I chunk = GetChunk(asteroid.GlobalPosition);
				bool enabled = Mathf.Abs(chunk.X - playerChunk.X) <= physicsRadius && Mathf.Abs(chunk.Y - playerChunk.Y) <= physicsRadius;
				asteroid.SetSimulationEnabled(enabled);
			}
		}
		foreach (Node child in _activeEntities.GetChildren())
		{
			if (child is ActiveEnemyController tracker)
			{
				Vector2I chunk = GetChunk(tracker.GlobalPosition);
				bool enabled = Mathf.Abs(chunk.X - playerChunk.X) <= physicsRadius && Mathf.Abs(chunk.Y - playerChunk.Y) <= physicsRadius;
				tracker.SetWithinPhysicsWindow(enabled);
			}
			else if (child is EnemyShipController enemyShip)
			{
				Vector2I chunk = GetChunk(enemyShip.GlobalPosition);
				int chunkDistance = Mathf.Max(Mathf.Abs(chunk.X - playerChunk.X), Mathf.Abs(chunk.Y - playerChunk.Y));
				const int enemyShipTrackingChunkRadius = 5;
				if (enemyShip.HasTargetLock && chunkDistance >= enemyShipTrackingChunkRadius)
					enemyShip.ClearTargetLock();
				bool enabled = chunkDistance <= physicsRadius || (enemyShip.HasTargetLock && chunkDistance < enemyShipTrackingChunkRadius);
				enemyShip.SetWithinPhysicsWindow(enabled);
			}
		}
	}

	private void RemovePassiveAsteroidsOutsideActiveRadius(Vector2I playerChunk)
	{
		int radius = Mathf.Max(0, GenerationRadius);
		HashSet<Vector2I> unloadedChunks = new();
		foreach (Node child in _passiveEntities.GetChildren())
		{
			if (child is not Node2D asteroid)
				continue;
			Vector2I asteroidChunk = GetChunk(asteroid.GlobalPosition);
			if (Mathf.Abs(asteroidChunk.X - playerChunk.X) > radius || Mathf.Abs(asteroidChunk.Y - playerChunk.Y) > radius)
			{
				unloadedChunks.Add(asteroidChunk);
				asteroid.QueueFree();
			}
		}
		foreach (Vector2I chunk in unloadedChunks)
			_generatedChunks.Remove(chunk);
	}

	private void GenerateChunk(Vector2I chunk, bool isInitialGeneration)
	{
		if (PassiveEntityScene == null || ActiveEnemyScene == null || EnemyShipScene == null)
		{
			GD.PrintErr("MapGen could not load the entity scenes.");
			return;
		}

		RandomNumberGenerator random = new();
		random.Seed = GetChunkSeed(chunk);
		if (_generatedChunks.Add(chunk))
		{
			float asteroidClearance = isInitialGeneration ? Mathf.Max(PlayerClearanceRadius, GetChunkHalfClearance()) : 0.0f;
			int passiveCount = GetPassiveCountForChunk();
			for (int i = 0; i < passiveCount; i++)
				SpawnEntity(PassiveEntityScene, _passiveEntities, $"Passive_{chunk.X}_{chunk.Y}_{i}", chunk, random, asteroidClearance);
			if (!IsInsideProtectionBox(chunk, GetChunk(_player?.GlobalPosition ?? Vector2.Zero)))
				SpawnEnemyShipsForChunk(chunk, isInitialGeneration);
		}
	}

	private void SpawnEnemyShipsForChunk(Vector2I chunk, bool isInitialGeneration)
	{
		if (EnemyShipScene == null || HasEnemyShipInChunk(chunk))
			return;
		int spawnAttempt = _enemyShipSpawnAttempts.GetValueOrDefault(chunk) + 1;
		_enemyShipSpawnAttempts[chunk] = spawnAttempt;
		RandomNumberGenerator random = new();
		random.Seed = GetChunkSeed(chunk) ^ 0xC6A4A7935BD1E995UL ^ (ulong)spawnAttempt * 2246822519UL;
		if (random.Randf() > Mathf.Clamp(EnemyShipSpawnChance, 0.0f, 1.0f))
			return;
		float clearance = isInitialGeneration
			? Mathf.Max(ActiveEnemySpawnClearanceRadius, GetChunkHalfClearance())
			: ActiveEnemySpawnClearanceRadius;
		int count = random.RandiRange(Mathf.Max(1, EnemyShipsMinPerChunk), Mathf.Max(1, EnemyShipsMaxPerChunk));
		List<EnemyShipController> spawnedShips = new();
		for (int i = 0; i < count; i++)
		{
			if (SpawnEntity(EnemyShipScene, _activeEntities, $"EnemyShip_{chunk.X}_{chunk.Y}_{i}", chunk, random, clearance) is EnemyShipController ship)
				spawnedShips.Add(ship);
		}
		if (spawnedShips.Count == 2)
		{
			spawnedShips[0].LinkWith(spawnedShips[1]);
			spawnedShips[1].LinkWith(spawnedShips[0]);
		}
	}

	private bool HasEnemyShipInChunk(Vector2I chunk)
	{
		foreach (Node child in _activeEntities.GetChildren())
		{
			if (child is EnemyShipController ship && GetChunk(ship.GlobalPosition) == chunk)
				return true;
		}
		return false;
	}

	private static bool IsInsideProtectionBox(Vector2I chunk, Vector2I playerChunk) => GetChunkDistance(chunk, playerChunk) <= 1;

	private static int GetChunkDistance(Vector2I first, Vector2I second) => Mathf.Max(Mathf.Abs(first.X - second.X), Mathf.Abs(first.Y - second.Y));

	private int GetPassiveCountForChunk()
	{
		float referenceArea = Mathf.Max(1.0f, ReferenceChunkSize * ReferenceChunkSize);
		float chunkArea = Mathf.Max(1.0f, ChunkSize.X * ChunkSize.Y);
		return Mathf.Max(0, Mathf.CeilToInt(PassiveEntitiesPerChunk * chunkArea / referenceArea * Mathf.Max(0.0f, PassiveDensityMultiplier)));
	}

	private float GetChunkHalfClearance() => Mathf.Min(ChunkSize.X, ChunkSize.Y) * 0.5f;

	private Node2D? SpawnEntity(PackedScene scene, Node2D container, string entityName, Vector2I chunk, RandomNumberGenerator random, float playerClearance)
	{
		Vector2? spawnPosition = null;
		for (int attempt = 0; attempt < 30; attempt++)
		{
			Vector2 position = new(
				chunk.X * ChunkSize.X + _randomChunkCoordinate(random, ChunkSize.X),
				chunk.Y * ChunkSize.Y + _randomChunkCoordinate(random, ChunkSize.Y));
			if (_player != null && position.DistanceTo(_player.GlobalPosition) < playerClearance)
				continue;
			if (IsSpawnPositionFree(container, position))
			{
				spawnPosition = position;
				break;
			}
		}
		if (spawnPosition == null)
			spawnPosition = FindChunkEdgeSpawnPosition(container, chunk, playerClearance);
		if (spawnPosition == null)
			return null;

		Node2D entity = scene.Instantiate<Node2D>();
		entity.Name = entityName;
		// Set transform before entering the tree so the first rendered frame is at its final position.
		entity.Position = container.ToLocal(spawnPosition.Value);
		if (entity is not EnemyShipController)
		{
			entity.Rotation = random.RandiRange(0, 3) * Mathf.Pi / 2.0f;
			if (random.Randf() < 0.5f)
				entity.Scale = new Vector2(-1.0f, 1.0f);
		}
		container.AddChild(entity);
		return entity;
	}

	private Vector2? FindChunkEdgeSpawnPosition(Node2D container, Vector2I chunk, float playerClearance)
	{
		Vector2 origin = new(chunk.X * ChunkSize.X, chunk.Y * ChunkSize.Y);
		Vector2[] candidates =
		{
			origin + new Vector2(40.0f, 40.0f),
			origin + new Vector2(ChunkSize.X - 40.0f, 40.0f),
			origin + new Vector2(40.0f, ChunkSize.Y - 40.0f),
			origin + new Vector2(ChunkSize.X - 40.0f, ChunkSize.Y - 40.0f)
		};
		foreach (Vector2 candidate in candidates)
		{
			if ((_player == null || candidate.DistanceTo(_player.GlobalPosition) >= playerClearance) && IsSpawnPositionFree(container, candidate))
				return candidate;
		}
		return null;
	}

	private static bool IsSpawnPositionFree(Node2D container, Vector2 position)
	{
		foreach (Node child in container.GetChildren())
		{
			if (child is Node2D other && other.GlobalPosition.DistanceTo(position) < 72.0f)
				return false;
		}
		return true;
	}

	private static float _randomChunkCoordinate(RandomNumberGenerator random, float dimension)
	{
		return random.RandfRange(32.0f, Mathf.Max(32.0f, dimension - 32.0f));
	}

	private Vector2I GetChunk(Vector2 position)
	{
		float width = Mathf.Max(64.0f, ChunkSize.X);
		float height = Mathf.Max(64.0f, ChunkSize.Y);
		return new Vector2I(Mathf.FloorToInt(position.X / width), Mathf.FloorToInt(position.Y / height));
	}

	private static ulong GetChunkSeed(Vector2I chunk)
	{
		ulong x = unchecked((ulong)(long)chunk.X);
		ulong y = unchecked((ulong)(long)chunk.Y);
		ulong seed = (x * 73856093UL) ^ (y * 19349663UL) ^ 0x9E3779B97F4A7C15UL;
		return seed == 0 ? 1UL : seed;
	}
}
