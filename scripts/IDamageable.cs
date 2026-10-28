#nullable enable
using Godot;

/// <summary>Implemented by entities that can be damaged by player weapons.</summary>
public interface IDamageable
{
	void TakeDamage(float amount, Node? source = null);
}
