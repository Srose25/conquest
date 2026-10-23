# Conquest architectural plan

## 1. Scope and current starting point

This project is already configured as a Godot 4.7 C# project. The current prototype in `MapGen.cs` creates a `CharacterBody2D` player, asteroid bodies, and `Area2D` mining shots directly from one `Node2D`. That prototype is useful for validating movement and aiming, but the production structure should move reusable objects into scenes and keep map generation, spawning, progression, and UI in separate systems.

The game is treated as a 2D top-down arena/shooter with a procedurally generated world. “Drawn” is interpreted as “instantiated and visible in the scene tree.” The world may retain lightweight data for far-away entities while only instantiating their visual/physics nodes near the player.

Godot scenes are reusable templates: load a `PackedScene`, call `Instantiate()`, and add the resulting node to the tree. This is the right pattern for bullets, enemies, resources, and chunks because many copies can be created at runtime. See the [Godot scene and node instancing documentation](https://docs.godotengine.org/en/stable/tutorials/scripting/nodes_and_scene_instances.html).

## 2. Proposed scene and folder structure

```text
res://
  scenes/
	game/
	  Game.tscn
	  World.tscn
	  CameraRig.tscn
	  HUD.tscn
	  LevelUpPanel.tscn
	actors/
	  Player.tscn
	  ActiveEnemy.tscn
	  PassiveEntity.tscn
	combat/
	  Bullet.tscn
	  DamageHitbox.tscn        # optional reusable hitbox
	resources/
	  ResourceDrop.tscn
	world/
	  WorldChunk.tscn
	  StaticObstacle.tscn
  scripts/
	game/Game.cs
	game/GameState.cs
	world/WorldGenerator.cs
	world/WorldStreamer.cs
	world/EntityRecord.cs
	world/EntityRegistry.cs
	actors/PlayerController.cs
	actors/EnemyController.cs
	actors/PassiveEntityController.cs
	combat/BulletController.cs
	resources/ResourceDropController.cs
	progression/ProgressionManager.cs
	progression/BuffDefinition.cs
	ui/HudController.cs
  data/
	enemies/                 # Resource files for enemy definitions
	buffs/                   # Resource files for random buffs
	maps/                    # Optional generation presets
```

Use C# consistently because the existing project is configured with the C# feature and already has C# gameplay code. Use `.tscn` files for reusable node hierarchies and `.tres`/custom `Resource` files for data that should not be tied to a scene instance.

## 3. Top-level scene tree

Create `scenes/game/Game.tscn` with this initial tree:

```text
Game (Node)
├── GameState (Node)                 [GameState.cs]
├── World (Node2D)                   [World.tscn instance]
├── EntityRegistry (Node)            [EntityRegistry.cs]
├── ProgressionManager (Node)        [ProgressionManager.cs]
├── CameraRig (Node2D)               [CameraRig.tscn instance]
├── Player (Player.tscn instance)
└── UI (CanvasLayer)                 [HUD.tscn instance]
```

`Game.cs` owns the new-run lifecycle: create a seed, ask the world generator to build the initial map, place the player, connect signals, and start the game. It should coordinate systems, not contain enemy AI or individual bullet logic.

Create this scene in the editor with **Scene > New Scene**, choose a root `Node`, add the children above, attach the named scripts, then save it as `res://scenes/game/Game.tscn`. Make it the main scene in Project Settings, or update the existing `run/main_scene` setting after the scene exists.

## 4. World generation and entity representation

### `World.tscn`

```text
World (Node2D)
├── Background (Node2D or TileMapLayer)
├── Chunks (Node2D)
├── ActiveEntities (Node2D)
├── PassiveEntities (Node2D)
├── ResourceDrops (Node2D)
└── WorldBounds (Area2D or StaticBody2D)
```

`WorldGenerator.cs` creates a deterministic map from a run seed. It generates chunk coordinates, obstacles, passive entity records, and initial active enemy records. It then asks `WorldStreamer` to instantiate records that are inside the sensor region.

Use `WorldChunk.tscn` if the map is large. A chunk is a reusable `Node2D` containing its local obstacles and entity-record references. The generator should produce data first and nodes second. This makes the map reproducible and prevents distant entities from consuming physics and rendering resources.

Use an `EntityRecord` data object for persistent state such as:

```text
id, type, world_position, chunk_coordinate, is_active,
health, resource_yield, alive, spawned_node_reference (runtime only)
```

An entity record is not a scene node. It is the authoritative lightweight representation used when the entity is outside the active simulation radius. When the record enters the sensor, the registry instantiates the matching scene and copies the record into it. When it exits, the scene writes state back to the record and is freed or returned to a pool.

### Entity categories

* `PassiveEntity.tscn`: use `StaticBody2D` for an immobile collidable object, or `Area2D` if it is only a target/resource source. Add `Sprite2D`, `CollisionShape2D`, and optionally `VisibleOnScreenNotifier2D`. Its controller must not run movement or attack logic. On destruction/mining, emit `resources_dropped(amount, position)` and mark its record dead.

* `ActiveEnemy.tscn`: use `CharacterBody2D` for scripted movement and collision. Add `Sprite2D`, `CollisionShape2D`, an attack `Timer`, a detection `Area2D`, and a `VisibleOnScreenNotifier2D`. Its controller follows a small state machine such as `Idle`, `Chase`, `Attack`, and `Dead`.

* `StaticObstacle.tscn`: use `StaticBody2D` plus `Sprite2D` and `CollisionShape2D`. Static bodies are intended for environment objects that do not need dynamic movement; see the [Godot physics introduction](https://docs.godotengine.org/en/stable/tutorials/physics/physics_introduction.html).

The requirement that active enemies drop more resources than passive entities belongs in data, not duplicated code: each entity definition should expose a base resource yield or a min/max yield range. The drop system applies the definition and current level modifiers.

## 5. Camera window and sensor

Create `CameraRig.tscn`:

```text
CameraRig (Node2D)
├── Camera2D (Camera2D)              # visible window
└── ActivationSensor (Area2D)         # larger simulation/draw radius
	└── CollisionShape2D (RectangleShape2D)
```

Make `CameraRig` follow the player by setting its position to the player position or by making it a child of the player. Prefer a separate rig node so camera smoothing, shake, limits, and sensor sizing remain independent from player movement. Set `Camera2D` limits from the generated map bounds.

The `ActivationSensor` is the authoritative “near player” test. Its rectangle should be larger than the camera viewport by a configurable margin, for example one to two screen widths on each side. On `body_entered`/`body_exited` or through the registry’s spatial query, call `WorldStreamer.Activate(record)` and `WorldStreamer.Deactivate(record)`.

Do not rely only on `VisibleOnScreenNotifier2D` for simulation. It is useful for visual lifecycle callbacks, but its signals represent render visibility and require the `CanvasItem` to be visible. See the [VisibleOnScreenNotifier2D documentation](https://docs.godotengine.org/en/4.4/classes/class_visibleonscreennotifier2d.html). The larger `Area2D` sensor directly expresses the requested window-versus-sensor behavior and can keep entities active slightly before they become visible.

When an entity leaves the sensor:

1. Stop its AI/attack timers and physics processing.
2. Save its current state to `EntityRecord`.
3. Free it or return it to an object pool.
4. Keep the record in the registry so it can be instantiated again when the player returns.

For a first playable version, freeing and re-instantiating is simpler. Add pooling only after profiling shows it is necessary.

## 6. Player, cursor, bullets, and combat

### `Player.tscn`

```text
Player (CharacterBody2D)
├── Sprite2D
├── CollisionShape2D
├── Muzzle (Marker2D)
├── FireCooldown (Timer)
└── AimPoint (Node2D)                  # optional debug/visual aim marker
```

Attach `PlayerController.cs`. Define input actions in Project Settings > Input Map: `move_up` (W), `move_down` (S), `move_left` (A), `move_right` (D), and `fire` (left mouse button). Read movement and update `Velocity` in `_PhysicsProcess`, then call `MoveAndSlide`; this is the intended use of `CharacterBody2D` for code-controlled movement. See [Using CharacterBody2D](https://docs.godotengine.org/en/4.7/tutorials/physics/using_character_body_2d.html).

Get the cursor position with `GetGlobalMousePosition()`. Rotate the player toward the cursor using `GlobalPosition.DirectionTo(mouse_position).Angle()`; add an art correction only if the ship texture does not face along the positive X axis. The player should not be rotated toward raw screen coordinates without converting them to world coordinates.

### `Bullet.tscn`

```text
Bullet (Area2D)
├── Sprite2D or Polygon2D
├── CollisionShape2D
└── VisibleOnScreenNotifier2D
```

`BulletController.cs` receives a normalized direction and speed from the player. Move it in `_PhysicsProcess`, detect enemy/passive targets using collision layers and masks, emit a hit signal, and call `QueueFree()` on impact. Connect `screen_exited` from `VisibleOnScreenNotifier2D` to despawn bullets after they leave the camera window. Add a lifetime timeout as a safety fallback for bullets that never enter a visible viewport or when camera behavior changes. `Area2D` is appropriate for overlap detection and emits `body_entered`; see the [Area2D reference](https://docs.godotengine.org/en/stable/classes/class_area2d.html).

Recommended initial collision layers:

```text
1 Player
2 World obstacles
3 Enemies/passive targets
4 Player bullets
5 Resource drops
```

Adjust masks so bullets detect intended targets and do not accidentally hit resource drops or the player.

## 7. Resources and drops

### `ResourceDrop.tscn`

```text
ResourceDrop (Area2D)
├── Sprite2D
├── CollisionShape2D
└── VisibleOnScreenNotifier2D       # optional
```

When an entity dies or is mined, it calls a drop service or emits a signal. The resource manager instantiates one or more drops under `World/ResourceDrops`. A drop has a value/type and emits `collected(value)` when the player enters it. The player or resource manager updates the run total.

Keep drop spawning separate from entity death so passive and active entities can share the same collection behavior while using different yield values. If drops can remain in the world for a long time, give them records too; otherwise, they may simply be active scene instances within the sensor.

## 8. Progression, pause, buffs, and difficulty

`ProgressionManager.cs` owns:

```text
level, current_resources, resources_required,
difficulty_scalar, enemy_spawn_interval, pending_level_up
```

When collected resources reach the threshold:

1. Emit `level_up_started`.
2. Pause gameplay while keeping the UI processable.
3. Show `LevelUpPanel.tscn` with a random selection or random single buff.
4. Apply the selected `BuffDefinition` to a player stat or system modifier.
5. Increment level and recalculate difficulty.
6. Reduce enemy spawn interval and/or increase enemy stats using a bounded formula.
7. Hide the panel and resume gameplay.

Use a `CanvasLayer` for `HUD.tscn` and `LevelUpPanel.tscn` so UI stays fixed while the world camera moves. Pause the game with a deliberate pause policy: set the gameplay tree to process only when not paused, and set the UI panel to `Process Mode = When Paused`. Godot `Timer` nodes are suitable for enemy spawn and attack cadence because they emit `timeout`; see the [Timer documentation](https://docs.godotengine.org/en/stable/classes/class_timer.html).

Suggested difficulty formula for a first pass:

```text
spawn_interval(level) = max(min_interval, base_interval * pow(interval_multiplier, level - 1))
enemy_count(level)    = floor(base_count + count_growth * (level - 1))
enemy_speed(level)    = base_speed * (1.0 + speed_growth * (level - 1))
```

Keep these values in a configuration resource so balancing does not require code changes. Difficulty should be applied by the spawn director and enemy definitions, not by editing every instantiated enemy directly.

## 9. Spawn and simulation control

Add a `SpawnDirector` node under `World` or `Game` with a `Timer` child. It creates active enemy records or active enemy instances at valid positions outside a configurable minimum distance from the player. Its interval is read from `ProgressionManager` and updated after each level-up.

Use a `GameState` node or autoload only for truly global run state. It should expose signals such as:

```text
resources_changed(total)
level_up_started()
level_changed(level)
run_paused(reason)
run_resumed()
```

Use signals between systems rather than having the player directly modify HUD labels or the map generator directly open the buff panel. Godot signals are intended to reduce coupling between nodes; see [Using signals](https://docs.godotengine.org/en/stable/getting_started/step_by_step/signals.html).

## 10. Build order for the first implementation

1. Split the current prototype into `Game.tscn`, `World.tscn`, `Player.tscn`, and `Bullet.tscn`; preserve the current WASD, cursor aim, and shooting behavior.
2. Add collision layers/masks and bullet impact signals.
3. Create `PassiveEntity.tscn`, `ActiveEnemy.tscn`, and `ResourceDrop.tscn`; verify that both entity types can produce different resource yields.
4. Add `EntityRecord`, `EntityRegistry`, and deterministic `WorldGenerator`; generate a fixed-size world from a seed.
5. Add `CameraRig` with `Camera2D` and a larger `ActivationSensor`; test activation/deactivation while moving across chunk boundaries.
6. Add `SpawnDirector` and active enemy behavior. Keep passive entities stationary and non-attacking.
7. Add resource totals, HUD, level-up pause, random buffs, and difficulty scaling.
8. Profile entity counts, physics time, and allocations before adding pooling or more complex chunk streaming.

## 11. Areas that need decisions or further research

The requirements leave several gameplay and technical choices open:

* Is the world finite, wraparound, or effectively endless? This determines whether chunks can be discarded permanently and how camera limits work.
* What exactly counts as an “active entity”? This plan treats active as simulated enemy/target behavior and passive as stationary, non-attacking targets.
* Should passive entities be visible immediately across the whole generated map, or only when inside the sensor? The plan uses sensor-based instantiation for both categories, with persistent records outside it.
* What resource types exist, and is the level-up threshold fixed, linear, or exponential?
* Does “random buff” mean one automatically applied buff or a choice among several cards/options?
* Can enemies attack the player directly, shoot projectiles, collide, or use melee range? The enemy scene can support all three, but the attack contract should be chosen before implementing AI.
* Is multiplayer, saving/loading, or deterministic replay required? These would affect the authority of `EntityRecord`, RNG usage, and whether an autoload/state serializer is needed.
* What is the target platform and expected maximum entity count? That determines whether simple instantiate/free is sufficient or whether pooling, multithreaded generation, and chunk budgets are necessary.

## 12. Useful references

* [Godot nodes and scene instances](https://docs.godotengine.org/en/stable/tutorials/scripting/nodes_and_scene_instances.html)
* [Godot CharacterBody2D movement](https://docs.godotengine.org/en/4.7/tutorials/physics/using_character_body_2d.html)
* [Godot Area2D](https://docs.godotengine.org/en/stable/classes/class_area2d.html)
* [Godot Timer](https://docs.godotengine.org/en/stable/classes/class_timer.html)
* [Godot VisibleOnScreenNotifier2D](https://docs.godotengine.org/en/4.4/classes/class_visibleonscreennotifier2d.html)
* [Godot physics introduction](https://docs.godotengine.org/en/stable/tutorials/physics/physics_introduction.html)
* [Godot signals](https://docs.godotengine.org/en/stable/getting_started/step_by_step/signals.html)
