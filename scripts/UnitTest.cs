using Godot;
using System.Linq;
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
		bool zoom = TestZoomBias();
		bool chase = await TestChase();
		bool formation = await TestFormation();
		bool allyCombat = await TestAllyCombat();
		bool stones = await TestStoneThrow();
		bool pike = await TestPikeBrace();
		bool swordman = await TestSwordmanCharge();
		bool bowman = await TestBowmanKite();
		bool registry = await TestRegistry();
		bool bounce = await TestKnockbackBounce();
		bool bumper = await TestBumperKick();
		bool weaponSwap = await TestWeaponSwap();

		GD.Print(death && knock && hook && zoom && chase && formation && allyCombat && stones && pike && swordman && bowman && registry && bounce && bumper && weaponSwap
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

	// Chunk 23: the live player zoom bias shifts the camera's target distance and stays
	// clamped at both ends, in both modes. Pure math on FollowCamera.StepZoom/DesiredDistance
	// (no tree / input needed) — the part the headless harness can verify; the feel is the
	// user's call.
	private bool TestZoomBias()
	{
		GD.Print("=== UnitTest: camera zoom bias (Chunk 23) ===");

		var cam = new FollowCamera
		{
			DynamicZoom = true,
			MinDistance = 30f, MaxDistance = 60f,
			FitScale = 1.5f, ZoomMargin = 10f,
			ZoomStep = 3f, ZoomBiasMin = -24f, ZoomBiasMax = 24f,
		};

		const float spread = 20f;                       // base = 20*1.5 + 10 = 40 m (mid-band)
		float baseDist = cam.DesiredDistance(spread);
		bool baseOk = Mathf.IsEqualApprox(baseDist, 40f, 0.01f);

		cam.StepZoom(+1f);                              // one notch out
		float outOne = cam.DesiredDistance(spread);
		bool shiftsOut = outOne > baseDist;

		for (int i = 0; i < 50; i++) cam.StepZoom(+1f); // saturate outward
		float far = cam.DesiredDistance(spread);
		bool clampHi = Mathf.IsEqualApprox(cam.ZoomBias, cam.ZoomBiasMax, 0.01f)
		             && Mathf.IsEqualApprox(far, cam.MaxDistance, 0.01f);

		for (int i = 0; i < 100; i++) cam.StepZoom(-1f); // saturate inward
		float near = cam.DesiredDistance(spread);
		bool clampLo = Mathf.IsEqualApprox(cam.ZoomBias, cam.ZoomBiasMin, 0.01f)
		             && Mathf.IsEqualApprox(near, cam.MinDistance, 0.01f);

		// Fixed mode: bias hangs off the authored Offset length, floored at MinFixedDistance
		// so a hard zoom-in can't pass through the player.
		var fixedCam = new FollowCamera
		{
			DynamicZoom = false,
			Offset = new Vector3(0, 18, 11),            // length ~21.1 m
			ZoomStep = 3f, ZoomBiasMin = -24f, ZoomBiasMax = 24f, MinFixedDistance = 6f,
		};
		float fixedBase = fixedCam.DesiredDistance(0f);
		bool fixedBaseOk = Mathf.IsEqualApprox(fixedBase, fixedCam.Offset.Length(), 0.01f);
		for (int i = 0; i < 100; i++) fixedCam.StepZoom(-1f);
		float fixedNear = fixedCam.DesiredDistance(0f); // 21.1 - 24 = -2.9 -> floored to 6
		bool fixedFloorOk = Mathf.IsEqualApprox(fixedNear, fixedCam.MinFixedDistance, 0.01f);

		bool pass = baseOk && shiftsOut && clampHi && clampLo && fixedBaseOk && fixedFloorOk;
		GD.Print($"dynamic: base={baseDist:0.0} outOne={outOne:0.0} far={far:0.0} near={near:0.0}; " +
		         $"fixed: base={fixedBase:0.0} near={fixedNear:0.0}");
		GD.Print(pass ? "PASS: zoom bias shifts the target distance and stays clamped"
		              : "FAIL: zoom bias math wrong");

		cam.Free();
		fixedCam.Free();
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

	// Chunk 14: bowman kite + arrow. Three checks in one run:
	//   (a) ARROW — a fired arrow damages a player-team unit (opposite team) and frees itself.
	//   (b) HOLD RANGE — a bowman with a target parked inside its range band stands roughly put
	//       (never charges into melee) and whittles it down with arrows.
	//   (c) FLEE — a bowman with a target right on top of it backpedals, opening the distance.
	// Plain stationary Units stand in for the player-team targets so only the bowman moves.
	private async Task<bool> TestBowmanKite()
	{
		GD.Print("=== UnitTest: bowman kite + arrow ===");

		// Wipe leftover units (and any in-flight projectiles) so nothing perturbs the test.
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// (a) Arrow hits an opposite-team unit.
		var arrowTarget = new Unit { Team = Unit.TeamId.Player, MaxHealth = 100f };
		AddChild(arrowTarget);
		arrowTarget.GlobalPosition = new Vector3(0f, 0f, -6f);
		var arrow = GD.Load<PackedScene>("res://scenes/Arrow.tscn").Instantiate<Arrow>();
		AddChild(arrow);
		arrow.GlobalPosition = Vector3.Zero;
		arrow.Launch(arrowTarget.GlobalPosition - arrow.GlobalPosition, Unit.TeamId.Enemy);
		for (int i = 0; i < 60; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		bool arrowHit = arrowTarget.Health < arrowTarget.MaxHealth && !IsInstanceValid(arrow);
		GD.Print($"arrow: target HP={arrowTarget.Health}/{arrowTarget.MaxHealth}, arrow freed={!IsInstanceValid(arrow)} (hit={arrowHit})");

		// (b) Hold range: target parked at 12 m (inside the 10–14 m band).
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var holdTarget = new Unit { Team = Unit.TeamId.Player, MaxHealth = 100f };
		AddChild(holdTarget);
		holdTarget.GlobalPosition = new Vector3(0f, 0f, 12f);

		var holdBow = GD.Load<PackedScene>("res://scenes/Bowman.tscn").Instantiate<Bowman>();
		AddChild(holdBow);
		holdBow.GlobalPosition = Vector3.Zero;
		float holdStartDist = holdBow.GlobalPosition.DistanceTo(holdTarget.GlobalPosition);
		GD.Print($"hold: start {holdStartDist:0.00} m (band {holdBow.PreferredRangeMin}–{holdBow.PreferredRangeMax}) -> should hold + shoot");

		for (int i = 0; i < 180; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		float holdEndDist = holdBow.GlobalPosition.DistanceTo(holdTarget.GlobalPosition);
		bool stayedInBand = holdEndDist >= holdBow.FleeRange && holdEndDist <= holdBow.PreferredRangeMax + 1.0f;
		bool shotTarget = holdTarget.Health < holdTarget.MaxHealth;
		GD.Print($"hold: end {holdEndDist:0.00} m (stayedInBand={stayedInBand}), target HP={holdTarget.Health}/{holdTarget.MaxHealth} (shot={shotTarget})");

		// (c) Flee: target right on top of the bowman (inside FleeRange).
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var meleeTarget = new Unit { Team = Unit.TeamId.Player, MaxHealth = 100f };
		AddChild(meleeTarget);
		meleeTarget.GlobalPosition = Vector3.Zero;

		var fleeBow = GD.Load<PackedScene>("res://scenes/Bowman.tscn").Instantiate<Bowman>();
		AddChild(fleeBow);
		fleeBow.GlobalPosition = new Vector3(0f, 0f, 3f);   // 3 m < FleeRange
		float fleeStartDist = fleeBow.GlobalPosition.DistanceTo(meleeTarget.GlobalPosition);
		GD.Print($"flee: start {fleeStartDist:0.00} m (fleeRange={fleeBow.FleeRange}) -> should back off");

		for (int i = 0; i < 90; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		float fleeEndDist = fleeBow.GlobalPosition.DistanceTo(meleeTarget.GlobalPosition);
		bool retreated = fleeEndDist > fleeStartDist + 1.0f;
		GD.Print($"flee: end {fleeEndDist:0.00} m (retreated={retreated})");

		bool pass = arrowHit && stayedInBand && shotTarget && retreated;
		GD.Print(pass
			? "PASS: arrow damages foes; bowman holds its band and shoots, and flees when charged"
			: $"FAIL: arrowHit={arrowHit}, stayedInBand={stayedInBand}, shot={shotTarget}, retreated={retreated}");
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

	// Chunk 17 (M5): the UnitRegistry that replaced the per-frame group scans tracks units
	// by team, returns the nearest living opponent, honours a max range, skips dead bodies
	// that haven't been freed yet, and drops units once they leave the tree.
	private async Task<bool> TestRegistry()
	{
		GD.Print("=== UnitTest: unit registry (nearest-opponent service) ===");

		// Clear leftovers so only this test's units populate the registry.
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var hero = new Unit { Team = Unit.TeamId.Player, MaxHealth = 100f };
		AddChild(hero);
		hero.GlobalPosition = Vector3.Zero;

		// Three enemies at increasing distance along +X.
		var near = new Unit { Team = Unit.TeamId.Enemy, MaxHealth = 30f };
		var mid = new Unit { Team = Unit.TeamId.Enemy, MaxHealth = 100f };
		var far = new Unit { Team = Unit.TeamId.Enemy, MaxHealth = 100f };
		AddChild(near); AddChild(mid); AddChild(far);
		near.GlobalPosition = new Vector3(3f, 0f, 0f);
		mid.GlobalPosition = new Vector3(7f, 0f, 0f);
		far.GlobalPosition = new Vector3(12f, 0f, 0f);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		int enemyCount = UnitRegistry.Opponents(Unit.TeamId.Player).Count;
		bool counted = enemyCount == 3;

		Unit pick1 = UnitRegistry.FindNearestOpponent(Unit.TeamId.Player, hero.GlobalPosition);
		bool nearestOk = pick1 == near;

		// maxRange caps the search: nothing lives within 2 m of the hero.
		bool cappedOk = UnitRegistry.FindNearestOpponent(Unit.TeamId.Player, hero.GlobalPosition, 2f) == null;

		// A dead-but-not-yet-freed unit is skipped, so the next-nearest is returned.
		near.TakeDamage(999f);   // lethal -> IsDead, lingers DeathLinger before QueueFree
		bool skipsDead = UnitRegistry.FindNearestOpponent(Unit.TeamId.Player, hero.GlobalPosition) == mid;

		// Enemies see the hero as their nearest opponent (buckets are symmetric).
		bool enemyViewOk = UnitRegistry.FindNearestOpponent(Unit.TeamId.Enemy, mid.GlobalPosition) == hero;

		// After the corpse frees (> DeathLinger 0.4 s), it leaves the registry entirely.
		for (int i = 0; i < 40; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		bool unregistered = !UnitRegistry.Opponents(Unit.TeamId.Player).Contains(near)
			&& UnitRegistry.Opponents(Unit.TeamId.Player).Count == 2;

		GD.Print($"count={enemyCount}(ok={counted}), nearest={(nearestOk ? "near" : "?")}, capped null={cappedOk}, " +
			$"skipsDead->mid={skipsDead}, enemyView->hero={enemyViewOk}, freedUnregistered={unregistered}");

		bool pass = counted && nearestOk && cappedOk && skipsDead && enemyViewOk && unregistered;
		GD.Print(pass
			? "PASS: registry buckets by team, finds nearest, honours range, skips dead, unregisters on free"
			: "FAIL: registry behaviour wrong");
		return pass;
	}

	// Chunk 20 (M6): pinball collision response. A "cue" skeleton flung straight at a stationary
	// "pin" skeleton should (a) hand the pin part of its momentum along the impact line, shoving
	// the pin onward, and (b) BOUNCE back off it. Both are on the Enemy team so neither has an
	// opponent to chase — only the cue's knockback moves it, isolating the collision response.
	// (Real scene instances, not plain Units, so they carry the collision shapes the bodies need
	// to actually ram each other.) Knockback on both decays each frame, so we sample per frame.
	private async Task<bool> TestKnockbackBounce()
	{
		GD.Print("=== UnitTest: knockback bounce + transfer (pinball) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Pin at the origin; cue 3 m away on +Z, about to be flung toward it (-Z).
		var pin = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		AddChild(pin);
		pin.GlobalPosition = Vector3.Zero;

		var cue = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		AddChild(cue);
		cue.GlobalPosition = new Vector3(0f, 0f, 3f);

		// Fling the cue straight at the pin, fast enough to clear MinBounceSpeed on contact.
		const float strength = 12f;
		cue.AddKnockback(new Vector3(0f, 0f, -strength));
		GD.Print($"cue flung at {strength} m/s toward the pin (minBounce={cue.MinBounceSpeed}, transfer={cue.KnockbackTransfer}, restitution={cue.KnockbackBounce})");

		// Step ~1.3 s, watching for the impact. The mechanic is in the velocities: the pin should
		// pick up a -Z shove (momentum handed ON, along the cue's travel) and the cue should
		// reverse to +Z (rebound). We assert on those, not final positions — two equal capsules
		// meeting head-on at speed overlap for a frame and Godot slides them through each other,
		// so post-impact positions are noisy even though the knockback impulses are exact.
		float pinShoveZ = 0f;    // most-negative Z knockback seen on the pin
		float cueReflectZ = 0f;  // most-positive Z knockback seen on the cue
		for (int i = 0; i < 80; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			pinShoveZ = Mathf.Min(pinShoveZ, pin.CurrentKnockback.Z);
			cueReflectZ = Mathf.Max(cueReflectZ, cue.CurrentKnockback.Z);
		}

		// Expected magnitude: contact speed (~11 m/s) scaled by the transfer / restitution fracs.
		bool transferred = pinShoveZ < -1f;              // pin got shoved along the cue's travel
		bool bounced = cueReflectZ > 1f;                 // cue rebounded back off the pin
		GD.Print($"pin peak shove Z={pinShoveZ:0.00} (transferred={transferred}), " +
			$"cue peak reflect Z={cueReflectZ:0.00} (bounced={bounced})");

		bool pass = transferred && bounced;
		GD.Print(pass
			? "PASS: a flung unit hands its momentum to what it rams and bounces back off it"
			: $"FAIL: transferred={transferred}, bounced={bounced}");
		return pass;
	}

	// Chunk 21 (M6): a pinball bumper. A skeleton shoved straight at a bumper should be KICKED
	// back out — leaving with MORE speed than it came in with, pointing AWAY from the bumper.
	// The skeleton is alone on its team so nothing else moves it; only the push + the bumper's
	// kick act on it. (Real Skeleton instance so it carries the collision shape that trips the
	// detection ring.) Knockback decays each frame, so we sample the peak outward shove seen.
	private async Task<bool> TestBumperKick()
	{
		GD.Print("=== UnitTest: bumper kick (pinball) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Bumper at the origin; a skeleton 3 m away on +Z, about to be shoved toward it (-Z).
		var bumper = GD.Load<PackedScene>("res://scenes/Bumper.tscn").Instantiate<Bumper>();
		AddChild(bumper);
		bumper.GlobalPosition = Vector3.Zero;

		var unit = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		AddChild(unit);
		unit.GlobalPosition = new Vector3(0f, 0f, 3f);

		// Shove it INTO the bumper (toward -Z). The bumper sits between it and the origin.
		const float pushSpeed = 6f;
		unit.AddKnockback(new Vector3(0f, 0f, -pushSpeed));
		GD.Print($"skeleton shoved at {pushSpeed} m/s into the bumper (strength={bumper.BumperStrength})");

		// Step ~1.3 s, watching the unit's knockback. The bumper sits at the origin and the
		// unit on +Z, so "away" is +Z: a successful kick shows a +Z shove bigger than pushSpeed.
		float peakOutZ = 0f;
		for (int i = 0; i < 80; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			peakOutZ = Mathf.Max(peakOutZ, unit.CurrentKnockback.Z);
		}

		bool flungAway = peakOutZ > pushSpeed;   // left pointing away (+Z) AND faster than it arrived
		GD.Print($"peak outward shove Z={peakOutZ:0.00} vs push {pushSpeed} (flungAway={flungAway})");

		bool pass = flungAway;
		GD.Print(pass
			? "PASS: the bumper kicks a unit back out faster than it came in"
			: $"FAIL: flungAway={flungAway}");
		return pass;
	}

	// Chunk 26 (M9): the captain's weapon swap. The spear is a long-reach, no-knockback poker;
	// the sword is a short-reach, hard-knockback flinger. Spawning with the spear, the swap
	// toggles to the sword and back, and each weapon's hitbox box LENGTH resizes to its reach
	// while damage/knockback/feel follow its profile. The real Captain scene is used so the
	// _Ready wiring (mesh nodes + owned hitbox) is exercised exactly as in play.
	private async Task<bool> TestWeaponSwap()
	{
		GD.Print("=== UnitTest: captain weapon swap (spear <-> sword) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var cap = GD.Load<PackedScene>("res://scenes/Captain.tscn").Instantiate<Player>();
		cap.StartingWeapon = Player.WeaponType.Spear;   // set before _Ready so it spawns with the spear
		AddChild(cap);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var spearMesh = cap.GetNode<Node3D>("SwordPivot/Spear");
		var swordMesh = cap.GetNode<Node3D>("SwordPivot/SwordMesh");

		// Spear: long reach, NO knockback, hitbox box length == SpearReach, only the spear shown.
		bool spearIsSpear = cap.CurrentWeapon == Player.WeaponType.Spear;
		bool spearNoKnock = Mathf.IsZeroApprox(cap.CurrentWeaponKnockback);
		bool spearHitbox = Mathf.IsEqualApprox(cap.HitboxLength, cap.SpearReach, 0.01f);
		bool spearMeshShown = spearMesh.Visible && !swordMesh.Visible;
		float spearReach = cap.EffectiveReach;
		GD.Print($"spear: weapon={cap.CurrentWeapon}, knockback={cap.CurrentWeaponKnockback}, " +
			$"hitboxLen={cap.HitboxLength:0.00} (want {cap.SpearReach}), reach={spearReach:0.00}, meshOk={spearMeshShown}");

		// Swap -> sword: short reach, knockback > 0, hitbox shrinks to SwordReach, sword mesh shown.
		cap.SwapWeapon();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		bool swordIsSword = cap.CurrentWeapon == Player.WeaponType.Sword;
		bool swordKnocks = cap.CurrentWeaponKnockback > 0f;
		bool swordHitbox = Mathf.IsEqualApprox(cap.HitboxLength, cap.SwordReach, 0.01f);
		bool swordMeshShown = swordMesh.Visible && !spearMesh.Visible;
		float swordReach = cap.EffectiveReach;
		GD.Print($"sword: weapon={cap.CurrentWeapon}, knockback={cap.CurrentWeaponKnockback}, " +
			$"hitboxLen={cap.HitboxLength:0.00} (want {cap.SwordReach}), reach={swordReach:0.00}, meshOk={swordMeshShown}");

		// The spear must genuinely out-range the sword, and a second swap returns to the spear.
		bool spearOutreaches = spearReach > swordReach;
		cap.SwapWeapon();
		bool swappedBack = cap.CurrentWeapon == Player.WeaponType.Spear;
		GD.Print($"spear reach {spearReach:0.00} > sword reach {swordReach:0.00} = {spearOutreaches}; swappedBack={swappedBack}");

		bool pass = spearIsSpear && spearNoKnock && spearHitbox && spearMeshShown
			&& swordIsSword && swordKnocks && swordHitbox && swordMeshShown
			&& spearOutreaches && swappedBack;
		GD.Print(pass
			? "PASS: weapon swap toggles reach, knockback, feel, and mesh per weapon profile"
			: $"FAIL: spear(is={spearIsSpear},noKnock={spearNoKnock},hitbox={spearHitbox},mesh={spearMeshShown}) " +
			  $"sword(is={swordIsSword},knocks={swordKnocks},hitbox={swordHitbox},mesh={swordMeshShown}) " +
			  $"outreach={spearOutreaches},back={swappedBack}");
		return pass;
	}
}
