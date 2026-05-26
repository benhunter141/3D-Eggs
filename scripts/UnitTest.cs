using Godot;

// Headless logic check for the Chunk 3 damage/death pipeline. Run with:
//   godot --headless --path . res://scenes/Tests/UnitTest.tscn
// Spawns a skeleton and hits it with sword-strength damage until it dies, printing
// each step — confirms the pipeline works without needing mouse input in-game.
public partial class UnitTest : Node3D
{
	public override void _Ready()
	{
		GD.Print("=== UnitTest: damage/death pipeline ===");

		var skel = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		AddChild(skel);   // triggers _Ready -> Health = MaxHealth

		const float swordDamage = 40f;
		int expected = Mathf.CeilToInt(skel.MaxHealth / swordDamage);
		GD.Print($"Skeleton {skel.MaxHealth} HP vs sword {swordDamage} dmg -> expect {expected} hits");

		int hits = 0;
		while (!skel.IsDead && hits < 20)
		{
			hits++;
			GD.Print($"-- swing {hits} --");
			skel.TakeDamage(swordDamage);
		}

		bool pass = skel.IsDead && hits == expected;
		GD.Print(pass
			? $"PASS: skeleton died after {hits} hits"
			: $"FAIL: dead={skel.IsDead}, hits={hits}, expected={expected}");

		GetTree().Quit();
	}
}
