using Godot;

// A skeleton. For now it just stands there and absorbs sword hits so we can prove the
// damage/death pipeline. Chunk 4 lets knockback fling it; Chunk 5 adds chase + attack AI.
public partial class Enemy : Unit
{
	public override void _Ready()
	{
		Team = TeamId.Enemy;   // a skeleton is always on the enemy team
		base._Ready();
	}

	public override void _PhysicsProcess(double delta)
	{
		// Stationary for now — but already obey any knockback so Chunk 4 just works.
		float dt = (float)delta;
		DecayKnockback(dt);
		Velocity = KnockbackVelocity;
		MoveAndSlide();
	}
}
