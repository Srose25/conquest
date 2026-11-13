#nullable enable
using Godot;

/// <summary>
/// One matched exploration/combat OST pair. This resource is created directly
/// by DynamicMusicController's Add Pair button, so no empty resource slot must
/// be manually created in the Inspector.
/// </summary>
[GlobalClass]
public partial class DynamicMusicTrackPair : Resource
{
	// Empty deliberately means silence. A combat-only pair is valid.
	[Export] public AudioStream? OutOfCombatTrack { get; set; }
	[Export] public AudioStream? CombatTrack { get; set; }
	[Export] public bool AlwaysLoadFirst { get; set; }

	public bool IsComplete => CombatTrack != null;
}
