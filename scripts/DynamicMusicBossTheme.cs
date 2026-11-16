#nullable enable
using Godot;

/// <summary>
/// A boss-owned music cue. Bosses can assign one of these resources directly,
/// keeping their theme and tempo together instead of relying on a global BPM.
/// </summary>
[GlobalClass]
public partial class DynamicMusicBossTheme : Resource
{
	// A readable label for the Inspector and for the boss that owns this cue.
	// It does not drive playback; the boss holds the resource itself, avoiding
	// fragile matching by filenames or array index.
	[Export] public string BossLabel { get; set; } = "Boss Theme";
	[Export] public AudioStream? Track { get; set; }
	[Export(PropertyHint.Range, "30,300,0.01")] public float TempoBpm { get; set; } = 135.0f;
}
