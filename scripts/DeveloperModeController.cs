#nullable enable
using System;
using Godot;

/// <summary>
/// Debug-only gameplay helpers. The control surface is intentionally absent
/// from Release builds, while Godot debug runs can enable it from Settings.
/// </summary>
public partial class DeveloperModeController : Node
{
	public bool Enabled { get; private set; }
	public bool EnemyRaycastVisualizationEnabled { get; private set; }
#if DEBUG
	private Label? _raycastStatusLabel;
	private float _raycastStatusFadeSeconds;
	private const float RaycastStatusFadeDurationSeconds = 2.5f;
#endif

	public override void _Ready()
	{
		// The confirmation must still fade while the pause menu has stopped the
		// gameplay tree.
		ProcessMode = ProcessModeEnum.Always;
	}

	public override void _Process(double delta)
	{
#if DEBUG
		if (_raycastStatusLabel == null || EnemyRaycastVisualizationEnabled || _raycastStatusFadeSeconds <= 0.0f)
			return;

		_raycastStatusFadeSeconds = Mathf.Max(0.0f, _raycastStatusFadeSeconds - (float)delta);
		float alpha = 0.25f * (_raycastStatusFadeSeconds / RaycastStatusFadeDurationSeconds);
		_raycastStatusLabel.Modulate = new Color(1.0f, 1.0f, 1.0f, alpha);
		if (_raycastStatusFadeSeconds <= 0.0f)
			_raycastStatusLabel.Visible = false;
#endif
	}

	public static bool IsAvailable
	{
		get
		{
#if DEBUG
			return OS.IsDebugBuild();
#else
			return false;
#endif
		}
	}

	public void SetEnabled(bool enabled)
	{
		Enabled = IsAvailable && enabled;
		if (!Enabled)
		{
			EnemyRaycastVisualizationEnabled = false;
			#if DEBUG
			if (_raycastStatusLabel != null)
				_raycastStatusLabel.Visible = false;
			#endif
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
#if DEBUG
		if (!Enabled || @event is not InputEventKey key || !key.Pressed || key.Echo)
			return;
		if (key.Keycode == Key.F6)
		{
			EnemyRaycastVisualizationEnabled = !EnemyRaycastVisualizationEnabled;
			QueueEnemyRaycastRedraws();
			ShowRaycastStatus();
			GetViewport().SetInputAsHandled();
			return;
		}
		PlayerController? player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (player == null || player.IsDead)
			return;

		switch (key.Keycode)
		{
			case Key.F1:
				player.DebugTakeDamageToLowHealth();
				break;
			case Key.F2:
				// Use the ordinary pickup path once, including its repair and SFX.
				player.CollectRepairPellet(1);
				break;
			case Key.F3:
				player.DebugPopShieldsWithBoltDamage();
				break;
			case Key.F4:
				player.DebugSelfDestruct();
				break;
			case Key.F5:
				SimulateXpCollectionToNextLevel(player);
				break;
			default:
				return;
		}
		GetViewport().SetInputAsHandled();
#endif
	}

#if DEBUG
	/// <summary>Uses the normal progression path to collect exactly the XP still
	/// needed for the next level, including the usual XP-collection sound.</summary>
	private void SimulateXpCollectionToNextLevel(PlayerController player)
	{
		ProgressionManager? progression = GetTree().CurrentScene?.GetNodeOrNull<ProgressionManager>("ProgressionManager");
		if (progression == null)
			return;
		int amount = Mathf.Max(1, progression.ExperienceRequired - progression.Experience);
		Action collectXp = () =>
		{
			progression.AddExperience(amount);
			SoundEffectsController.PlayOneShotWithRandomPitch(
				player,
				SoundEffectsController.Effect.XpCollect,
				-50.0f,
				50.0f);
		};
		ExperienceBarController? experienceBar = GetTree().CurrentScene?.GetNodeOrNull<ExperienceBarController>("UI/ExperienceBar");
		if (experienceBar == null || !experienceBar.PlayLevelUpPreview(0.125f, collectXp))
			collectXp();
	}

	private void QueueEnemyRaycastRedraws()
	{
		foreach (Node node in GetTree().GetNodesInGroup("enemy_ship"))
		{
			if (node is EnemyShipController enemy && IsInstanceValid(enemy))
				enemy.QueueRedraw();
		}
	}

	private void ShowRaycastStatus()
	{
		if (_raycastStatusLabel == null)
		{
			CanvasLayer layer = new()
			{
				Name = "RaycastStatusLayer",
				Layer = 100
			};
			AddChild(layer);
			_raycastStatusLabel = new Label
			{
				Name = "RaycastStatus",
				AnchorLeft = 1.0f,
				AnchorTop = 0.0f,
				AnchorRight = 1.0f,
				AnchorBottom = 0.0f,
				OffsetLeft = -310.0f,
				OffsetTop = 22.0f,
				OffsetRight = -24.0f,
				OffsetBottom = 48.0f,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Modulate = new Color(1.0f, 1.0f, 1.0f, 0.25f)
			};
			FontFile? oxanium = GD.Load<FontFile>("res://Fonts/Oxanium/Oxanium-VariableFont_wght.ttf");
			if (oxanium != null)
				_raycastStatusLabel.AddThemeFontOverride("font", oxanium);
			_raycastStatusLabel.AddThemeFontSizeOverride("font_size", 16);
			layer.AddChild(_raycastStatusLabel);
		}

		_raycastStatusLabel.Text = $"RAYCAST VISUALS: {(EnemyRaycastVisualizationEnabled ? "ON" : "OFF")}";
		_raycastStatusFadeSeconds = EnemyRaycastVisualizationEnabled ? 0.0f : RaycastStatusFadeDurationSeconds;
		_raycastStatusLabel.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.25f);
		_raycastStatusLabel.Visible = true;
	}
#endif
}
