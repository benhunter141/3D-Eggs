using Godot;
using System.Collections.Generic;

// An arrow fired by a bowman. Mirrors Stone (Chunk 8): flies in a straight line from where
// it was loosed (aimed at the target's position at fire time) and, on coming within HitRadius
// of any opposite-team Unit, deals damage with NO knockback (only the captain's pike/sword
// shoves) and frees itself. Also frees after MaxLifetime so stray arrows don't accumulate.
// Owner team is set on launch so an arrow never hits the side that fired it.
// (A later cleanup may unify Stone + Arrow — kept separate for now.)
public partial class Arrow : Node3D
{
	[Export] public float Speed = 24.0f;       // flight speed (m/s) — faster than a lobbed stone
	[Export] public float Damage = 10.0f;      // damage dealt on hit (no knockback)
	[Export] public float HitRadius = 0.6f;    // how close to a unit counts as a hit
	[Export] public float MaxLifetime = 2.5f;  // seconds before it gives up and frees
	[Export] public float Gravity = Ballistics.Gravity;  // (ballistic only) downward accel of a lobbed arrow

	private Vector3 _direction = Vector3.Forward;
	private Vector3 _velocity;            // (ballistic only) live velocity, integrated under gravity
	private bool _ballistic;             // grounded levels lob an arc; flat levels skim level
	private Unit.TeamId _ownerTeam = Unit.TeamId.Enemy;
	private float _life;

	// Aim the arrow before it's added to the tree (or right after). Direction is flattened
	// onto the ground plane so the arrow skims at its launch height; the mesh is also yawed
	// to point the way it flies. Flat-level path — straight, level flight.
	public void Launch(Vector3 direction, Unit.TeamId ownerTeam)
	{
		direction.Y = 0f;
		if (direction.LengthSquared() > 0.0001f)
		{
			_direction = direction.Normalized();
			// Point the shaft along flight (mesh long axis is -Z, matching unit forward).
			Rotation = new Vector3(0f, Mathf.Atan2(-_direction.X, -_direction.Z), 0f);
		}
		_ownerTeam = ownerTeam;
		_ballistic = false;
	}

	// Grounded-level path (Chunk 63): lob a gravity arc from `from` that passes through the
	// target's REAL position (height included), so up/downhill shots connect. Set GlobalPosition
	// to `from` too — the arc is solved relative to it. The shaft tips along its arc each frame.
	public void LaunchBallistic(Vector3 from, Vector3 to, Unit.TeamId ownerTeam)
	{
		_ownerTeam = ownerTeam;
		_ballistic = true;
		_velocity = Ballistics.SolveArcVelocity(from, to, Speed, Gravity);
		PointAlong(_velocity);
	}

	// Orient the shaft (mesh long axis is -Z) along a flight direction, including pitch so a
	// descending arrow tips downward.
	private void PointAlong(Vector3 dir)
	{
		if (dir.LengthSquared() < 0.0001f)
			return;
		float yaw = Mathf.Atan2(-dir.X, -dir.Z);
		float pitch = Mathf.Atan2(dir.Y, new Vector2(dir.X, dir.Z).Length());
		Rotation = new Vector3(pitch, yaw, 0f);
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
			PointAlong(_velocity);

			// A lobbed arrow that drops below the terrain has buried itself — free it so arcs
			// into a hillside don't sail on underground. No-op off a terrain level (no active terrain).
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

		// Proximity hit test against living opposite-team units (cheap, no physics layers).
		// Scans only the opposing team's registry bucket — no per-frame group marshal.
		float hitSq = HitRadius * HitRadius;
		IReadOnlyList<Unit> foes = UnitRegistry.Opponents(_ownerTeam);
		for (int i = 0; i < foes.Count; i++)
		{
			Unit u = foes[i];
			if (u != null && IsInstanceValid(u) && !u.IsDead
				&& GlobalPosition.DistanceSquaredTo(u.GlobalPosition) <= hitSq)
			{
				u.TakeDamage(Damage);   // arrows: damage only, no shove
				GD.Print($"[Arrow] hit {u.Name} for {Damage}");
				QueueFree();
				return;
			}
		}
	}
}
