using Godot;

// Escalating survival waves for the Co-op Card Brawl (M15, Chunk 70). Each round of the brawl is one
// wave: when the round begins (End Turn → PLAY) the manager spawns CountForWave(wave) enemies around the
// arena edge, aimed inward. They're real Enemy-team units (Skeletons) so the existing chase AI drives
// them at the eggs + their soldiers. Waves grow each round (BaseCount + (wave-1)*PerWave), and leftover
// enemies from an unfinished wave persist — the pressure compounds. Pure spawning + a count formula, so
// the scaling is headless-testable; CardBrawl drives SpawnWave each round.
public partial class WaveManager : Node
{
	[Export] public PackedScene EnemyScene;          // falls back to res://scenes/Skeleton.tscn
	[Export] public int BaseCount = 3;               // enemies in wave 1
	[Export] public int PerWave = 2;                 // extra enemies per later wave
	[Export] public float SpawnRadius = 16.0f;       // ring radius the wave appears on
	[Export] public float SpawnHeight = 1.0f;        // lift onto the ground like the level scenes

	// Enemies spawned for a given (1-based) wave number — grows linearly with the wave.
	public int CountForWave(int wave) => BaseCount + Mathf.Max(0, wave - 1) * PerWave;

	// Spawn one wave's worth of enemies into `parent`, ringed around `center`. Returns how many spawned.
	// Each enemy is the Enemy team (the scene's own default) so the eggs/soldiers read as its foes.
	public int SpawnWave(int wave, Node parent, Vector3 center)
	{
		EnemyScene ??= GD.Load<PackedScene>("res://scenes/Skeleton.tscn");
		if (EnemyScene == null || parent == null)
		{
			GD.PrintErr("[WaveManager] no enemy scene / parent to spawn into");
			return 0;
		}

		int count = CountForWave(wave);
		for (int i = 0; i < count; i++)
		{
			// Spread the ring evenly, nudged each wave so successive waves don't stack on the same spots.
			float angle = Mathf.Tau * (i + 0.37f * wave) / Mathf.Max(1, count);
			Vector3 pos = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SpawnRadius;
			pos.Y = SpawnHeight;

			var enemy = EnemyScene.Instantiate<Node3D>();
			parent.AddChild(enemy);
			enemy.GlobalPosition = pos;
		}
		GD.Print($"[WaveManager] wave {wave} spawned {count} enemies");
		return count;
	}
}
