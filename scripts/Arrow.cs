using Godot;

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

	private Vector3 _direction = Vector3.Forward;
	private Unit.TeamId _ownerTeam = Unit.TeamId.Enemy;
	private float _life;

	// Aim the arrow before it's added to the tree (or right after). Direction is flattened
	// onto the ground plane so the arrow skims at its launch height; the mesh is also yawed
	// to point the way it flies.
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

		// Proximity hit test against living opposite-team units (cheap, no physics layers).
		float hitSq = HitRadius * HitRadius;
		foreach (Node n in GetTree().GetNodesInGroup("units"))
		{
			if (n is Unit u && !u.IsDead && u.Team != _ownerTeam
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
