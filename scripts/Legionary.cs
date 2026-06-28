using Godot;

// Roman Legionary — the tier-3 shielded infantry of the M17 bestiary (Chunk 88). It marches in as a
// cohesive LEGION BLOCK (WaveManager Formation.Block — tight rows, shared facing) rather than the loose
// spread the hordes use, and its frontal scutum SHIELD soaks part of any blow landing from the front:
// a frontal hit is reduced by ShieldReduction, while a hit from the flank or rear (outside the shield
// arc) bites in FULL. So the counter to a legion is to flank it, not trade blows head-on. Everything
// else is the base Enemy behaviour (chase the nearest opponent, melee on contact, no knockback). Its
// stat block lives on the scene root (Legionary.tscn) so it stays editor-tunable; this class adds the
// directional shield + a type identity so waves can compose Legions by type.
public partial class Legionary : Enemy
{
	[Export] public float ShieldReduction = 0.6f;   // fraction of a FRONTAL blow the scutum soaks (0..1)
	[Export] public float ShieldArcDegrees = 200f;  // total frontal cone the shield covers (>180 = a touch past the sides)

	// Soak frontal damage with the scutum before the base Unit logic resolves the hit. `hitDirection`
	// points the way we'd be shoved (attacker→us), so the attacker lies along −hitDirection; the hit is
	// FRONTAL when that direction falls inside the shield arc around our facing (−Z is forward). A
	// direction-less hit (the default Vector3.Zero most melee passes) is treated as frontal — the
	// legionary faces the foe it's fighting, so its shield is between them. Flank/rear hits land in full.
	public override void TakeDamage(float amount, Vector3 hitDirection = default, float knockbackStrength = 0.0f)
	{
		if (BlocksFrontal(hitDirection))
			amount *= 1f - Mathf.Clamp(ShieldReduction, 0f, 1f);
		base.TakeDamage(amount, hitDirection, knockbackStrength);
	}

	// True when an incoming hit comes from within the shield's frontal arc (so the scutum soaks it).
	// Public so the headless test can assert the front/rear split directly.
	public bool BlocksFrontal(Vector3 hitDirection)
	{
		hitDirection.Y = 0f;
		if (hitDirection.LengthSquared() < 0.0001f)
			return true;                                  // no direction info — assume we face the threat

		Vector3 attackerDir = (-hitDirection).Normalized();   // points from us toward the attacker
		Vector3 forward = -GlobalTransform.Basis.Z;           // the way we face (−Z is forward)
		forward.Y = 0f;
		if (forward.LengthSquared() < 0.0001f)
			return true;
		forward = forward.Normalized();

		float cosHalf = Mathf.Cos(Mathf.DegToRad(ShieldArcDegrees * 0.5f));
		return attackerDir.Dot(forward) >= cosHalf;           // attacker inside the frontal cone
	}
}
