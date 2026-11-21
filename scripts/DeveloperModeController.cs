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
	}

	public override void _UnhandledInput(InputEvent @event)
	{
#if DEBUG
		if (!Enabled || @event is not InputEventKey key || !key.Pressed || key.Echo)
			return;
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
#endif
}
