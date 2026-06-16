using Godot;

// A pinball bumper (M6, Chunk 21): a solid post that KICKS any unit that touches it
// straight back out, faster than it came in — so the arena itself becomes a pinball
// table that sword-knockback and chain-bounces ricochet off.
//
// Detection is an Area3D ring slightly wider than the solid core (the StaticBody's own
// CollisionShape), so a unit trips the kick as it reaches the post. On entry we REPLACE the
// unit's shove with a single fixed outward launch of BumperStrength m/s, pointing away from
// the bumper centre. The launch goes through Unit.AddKnockback, so it's clamped to the unit's
// MaxKnockback and then DECAYS like every other shove — the unit slows back to a stop and
// regains control. No damage — bumpers only bounce.
//
// The kick is a FIXED speed, not the incoming speed amplified: an >1 amplifier let a unit
// ricocheting between the bumper cluster gain speed on every touch until it pinned the
// MaxKnockback cap, which read as the unit "accelerating away forever" instead of slowing.
//
// We use Area3D.BodyEntered (one clean kick per entry) rather than the unit's
// ResolveKnockbackBounce wall path, because that path only fires for a unit already
// carrying a real shove (> MinBounceSpeed); the area kicks anyone who wanders in.
public partial class Bumper : StaticBody3D
{
	[Export] public float BumperStrength = 8.0f;  // fixed outward fling speed (m/s) imparted on touch

	public override void _Ready()
	{
		var area = GetNode<Area3D>("DetectArea");
		area.BodyEntered += OnBodyEntered;
	}

	private void OnBodyEntered(Node3D body)
	{
		if (body is not Unit unit || unit.IsDead)
			return;   // only living units get bounced; ignore the ground/other static bodies

		// Outward = from the bumper centre toward the unit, flattened onto the ground plane.
		Vector3 outward = unit.GlobalPosition - GlobalPosition;
		outward.Y = 0f;
		if (outward.LengthSquared() < 0.0001f)
			outward = Vector3.Forward;   // unit sitting dead-centre: pick any horizontal direction
		outward = outward.Normalized();

		// AddKnockback is additive, so cancel the current shove first; the unit then leaves
		// carrying exactly outward * BumperStrength (clamped to its MaxKnockback inside).
		unit.AddKnockback(-unit.CurrentKnockback + outward * BumperStrength);
		GD.Print($"[Bumper] {Name} flung {unit.Name} outward at {BumperStrength:0.0} m/s");
	}
}
