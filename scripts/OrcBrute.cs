using Godot;

// Orc Brute — the tier-4 heavy bruiser of the M17 bestiary (Chunk 89). A big, slow, dark-green egg
// hefting a club. It is the ONLY non-player foe that SHOVES: its club connects with real knockback
// (routed through Unit.AddKnockback, the canonical no-overflow shove path) so a brute can punt an egg
// or scatter a soldier line — you can't just stand and trade blows with it. Everything else is the base
// Enemy behaviour (chase the nearest opponent, melee on contact). High HP + low speed = the heavy tax;
// its stat block lives on the scene root (OrcBrute.tscn) so it stays editor-tunable. The class adds the
// club knockback + a type identity so waves can compose Brutes by type.
public partial class OrcBrute : Enemy
{
	[Export] public float ClubKnockback = 12.0f;   // shove speed the club lands on a hit (m/s)

	// Override the base Enemy strike: club the victim back along the swing direction (us → target),
	// then deal damage. TakeDamage normalizes the direction and scales it by ClubKnockback, so the
	// shove always points the way we hit. The dead absorb no shove (AddKnockback early-outs).
	protected override void MeleeStrike(Unit target)
	{
		Vector3 dir = target.GlobalPosition - GlobalPosition;
		dir.Y = 0f;
		target.TakeDamage(AttackDamage, dir, ClubKnockback);
		GD.Print($"[OrcBrute] {Name} clubbed {target.Name} for {AttackDamage} (+{ClubKnockback} knockback)");
	}
}
