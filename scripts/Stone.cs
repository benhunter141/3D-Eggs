using Godot;
using System.Collections.Generic;

// A thrown stone. Flies in a straight line from where it was launched (aimed at the
// target's position at throw time) and, on coming within HitRadius of any enemy-team
// Unit, deals damage with NO knockback (only the player's sword shoves) and frees
// itself. Also frees after MaxLifetime so stray stones don't accumulate.
// Spawned by stone-throwing allies (Chunk 8). Owner team is set on launch so a stone
// never hits the side that threw it.
public partial class Stone : Node3D
{
	[Export] public float Speed = 18.0f;       // flight speed (m/s)
	[Export] public float Damage = 12.0f;      // damage dealt on hit (no knockback)
	[Export] public float HitRadius = 0.6f;    // how close to a unit counts as a hit
	[Export] public float MaxLifetime = 2.0f;  // seconds before it gives up and frees

	private Vector3 _direction = Vector3.Forward;
	private Unit.TeamId _ownerTeam = Unit.TeamId.Player;
	private float _life;

	// Aim the stone before it's added to the tree (or right after). Direction is
	// flattened onto the ground plane so the stone skims at its launch height.
	public void Launch(Vector3 direction, Unit.TeamId ownerTeam)
	{
		direction.Y = 0f;
		if (direction.LengthSquared() > 0.0001f)
			_direction = direction.Normalized();
		_ownerTeam = ownerTeam;
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

		GlobalPosition += _direction * Speed * dt;

		// Proximity hit test against living enemy-team units (cheap, no physics layers).
		// Scans only the opposing team's registry bucket — no per-frame group marshal.
		float hitSq = HitRadius * HitRadius;
		IReadOnlyList<Unit> foes = UnitRegistry.Opponents(_ownerTeam);
		for (int i = 0; i < foes.Count; i++)
		{
			Unit u = foes[i];
			if (u != null && IsInstanceValid(u) && !u.IsDead
				&& GlobalPosition.DistanceSquaredTo(u.GlobalPosition) <= hitSq)
			{
				u.TakeDamage(Damage);   // stones: damage only, no shove
				GD.Print($"[Stone] hit {u.Name} for {Damage}");
				QueueFree();
				return;
			}
		}
	}
}
