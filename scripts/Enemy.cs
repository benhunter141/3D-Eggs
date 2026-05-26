using Godot;

// A skeleton. For now it just stands there, absorbs sword hits, and gets flung by
// knockback. Chunk 5 adds chase + attack AI.
public partial class Enemy : Unit
{
	public override void _Ready()
	{
		Team = TeamId.Enemy;   // a skeleton is always on the enemy team
		base._Ready();
	}

	public override void _PhysicsProcess(double delta)
	{
		// Stationary for now — but obey any knockback the sword applies.
		float dt = (float)delta;
		DecayKnockback(dt);
		Velocity = KnockbackVelocity;
		MoveAndSlide();
	}
}
