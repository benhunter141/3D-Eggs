using Godot;
using System.Collections.Generic;

// A cast fireball (M15, Chunk 67) — the player's granted MAGIC ability. Mirrors Stone/Arrow: flies
// from where it was cast (along the caster's facing), proximity-hits any enemy-team Unit within
// HitRadius, then frees itself; also frees after MaxLifetime so stray bolts don't accumulate. The
// difference is flavour, not mechanics: its Damage is baked in at cast time by Player.ScaledMagicDamage
// (so INTELLIGENCE scales it, not Strength), and it carries an optional Knockback (default 0, like
// stones/arrows — only the captain's sword shoves unless a level tunes this up). Owner team is set on
// launch so a bolt never hits the side that cast it.
public partial class Fireball : Node3D
{
	[Export] public float Speed = 20.0f;       // flight speed (m/s)
	[Export] public float Damage = 16.0f;      // set by the caster at launch (already Int-scaled)
	[Export] public float Knockback = 0.0f;    // optional shove on hit (0 = none, like stones/arrows)
	[Export] public float HitRadius = 0.8f;    // how close to a unit counts as a hit
	[Export] public float MaxLifetime = 2.5f;  // seconds before it gives up and frees
	[Export] public float Gravity = Ballistics.Gravity;  // (ballistic only) downward accel of a lobbed bolt

	private Vector3 _direction = Vector3.Forward;
	private Vector3 _velocity;            // (ballistic only) live velocity, integrated under gravity
	private bool _ballistic;             // grounded levels lob an arc; flat levels skim level
	private Unit.TeamId _ownerTeam = Unit.TeamId.Player;
	private float _life;

	// Flat-level path: straight, level flight along `direction` (flattened onto the ground plane).
	public void Launch(Vector3 direction, Unit.TeamId ownerTeam)
	{
		direction.Y = 0f;
		if (direction.LengthSquared() > 0.0001f)
			_direction = direction.Normalized();
		_ownerTeam = ownerTeam;
		_ballistic = false;
	}

	// Grounded-level path (mirrors Stone/Arrow, Chunk 63): lob a gravity arc from `from` through the
	// target's REAL position (height included). Set GlobalPosition to `from` too — the arc is relative to it.
	public void LaunchBallistic(Vector3 from, Vector3 to, Unit.TeamId ownerTeam)
	{
		_ownerTeam = ownerTeam;
		_ballistic = true;
		_velocity = Ballistics.SolveArcVelocity(from, to, Speed, Gravity);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		_life += dt;
		if (_life >= MaxLifetime)
		{
			QueueFree();
			return;
		}

		if (_ballistic)
		{
			_velocity.Y -= Gravity * dt;
			GlobalPosition += _velocity * dt;

			// A lobbed bolt that drops below the terrain has buried itself — free it. No-op off a
			// terrain level (no active terrain).
			if (GlobalPosition.Y < Scenery.SampleActiveHeight(GlobalPosition.X, GlobalPosition.Z, float.NegativeInfinity))
			{
				QueueFree();
				return;
			}
		}
		else
		{
			GlobalPosition += _direction * Speed * dt;
		}

		// Proximity hit test against living enemy-team units (cheap, no physics layers). Scans only the
		// opposing team's registry bucket — no per-frame group marshal.
		float hitSq = HitRadius * HitRadius;
		IReadOnlyList<Unit> foes = UnitRegistry.Opponents(_ownerTeam);
		for (int i = 0; i < foes.Count; i++)
		{
			Unit u = foes[i];
			if (u != null && IsInstanceValid(u) && !u.IsDead
				&& GlobalPosition.DistanceSquaredTo(u.GlobalPosition) <= hitSq)
			{
				if (Knockback > 0f)
					u.TakeDamage(Damage, u.GlobalPosition - GlobalPosition, Knockback);
				else
					u.TakeDamage(Damage);   // magic bolt: damage only by default
				GD.Print($"[Fireball] hit {u.Name} for {Damage:0.0}");
				QueueFree();
				return;
			}
		}
	}
}
