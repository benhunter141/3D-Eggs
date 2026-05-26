using Godot;
using System.Threading.Tasks;

// Headless logic checks for the combat pipeline. Run with:
//   godot --headless --path . res://scenes/Tests/UnitTest.tscn
// Covers: damage/death (Chunk 3), sword knockback (Chunk 4), and Chunk 5's
// skeleton chase/attack AI plus the player-style death hook (death without
// being freed). Output goes to stdout for Claude to read.
public partial class UnitTest : Node3D
{
	// A stand-in for the Player's death behaviour: stays in the tree and flips a
	// flag instead of QueueFree-ing, so we can verify the OnDeath hook in isolation.
	private partial class DeathHookUnit : Unit
	{
		public bool OnDeathCalled;
		protected override void OnDeath() => OnDeathCalled = true; // no QueueFree
	}

	public override void _Ready()
	{
		_ = RunAll();
	}

	private async Task RunAll()
	{
		bool death = TestDamageDeath();
		bool knock = TestKnockback();
		bool hook = TestDeathHook();
		bool chase = await TestChase();
		bool formation = await TestFormation();

		GD.Print(death && knock && hook && chase && formation ? "=== ALL PASS ===" : "=== FAIL ===");
		GetTree().Quit();
	}

	// Chunk 3: a skeleton dies after the expected number of sword hits.
	private bool TestDamageDeath()
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
		return pass;
	}

	// Chunk 4: a fresh skeleton hit from one side gains a flat shove of the right
	// speed/direction that bleeds off; dead units ignore further knockback.
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

	// Chunk 5: the player-style death hook. A lethal hit marks the unit dead and
	// runs OnDeath, but (unlike a skeleton) it stays in the tree — it isn't freed.
	private bool TestDeathHook()
	{
		GD.Print("=== UnitTest: death hook (player-style, no free) ===");

		var u = new DeathHookUnit { MaxHealth = 30f };
		AddChild(u);                 // Health = 30
		u.TakeDamage(40f);           // lethal

		bool pass = u.IsDead && u.OnDeathCalled && IsInstanceValid(u) && u.IsInsideTree();
		GD.Print($"dead={u.IsDead}, hookRan={u.OnDeathCalled}, stillInTree={u.IsInsideTree()}");
		GD.Print(pass ? "PASS: died without being freed" : "FAIL: death hook wrong");
		return pass;
	}

	// Chunk 5: a skeleton chases the nearest enemy-team unit and melees it on
	// contact. Frame-stepped: place a stationary player-team target and a skeleton
	// 6 m away, run physics, then assert it closed in and dealt damage.
	private async Task<bool> TestChase()
	{
		GD.Print("=== UnitTest: skeleton chase + melee ===");

		// Clear the leftover bodies from the earlier sync tests so they don't run
		// their own AI during the frames we step here.
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Stationary player-team target at the origin (plain Unit = no AI, no movement).
		var target = new Unit { Team = Unit.TeamId.Player, MaxHealth = 100f };
		AddChild(target);
		target.GlobalPosition = Vector3.Zero;

		var skel = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		AddChild(skel);
		skel.GlobalPosition = new Vector3(0f, 0f, 6f);

		float startDist = skel.GlobalPosition.DistanceTo(target.GlobalPosition);
		GD.Print($"start dist={startDist:0.00}, attackRange={skel.AttackRange}");

		// ~3.3 s of physics at 60 Hz — enough to close 6 m at 4 m/s and land a few hits.
		for (int i = 0; i < 200; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		float endDist = skel.GlobalPosition.DistanceTo(target.GlobalPosition);
		bool closed = endDist <= skel.AttackRange + 0.3f;
		bool damaged = target.Health < target.MaxHealth;
		GD.Print($"end dist={endDist:0.00}, target HP={target.Health}/{target.MaxHealth}");

		bool pass = closed && damaged;
		GD.Print(pass
			? "PASS: skeleton chased into range and dealt damage"
			: $"FAIL: closed={closed}, damaged={damaged}");
		return pass;
	}

	// Chunk 6: an ally marches to its formation slot and holds it, and that slot
	// rotates with the player — turn the player and the ally chases the new spot.
	private async Task<bool> TestFormation()
	{
		GD.Print("=== UnitTest: ally formation follow ===");

		// Wipe leftover units so nobody chases/shoves the ally during these frames.
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// A plain Node3D in the "player" group is all the Ally needs as its anchor.
		var player = new Node3D();
		AddChild(player);
		player.AddToGroup("player");
		player.GlobalPosition = Vector3.Zero;

		var ally = GD.Load<PackedScene>("res://scenes/Ally.tscn").Instantiate<Ally>();
		ally.FormationOffset = new Vector3(2f, 0f, 1.5f);
		AddChild(ally);                                 // _Ready grabs the player from the group
		ally.GlobalPosition = new Vector3(0f, 0f, 7f);  // start well away from its slot

		Vector3 slot0 = ally.SlotWorldPosition();
		GD.Print($"slot(yaw 0)={slot0}; ally starts {ally.GlobalPosition.DistanceTo(slot0):0.00} m away");

		// ~2.5 s of physics: long enough to march in and settle.
		for (int i = 0; i < 150; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		float restDist = ally.GlobalPosition.DistanceTo(ally.SlotWorldPosition());
		bool arrived = restDist < 0.3f;
		GD.Print($"after follow: {restDist:0.00} m from slot (arrived={arrived})");

		// Turn the player 90°: the slot should swing around and the ally re-form on it.
		player.Rotation = new Vector3(0f, Mathf.Pi * 0.5f, 0f);
		Vector3 slot90 = ally.SlotWorldPosition();
		bool slotMoved = slot0.DistanceTo(slot90) > 0.5f;
		GD.Print($"slot(yaw 90)={slot90}; moved with player turn={slotMoved}");

		for (int i = 0; i < 150; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		float restDist2 = ally.GlobalPosition.DistanceTo(ally.SlotWorldPosition());
		bool reformed = restDist2 < 0.3f;
		GD.Print($"after turn: {restDist2:0.00} m from rotated slot (reformed={reformed})");

		bool pass = arrived && slotMoved && reformed;
		GD.Print(pass
			? "PASS: ally holds its slot and the formation rotates with the player"
			: $"FAIL: arrived={arrived}, slotMoved={slotMoved}, reformed={reformed}");
		return pass;
	}
}
