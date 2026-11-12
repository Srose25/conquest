#nullable enable
using Godot;

/// <summary>Owns run XP, level thresholds, and automatic player upgrades.</summary>
public partial class ProgressionManager : Node
{
	[Signal] public delegate void ExperienceChangedEventHandler(int current, int required, int level);
	[Signal] public delegate void LevelChangedEventHandler(int level);

	// The first value is the XP needed to leave level 0; later values are the
	// thresholds documented in progression.md. Level 10 is the last authored tier.
	public static readonly int[] ExperienceThresholds = { 50, 100, 150, 200, 300, 600, 1200, 2000, 4000, 8000 };
	[Export] public int PlayerStatUpgradeStartLevel { get; set; } = 5;
	public int Level { get; private set; }
	public int Experience { get; private set; }
	public int ExperienceRequired => Level < ExperienceThresholds.Length ? ExperienceThresholds[Level] : ExperienceThresholds[^1] * 2;

	private PlayerController? _player;

	public override void _Ready()
	{
		AddToGroup("progression_manager");
		CallDeferred(MethodName.FindPlayer);
	}

	public void AddExperience(int amount)
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (amount <= 0 || _player?.IsDead == true)
			return;
		Experience += amount;
		while (Experience >= ExperienceRequired)
		{
			Experience -= ExperienceRequired;
			Level++;
			ApplyLevelUpUpgrade();
			EmitSignal(SignalName.LevelChanged, Level);
		}
		EmitSignal(SignalName.ExperienceChanged, Experience, ExperienceRequired, Level);
	}

	private void FindPlayer()
	{
		_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		EmitSignal(SignalName.ExperienceChanged, Experience, ExperienceRequired, Level);
	}

	private void ApplyLevelUpUpgrade()
	{
		if (_player == null)
			return;
		// The early run stays at its starting HP, shield, and bolt damage. All
		// player stat scaling begins together at level 5.
		bool canScaleStats = Level >= Mathf.Max(1, PlayerStatUpgradeStartLevel);
		_player.ApplyLevelUpUpgrade(
			canScaleStats ? 2.0f : 0.0f,
			canScaleStats ? 2.0f : 0.0f,
			canScaleStats ? 0.25f : 0.0f);
	}
}
