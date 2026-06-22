using Godot;
using System.Collections.Generic;

// Squad placement grid for the Co-op Card Brawl (M15 redesign). A Soldier card doesn't just drop a body
// ahead of the egg any more — it attaches in one of the FOUR cardinal directions next to an egg the
// player already controls (their hero or a previously-placed soldier), and it can't land on a cell that's
// already taken. That builds a little tile-squad around the captain.
//
// This is the pure model: it maps world XZ <-> integer cells at a fixed spacing, so adjacency and overlap
// are exact integer math (no floating-point slop), and turns a chosen cell back into a world spawn point.
// The CardBrawl view feeds it the live positions of the controlled eggs (anchors) and everything that's
// standing (occupied), and renders/► resolves the cells it returns. Fully headless-testable.
public static class SquadGrid
{
	// Metres between adjacent eggs — a touch over a capsule's diameter so neighbours don't interpenetrate.
	public const float CellSize = 2.2f;

	public static Vector2I WorldToCell(Vector3 world) =>
		new Vector2I(Mathf.RoundToInt(world.X / CellSize), Mathf.RoundToInt(world.Z / CellSize));

	public static Vector3 CellToWorld(Vector2I cell, float y) =>
		new Vector3(cell.X * CellSize, y, cell.Y * CellSize);

	// The four cardinal neighbours, in a stable order (right / left / down / up in cell space).
	private static readonly Vector2I[] Dirs =
	{
		new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1),
	};

	// Every empty cell that is 4-adjacent to one of `anchors` (the player's controlled eggs) and not in
	// `occupied` (everything already standing, friend or foe). Deduplicated; stable order so the gamepad
	// cursor cycles them predictably.
	public static List<Vector2I> ValidPlacements(IEnumerable<Vector2I> anchors, HashSet<Vector2I> occupied)
	{
		var result = new List<Vector2I>();
		var seen = new HashSet<Vector2I>();
		foreach (Vector2I a in anchors)
			foreach (Vector2I d in Dirs)
			{
				Vector2I c = a + d;
				if (occupied.Contains(c) || seen.Contains(c))
					continue;
				seen.Add(c);
				result.Add(c);
			}
		return result;
	}
}
