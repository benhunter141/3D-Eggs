using Godot;
using System.Collections.Generic;

// Central registry of living units, bucketed by team, so AI and projectiles can find
// the nearest opposing unit WITHOUT calling GetTree().GetNodesInGroup("units") every
// physics frame.
//
// Why this exists (M5 — crowds): that group lookup marshals a brand-new Godot array
// across the C++<->C# boundary on every call. With dozens of units each re-scanning
// every frame (Enemy/Swordman/Bowman/Ally + every Stone/Arrow in flight) that array
// churn — O(n) allocation x n scanners — was the dominant per-frame cost and the first
// wall in the way of 50-100 unit battles. The registry keeps two plain C# lists that
// mutate only when a unit enters or leaves the tree, so a scan is a cheap array walk
// over just the opposing team with zero per-frame allocation.
//
// Lifecycle: units Register in Unit._Ready and Unregister in Unit._ExitTree, so the
// lists hold exactly the units currently in the active scene. The registry is static
// (process-wide), but because every unit unregisters on _ExitTree a scene reload empties
// it cleanly. Queries still defensively skip dead/invalid entries, so a corpse lingering
// before QueueFree never surfaces as a phantom target.
public static class UnitRegistry
{
	private static readonly List<Unit> Players = new();
	private static readonly List<Unit> Enemies = new();

	private static List<Unit> Bucket(Unit.TeamId team) =>
		team == Unit.TeamId.Player ? Players : Enemies;

	public static void Register(Unit u)
	{
		List<Unit> list = Bucket(u.Team);
		if (!list.Contains(u))
			list.Add(u);
	}

	public static void Unregister(Unit u)
	{
		// A unit's Team is fixed at registration time in this project, but remove from
		// both buckets anyway so a stale entry can never be left behind.
		Players.Remove(u);
		Enemies.Remove(u);
	}

	// Living units on the team OPPOSITE to `team`. The returned list is the live backing
	// store: read-only by convention, callers must not mutate it. It can contain dead or
	// freed entries until they leave the tree, so callers must skip those (see the loop
	// in FindNearestOpponent for the pattern).
	public static IReadOnlyList<Unit> Opponents(Unit.TeamId team) =>
		team == Unit.TeamId.Player ? Enemies : Players;

	// Nearest living opposing unit to `from`, or null if there is none in range.
	// `maxRange` caps the search radius (default: unbounded).
	public static Unit FindNearestOpponent(Unit.TeamId team, Vector3 from, float maxRange = float.MaxValue)
	{
		IReadOnlyList<Unit> foes = Opponents(team);
		Unit best = null;
		float bestSq = maxRange >= float.MaxValue ? float.MaxValue : maxRange * maxRange;
		for (int i = 0; i < foes.Count; i++)
		{
			Unit u = foes[i];
			if (u == null || !GodotObject.IsInstanceValid(u) || u.IsDead)
				continue;
			float d = from.DistanceSquaredTo(u.GlobalPosition);
			if (d < bestSq)
			{
				bestSq = d;
				best = u;
			}
		}
		return best;
	}
}
