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

		bool deathPass = skel.IsDead && hits == expected;
		GD.Print(deathPass
			? $"PASS: skeleton died after {hits} hits"
			: $"FAIL: dead={skel.IsDead}, hits={hits}, expected={expected}");

		bool knockPass = TestKnockback();

		GD.Print(deathPass && knockPass ? "=== ALL PASS ===" : "=== FAIL ===");
		GetTree().Quit();
	}

	// Verify the sword's knockback impulse: a fresh skeleton hit from one side should
	// gain a horizontal shove of the right speed pointing away from the attacker, and
	// that shove should bleed off via DecayKnockback. (Dead units take no knockback.)
	private bool TestKnockback()
	{
		GD.Print("=== UnitTest: knockback impulse ===");

		var skel = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		AddChild(skel);

		// Attacker at -X, target at origin -> shove points toward +X. A small +Y is fed
		// in to confirm TakeDamage flattens knockback onto the ground plane.
		const float strength = 10f;
		Vector3 hitDir = new Vector3(2f, 5f, 0f);
		skel.TakeDamage(5f, hitDir, strength);

		Vector3 kb = skel.CurrentKnockback;
		bool flat = Mathf.IsZeroApprox(kb.Y);
		bool speedOk = Mathf.IsEqualApprox(kb.Length(), strength, 0.01f);
		bool dirOk = kb.X > 0f && Mathf.IsZeroApprox(kb.Z);
		GD.Print($"after hit: knockback={kb} len={kb.Length():0.00} (flat={flat}, speed={speedOk}, dir={dirOk})");

		// A dead unit ignores further knockback (TakeDamage early-returns when IsDead).
		skel.TakeDamage(9999f);                                     // kill, no shove
		Vector3 kbAtDeath = skel.CurrentKnockback;
		skel.TakeDamage(5f, new Vector3(0f, 0f, 1f), strength);     // post-death hit
		bool deadIgnores = skel.CurrentKnockback == kbAtDeath;
		GD.Print($"dead unit ignores further knockback: {deadIgnores}");

		bool pass = flat && speedOk && dirOk && deadIgnores;
		GD.Print(pass ? "PASS: knockback impulse correct" : "FAIL: knockback wrong");
		return pass;
	}
}
