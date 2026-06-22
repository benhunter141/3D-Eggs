using Godot;

// Escalating survival waves for the Co-op Card Brawl (M15, Chunk 70). Each round of the brawl is one
// wave: when the round begins (End Turn → PLAY) the manager spawns CountForWave(wave) enemies. They line
// up across the TOP of the arena (−Z, far from the camera) and march DOWN toward the eggs, who start at
// the bottom (+Z) — a clear "foes up top, players down low" stand. They're real Enemy-team units
// (Skeletons) so the existing chase AI drives them at the eggs + their soldiers. Waves grow each round
// (BaseCount + (wave-1)*PerWave), and leftover enemies from an unfinished wave persist — the pressure
// compounds. Pure spawning + a count formula, so the scaling is headless-testable; CardBrawl drives
// SpawnWave each round, passing the players' X centroid so the foes line up above them.
public partial class WaveManager : Node
{
	[Export] public PackedScene EnemyScene;          // falls back to res://scenes/Skeleton.tscn
	[Export] public int BaseCount = 3;               // enemies in wave 1
	[Export] public int PerWave = 2;                 // extra enemies per later wave
	[Export] public float SpawnZ = -16.0f;           // top-of-arena line the foes spawn on (−Z = far)
	[Export] public float SpawnSpread = 34.0f;       // horizontal width the line spreads across
	[Export] public int PerRow = 8;                  // foes per row before stacking another row behind
	[Export] public float RowGap = 2.5f;             // depth (−Z) between stacked rows
	[Export] public float SpawnHeight = 1.0f;        // lift onto the ground like the level scenes

	// Enemies spawned for a given (1-based) wave number — grows linearly with the wave.
	public int CountForWave(int wave) => BaseCount + Mathf.Max(0, wave - 1) * PerWave;

	// Spawn one wave's worth of enemies into `parent`, lined up across the top of the arena (centered on
	// `center.X`) and marching down toward the players. Returns how many spawned. Each enemy is the Enemy
	// team (the scene's own default) so the eggs/soldiers read as its foes.
	public int SpawnWave(int wave, Node parent, Vector3 center)
	{
		EnemyScene ??= GD.Load<PackedScene>("res://scenes/Skeleton.tscn");
		if (EnemyScene == null || parent == null)
		{
			GD.PrintErr("[WaveManager] no enemy scene / parent to spawn into");
			return 0;
		}

		int count = CountForWave(wave);
		int perRow = Mathf.Max(1, PerRow);
		for (int i = 0; i < count; i++)
		{
			int row = i / perRow;
			int col = i % perRow;
			int rowCount = Mathf.Min(perRow, count - row * perRow);
			// Evenly space this row across the spread; a lone foe sits dead center.
			float t = rowCount > 1 ? (float)col / (rowCount - 1) : 0.5f;
			float x = center.X + (t - 0.5f) * SpawnSpread;
			float z = SpawnZ - row * RowGap;   // each extra row stacks further back (more −Z)

			var enemy = EnemyScene.Instantiate<Node3D>();
			parent.AddChild(enemy);
			enemy.GlobalPosition = new Vector3(x, SpawnHeight, z);
		}
		GD.Print($"[WaveManager] wave {wave} spawned {count} enemies across the top");
		return count;
	}
}
