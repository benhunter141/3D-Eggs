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
		bool allyCombat = await TestAllyCombat();
		bool stones = await TestStoneThrow();
		bool pike = await TestPikeBrace();
		bool swordman = await TestSwordmanCharge();

		GD.Print(death && knock && hook && chase && formation && allyCombat && stones && pike && swordman
			? "=== ALL PASS ===" : "=== FAIL ===");
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

	// Chunk 7: loose-leash combat. An ally engages and punches an enemy parked near its
	// slot, but ignores one far outside the leash — it holds formation instead of running
	// off (no scatter). Stationary plain Units stand in for enemies so only the ally moves.
	private async Task<bool> TestAllyCombat()
	{
		GD.Print("=== UnitTest: ally combat (loose leash + fists) ===");

		// Wipe leftover units so stray AI/knockback doesn't perturb the ally.
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var player = new Node3D();
		AddChild(player);
		player.AddToGroup("player");
		player.GlobalPosition = Vector3.Zero;

		var ally = GD.Load<PackedScene>("res://scenes/Ally.tscn").Instantiate<Ally>();
		ally.FormationOffset = new Vector3(2f, 0f, 1.5f);
		AddChild(ally);                                   // _Ready grabs the player anchor
		ally.GlobalPosition = ally.SlotWorldPosition();   // start parked in its slot

		// A stationary enemy a couple of metres from the slot — inside the leash.
		var near = new Unit { Team = Unit.TeamId.Enemy, MaxHealth = 100f };
		AddChild(near);
		near.GlobalPosition = new Vector3(4f, 0f, 3f);
		float nearToSlot = ally.SlotWorldPosition().DistanceTo(near.GlobalPosition);
		GD.Print($"near enemy {nearToSlot:0.00} m from slot (leash={ally.LeashRadius}) -> should engage");

		// ~2 s: long enough to close the gap and land a few punches.
		for (int i = 0; i < 120; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		bool engaged = near.Health < near.MaxHealth;
		bool stayedClose = ally.GlobalPosition.DistanceTo(ally.SlotWorldPosition()) <= ally.LeashRadius;
		// Forward is -Z: the ally's front should point AT the enemy it's punching, not away.
		Vector3 fwd = -ally.GlobalTransform.Basis.Z;
		Vector3 toEnemy = (near.GlobalPosition - ally.GlobalPosition).Normalized();
		float facingDot = fwd.Dot(toEnemy);
		bool facing = facingDot > 0.9f;
		GD.Print($"near enemy HP={near.Health}/{near.MaxHealth} (engaged={engaged}), " +
			$"ally {ally.GlobalPosition.DistanceTo(ally.SlotWorldPosition()):0.00} m from slot (stayedClose={stayedClose}), " +
			$"facing dot={facingDot:0.00} (facing={facing})");

		// Now a fresh fight: enemy far outside the leash. The ally must ignore it and
		// re-form on its slot rather than chasing across the map.
		near.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var far = new Unit { Team = Unit.TeamId.Enemy, MaxHealth = 100f };
		AddChild(far);
		far.GlobalPosition = new Vector3(15f, 0f, 15f);
		float farToSlot = ally.SlotWorldPosition().DistanceTo(far.GlobalPosition);
		GD.Print($"far enemy {farToSlot:0.00} m from slot (leash={ally.LeashRadius}) -> should ignore");

		for (int i = 0; i < 120; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		bool ignored = far.Health == far.MaxHealth;
		bool reformed = ally.GlobalPosition.DistanceTo(ally.SlotWorldPosition()) < 0.3f;
		GD.Print($"far enemy HP={far.Health}/{far.MaxHealth} (ignored={ignored}), " +
			$"ally {ally.GlobalPosition.DistanceTo(ally.SlotWorldPosition()):0.00} m from slot (reformed={reformed})");

		bool pass = engaged && stayedClose && facing && ignored && reformed;
		GD.Print(pass
			? "PASS: ally engages in-leash enemies, faces them, ignores far ones, and re-forms"
			: $"FAIL: engaged={engaged}, stayedClose={stayedClose}, facing={facing}, ignored={ignored}, reformed={reformed}");
		return pass;
	}

	// Chunk 8: a stone-throwing ally pelts an enemy in leash range from where it stands.
	// Park a stone-ally in its slot with a stationary enemy a few metres away (inside both
	// leash and throw range); after a couple of seconds the enemy should have taken stone
	// damage WITHOUT the ally charging into melee — ranged allies hold their ground.
	private async Task<bool> TestStoneThrow()
	{
		GD.Print("=== UnitTest: ally stone throwing (ranged) ===");

		// Wipe leftover units (and any in-flight stones) so nothing perturbs the test.
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var player = new Node3D();
		AddChild(player);
		player.AddToGroup("player");
		player.GlobalPosition = Vector3.Zero;

		var ally = GD.Load<PackedScene>("res://scenes/Ally.tscn").Instantiate<Ally>();
		ally.Weapon = Ally.WeaponType.Stones;          // set before _Ready so it loads the stone scene
		ally.FormationOffset = new Vector3(2f, 0f, 1.5f);
		AddChild(ally);
		ally.GlobalPosition = ally.SlotWorldPosition();   // parked in its slot

		// Stationary enemy ~2.5 m from the slot: inside both leash and throw range.
		var enemy = new Unit { Team = Unit.TeamId.Enemy, MaxHealth = 100f };
		AddChild(enemy);
		enemy.GlobalPosition = new Vector3(4f, 0f, 3f);
		float toSlot = ally.SlotWorldPosition().DistanceTo(enemy.GlobalPosition);
		GD.Print($"enemy {toSlot:0.00} m from slot (leash={ally.LeashRadius}, throwRange={ally.ThrowRange}) -> should be pelted");

		// ~2 s: enough for a throw or two to land at StoneDamage each.
		for (int i = 0; i < 120; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		bool hit = enemy.Health < enemy.MaxHealth;
		float allyToSlot = ally.GlobalPosition.DistanceTo(ally.SlotWorldPosition());
		bool heldGround = allyToSlot < 1.0f;   // didn't rush into melee like a fist-ally would
		GD.Print($"enemy HP={enemy.Health}/{enemy.MaxHealth} (hit={hit}), " +
			$"ally {allyToSlot:0.00} m from slot (heldGround={heldGround})");

		bool pass = hit && heldGround;
		GD.Print(pass
			? "PASS: stone-ally pelted the enemy from range without charging in"
			: $"FAIL: hit={hit}, heldGround={heldGround}");
		return pass;
	}

	// Chunk 13: a swordman acquires a stationary player-team target, CHARGE-bursts toward it
	// (closing more ground in the charge window than its walk speed alone could), then closes
	// the rest of the way and lands a melee hit. A plain Unit stands in for the target so only
	// the swordman moves.
	private async Task<bool> TestSwordmanCharge()
	{
		GD.Print("=== UnitTest: swordman charge + melee ===");

		// Wipe leftover units so nothing else moves or gets targeted during the run.
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var target = new Unit { Team = Unit.TeamId.Player, MaxHealth = 100f };
		AddChild(target);
		target.GlobalPosition = Vector3.Zero;

		var sword = GD.Load<PackedScene>("res://scenes/Swordman.tscn").Instantiate<Swordman>();
		AddChild(sword);
		sword.GlobalPosition = new Vector3(0f, 0f, 10f);

		float startDist = sword.GlobalPosition.DistanceTo(target.GlobalPosition);
		float walkOnly = sword.MoveSpeed * sword.ChargeDuration; // ground a plain walk covers in the charge window
		GD.Print($"start dist={startDist:0.00}, chargeDur={sword.ChargeDuration}s, walk-only would close ~{walkOnly:0.00} m");

		// Step exactly the charge window (~60 Hz physics) and measure ground closed.
		int chargeFrames = Mathf.CeilToInt(sword.ChargeDuration * 60f);
		for (int i = 0; i < chargeFrames; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		float midDist = sword.GlobalPosition.DistanceTo(target.GlobalPosition);
		float closed = startDist - midDist;
		bool burst = closed > walkOnly;   // the charge must outrun a plain walk
		GD.Print($"after charge window: dist={midDist:0.00}, closed={closed:0.00} m (burst beats walk={burst})");

		// Let it finish closing and land a hit.
		for (int i = 0; i < 140; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		float endDist = sword.GlobalPosition.DistanceTo(target.GlobalPosition);
		bool reached = endDist <= sword.AttackRange + 0.3f;
		bool damaged = target.Health < target.MaxHealth;
		GD.Print($"end dist={endDist:0.00}, target HP={target.Health}/{target.MaxHealth} (reached={reached}, damaged={damaged})");

		bool pass = burst && reached && damaged;
		GD.Print(pass
			? "PASS: swordman charge-bursts in and lands a hit"
			: $"FAIL: burst={burst}, reached={reached}, damaged={damaged}");
		return pass;
	}

	// Chunk 12: pike reach gate + brace. A BRACED pikeman holds its slot (doesn't chase),
	// faces the captain's yaw, and damages + repels an enemy planted inside PikeReach in its
	// front — but an enemy just BEYOND reach is untouched. Plain Units stand in for enemies:
	// they have no _PhysicsProcess, so a repel impulse stays on their KnockbackVelocity
	// (never decayed, never applied), making the shove easy to observe.
	private async Task<bool> TestPikeBrace()
	{
		GD.Print("=== UnitTest: pikeman brace (reach gate + repel) ===");

		// Wipe leftover units so nothing else perturbs the test.
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Captain anchor at origin, yaw 0 -> facing -Z. The braced pike faces this yaw.
		var player = new Node3D();
		AddChild(player);
		player.AddToGroup("player");
		player.GlobalPosition = Vector3.Zero;
		player.Rotation = Vector3.Zero;

		var pike = GD.Load<PackedScene>("res://scenes/Pikeman.tscn").Instantiate<Ally>();
		pike.FormationOffset = new Vector3(0f, 0f, 2f);   // slot straight behind the captain
		AddChild(pike);
		pike.GlobalPosition = pike.SlotWorldPosition();   // parked in its slot
		pike.Rotation = Vector3.Zero;                     // forward = -Z, aligned with captain yaw
		GD.Print($"pike weapon={pike.Weapon}, reach={pike.PikeReach}, slot={pike.SlotWorldPosition()}");

		// Hold BRACE for the whole test (polled by the pikeman each frame).
		Input.ActionPress("brace");

		// HIT case: enemy 2.5 m in front (-Z), inside PikeReach (3 m).
		var inReach = new Unit { Team = Unit.TeamId.Enemy, MaxHealth = 100f };
		AddChild(inReach);
		inReach.GlobalPosition = pike.GlobalPosition + new Vector3(0f, 0f, -2.5f);

		// MISS case: enemy 3.6 m in front, just BEYOND PikeReach.
		var outOfReach = new Unit { Team = Unit.TeamId.Enemy, MaxHealth = 100f };
		AddChild(outOfReach);
		outOfReach.GlobalPosition = pike.GlobalPosition + new Vector3(0f, 0f, -3.6f);

		Vector3 slotBefore = pike.SlotWorldPosition();

		// ~1.5 s: enough for a couple of brace pulses on cooldown.
		for (int i = 0; i < 90; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		Input.ActionRelease("brace");

		bool damagedInReach = inReach.Health < inReach.MaxHealth;
		bool untouchedOut = outOfReach.Health == outOfReach.MaxHealth;
		// Repel: the in-reach enemy should carry a shove pointing AWAY from the pike (toward -Z).
		Vector3 kb = inReach.CurrentKnockback;
		bool repelled = kb.Length() > 0.1f && kb.Z < 0f;
		// Held the line: braced pike stays planted on its slot, never chases.
		bool heldSlot = pike.GlobalPosition.DistanceTo(slotBefore) < 0.3f;

		GD.Print($"in-reach HP={inReach.Health}/{inReach.MaxHealth} (damaged={damagedInReach}), " +
			$"repel kb={kb} (repelled={repelled})");
		GD.Print($"out-of-reach HP={outOfReach.Health}/{outOfReach.MaxHealth} (untouched={untouchedOut}), " +
			$"pike {pike.GlobalPosition.DistanceTo(slotBefore):0.00} m from slot (heldSlot={heldSlot})");

		bool pass = damagedInReach && repelled && untouchedOut && heldSlot;
		GD.Print(pass
			? "PASS: braced pike impales + repels its front within reach, ignores beyond reach, holds the line"
			: $"FAIL: damaged={damagedInReach}, repelled={repelled}, untouched={untouchedOut}, heldSlot={heldSlot}");
		return pass;
	}
}
