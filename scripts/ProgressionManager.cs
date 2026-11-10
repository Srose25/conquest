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
	public int Level { get; private set; }
	public int Experience { get; private set; }
	public int ExperienceRequired => Level < ExperienceThresholds.Length ? ExperienceThresholds[Level] : ExperienceThresholds[^1] * 2;

	private PlayerController? _player;

	public override void _Ready()
	{
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
		// Automatic, deliberately modest upgrades: +2 max/current HP, +2 shield,
		// and +0.25 bullet damage per level. Values are documented through level 10.
		_player.ApplyLevelUpUpgrade(2.0f, 2.0f, 0.25f);
	}
}
