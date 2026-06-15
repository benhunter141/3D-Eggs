using Godot;

// A skeleton. Chases the nearest enemy-team unit (the player, for now) and melees
// it on contact — plain damage, no knockback (only the player's sword shoves).
// Still obeys any knockback the sword applies, decaying it each frame.
public partial class Enemy : Unit
{
	[Export] public float MoveSpeed = 4.0f;       // chase speed (m/s)
	[Export] public float AttackRange = 1.6f;     // stop & swing once this close
	[Export] public float AttackDamage = 10.0f;   // damage per melee hit
	[Export] public float AttackCooldown = 1.0f;  // seconds between melee hits
	[Export] public float TurnLerp = 10.0f;       // how fast it rotates to face its target

	private float _attackTimer; // counts down; > 0 blocks the next melee hit

	public override void _Ready()
	{
		Team = TeamId.Enemy;   // a skeleton is always on the enemy team
		base._Ready();
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		DecayKnockback(dt);

		if (IsDead)
		{
			// Dead skeletons are usually freed, but ride out any lingering shove
			// during the frame before QueueFree takes effect.
			Velocity = KnockbackVelocity;
			MoveAndSlide();
			return;
		}

		if (_attackTimer > 0f)
			_attackTimer -= dt;

		Vector3 chase = Vector3.Zero;
		Unit target = UnitRegistry.FindNearestOpponent(Team, GlobalPosition);
		if (target != null)
		{
			Vector3 toTarget = target.GlobalPosition - GlobalPosition;
			toTarget.Y = 0f;
			float dist = toTarget.Length();

			FaceTowards(target.GlobalPosition, dt);

			if (dist > AttackRange)
			{
				chase = toTarget.Normalized() * MoveSpeed;
			}
			else if (_attackTimer <= 0f)
			{
				// In range: melee on cooldown. No knockback for skeleton hits.
				target.TakeDamage(AttackDamage);
				_attackTimer = AttackCooldown;
				GD.Print($"[Enemy] {Name} hit {target.Name} for {AttackDamage}");
			}
		}

		// Chase plus any lingering sword knockback.
		Velocity = chase + KnockbackVelocity;
		MoveAndSlide();
	}

	// Smoothly rotate to face a world point on the flat plane (forward is -Z).
	private void FaceTowards(Vector3 worldPos, float dt)
	{
		Vector3 flat = new Vector3(worldPos.X, GlobalPosition.Y, worldPos.Z);
		if (GlobalPosition.DistanceSquaredTo(flat) < 0.0025f)
			return;

		// Forward is -Z, so negate the delta: atan2(dx,dz) would aim our BACK at the target.
		Vector3 dir = flat - GlobalPosition;
		float desiredYaw = Mathf.Atan2(-dir.X, -dir.Z);
		float t = 1f - Mathf.Exp(-TurnLerp * dt);
		Vector3 rot = Rotation;
		rot.Y = Mathf.LerpAngle(rot.Y, desiredYaw, t);
		Rotation = rot;
	}
}
