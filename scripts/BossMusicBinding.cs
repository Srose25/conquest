#nullable enable
using Godot;

/// <summary>
/// Add this as a child of a boss scene and assign that boss's own
/// DynamicMusicBossTheme resource. The boss can then call BeginBossMusic when
/// its encounter starts and EndBossMusic when it is defeated or abandoned.
/// This is a direct resource reference, never a name or array-index lookup.
/// </summary>
[GlobalClass]
public partial class BossMusicBinding : Node
{
	[Export] public DynamicMusicBossTheme? BossTheme { get; set; }

	public void BeginBossMusic()
	{
		if (BossTheme?.Track == null)
			return;
		(GetTree().GetFirstNodeInGroup("dynamic_music") as DynamicMusicController)?.RequestBossTheme(BossTheme);
	}

	public void EndBossMusic()
	{
		(GetTree().GetFirstNodeInGroup("dynamic_music") as DynamicMusicController)?.EndBossTheme();
	}
}
