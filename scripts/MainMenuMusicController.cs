#nullable enable
using Godot;

/// <summary>Inspector configuration for the title-screen music player.</summary>
public partial class MainMenuMusicController : AudioStreamPlayer
{
	[ExportCategory("Main Menu Music")]
	[Export] public AudioStream? MenuTrack { get; set; }
	[Export(PropertyHint.Range, "0,10,0.05")] public float FadeInSeconds { get; set; } = 2.0f;
}
