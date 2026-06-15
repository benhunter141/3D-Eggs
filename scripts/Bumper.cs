using Godot;

// A pinball bumper (M6, Chunk 21): a solid post that KICKS any unit that touches it
// straight back out, faster than it came in — so the arena itself becomes a pinball
// table that sword-knockback and chain-bounces ricochet off.
//
// Detection is an Area3D ring slightly wider than the solid core (the StaticBody's own
// CollisionShape), so a unit trips the kick as it reaches the post. On entry we REPLACE
// the unit's shove with one pointing away from the bumper centre, sized to the larger of
// a flat BumperStrength (so even a slow walker is flung) and the unit's incoming speed
// amplified by SpeedAmplify (so a fast pinball impact leaves even faster). The fling goes
// through Unit.AddKnockback, so it's clamped to the unit's MaxKnockback and decays and
// chains like every other shove. No damage — bumpers only bounce.
//
// We use Area3D.BodyEntered (one clean kick per entry) rather than the unit's
// ResolveKnockbackBounce wall path, because that path only fires for a unit already
// carrying a real shove (> MinBounceSpeed); the area kicks anyone who wanders in.
public partial class Bumper : StaticBody3D
{
	[Export] public float BumperStrength = 12.0f; // minimum outward fling speed (m/s)
	[Export] public float SpeedAmplify = 1.5f;    // incoming speed is multiplied by this; the larger of the two wins

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

		// Fling speed: flat strength, or the incoming shove amplified — whichever is bigger.
		float incoming = unit.CurrentKnockback.Length();
		float target = Mathf.Max(BumperStrength, incoming * SpeedAmplify);

		// AddKnockback is additive, so cancel the current shove first; the unit then leaves
		// carrying exactly outward * target (clamped to its MaxKnockback inside AddKnockback).
		unit.AddKnockback(-unit.CurrentKnockback + outward * target);
		GD.Print($"[Bumper] {Name} flung {unit.Name} outward at {target:0.0} m/s");
	}
}
