using Godot;

/// <summary>Common movement contract for collectible drops attracted by the player.</summary>
public interface IMagnetizableDrop
{
	void PullToward(Vector2 target, float speed, float delta);
}
