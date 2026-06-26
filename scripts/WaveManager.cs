using Godot;
using System.Collections.Generic;

// Escalating survival waves for the Co-op Card Brawl (M15, Chunk 70; M17 bestiary, Chunk 83). Each round of
// the brawl is one wave: when the round begins (End Turn → PLAY) the manager spawns one wave's worth of foes.
// They line up across the TOP of the arena (−Z, far from the camera) and march DOWN toward the eggs, who start
// at the bottom (+Z) — a clear "foes up top, players down low" stand. They're real Enemy-team units so the
// existing chase AI drives them at the eggs + their soldiers, and leftover foes from an unfinished wave persist
// (the pressure compounds).
//
// M17 grows this from a single EnemyScene line into a per-wave COMPOSITION TABLE (`WaveTable`): each wave maps
// to a `WaveComposition` — a list of `(scene, count)` entries plus a `Formation` placement hint (Spread line /
// tight Block / Solo boss). Later bestiary chunks (84–93) just drop their enemy scene into a wave row. With NO
// table set, `CompositionForWave` falls back to today's single-Skeleton Spread line sized by `BaseCount +
// (wave-1)*PerWave`, so existing brawl behaviour + tests are byte-identical. `CountForWave` now = the sum of
// the wave's row. Pure data + placement, so the schedule is headless-testable; CardBrawl drives `SpawnWave`
// each round, passing the players' X centroid so the foes line up above them.
public partial class WaveManager : Node
{
	// How a wave's foes are arranged when spawned.
	public enum Formation
	{
		Spread,   // even line(s) across the arena width (the classic horde)
		Block,    // tight grid centred on the spawn line (a Legion shield-wall)
		Solo,     // one foe dead-centre (a boss)
	}

	// One foe type + how many of it in a wave.
	public class WaveEntry
	{
		public PackedScene Scene;
		public int Count;
		public WaveEntry() { }
		public WaveEntry(PackedScene scene, int count) { Scene = scene; Count = count; }
	}

	// The full makeup of a single wave: a mix of entries + how to arrange them.
	public class WaveComposition
	{
		public List<WaveEntry> Entries = new();
		public Formation Arrangement = Formation.Spread;

		public WaveComposition() { }
		public WaveComposition(Formation arrangement) { Arrangement = arrangement; }

		// Total foes across all entries — what `CountForWave` reports.
		public int TotalCount
		{
			get
			{
				int n = 0;
				foreach (var e in Entries) n += Mathf.Max(0, e.Count);
				return n;
			}
		}

		public WaveComposition Add(PackedScene scene, int count)
		{
			Entries.Add(new WaveEntry(scene, count));
			return this;
		}
	}

	[Export] public PackedScene EnemyScene;          // fallback foe (falls back to res://scenes/Skeleton.tscn)
	[Export] public int BaseCount = 3;               // fallback: enemies in wave 1
	[Export] public int PerWave = 2;                 // fallback: extra enemies per later wave
	[Export] public float SpawnZ = -16.0f;           // top-of-arena line the foes spawn on (−Z = far)
	[Export] public float SpawnSpread = 34.0f;       // horizontal width a Spread line spreads across
	[Export] public int PerRow = 8;                  // foes per row before stacking another row behind
	[Export] public float RowGap = 2.5f;             // depth (−Z) between stacked rows
	[Export] public float SpawnHeight = 1.0f;        // lift onto the ground like the level scenes
	[Export] public float BlockGap = 2.0f;           // tight spacing between foes in a Block formation

	// The authored per-wave bestiary. Index 0 = wave 1, etc. Empty = use the Skeleton-line fallback. Waves
	// past the end of the table reuse the LAST (hardest) row, so the run keeps escalating off the end.
	public readonly List<WaveComposition> WaveTable = new();

	// The makeup of a given (1-based) wave. Pulls from `WaveTable` when authored (clamped to the last row for
	// waves beyond it); otherwise builds the legacy single-Skeleton Spread line sized by the count formula.
	public WaveComposition CompositionForWave(int wave)
	{
		if (WaveTable.Count > 0)
		{
			int idx = Mathf.Clamp(wave - 1, 0, WaveTable.Count - 1);
			return WaveTable[idx];
		}

		EnemyScene ??= GD.Load<PackedScene>("res://scenes/Skeleton.tscn");
		int count = BaseCount + Mathf.Max(0, wave - 1) * PerWave;
		var comp = new WaveComposition(Formation.Spread);
		comp.Add(EnemyScene, count);
		return comp;
	}

	// Enemies spawned for a given (1-based) wave — the sum of that wave's composition.
	public int CountForWave(int wave) => CompositionForWave(wave).TotalCount;

	// Spawn one wave's worth of enemies into `parent`, arranged per the wave's Formation and centred on
	// `center.X`. Returns how many spawned. Each enemy keeps its scene's own (Enemy) team so the eggs/soldiers
	// read as its foes.
	public int SpawnWave(int wave, Node parent, Vector3 center)
	{
		if (parent == null)
		{
			GD.PrintErr("[WaveManager] no parent to spawn into");
			return 0;
		}

		var comp = CompositionForWave(wave);

		// Flatten the composition into an ordered scene list (each type grouped together).
		var scenes = new List<PackedScene>();
		foreach (var entry in comp.Entries)
		{
			if (entry.Scene == null) continue;
			for (int i = 0; i < entry.Count; i++) scenes.Add(entry.Scene);
		}
		if (scenes.Count == 0)
		{
			GD.PrintErr("[WaveManager] wave composition has no foes to spawn");
			return 0;
		}

		int total = scenes.Count;
		for (int i = 0; i < total; i++)
		{
			Vector3 offset = PlaceFoe(comp.Arrangement, i, total);
			var enemy = scenes[i].Instantiate<Node3D>();
			parent.AddChild(enemy);
			enemy.GlobalPosition = new Vector3(center.X + offset.X, SpawnHeight, SpawnZ + offset.Z);
		}
		GD.Print($"[WaveManager] wave {wave} spawned {total} foes ({comp.Arrangement})");
		return total;
	}

	// Local (X,Z) offset of the i-th of `total` foes for a given formation. X is the cross-arena offset,
	// Z is depth behind the spawn line (negative = further from the eggs).
	private Vector3 PlaceFoe(Formation formation, int i, int total)
	{
		switch (formation)
		{
			case Formation.Solo:
				return Vector3.Zero;   // boss sits dead-centre on the line

			case Formation.Block:
			{
				// Tight centred grid: PerRow columns, fixed BlockGap spacing, rows stack back.
				int perRow = Mathf.Max(1, PerRow);
				int rows = Mathf.CeilToInt((float)total / perRow);
				int row = i / perRow;
				int col = i % perRow;
				int rowCount = Mathf.Min(perRow, total - row * perRow);
				float x = (col - (rowCount - 1) * 0.5f) * BlockGap;
				float z = -(row - (rows - 1) * 0.5f) * BlockGap;   // centre the block depth-wise around the line
				return new Vector3(x, 0f, z);
			}

			default:   // Spread
			{
				int perRow = Mathf.Max(1, PerRow);
				int row = i / perRow;
				int col = i % perRow;
				int rowCount = Mathf.Min(perRow, total - row * perRow);
				// Evenly space this row across the spread; a lone foe sits dead-centre.
				float t = rowCount > 1 ? (float)col / (rowCount - 1) : 0.5f;
				float x = (t - 0.5f) * SpawnSpread;
				float z = -row * RowGap;   // each extra row stacks further back (more −Z)
				return new Vector3(x, 0f, z);
			}
		}
	}
}
