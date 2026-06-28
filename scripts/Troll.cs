using Godot;

// Cave Troll — the tier-5 BOSS of the M17 bestiary (Chunk 92). A giant, slow, enormously tough egg that
// spawns SOLO (the wave table's Formation.Solo row). It chases + clubs like any Enemy, but on its own
// cadence it performs a SLAM: a radial knockback shockwave that damages AND punts every opposing unit
// (eggs + their soldiers) inside SlamRadius, scattering a clustered defence. The slam only fires when a foe
// is actually within SlamRange, so it never wastes the cooldown on empty air. Built on the shared Unit/Enemy
// spine (chase + contact melee) with the slam layered on top; its stat block lives on the scene root
// (Troll.tscn). The radial query is one cheap UnitRegistry walk — no physics overlap, no particles needed
// for the logic — so it's crowd-safe and headless-testable. Enemy team.
public partial class Troll : Enemy
{
	[Export] public float SlamRadius = 6.0f;     // shockwave reach (m)
	[Export] public float SlamDamage = 24.0f;    // damage dealt to each unit caught in the slam
	[Export] public float SlamKnockback = 16.0f; // radial shove speed imparted outward from the troll (m/s)
	[Export] public float SlamCooldown = 4.0f;   // seconds between slams
	[Export] public float SlamRange = 5.0f;      // a foe must be within this for the troll to bother slamming

	private float _slamTimer;

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);   // chase + contact melee + knockback decay/bounce (shared Enemy logic)
		if (IsDead)
			return;

		if (_slamTimer > 0f)
			_slamTimer -= (float)delta;
		else if (UnitRegistry.FindNearestOpponent(Team, GlobalPosition, SlamRange) != null)
		{
			Slam();
			_slamTimer = SlamCooldown;
		}
	}

	// Radial shockwave: every living opposing unit within SlamRadius takes SlamDamage and a shove pointing
	// straight away from the troll. Returns how many units it caught — used by the headless test. Public so
	// the test can fire it deterministically. A unit standing exactly on the troll (no direction) is skipped.
	public int Slam()
	{
		var foes = UnitRegistry.Opponents(Team);
		float rSq = SlamRadius * SlamRadius;
		int hit = 0;
		for (int i = 0; i < foes.Count; i++)
		{
			Unit u = foes[i];
			if (u == null || !IsInstanceValid(u) || u.IsDead)
				continue;
			Vector3 to = u.GlobalPosition - GlobalPosition;
			to.Y = 0f;
			float dSq = to.LengthSquared();
			if (dSq > rSq || dSq < 0.0001f)
				continue;
			Vector3 dir = to.Normalized();
			u.TakeDamage(SlamDamage, dir, SlamKnockback);
			hit++;
		}
		GD.Print($"[Troll] {Name} SLAM! caught {hit} foe(s) within {SlamRadius} m");
		return hit;
	}
}
