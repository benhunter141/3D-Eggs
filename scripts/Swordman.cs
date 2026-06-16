using Godot;

// An enemy swordman. Like the skeleton it chases the nearest player-team unit and melees
// on contact (plain damage, NO knockback — only the captain's pike/sword shoves), but it
// is built to defeat a braced pike wall in two ways:
//   1. CHARGE BURST — the instant it acquires a target it sprints (ChargeMultiplier) for
//      ChargeDuration, closing ground fast before settling to its walk speed.
//   2. FLANK-OFFSET APPROACH — instead of running straight down a pike's throat it aims at
//      a point offset to one side of the target (its picked flank), curling around the
//      front of the wall; the offset fades to zero as it closes so it still lands the hit.
// Still obeys any knockback the captain applies, decaying it each frame (shared Unit logic).
public partial class Swordman : Unit
{
	[Export] public float MoveSpeed = 4.5f;        // walk/chase speed (m/s) once the charge fades
	[Export] public float ChargeMultiplier = 2.0f; // speed multiplier during the acquire charge burst
	[Export] public float ChargeDuration = 0.8f;   // seconds the charge burst lasts after acquiring
	[Export] public float AttackRange = 1.6f;      // stop & swing once this close to the target
	[Export] public float AttackDamage = 10.0f;    // damage per melee hit (no knockback)
	[Export] public float AttackCooldown = 1.0f;   // seconds between melee hits
	[Export] public float TurnLerp = 10.0f;        // how fast it rotates to face its target

	// --- Flanking ---
	[Export] public float FlankOffset = 2.5f;      // how far to one side the approach point sits when far out
	[Export] public float FlankFalloff = 5.0f;     // offset fades from full to zero over this closing distance

	private float _attackTimer; // counts down; > 0 blocks the next melee hit
	private float _chargeTimer;  // > 0 while the charge burst is active
	private bool _hadTarget;     // last frame's acquire state, to detect a fresh acquire
	private int _flankSide = 1;  // +1 or -1: which way this swordman curls around the wall

	public override void _Ready()
	{
		Team = TeamId.Enemy;   // a swordman is always on the enemy team
		base._Ready();
		// Pick a flank side once so the screen of swordmen splits around the wall instead
		// of all curling the same way.
		_flankSide = GD.RandRange(0, 1) == 0 ? -1 : 1;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		DecayKnockback(dt);

		if (IsDead)
		{
			// Dead bodies are freed shortly after; ride out any lingering shove meanwhile.
			Velocity = KnockbackVelocity;
			MoveAndSlide();
			return;
		}

		if (_attackTimer > 0f)
			_attackTimer -= dt;
		if (_chargeTimer > 0f)
			_chargeTimer -= dt;

		Vector3 chase = Vector3.Zero;
		// Throttled nearest-foe pick (M5): re-scan only every few frames, but keep chasing the
		// cached target's live position every frame.
		if (ShouldRescanTarget())
			CachedTarget = UnitRegistry.FindNearestOpponent(Team, GlobalPosition);
		Unit target = LiveTarget;

		// Fresh acquire (had nobody last frame, have someone now) -> kick off a charge burst.
		if (target != null && !_hadTarget)
			_chargeTimer = ChargeDuration;
		_hadTarget = target != null;

		if (target != null)
		{
			Vector3 toTarget = target.GlobalPosition - GlobalPosition;
			toTarget.Y = 0f;
			float dist = toTarget.Length();

			// Always face the real target so the swing lands even while curling around it.
			FaceTowards(target.GlobalPosition, dt);

			if (dist > AttackRange)
			{
				float speed = MoveSpeed * (_chargeTimer > 0f ? ChargeMultiplier : 1f);
				chase = ApproachDir(toTarget, dist) * speed;
			}
			else if (_attackTimer <= 0f)
			{
				// In range: melee on cooldown. No knockback for swordman hits.
				target.TakeDamage(AttackDamage);
				_attackTimer = AttackCooldown;
				GD.Print($"[Swordman] {Name} hit {target.Name} for {AttackDamage}");
			}
		}

		// A strong shove takes over (ride it out and slow down); otherwise chase plus any
		// lingering knockback that's already small enough to walk through.
		Velocity = IsKnockbackControlled ? KnockbackVelocity : chase + KnockbackVelocity;
		MoveAndSlide();
		ResolveKnockbackBounce();   // pinball: pass on / bounce a real shove off whatever we rammed
	}

	// Direction to move: the target's direction nudged sideways by a flank offset that's
	// full at range and fades to zero near the target, so the swordman curls around the
	// wall's front yet still converges to land its hit.
	private Vector3 ApproachDir(Vector3 toTarget, float dist)
	{
		Vector3 dir = toTarget.Normalized();
		// Perpendicular on the flat plane (rotate dir 90° about +Y).
		Vector3 right = new Vector3(dir.Z, 0f, -dir.X);
		float flankWeight = Mathf.Clamp((dist - AttackRange) / FlankFalloff, 0f, 1f);
		Vector3 aim = toTarget + right * (_flankSide * FlankOffset * flankWeight);
		aim.Y = 0f;
		return aim.LengthSquared() > 0.0001f ? aim.Normalized() : dir;
	}

	// Smoothly rotate to face a world point on the flat plane (forward is -Z).
	private void FaceTowards(Vector3 worldPos, float dt)
	{
		Vector3 flat = new Vector3(worldPos.X, GlobalPosition.Y, worldPos.Z);
		if (GlobalPosition.DistanceSquaredTo(flat) < 0.0025f)
			return;

		Vector3 dir = flat - GlobalPosition;
		float desiredYaw = Mathf.Atan2(-dir.X, -dir.Z);
		float t = 1f - Mathf.Exp(-TurnLerp * dt);
		Vector3 rot = Rotation;
		rot.Y = Mathf.LerpAngle(rot.Y, desiredYaw, t);
		Rotation = rot;
	}
}
