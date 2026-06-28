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

	// The speed this enemy actually moves at this frame. Defaults to the plain MoveSpeed (so every existing
	// foe is byte-identical), but a buffable archetype like the rallied Legionary (Chunk 91) overrides it to
	// fold in a temporary aura without touching the shared chase code. Used for both chase + march velocity.
	protected virtual float EffectiveMoveSpeed => MoveSpeed;

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
			Velocity = ComposeMovement(KnockbackVelocity, dt);
			MoveAndSlide();
			return;
		}

		if (_attackTimer > 0f)
			_attackTimer -= dt;

		Vector3 chase = Vector3.Zero;
		// Throttled nearest-foe pick (M5): re-scan only every few frames, but keep chasing the
		// cached target's live position every frame.
		if (ShouldRescanTarget())
			CachedTarget = ScanNearestOpponent();
		Unit target = LiveTarget;
		if (target != null)
		{
			Vector3 toTarget = target.GlobalPosition - GlobalPosition;
			toTarget.Y = 0f;
			float dist = toTarget.Length();

			FaceTowards(target.GlobalPosition, dt);

			if (dist > AttackRange)
			{
				chase = toTarget.Normalized() * EffectiveMoveSpeed;
			}
			else if (_attackTimer <= 0f)
			{
				// In range: melee on cooldown.
				MeleeStrike(target);
				_attackTimer = AttackCooldown;
			}
		}
		else if (MarchMode)
		{
			// Football-pitch auto-battler (Chunk 42): no foe in aggro range — advance to the enemy endzone.
			chase = MarchVelocity(EffectiveMoveSpeed);
			FaceTowards(GlobalPosition + MarchGoalDirection, dt);
		}

		// A strong shove takes over (ride it out and slow down); as it decays the unit eases its
		// chase back in (OwnMovementScale) instead of snapping it on — no spurious second bump.
		// ComposeMovement folds in gravity on grounded terrain (flat levels: Y stays 0, unchanged).
		Velocity = ComposeMovement(chase * OwnMovementScale + KnockbackVelocity, dt);
		MoveAndSlide();
		ResolveKnockbackBounce();   // pinball: pass on / bounce a real shove off whatever we rammed
	}

	// The actual melee hit, once in range and off cooldown. Default = plain damage, no knockback
	// (the skeleton rule — only the player's sword shoves). Virtual so a heavy archetype like the
	// Orc Brute (Chunk 89) can override it to club its victim back. Kept byte-identical for the base.
	protected virtual void MeleeStrike(Unit target)
	{
		target.TakeDamage(AttackDamage);
		GD.Print($"[Enemy] {Name} hit {target.Name} for {AttackDamage}");
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
