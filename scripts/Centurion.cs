using Godot;

// Roman Centurion — the tier-5 elite Legion leader of the M17 bestiary (Chunk 91). A Centurion IS a
// Legionary (it carries the same frontal scutum) but bigger, tankier, and stronger in melee — and it
// projects a RALLY AURA: every frame it refreshes a short-lived buff on each friendly Legionary within
// RallyRadius, making them move faster and shrug off extra damage. The buff is re-applied continuously,
// so it persists only while the Centurion lives and the legionary stays in range; kill the Centurion (or
// pull a legionary out of the aura) and the buff lapses on its own. So a Centurion-led Legion hits like a
// hammer until you behead it. Its stat block lives on the scene root (Centurion.tscn). Enemy team.
public partial class Centurion : Legionary
{
	[Export] public float RallyRadius = 8.0f;          // legionaries within this get the aura
	[Export] public float RallySpeedMultiplier = 1.4f; // speed boost granted to rallied legionaries
	[Export] public float RallyDamageReduction = 0.25f;// extra fraction of damage rallied legionaries soak
	[Export] public float RallyRefresh = 0.5f;         // how long each pulse keeps the buff alive (re-applied each frame)

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);   // Legionary/Enemy chase + melee + the Centurion's own rally-timer tick
		if (IsDead)
			return;
		RallyNearbyLegionaries();
	}

	// Re-apply the rally buff to every living friendly Legionary within RallyRadius (excluding ourselves).
	// Other Centurions count as Legionaries, so a pair of leaders reinforce each other's lines.
	private void RallyNearbyLegionaries()
	{
		var allies = UnitRegistry.OnTeam(Team);
		float rSq = RallyRadius * RallyRadius;
		for (int i = 0; i < allies.Count; i++)
		{
			Unit u = allies[i];
			if (u == null || u == this || !IsInstanceValid(u) || u.IsDead)
				continue;
			if (u is Legionary leg && GlobalPosition.DistanceSquaredTo(u.GlobalPosition) <= rSq)
				leg.ApplyRally(RallyRefresh, RallySpeedMultiplier, RallyDamageReduction);
		}
	}
}
