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
		bool archetypes = await TestWeaponArchetypes();
		bool basicEgg = await TestBasicEgg();
		bool mount = await TestMount();
		bool chocobo = await TestChocobo();
		bool capturePoint = await TestCapturePoint();
		bool cardDeck = TestCardDeck();
		bool cardPlay = TestCardPlay();
		bool roundLoop = TestRoundLoop();
		bool unitStats = await TestUnitStats();
		bool cardEnergy = TestCardEnergy();
		bool runMap = TestRunMap();
		bool relicsPotions = TestRelicsPotions();
		bool endzone = TestEndzone();
		bool march = await TestMarch();
		bool deckTuning = TestDeckTuning();
		bool stickAim = TestStickAim();
		bool squadOwnership = await TestSquadOwnership();
		bool coopCamera = TestCoopCamera();
		bool coopLose = await TestCoopLose();
		bool allyCommands = await TestAllyCommands();
		bool squadCommands = await TestSquadCommands();
		bool terrainCollision = await TestTerrainCollision();
		bool groundedMovement = await TestGroundedMovement();
		bool spawnFormationHeight = await TestSpawnFormationHeight();
		bool ballistic = await TestBallisticProjectiles();
		bool terrainCamera = TestTerrainCamera();

		GD.Print(death && knock && hook && zoom && chase && formation && allyCombat && stones && pike && swordman && bowman && registry && bounce && bumper && weaponSwap && archetypes && basicEgg && mount && chocobo && capturePoint && cardDeck && cardPlay && roundLoop && unitStats && cardEnergy && runMap && relicsPotions && endzone && march && deckTuning && stickAim && squadOwnership && coopCamera && coopLose && allyCommands && squadCommands && terrainCollision && groundedMovement && spawnFormationHeight && ballistic && terrainCamera
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

		const float spread = 20f;                       // framed = clamp(20*1.5 + 10, 30, 60) = 40 m (mid-band)
		float baseDist = cam.DesiredDistance(spread);
		bool baseOk = Mathf.IsEqualApprox(baseDist, 40f, 0.01f);

		cam.StepZoom(+1f);                              // one notch out
		float outOne = cam.DesiredDistance(spread);
		bool shiftsOut = outOne > baseDist;

		// Bias sits ON TOP of the auto-frame now, so saturating outward pushes PAST MaxDistance
		// (framed 40 + ZoomBiasMax 24 = 64), not capped at it.
		for (int i = 0; i < 50; i++) cam.StepZoom(+1f); // saturate outward
		float far = cam.DesiredDistance(spread);
		bool clampHi = Mathf.IsEqualApprox(cam.ZoomBias, cam.ZoomBiasMax, 0.01f)
		             && Mathf.IsEqualApprox(far, 40f + cam.ZoomBiasMax, 0.01f);

		// Saturating inward now pulls CLOSER than MinDistance (framed 40 + ZoomBiasMin -24 = 16,
		// above the MinFixedDistance floor), the whole point of this change.
		for (int i = 0; i < 100; i++) cam.StepZoom(-1f); // saturate inward
		float near = cam.DesiredDistance(spread);
		bool clampLo = Mathf.IsEqualApprox(cam.ZoomBias, cam.ZoomBiasMin, 0.01f)
		             && Mathf.IsEqualApprox(near, 40f + cam.ZoomBiasMin, 0.01f);

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

		// A player-team dummy on the FAR side of the bumper so the skeleton (enemy) keeps
		// chasing toward -Z, straight into and through the bumper — reproducing real play
		// where a unit drives into a bumper instead of being shoved at it once.
		var lure = GD.Load<PackedScene>("res://scenes/Captain.tscn").Instantiate<Player>();
		AddChild(lure);
		lure.GlobalPosition = new Vector3(0f, 0f, -6f);

		// Shove it INTO the bumper (toward -Z). The bumper sits between it and the origin.
		const float pushSpeed = 6f;
		unit.AddKnockback(new Vector3(0f, 0f, -pushSpeed));
		GD.Print($"skeleton shoved at {pushSpeed} m/s into the bumper (strength={bumper.BumperStrength})");

		// Step ~1.3 s. Two things to verify on ONE bumper touch:
		//   (1) it's flung back out faster than it came in (the kick), and
		//   (2) the kick is the ONLY abrupt velocity change — as the shove decays the unit must
		//       ease its chase back in, NOT snap it on at a hard threshold. That snap was a bug:
		//       it jolted the unit (often reversing it) ~half a second after impact, reading as a
		//       spurious SECOND bump. So after the kick frame, no single frame may swing velZ hard.
		float peakOutZ = 0f;
		float maxPostKickJolt = 0f;   // largest single-frame |ΔvelZ| once the unit is riding outward
		float prevVelZ = unit.Velocity.Z;
		bool ridingOut = false;       // true once the kick has launched us outward (+Z)
		for (int i = 0; i < 80; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			float velZ = unit.Velocity.Z;
			peakOutZ = Mathf.Max(peakOutZ, unit.CurrentKnockback.Z);

			// Scope the smoothness check to the FIRST touch: once the kick has launched us outward,
			// measure single-frame velZ swings until that shove fully decays. We stop there so the
			// unit walking BACK into the bumper for a second, legitimate kick isn't mistaken for the
			// handoff jolt we're guarding against. The kick frame itself (inward -> outward launch)
			// is the one allowed abrupt change, so we only start measuring once riding outward.
			if (ridingOut)
			{
				maxPostKickJolt = Mathf.Max(maxPostKickJolt, Mathf.Abs(velZ - prevVelZ));
				if (unit.CurrentKnockback.Z <= 0.2f)
					break;   // first kick spent; the single-touch handoff is fully observed
			}
			if (velZ > 0.5f)
				ridingOut = true;
			prevVelZ = velZ;
		}

		bool flungAway = peakOutZ > pushSpeed;   // left pointing away (+Z) AND faster than it arrived
		// The smooth handoff ramps velZ by ~Acceleration*dt per frame; a hard snap jumped it by
		// several m/s in one frame. 1.0 m/s sits well above the smooth ramp, well below the old snap.
		bool noSecondBump = maxPostKickJolt < 1.0f;
		GD.Print($"peak outward shove Z={peakOutZ:0.00} vs push {pushSpeed} (flungAway={flungAway}); " +
			$"max post-kick single-frame velZ jump={maxPostKickJolt:0.00} (noSecondBump={noSecondBump})");

		bool pass = flungAway && noSecondBump;
		GD.Print(pass
			? "PASS: bumper kicks the unit out as ONE clean shove that eases back into control (no second bump)"
			: $"FAIL: flungAway={flungAway}, noSecondBump={noSecondBump}");
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

		// The spear must genuinely out-range the sword, and cycling the rest of the way around
		// (one swap per remaining weapon) wraps back to the spear we started on.
		bool spearOutreaches = spearReach > swordReach;
		for (int i = 0; i < Player.SwappableWeaponCount - 1; i++) cap.SwapWeapon();
		bool swappedBack = cap.CurrentWeapon == Player.WeaponType.Spear;
		GD.Print($"spear reach {spearReach:0.00} > sword reach {swordReach:0.00} = {spearOutreaches}; cycled back to spear={swappedBack}");

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

	// Chunk 27 (M9): the extra weapon archetypes. Walk every WeaponType through the data-driven
	// profile table on the real Captain scene and assert each archetype's stats resolve to its
	// design role: the axe lands the BIGGEST hit and is the SLOWEST (longest cooldown), the mace
	// flings HARDEST (most knockback), the spear reaches FURTHEST with NO knockback, and every
	// weapon shows only its own mesh while its hitbox length tracks its reach.
	private async Task<bool> TestWeaponArchetypes()
	{
		GD.Print("=== UnitTest: weapon archetypes (axe/mace + table) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var cap = GD.Load<PackedScene>("res://scenes/Captain.tscn").Instantiate<Player>();
		AddChild(cap);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Every weapon's mesh node, to confirm only the active one is shown.
		var meshes = new System.Collections.Generic.Dictionary<Player.WeaponType, Node3D>
		{
			[Player.WeaponType.Spear] = cap.GetNode<Node3D>("SwordPivot/Spear"),
			[Player.WeaponType.Sword] = cap.GetNode<Node3D>("SwordPivot/SwordMesh"),
			[Player.WeaponType.Axe]   = cap.GetNode<Node3D>("SwordPivot/AxeMesh"),
			[Player.WeaponType.Mace]  = cap.GetNode<Node3D>("SwordPivot/MaceMesh"),
		};

		var dmg = new System.Collections.Generic.Dictionary<Player.WeaponType, float>();
		var knock = new System.Collections.Generic.Dictionary<Player.WeaponType, float>();
		var reach = new System.Collections.Generic.Dictionary<Player.WeaponType, float>();
		var cooldown = new System.Collections.Generic.Dictionary<Player.WeaponType, float>();
		bool allMeshesIsolated = true;
		bool allHitboxesMatch = true;

		foreach (Player.WeaponType w in System.Enum.GetValues<Player.WeaponType>())
		{
			cap.SetWeapon(w);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			dmg[w] = cap.CurrentDamage;
			knock[w] = cap.CurrentWeaponKnockback;
			reach[w] = cap.CurrentReach;
			cooldown[w] = cap.CurrentSwingCooldown;

			// Hitbox length tracks the active weapon's reach.
			if (!Mathf.IsEqualApprox(cap.HitboxLength, cap.CurrentReach, 0.01f))
				allHitboxesMatch = false;

			// Only this weapon's mesh is visible.
			foreach (var (type, mesh) in meshes)
				if (mesh.Visible != (type == w))
					allMeshesIsolated = false;

			GD.Print($"  {w}: dmg={dmg[w]:0.0}, knockback={knock[w]:0.0}, reach={reach[w]:0.0}, " +
				$"cooldown={cooldown[w]:0.00}s, hitboxLen={cap.HitboxLength:0.0}");
		}

		// Each archetype owns its niche, relative to the others.
		bool axeHardestHit = dmg[Player.WeaponType.Axe] > dmg[Player.WeaponType.Sword]
			&& dmg[Player.WeaponType.Axe] > dmg[Player.WeaponType.Mace]
			&& dmg[Player.WeaponType.Axe] > dmg[Player.WeaponType.Spear];
		bool axeSlowest = cooldown[Player.WeaponType.Axe] > cooldown[Player.WeaponType.Sword]
			&& cooldown[Player.WeaponType.Axe] > cooldown[Player.WeaponType.Mace]
			&& cooldown[Player.WeaponType.Axe] > cooldown[Player.WeaponType.Spear];
		bool maceHardestFling = knock[Player.WeaponType.Mace] > knock[Player.WeaponType.Sword]
			&& knock[Player.WeaponType.Mace] > knock[Player.WeaponType.Axe]
			&& knock[Player.WeaponType.Mace] > knock[Player.WeaponType.Spear];
		bool spearLongestReach = reach[Player.WeaponType.Spear] > reach[Player.WeaponType.Sword]
			&& reach[Player.WeaponType.Spear] > reach[Player.WeaponType.Axe]
			&& reach[Player.WeaponType.Spear] > reach[Player.WeaponType.Mace];
		bool spearNoKnock = Mathf.IsZeroApprox(knock[Player.WeaponType.Spear]);

		bool pass = axeHardestHit && axeSlowest && maceHardestFling && spearLongestReach
			&& spearNoKnock && allMeshesIsolated && allHitboxesMatch;
		GD.Print(pass
			? "PASS: every archetype resolves to its role (axe=heaviest+slowest, mace=hardest fling, spear=longest+no knockback), mesh + hitbox per weapon"
			: $"FAIL: axeHardestHit={axeHardestHit}, axeSlowest={axeSlowest}, maceHardestFling={maceHardestFling}, " +
			  $"spearLongestReach={spearLongestReach}, spearNoKnock={spearNoKnock}, meshIsolated={allMeshesIsolated}, hitboxMatch={allHitboxesMatch}");
		return pass;
	}

	// Chunk 66 (M15): the basic egg loadout. A captain spawned with StartUnarmed wields the weak Punch —
	// low damage, NO knockback, reach UNDER the sword — and EquipWeapon arms it at runtime: switching to
	// the sword raises reach + damage and restores knockback, proving the egg gets stronger only via cards.
	private async Task<bool> TestBasicEgg()
	{
		GD.Print("=== UnitTest: basic egg loadout + runtime EquipWeapon ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var egg = GD.Load<PackedScene>("res://scenes/Captain.tscn").Instantiate<Player>();
		egg.StartUnarmed = true;   // spawn as a basic egg, not the captain's normal weapon
		AddChild(egg);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Unarmed: the Punch is up, weak, no knockback, shorter than the sword.
		bool isPunch = egg.CurrentWeapon == Player.WeaponType.Punch;
		float punchDmg = egg.CurrentDamage;
		bool punchNoKnock = Mathf.IsZeroApprox(egg.CurrentWeaponKnockback);
		bool punchShort = egg.CurrentReach < egg.SwordReach;
		bool punchHitbox = Mathf.IsEqualApprox(egg.HitboxLength, egg.PunchReach, 0.01f);
		bool punchWeak = punchDmg < egg.SwordDamage;
		GD.Print($"unarmed: weapon={egg.CurrentWeapon}, dmg={punchDmg:0.0} (sword {egg.SwordDamage}), " +
			$"knockback={egg.CurrentWeaponKnockback}, reach={egg.CurrentReach:0.0} (sword {egg.SwordReach}), hitboxLen={egg.HitboxLength:0.00}");

		// Card arms the egg: EquipWeapon(Sword) -> more reach, more damage, real knockback.
		egg.EquipWeapon(Player.WeaponType.Sword);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		bool nowSword = egg.CurrentWeapon == Player.WeaponType.Sword;
		bool armedKnocks = egg.CurrentWeaponKnockback > 0f;
		bool armedReachUp = egg.CurrentReach > egg.PunchReach;
		bool armedDmgUp = egg.CurrentDamage > punchDmg;
		bool swordHitbox = Mathf.IsEqualApprox(egg.HitboxLength, egg.SwordReach, 0.01f);
		GD.Print($"armed: weapon={egg.CurrentWeapon}, dmg={egg.CurrentDamage:0.0}, " +
			$"knockback={egg.CurrentWeaponKnockback}, reach={egg.CurrentReach:0.0}, hitboxLen={egg.HitboxLength:0.00}");

		bool pass = isPunch && punchWeak && punchNoKnock && punchShort && punchHitbox
			&& nowSword && armedKnocks && armedReachUp && armedDmgUp && swordHitbox;
		GD.Print(pass
			? "PASS: basic egg punches weakly with no knockback; EquipWeapon(Sword) arms it (reach + damage + knockback up)"
			: $"FAIL: punch(is={isPunch},weak={punchWeak},noKnock={punchNoKnock},short={punchShort},hitbox={punchHitbox}) " +
			  $"armed(sword={nowSword},knocks={armedKnocks},reachUp={armedReachUp},dmgUp={armedDmgUp},hitbox={swordHitbox})");
		return pass;
	}

	// Chunk 28 (M10): mounting. The captain climbs onto a nearby Donkey — its top Speed rises to the
	// mount's MountSpeed, it registers as the mount's rider, and the mount's collision switches off
	// while carried. Dismounting restores the foot speed and ground height and frees the mount again.
	// A mount placed out of range can't be climbed. Real Captain + Donkey scenes so the _Ready wiring
	// (groups, collision lookup) runs exactly as in play.
	private async Task<bool> TestMount()
	{
		GD.Print("=== UnitTest: mount + dismount (donkey) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var cap = GD.Load<PackedScene>("res://scenes/Captain.tscn").Instantiate<Player>();
		AddChild(cap);
		cap.GlobalPosition = new Vector3(0f, 1f, 0f);

		var donkey = GD.Load<PackedScene>("res://scenes/Donkey.tscn").Instantiate<Mount>();
		AddChild(donkey);
		donkey.GlobalPosition = new Vector3(1.5f, 0f, 0f);   // within MountRange of the captain
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		float footSpeed = cap.Speed;
		bool mounted = cap.TryMount();
		float rideSpeed = cap.Speed;
		bool faster = rideSpeed > footSpeed;
		bool ridden = donkey.IsRidden && donkey.Rider == cap && cap.IsMounted && cap.CurrentMount == donkey;
		GD.Print($"mount: success={mounted}, speed {footSpeed:0.0} -> {rideSpeed:0.0} (faster={faster}), ridden={ridden}");

		// Let a couple of physics frames run so the mount slides under the rider as it would in play.
		for (int i = 0; i < 4; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		bool carriedUnder = Mathf.IsEqualApprox(donkey.GlobalPosition.X, cap.GlobalPosition.X, 0.05f)
			&& Mathf.IsEqualApprox(donkey.GlobalPosition.Z, cap.GlobalPosition.Z, 0.05f)
			&& cap.GlobalPosition.Y > donkey.GlobalPosition.Y;   // rider sits above the steed
		GD.Print($"carried: donkey under rider={carriedUnder} (cap Y={cap.GlobalPosition.Y:0.0}, donkey Y={donkey.GlobalPosition.Y:0.0})");

		cap.Dismount();
		bool restored = Mathf.IsEqualApprox(cap.Speed, footSpeed, 0.01f) && !cap.IsMounted && !donkey.IsRidden;
		bool onGround = Mathf.IsEqualApprox(cap.GlobalPosition.Y, 1f, 0.05f);   // back to foot height
		GD.Print($"dismount: speed restored to {cap.Speed:0.0} ({restored}), back on ground Y={cap.GlobalPosition.Y:0.0} ({onGround})");

		// Out of range: shove the donkey far away — the captain can't climb on.
		donkey.GlobalPosition = new Vector3(40f, 0f, 0f);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		bool cantReach = !cap.TryMount() && !cap.IsMounted;
		GD.Print($"out of range: mount refused={cantReach}");

		bool pass = mounted && faster && ridden && carriedUnder && restored && onGround && cantReach;
		GD.Print(pass
			? "PASS: captain mounts a nearby donkey (faster, carried), dismounts (speed/height restored), can't mount one out of range"
			: $"FAIL: mounted={mounted}, faster={faster}, ridden={ridden}, carried={carriedUnder}, restored={restored}, onGround={onGround}, cantReach={cantReach}");
		return pass;
	}

	// Chunk 29 (M10): the chocobo mount. It reuses the same Mount plumbing as the donkey but is the
	// SPEEDIER steed: its MountSpeed out-paces the donkey's, so riding a chocobo lends the captain a
	// higher top speed than riding a donkey would. We compare the two scenes' MountSpeed directly and
	// confirm an actual mount of each yields the faster ride for the chocobo. Real scenes so the
	// _Ready wiring (groups, collision lookup) runs exactly as in play.
	private async Task<bool> TestChocobo()
	{
		GD.Print("=== UnitTest: chocobo mount (faster than donkey) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Compare the two mounts' top speeds straight off their scenes.
		var donkey = GD.Load<PackedScene>("res://scenes/Donkey.tscn").Instantiate<Mount>();
		var chocobo = GD.Load<PackedScene>("res://scenes/Chocobo.tscn").Instantiate<Mount>();
		AddChild(donkey);
		AddChild(chocobo);
		donkey.GlobalPosition = new Vector3(20f, 0f, 0f);    // parked out of the way
		chocobo.GlobalPosition = new Vector3(1.5f, 0f, 0f);  // within MountRange of the captain below
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		bool fasterScene = chocobo.MountSpeed > donkey.MountSpeed;
		GD.Print($"scene speeds: donkey={donkey.MountSpeed:0.0}, chocobo={chocobo.MountSpeed:0.0} (chocobo faster={fasterScene})");

		// Mount the chocobo: the captain's ride speed should equal the chocobo's MountSpeed, which
		// is itself faster than what the donkey would have lent.
		var cap = GD.Load<PackedScene>("res://scenes/Captain.tscn").Instantiate<Player>();
		AddChild(cap);
		cap.GlobalPosition = new Vector3(0f, 1f, 0f);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		float footSpeed = cap.Speed;
		bool mounted = cap.TryMount();
		bool rodeChocobo = cap.CurrentMount == chocobo;
		float chocoRide = cap.Speed;
		bool rideMatchesChocobo = Mathf.IsEqualApprox(chocoRide, chocobo.MountSpeed, 0.01f);
		bool rideBeatsDonkey = chocoRide > donkey.MountSpeed && chocoRide > footSpeed;
		GD.Print($"mounted chocobo={mounted && rodeChocobo}, ride speed={chocoRide:0.0} " +
			$"(matches chocobo={rideMatchesChocobo}, beats donkey ride={rideBeatsDonkey})");

		cap.Dismount();

		bool pass = fasterScene && mounted && rodeChocobo && rideMatchesChocobo && rideBeatsDonkey;
		GD.Print(pass
			? "PASS: the chocobo is the faster steed — riding it tops the donkey's ride speed"
			: $"FAIL: fasterScene={fasterScene}, mounted={mounted && rodeChocobo}, rideMatches={rideMatchesChocobo}, beatsDonkey={rideBeatsDonkey}");
		return pass;
	}

	// Chunk 30 (M11): capture zone + period scoring. A CapturePoint zone awards a point to the
	// team that HOLDS it alone at period end; if BOTH teams have units inside (contested), nobody
	// scores. Real scene instances (Skeleton + Captain) so they carry collision shapes the Area3D
	// can detect. Short period (0.5 s) to keep the test fast.
	private async Task<bool> TestCapturePoint()
	{
		GD.Print("=== UnitTest: capture point (hold + contested) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var cp = GD.Load<PackedScene>("res://scenes/CapturePoint.tscn").Instantiate<CapturePoint>();
		cp.PeriodSeconds = 0.5f;
		AddChild(cp);
		cp.GlobalPosition = Vector3.Zero;

		// A lone enemy inside the zone — should hold it and score at period end.
		var enemy = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		AddChild(enemy);
		enemy.GlobalPosition = new Vector3(1f, 0f, 0f);

		// ~40 frames ≈ 0.67 s: a few frames for the physics server to register the overlap,
		// then the 0.5 s period fires and awards a point.
		for (int i = 0; i < 40; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		bool enemyScored = cp.EnemyScore >= 1;
		bool playerZero = cp.PlayerScore == 0;
		GD.Print($"hold: enemy score={cp.EnemyScore} (scored={enemyScored}), " +
			$"player score={cp.PlayerScore} (zero={playerZero}), state={cp.State}");

		// Add a player unit too → contested. Neither side should score this period.
		var player = GD.Load<PackedScene>("res://scenes/Captain.tscn").Instantiate<Player>();
		AddChild(player);
		player.GlobalPosition = new Vector3(-1f, 0f, 0f);

		int enemyBefore = cp.EnemyScore;
		int playerBefore = cp.PlayerScore;

		for (int i = 0; i < 40; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		bool noNewEnemy = cp.EnemyScore == enemyBefore;
		bool noNewPlayer = cp.PlayerScore == playerBefore;
		bool contested = cp.State == CapturePoint.ZoneState.Contested;
		GD.Print($"contested: enemy score={cp.EnemyScore} (noNew={noNewEnemy}), " +
			$"player score={cp.PlayerScore} (noNew={noNewPlayer}), state={cp.State} (contested={contested})");

		bool pass = enemyScored && playerZero && noNewEnemy && noNewPlayer && contested;
		GD.Print(pass
			? "PASS: holder at period end scores; contested scores nobody"
			: $"FAIL: enemyScored={enemyScored}, playerZero={playerZero}, noNewEnemy={noNewEnemy}, noNewPlayer={noNewPlayer}, contested={contested}");
		return pass;
	}

	// Chunk 32 (M12): the card deck's draw / hand / discard piles. Cards cycle DrawPile -> Hand ->
	// DiscardPile and the discard reshuffles back in when the draw pile empties — and through all of
	// it the TOTAL number of cards across the three piles is conserved (cards only move, never
	// vanish). Pure model, so this is a synchronous check (no tree / physics). Seeded for repeatability.
	private bool TestCardDeck()
	{
		GD.Print("=== UnitTest: card deck piles + reshuffle (Chunk 32) ===");

		var deck = new Deck(seed: 1234);
		var starter = CardLibrary.StarterDeck();
		int total = starter.Count;
		deck.LoadStarter(starter);

		// Fresh load: every card sits in the draw pile, hand + discard empty.
		bool loaded = deck.DrawPile.Count == total && deck.Hand.Count == 0
			&& deck.DiscardPile.Count == 0 && deck.TotalCount == total;

		// Draw a hand of 5 off the top.
		int drawn = deck.Draw(5);
		bool drewHand = drawn == 5 && deck.Hand.Count == 5
			&& deck.DrawPile.Count == total - 5 && deck.TotalCount == total;

		// Discard the whole hand.
		deck.DiscardHand();
		bool discarded = deck.Hand.Count == 0 && deck.DiscardPile.Count == 5 && deck.TotalCount == total;

		// Draw the ENTIRE deck out: drains the draw pile, auto-reshuffles the discard back in, and
		// ends with every card in hand — total conserved throughout, nothing duplicated or lost.
		int more = deck.Draw(total);
		bool drewAll = deck.Hand.Count == total && deck.DrawPile.Count == 0
			&& deck.DiscardPile.Count == 0 && deck.TotalCount == total;

		// Asking for more than exists returns only what's left (never invents cards).
		deck.DiscardHand();
		int got = deck.Draw(total + 10);
		bool capped = got == total && deck.Hand.Count == total && deck.TotalCount == total;

		GD.Print($"loaded={loaded}, drewHand={drewHand}(drew {drawn}), discarded={discarded}, " +
			$"drewAll={drewAll}(drew {more}), capped={capped}(asked {total + 10}, got {got}); total stays {total}");

		bool pass = loaded && drewHand && discarded && drewAll && capped;
		GD.Print(pass
			? "PASS: piles cycle draw->hand->discard and reshuffle conserves the deck"
			: "FAIL: deck pile bookkeeping wrong");
		return pass;
	}

	// Chunk 43 (M12.5): endzone-mode tuning. The starter deck is reweighted to be UNIT-HEAVY (deploy a
	// force) and the card battle's default round is shortened to 5 s. Pure model — synchronous: count
	// the starter deck's kinds and read CardBattle's script default RoundSeconds.
	private bool TestDeckTuning()
	{
		GD.Print("=== UnitTest: endzone deck + round tuning (Chunk 43) ===");

		var starter = CardLibrary.StarterDeck();
		int units = 0, actions = 0;
		foreach (Card c in starter)
		{
			if (c.Kind == Card.CardKind.Unit) units++;
			else actions++;
		}
		bool unitMajority = units > actions;

		// CardBattle's script default round length (the .tscn also sets it explicitly to match).
		var battle = new CardBattle();
		float defaultRound = battle.RoundSeconds;
		bool fiveSecond = Mathf.IsEqualApprox(defaultRound, 5f);
		battle.Free();

		GD.Print($"starter: {units} Unit / {actions} Action (unit-majority={unitMajority}); " +
			$"default RoundSeconds={defaultRound:0} (5s={fiveSecond})");

		bool pass = unitMajority && fiveSecond;
		GD.Print(pass
			? "PASS: starter deck is unit-heavy and the default round is 5 s"
			: "FAIL: deck/round tuning wrong");
		return pass;
	}

	// Fake battlefield: records every Unit-card spawn so the test can assert where/what landed.
	private class FakeField : ICardField
	{
		public readonly System.Collections.Generic.List<(Card card, Vector3 at)> Spawns = new();
		public ICardUnit SpawnUnit(Card card, Vector3 location)
		{
			var u = new FakeUnit { IsFriendly = true };
			Spawns.Add((card, location));
			return u;
		}
	}

	// Fake friendly/enemy unit: records the Action cards it was made to perform.
	private class FakeUnit : ICardUnit
	{
		public bool IsFriendly { get; set; }
		public readonly System.Collections.Generic.List<Card> Performed = new();
		public void PerformAction(Card action) => Performed.Add(action);
	}

	// Chunk 33 (M12): card PLAY targeting routes through CardPlay — a UNIT card spawns at a LOCATION,
	// an ACTION card makes a FRIENDLY unit act. Verifies each kind reaches the right target and that
	// the guard rails hold (no field, no unit, or an enemy target all reject without side effects).
	// Pure model (fakes stand in for the real battlefield/units), so this is a synchronous check.
	private bool TestCardPlay()
	{
		GD.Print("=== UnitTest: card play targeting (Chunk 33) ===");

		var field = new FakeField();
		var friendly = new FakeUnit { IsFriendly = true };
		var enemy = new FakeUnit { IsFriendly = false };

		var unitCard = new Card("Recruit", Card.CardKind.Unit, 1, "", spawnPath: "res://scenes/Ally.tscn");
		var actionCard = new Card("Charge", Card.CardKind.Action, 1, "", action: Card.ActionKind.Charge);

		// Target kind follows from kind: Unit → location, Action → friendly unit.
		bool targets = unitCard.Target == Card.TargetKind.Location
			&& actionCard.Target == Card.TargetKind.FriendlyUnit;

		// Unit card spawns at the clicked location.
		var loc = new Vector3(3, 0, -5);
		bool unitPlayed = CardPlay.Play(unitCard, field, loc, null);
		bool spawned = unitPlayed && field.Spawns.Count == 1
			&& field.Spawns[0].card == unitCard && field.Spawns[0].at == loc;

		// Action card makes the friendly unit perform exactly that card.
		bool actionPlayed = CardPlay.Play(actionCard, field, Vector3.Zero, friendly);
		bool acted = actionPlayed && friendly.Performed.Count == 1 && friendly.Performed[0] == actionCard;

		// Guard rails — each rejects (false) and changes nothing:
		//  • Action card on an ENEMY unit,
		//  • Action card with NO target,
		//  • Unit card with NO field.
		bool enemyRejected = !CardPlay.Play(actionCard, field, Vector3.Zero, enemy) && enemy.Performed.Count == 0;
		bool noTargetRejected = !CardPlay.Play(actionCard, field, Vector3.Zero, null);
		bool noFieldRejected = !CardPlay.Play(unitCard, null, loc, null) && field.Spawns.Count == 1;

		GD.Print($"targets={targets}, spawned={spawned}(at {(field.Spawns.Count > 0 ? field.Spawns[0].at : Vector3.Zero)}), " +
			$"acted={acted}, enemyRejected={enemyRejected}, noTargetRejected={noTargetRejected}, noFieldRejected={noFieldRejected}");

		bool pass = targets && spawned && acted && enemyRejected && noTargetRejected && noFieldRejected;
		GD.Print(pass
			? "PASS: unit cards spawn at a location, action cards act on a friendly unit, guards hold"
			: "FAIL: card play targeting wrong");
		return pass;
	}

	// Chunk 34 (M12): the round-loop state machine. The battle STARTS PAUSED; End Turn begins a round
	// (PAUSE -> PLAY, full clock); the clock counts down in PLAY and at TIMEOUT the round counter
	// advances and it flips back to PAUSE (the view redeals there); cards stay playable in both
	// phases. Also covers the Chunk-35 dev hooks: EndPlayPhase / Resume freeze & continue the SAME
	// round (no counter bump), and RetuneRoundSeconds caps the live clock. Pure model — synchronous.
	private bool TestRoundLoop()
	{
		GD.Print("=== UnitTest: round loop state machine (Chunk 34) ===");

		var loop = new RoundLoop(roundSeconds: 15f);

		int playEdges = 0, pauseEdges = 0;
		loop.PhaseChanged += p => { if (p == RoundLoop.Phase.Play) playEdges++; else pauseEdges++; };

		// Starts PAUSED on round 1 with a full clock (waiting for the opening End Turn).
		bool startsPaused = loop.Current == RoundLoop.Phase.Pause
			&& Mathf.IsEqualApprox(loop.TimeLeft, 15f) && loop.CardsPlayable && loop.RoundNumber == 1;

		// End Turn BEGINS round 1: PAUSE -> PLAY, full clock, counter stays 1 (it bumps at timeout).
		loop.EndTurn();
		bool began = loop.Current == RoundLoop.Phase.Play && Mathf.IsEqualApprox(loop.TimeLeft, 15f)
			&& loop.RoundNumber == 1 && playEdges == 1;

		// Tick a few seconds — still PLAY, clock falling, cards playable.
		loop.Tick(5f);
		bool ticking = loop.Current == RoundLoop.Phase.Play
			&& Mathf.IsEqualApprox(loop.TimeLeft, 10f) && loop.CardsPlayable;

		// Run the clock out — the round ENDS: counter -> 2, PLAY -> PAUSE once, clock clamps at 0.
		loop.Tick(20f);
		bool expired = loop.Current == RoundLoop.Phase.Pause && Mathf.IsZeroApprox(loop.TimeLeft)
			&& loop.RoundNumber == 2 && pauseEdges == 1 && loop.CardsPlayable;

		// Ticking while paused does nothing (battlefield frozen, no time passes).
		loop.Tick(5f);
		bool frozen = loop.Current == RoundLoop.Phase.Pause && Mathf.IsZeroApprox(loop.TimeLeft)
			&& loop.RoundNumber == 2;

		// End Turn begins round 2: PAUSE -> PLAY, full clock, counter stays 2.
		loop.EndTurn();
		bool resumed = loop.Current == RoundLoop.Phase.Play && Mathf.IsEqualApprox(loop.TimeLeft, 15f)
			&& playEdges == 2 && loop.RoundNumber == 2 && loop.CardsPlayable;

		// End Turn during PLAY is a no-op (it's the PAUSE -> PLAY control only).
		loop.EndTurn();
		bool endTurnNoop = loop.Current == RoundLoop.Phase.Play && playEdges == 2 && loop.RoundNumber == 2;

		// Dev EndPlayPhase freezes mid-round WITHOUT ending it (no counter bump); 2nd call no-ops.
		loop.EndPlayPhase();
		bool earlyPause = loop.Current == RoundLoop.Phase.Pause && pauseEdges == 2 && loop.RoundNumber == 2;
		loop.EndPlayPhase();
		bool earlyNoop = loop.Current == RoundLoop.Phase.Pause && pauseEdges == 2;

		// Chunk 35 dev controls. Resume continues the SAME round: PAUSE -> PLAY, no round bump, clock kept.
		loop.Resume();
		bool devResumed = loop.Current == RoundLoop.Phase.Play && loop.RoundNumber == 2
			&& playEdges == 3 && Mathf.IsEqualApprox(loop.TimeLeft, 15f);

		// Live retune: shrinking the round length caps the current clock immediately...
		loop.Tick(3f);                      // TimeLeft 15 -> 12
		loop.RetuneRoundSeconds(8f);
		bool retuneDown = Mathf.IsEqualApprox(loop.RoundSeconds, 8f) && Mathf.IsEqualApprox(loop.TimeLeft, 8f);
		// ...while growing it doesn't inflate the in-progress clock (takes full effect next round).
		loop.RetuneRoundSeconds(20f);
		bool retuneUp = Mathf.IsEqualApprox(loop.RoundSeconds, 20f) && Mathf.IsEqualApprox(loop.TimeLeft, 8f);

		GD.Print($"startsPaused={startsPaused}, began={began}, tick={ticking}, expired={expired}(round={loop.RoundNumber}), " +
			$"frozen={frozen}, resumed={resumed}(playEdges={playEdges}), endTurnNoop={endTurnNoop}, " +
			$"earlyPause={earlyPause}, earlyNoop={earlyNoop}, devResumed={devResumed}, retuneDown={retuneDown}, retuneUp={retuneUp}");

		bool pass = startsPaused && began && ticking && expired && frozen && resumed && endTurnNoop
			&& earlyPause && earlyNoop && devResumed && retuneDown && retuneUp;
		GD.Print(pass
			? "PASS: starts paused; End Turn begins a round; timeout advances the round + repauses; dev pause/resume/retune hold"
			: "FAIL: round loop state machine wrong");
		return pass;
	}

	// Deal one card action and report how much HP the foe lost. Sets up a clean scene with a single
	// caster (given Str/Int) and a lone foe in reach, runs PerformAction, then measures the damage.
	// Isolated per call so the caster's FindNearestOpponent can only pick this foe.
	private async Task<float> MeasureCardActionDamage(Card card, int strength, int intelligence)
	{
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var caster = new Unit { Team = Unit.TeamId.Player, MaxHealth = 100f, Strength = strength, Intelligence = intelligence };
		AddChild(caster);
		caster.GlobalPosition = Vector3.Zero;

		var foe = new Unit { Team = Unit.TeamId.Enemy, MaxHealth = 1000f };   // big pool so it survives the hit
		AddChild(foe);
		foe.GlobalPosition = new Vector3(2f, 0f, 0f);                          // well inside CardActionRange
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);          // register both in UnitRegistry

		caster.PerformAction(card);
		return foe.MaxHealth - foe.Health;
	}

	// Chunk 36 (M12): HP / Str / Int stats. STRENGTH scales weapon attack power (the captain's swing)
	// AND strength-based card actions (Rally); INTELLIGENCE scales magic actions (Firebolt). The two
	// stats route to different effects, so a Strength buff must NOT inflate magic and an Intelligence
	// buff must NOT inflate a weapon strike. Stat 0 resolves to the base numbers (multiplier 1.0).
	private async Task<bool> TestUnitStats()
	{
		GD.Print("=== UnitTest: unit stats HP/Str/Int (Chunk 36) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// (a) Strength scales the captain's actual weapon attack power.
		var weakCap = GD.Load<PackedScene>("res://scenes/Captain.tscn").Instantiate<Player>();
		weakCap.Strength = 0;
		AddChild(weakCap);
		var strongCap = GD.Load<PackedScene>("res://scenes/Captain.tscn").Instantiate<Player>();
		strongCap.Strength = 5;                                  // +50% at StrengthScale 0.10
		AddChild(strongCap);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		bool baseIsUnscaled = Mathf.IsEqualApprox(weakCap.CurrentAttackDamage, weakCap.CurrentDamage, 0.01f);
		bool weaponScalesWithStr = strongCap.CurrentAttackDamage > weakCap.CurrentAttackDamage + 0.01f;
		GD.Print($"weapon: Str0 hit={weakCap.CurrentAttackDamage:0.0} (base {weakCap.CurrentDamage:0.0}, unscaled={baseIsUnscaled}), " +
			$"Str5 hit={strongCap.CurrentAttackDamage:0.0} (scales={weaponScalesWithStr})");

		var rally = new Card("Rally", Card.CardKind.Action, 1, "", action: Card.ActionKind.Rally);
		var firebolt = new Card("Firebolt", Card.CardKind.Action, 1, "", action: Card.ActionKind.Firebolt);

		// (b) Strength scales a strength action (Rally), and does NOT touch a magic action.
		float rallyWeak = await MeasureCardActionDamage(rally, strength: 0, intelligence: 0);
		float rallyStrong = await MeasureCardActionDamage(rally, strength: 6, intelligence: 0);
		bool strScalesAction = rallyStrong > rallyWeak + 0.01f;

		// (c) Intelligence scales a magic action (Firebolt), and does NOT touch a weapon strike.
		float boltDumb = await MeasureCardActionDamage(firebolt, strength: 0, intelligence: 0);
		float boltSmart = await MeasureCardActionDamage(firebolt, strength: 0, intelligence: 6);
		bool intScalesMagic = boltSmart > boltDumb + 0.01f;

		// Cross-checks: Strength must not feed magic; Intelligence must not feed the weapon strike.
		float boltStrong = await MeasureCardActionDamage(firebolt, strength: 6, intelligence: 0);
		bool strNotMagic = Mathf.IsEqualApprox(boltStrong, boltDumb, 0.01f);
		float rallySmart = await MeasureCardActionDamage(rally, strength: 0, intelligence: 6);
		bool intNotWeapon = Mathf.IsEqualApprox(rallySmart, rallyWeak, 0.01f);

		GD.Print($"action: rally Str0={rallyWeak:0.0} Str6={rallyStrong:0.0} (strScales={strScalesAction}); " +
			$"firebolt Int0={boltDumb:0.0} Int6={boltSmart:0.0} (intScales={intScalesMagic})");
		GD.Print($"cross: firebolt Str6={boltStrong:0.0} (str!=magic {strNotMagic}), rally Int6={rallySmart:0.0} (int!=weapon {intNotWeapon})");

		bool pass = baseIsUnscaled && weaponScalesWithStr && strScalesAction && intScalesMagic && strNotMagic && intNotWeapon;
		GD.Print(pass
			? "PASS: Str scales weapon power + strength actions, Int scales magic, and the two stats stay in their lanes"
			: $"FAIL: baseUnscaled={baseIsUnscaled}, weaponStr={weaponScalesWithStr}, rallyStr={strScalesAction}, " +
			  $"boltInt={intScalesMagic}, strNotMagic={strNotMagic}, intNotWeapon={intNotWeapon}");
		return pass;
	}

	// Chunk 37 (M12): energy from KotH points. The card economy is fed by the ground you hold —
	// EnergyPool grants a base allowance plus a bonus per capture point held at the pause, so HOLDING
	// MORE POINTS GRANTS MORE ENERGY. Energy then GATES plays: a card you can't afford can't be
	// spent (Spend rejects and the pool is unchanged). Pure model — synchronous, no tree.
	private bool TestCardEnergy()
	{
		GD.Print("=== UnitTest: card energy from KotH points + gating (Chunk 37) ===");

		var pool = new EnergyPool(baseEnergy: 3, perPoint: 2);

		// Fresh pool sits at the base allowance (opening hand playable with no ground held).
		bool baseOk = pool.Energy == 3 && pool.Granted == 3 && pool.EnergyFor(0) == 3;

		// More held points grant strictly more energy: 3 -> 5 -> 7 for 0/1/2 points.
		bool moreGrantsMore = pool.EnergyFor(1) > pool.EnergyFor(0)
			&& pool.EnergyFor(2) > pool.EnergyFor(1)
			&& pool.EnergyFor(1) == 5 && pool.EnergyFor(2) == 7;

		// Refill at the pause from points held: holding 2 points funds 7 energy this round.
		pool.Refill(2);
		bool refilled = pool.Energy == 7 && pool.Granted == 7;

		// Gating: build a cheap (cost 1) and a pricey (cost 6) card.
		var cheap = new Card("Cheap", Card.CardKind.Action, 1, action: Card.ActionKind.Brace);
		var pricey = new Card("Pricey", Card.CardKind.Unit, 6, spawnPath: "res://scenes/Ally.tscn");

		// With 7 energy both are affordable; spending the cheap one deducts its cost.
		bool affordsBoth = pool.CanAfford(cheap) && pool.CanAfford(pricey);
		bool spentCheap = pool.Spend(cheap) && pool.Energy == 6;

		// Now drop the pool to 1 (hold no ground): the pricey card is gated out, the cheap one still plays.
		pool.Refill(0);                                    // -> base 3
		pool.Spend(cheap); pool.Spend(cheap);              // 3 -> 2 -> 1
		bool atOne = pool.Energy == 1;
		bool priceyGated = !pool.CanAfford(pricey) && !pool.Spend(pricey) && pool.Energy == 1; // refused, unchanged
		bool cheapStillPlays = pool.CanAfford(cheap) && pool.Spend(cheap) && pool.Energy == 0;

		GD.Print($"baseOk={baseOk}, moreGrantsMore={moreGrantsMore}(0->{pool.EnergyFor(0)} 1->{pool.EnergyFor(1)} 2->{pool.EnergyFor(2)}), " +
			$"refilled={refilled}, affordsBoth={affordsBoth}, spentCheap={spentCheap}, " +
			$"atOne={atOne}, priceyGated={priceyGated}, cheapStillPlays={cheapStillPlays}");

		bool pass = baseOk && moreGrantsMore && refilled && affordsBoth && spentCheap
			&& atOne && priceyGated && cheapStillPlays;
		GD.Print(pass
			? "PASS: holding more points grants more energy, and energy gates plays (can't afford -> can't play)"
			: "FAIL: card energy economy / gating wrong");
		return pass;
	}

	// Chunk 38 (M12): the run structure. A run is a fixed-shape sequence of rooms (combat / event /
	// boss) you traverse; CLEARING the current room advances the map AND hands back a reward to offer,
	// and TAKING a reward grows the run's carried deck (Collection). Pure model — synchronous,
	// deterministic under a seed.
	private bool TestRunMap()
	{
		GD.Print("=== UnitTest: run structure (rooms + rewards) (Chunk 38) ===");

		var run = new RunMap(seed: 99);

		// Fresh run: positioned at the first room, nothing cleared, deck seeded from the starter deck,
		// and the room mix includes both an event and a boss (the shape isn't all combat).
		int startCollection = run.Collection.Count;
		bool startsAtFirst = run.CurrentIndex == 0 && !run.IsComplete && run.Current != null
			&& run.RoomNumber == 1 && run.RoomCount == run.Rooms.Count;
		bool seededDeck = startCollection == CardLibrary.StarterDeck().Count;
		bool hasEvent = run.Rooms.Exists(r => r.Type == RunMap.RoomType.Event);
		bool hasBoss = run.Rooms.Exists(r => r.Type == RunMap.RoomType.Boss);
		GD.Print($"start: room {run.RoomNumber}/{run.RoomCount} '{run.Current.Title}' ({run.Current.Type}); " +
			$"deck={startCollection} (seeded={seededDeck}), hasEvent={hasEvent}, hasBoss={hasBoss}");

		// Clear the first room: it's marked cleared, the map advances, and a reward with choices is offered.
		RunMap.Room first = run.Current;
		RunMap.RoomReward reward = run.CompleteCurrentRoom();
		bool advanced = first.Cleared && run.CurrentIndex == 1 && reward != null
			&& reward.Choices.Count > 0 && !reward.Resolved;
		GD.Print($"cleared room 1: cleared={first.Cleared}, index->{run.CurrentIndex}, " +
			$"reward '{reward?.Prompt}' with {reward?.Choices.Count} choices");

		// Take a card: it's added to the run deck and the reward resolves (and can't be taken twice).
		int beforeTake = run.Collection.Count;
		run.TakeReward(reward, reward.Choices[0]);
		bool tookCard = run.Collection.Count == beforeTake + 1 && reward.Resolved && reward.Chosen != null;
		run.TakeReward(reward, reward.Choices[0]);   // idempotent: a resolved reward does nothing
		bool takeIdempotent = run.Collection.Count == beforeTake + 1;
		GD.Print($"took a card: deck {beforeTake}->{run.Collection.Count} (tookCard={tookCard}), idempotent={takeIdempotent}");

		// Skipping a reward resolves it without growing the deck.
		RunMap.RoomReward r2 = run.CompleteCurrentRoom();
		int beforeSkip = run.Collection.Count;
		run.TakeReward(r2, null);
		bool skipped = r2.Resolved && r2.Chosen == null && run.Collection.Count == beforeSkip;
		GD.Print($"skipped reward: resolved={r2.Resolved}, deck unchanged={skipped} ({run.Collection.Count})");

		// Clear out the rest of the run: every CompleteCurrentRoom offers a reward until the map ends,
		// then it returns null and Current is null (run complete).
		int rewardsOffered = 2;   // the two above
		while (!run.IsComplete)
		{
			RunMap.RoomReward rr = run.CompleteCurrentRoom();
			if (rr != null) rewardsOffered++;
		}
		bool finished = run.IsComplete && run.Current == null
			&& run.CurrentIndex == run.RoomCount && run.Rooms.TrueForAll(r => r.Cleared);
		bool noRewardWhenDone = run.CompleteCurrentRoom() == null;
		bool everyRoomOfferedReward = rewardsOffered == run.RoomCount;
		GD.Print($"finished run: complete={run.IsComplete}, current null={run.Current == null}, " +
			$"all cleared={run.Rooms.TrueForAll(r => r.Cleared)}, rewards offered={rewardsOffered}/{run.RoomCount}, " +
			$"no-reward-when-done={noRewardWhenDone}");

		// Same seed => same room shape (deterministic runs for tests).
		var run2 = new RunMap(seed: 99);
		bool deterministic = run2.RoomCount == run.RoomCount;
		for (int i = 0; i < run2.RoomCount && deterministic; i++)
			deterministic = run2.Rooms[i].Type == run.Rooms[i].Type && run2.Rooms[i].Title == run.Rooms[i].Title;

		bool pass = startsAtFirst && seededDeck && hasEvent && hasBoss && advanced && tookCard
			&& takeIdempotent && skipped && finished && noRewardWhenDone && everyRoomOfferedReward && deterministic;
		GD.Print(pass
			? "PASS: rooms traverse with combat/event/boss; clearing advances the map + offers a reward; rewards grow the run deck"
			: $"FAIL: startsAtFirst={startsAtFirst}, seededDeck={seededDeck}, hasEvent={hasEvent}, hasBoss={hasBoss}, " +
			  $"advanced={advanced}, tookCard={tookCard}, idempotent={takeIdempotent}, skipped={skipped}, " +
			  $"finished={finished}, noRewardDone={noRewardWhenDone}, everyRoomReward={everyRoomOfferedReward}, deterministic={deterministic}");
		return pass;
	}

	// Chunk 39 (M12): relics & potions. A RELIC is a permanent run-long passive whose modifier APPLIES
	// (here: a BonusEnergy relic raises the EnergyPool's per-round grant). A POTION is a one-shot that
	// CONSUMES and triggers its effect (energy/draw), refusing a second use. Plus the run grants them:
	// event rooms hand a potion, the boss hands a relic, both landing in the run Inventory on take.
	// Pure model — synchronous, deterministic under a seed.
	private bool TestRelicsPotions()
	{
		GD.Print("=== UnitTest: relics & potions (Chunk 39) ===");

		// (a) A relic's modifier APPLIES: a +1 BonusEnergy relic raises the pool's per-round grant.
		var inv = new RunInventory();
		var pool = new EnergyPool(baseEnergy: 3, perPoint: 1);
		int beforeRelic = pool.EnergyFor(0);                       // 3 with no relics
		inv.AddRelic(new Relic("Egg of Plenty", Relic.RelicKind.BonusEnergy, 1));
		pool.BonusEnergy = inv.BonusEnergy;                        // the battle folds the aggregate in
		int afterRelic = pool.EnergyFor(0);                       // 4 now
		bool relicApplies = inv.BonusEnergy == 1 && afterRelic == beforeRelic + 1;

		// Relic aggregates sum by kind and stay in their lanes (hand-size + spawn-strength).
		inv.AddRelic(new Relic("Captain's Banner", Relic.RelicKind.BonusHandSize, 1));
		inv.AddRelic(new Relic("Warlord's Crest", Relic.RelicKind.SpawnStrength, 2));
		inv.AddRelic(new Relic("Egg of Plenty II", Relic.RelicKind.BonusEnergy, 1));   // stacks
		bool aggregates = inv.BonusEnergy == 2 && inv.BonusHandSize == 1 && inv.SpawnStrengthBonus == 2;
		GD.Print($"relic: grant {beforeRelic}->{afterRelic} (applies={relicApplies}); " +
			$"aggregates energy={inv.BonusEnergy} hand={inv.BonusHandSize} str={inv.SpawnStrengthBonus} (ok={aggregates})");

		// (b) A potion CONSUMES + triggers: an energy potion adds to the pool exactly once.
		var energyPotion = new Potion("Energy Draught", Potion.PotionKind.Energy, 2);
		pool.Refill(0);                                            // grant = base(3) + synced relic bonus(1) = 4
		int beforePop = pool.Energy;
		bool firstPop = energyPotion.Apply(pool, null) && energyPotion.Consumed && pool.Energy == beforePop + 2;
		bool oneShot = !energyPotion.Apply(pool, null) && pool.Energy == beforePop + 2;   // refused, unchanged
		GD.Print($"energy potion: {beforePop}->{pool.Energy} (popped={firstPop}), second use refused={oneShot}");

		// A draw potion pulls extra cards into the hand, once.
		var deck = new Deck(seed: 1);
		deck.LoadStarter(CardLibrary.StarterDeck());
		deck.Draw(2);
		int handBefore = deck.Hand.Count;
		var drawPotion = new Potion("Scroll of Insight", Potion.PotionKind.Draw, 2);
		bool drawPops = drawPotion.Apply(null, deck) && deck.Hand.Count == handBefore + 2;
		bool drawOneShot = !drawPotion.Apply(null, deck) && deck.Hand.Count == handBefore + 2;
		GD.Print($"draw potion: hand {handBefore}->{deck.Hand.Count} (popped={drawPops}), second use refused={drawOneShot}");

		// (c) The run GRANTS them: clear rooms until both an event (potion) and the boss (relic) land
		// in the inventory. Take each reward (card null = just the bonus) and watch the inventory grow.
		var run = new RunMap(seed: 7);
		bool bossRelicGranted = false, eventPotionGranted = false;
		while (!run.IsComplete)
		{
			RunMap.Room room = run.Current;
			RunMap.RoomReward reward = run.CompleteCurrentRoom();
			if (room.Type == RunMap.RoomType.Boss)
				bossRelicGranted = reward.BonusRelic != null;
			if (room.Type == RunMap.RoomType.Event)
				eventPotionGranted = reward.BonusPotion != null;
			run.TakeReward(reward, null);             // skip the card; still collect the bonus item
		}
		bool collected = run.Inventory.Relics.Count >= 1 && run.Inventory.Potions.Count >= 1;
		GD.Print($"run grants: boss relic offered={bossRelicGranted}, event potion offered={eventPotionGranted}; " +
			$"collected relics={run.Inventory.Relics.Count}, potions={run.Inventory.Potions.Count} (ok={collected})");

		bool pass = relicApplies && aggregates && firstPop && oneShot && drawPops && drawOneShot
			&& bossRelicGranted && eventPotionGranted && collected;
		GD.Print(pass
			? "PASS: relic modifiers apply + aggregate; potions consume one-shot effects; the run grants both"
			: $"FAIL: relicApplies={relicApplies}, aggregates={aggregates}, energyPop={firstPop}, energyOneShot={oneShot}, " +
			  $"drawPop={drawPops}, drawOneShot={drawOneShot}, bossRelic={bossRelicGranted}, eventPotion={eventPotionGranted}, collected={collected}");
		return pass;
	}

	// Chunk 41 (M12.5): endzone-gated unit placement. Unit cards may only drop inside the player's
	// endzone rectangle. The Endzone struct is the pure bounds test CardBattle validates each placement
	// click against. Mirrors CardBattle's default field bounds: half-width 14, z ∈ [14, 22].
	private bool TestEndzone()
	{
		GD.Print("=== UnitTest: endzone-gated placement (Chunk 41) ===");

		var zone = new Endzone(halfWidth: 14f, farZ: 14f, nearZ: 22f);

		// Inside: centre and a point near (but within) each edge are accepted.
		bool centreIn = zone.Contains(new Vector3(0f, 0f, 18f));
		bool edgesIn = zone.Contains(new Vector3(13.9f, 0f, 14.1f)) && zone.Contains(new Vector3(-13.9f, 0f, 21.9f));

		// Outside: past the far edge (enemy side), past the near edge, and beyond the side line are rejected.
		bool farOut = !zone.Contains(new Vector3(0f, 0f, 10f));      // −Z of the inner edge (mid-pitch)
		bool nearOut = !zone.Contains(new Vector3(0f, 0f, 26f));     // +Z behind the back line
		bool wideOut = !zone.Contains(new Vector3(20f, 0f, 18f));    // outside the side line

		// Y is ignored — only the ground footprint matters.
		bool ignoresY = zone.Contains(new Vector3(0f, 99f, 18f));

		// Bounds passed in either order resolve the same rectangle.
		var flipped = new Endzone(halfWidth: 14f, farZ: 22f, nearZ: 14f);
		bool orderAgnostic = flipped.Contains(new Vector3(0f, 0f, 18f)) && !flipped.Contains(new Vector3(0f, 0f, 10f));

		GD.Print($"centreIn={centreIn}, edgesIn={edgesIn}, farOut={farOut}, nearOut={nearOut}, " +
			$"wideOut={wideOut}, ignoresY={ignoresY}, orderAgnostic={orderAgnostic}");

		bool pass = centreIn && edgesIn && farOut && nearOut && wideOut && ignoresY && orderAgnostic;
		GD.Print(pass
			? "PASS: points inside the endzone are accepted, points outside are rejected"
			: "FAIL: endzone bounds test wrong");
		return pass;
	}

	// Chunk 42 (M12.5): the opt-in forward-march AI. (a) A march-mode unit with NO foe in range walks
	// toward its goal direction (−Z); (b) the same unit with a foe parked inside AggroRange — placed on
	// +Z, OPPOSITE the march goal — stops advancing and engages it (moves toward the foe and damages it),
	// proving the aggro check overrides the march. Real Enemy (Skeleton) instances so the registry +
	// chase/attack pipeline runs exactly as in play.
	private async Task<bool> TestMarch()
	{
		GD.Print("=== UnitTest: forward-march AI (Chunk 42) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// (a) No foe anywhere: the marcher advances toward its goal direction (−Z).
		var marcher = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		marcher.MarchMode = true;
		marcher.MarchGoalDirection = Vector3.Forward;   // (0,0,−1) — toward the far endzone
		marcher.AggroRange = 8f;
		AddChild(marcher);
		marcher.GlobalPosition = Vector3.Zero;
		float startZ = marcher.GlobalPosition.Z;

		for (int i = 0; i < 60; i++)   // ~1 s
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		float marchedZ = marcher.GlobalPosition.Z;
		bool advanced = marchedZ < startZ - 1f;   // moved meaningfully along −Z toward the goal
		GD.Print($"march: z {startZ:0.00} -> {marchedZ:0.00} (advanced={advanced})");

		// (b) Foe inside AggroRange, on +Z (the OPPOSITE direction to the march goal): the marcher must
		// break off and engage — moving toward the foe (+Z) and damaging it, not marching away (−Z).
		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var engager = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		engager.MarchMode = true;
		engager.MarchGoalDirection = Vector3.Forward;   // would walk −Z if it saw no foe
		engager.AggroRange = 8f;
		AddChild(engager);
		engager.GlobalPosition = Vector3.Zero;

		var foe = new Unit { Team = Unit.TeamId.Player, MaxHealth = 100f };
		AddChild(foe);
		foe.GlobalPosition = new Vector3(0f, 0f, 5f);   // +Z, well within AggroRange
		float engagerStartZ = engager.GlobalPosition.Z;

		for (int i = 0; i < 120; i++)   // ~2 s to close and strike
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		bool movedToFoe = engager.GlobalPosition.Z > engagerStartZ + 0.5f;   // went +Z toward the foe, not −Z
		bool damagedFoe = foe.Health < foe.MaxHealth;
		GD.Print($"engage: marcher z->{engager.GlobalPosition.Z:0.00} (movedToFoe={movedToFoe}), " +
			$"foe HP={foe.Health}/{foe.MaxHealth} (damaged={damagedFoe})");

		bool pass = advanced && movedToFoe && damagedFoe;
		GD.Print(pass
			? "PASS: a march-mode unit advances with no foe near, and breaks off to engage one in aggro range"
			: $"FAIL: advanced={advanced}, movedToFoe={movedToFoe}, damagedFoe={damagedFoe}");
		return pass;
	}

	// Chunk 44: the gamepad right-stick aim helper resolves to the correct facing yaw, and the
	// Any control scheme still reads the blended move path (the single-player default).
	private bool TestStickAim()
	{
		GD.Print("=== UnitTest: control scheme + stick aim (Chunk 44) ===");

		// Forward is -Z (yaw 0). Stick up is reported NEGATIVE on the Y axis -> should aim forward.
		bool up    = Mathf.IsEqualApprox(AimMath.StickToYaw(new Vector2(0f, -1f)), 0f, 0.001f);
		bool down  = Mathf.IsEqualApprox(Mathf.Abs(AimMath.StickToYaw(new Vector2(0f, 1f))), Mathf.Pi, 0.001f);
		bool right = Mathf.IsEqualApprox(AimMath.StickToYaw(new Vector2(1f, 0f)), -Mathf.Pi / 2f, 0.001f);
		bool left  = Mathf.IsEqualApprox(AimMath.StickToYaw(new Vector2(-1f, 0f)), Mathf.Pi / 2f, 0.001f);

		// A diagonal: up-right (forward + right) lands between yaw 0 and -π/2.
		float diag = AimMath.StickToYaw(new Vector2(1f, -1f));
		bool diagonal = Mathf.IsEqualApprox(diag, -Mathf.Pi / 4f, 0.001f);

		GD.Print($"yaws: up={up}, down={down}, right={right}, left={left}, diag={diagonal}");

		// Default control scheme is Any, and MoveInput() in Any reads the blended action vector
		// (zero here with no input) — the same path single-player levels use, untouched.
		var p = new Player();
		bool defaultsAny = p.Control == Player.ControlScheme.Any;
		bool anyBlended = p.MoveInput() == Input.GetVector("move_left", "move_right", "move_up", "move_down");
		// A device-scoped scheme reads only its own pad — no phantom input from an unused device.
		p.Control = Player.ControlScheme.Gamepad;
		p.DeviceId = 99;   // no such pad in a headless run
		bool gamepadIsolated = p.MoveInput() == Vector2.Zero;
		p.Free();

		GD.Print($"defaultsAny={defaultsAny}, anyBlended={anyBlended}, gamepadIsolated={gamepadIsolated}");

		bool pass = up && down && right && left && diagonal && defaultsAny && anyBlended && gamepadIsolated;
		GD.Print(pass
			? "PASS: stick aim resolves to the right yaw; Any reads the blended path, Gamepad stays device-scoped"
			: "FAIL: control-scheme/stick-aim wrong");
		return pass;
	}

	// Chunk 45 (M12.7): squad ownership. An ally with an explicit CaptainPath anchors its
	// formation slot to THAT captain, not whichever node happens to be first in the "player"
	// group — so two captains can each lead their own squad. We park two captains apart, bind
	// the ally to the SECOND one, and confirm its slot (and the spot it marches to) tracks that
	// captain. A plain Node3D in the "player" group stands in for each captain.
	private async Task<bool> TestSquadOwnership()
	{
		GD.Print("=== UnitTest: squad ownership (ally bound to a captain) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Two captains far apart. The FIRST in the group is the one the legacy lookup would grab;
		// our ally must instead follow the SECOND, which CaptainPath points at.
		var captainA = new Node3D { Name = "CaptainA" };
		AddChild(captainA);
		captainA.AddToGroup("player");
		captainA.GlobalPosition = Vector3.Zero;

		var captainB = new Node3D { Name = "CaptainB" };
		AddChild(captainB);
		captainB.AddToGroup("player");
		captainB.GlobalPosition = new Vector3(20f, 0f, 0f);

		var ally = GD.Load<PackedScene>("res://scenes/Ally.tscn").Instantiate<Ally>();
		ally.FormationOffset = new Vector3(1.5f, 0f, 1.5f);
		ally.CaptainPath = captainB.GetPath();           // bind to the SECOND captain before _Ready
		AddChild(ally);                                  // _Ready resolves the captain from the path
		ally.GlobalPosition = new Vector3(20f, 0f, 6f);  // start near captain B

		// The ally's slot must be captain B's position + offset, NOT captain A's (the group head).
		Vector3 slot = ally.SlotWorldPosition();
		Vector3 wantB = captainB.GlobalPosition + ally.FormationOffset;   // both yaw 0 -> basis is identity
		Vector3 wantA = captainA.GlobalPosition + ally.FormationOffset;
		bool slotOnB = slot.DistanceTo(wantB) < 0.01f;
		bool notOnA = slot.DistanceTo(wantA) > 1f;
		GD.Print($"slot={slot}; wantB={wantB} (onB={slotOnB}), wantA={wantA} (notOnA={notOnA})");

		// And it actually FORMS on captain B: let it march in and settle on that slot.
		for (int i = 0; i < 150; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		float restDist = ally.GlobalPosition.DistanceTo(ally.SlotWorldPosition());
		bool formedOnB = restDist < 0.3f && ally.GlobalPosition.DistanceTo(captainB.GlobalPosition) < 5f;
		GD.Print($"after follow: {restDist:0.00} m from B's slot, " +
			$"{ally.GlobalPosition.DistanceTo(captainB.GlobalPosition):0.00} m from captain B (formedOnB={formedOnB})");

		bool pass = slotOnB && notOnA && formedOnB;
		GD.Print(pass
			? "PASS: an ally with CaptainPath follows that captain's slot, not the group head"
			: $"FAIL: slotOnB={slotOnB}, notOnA={notOnA}, formedOnB={formedOnB}");
		return pass;
	}

	// Chunk 46 (M12.7): the shared two-captain camera. With a single Target the focus is that
	// target (single-player path, unchanged); set a second target and the focus becomes the
	// MIDPOINT of both, while the dynamic-zoom distance GROWS as the captains pull apart (half
	// their separation feeds the framing spread, so both stay in shot). Pure math on
	// FollowCamera.FocusPoint / HalfSeparation / DesiredDistance — no tree or input needed.
	private bool TestCoopCamera()
	{
		GD.Print("=== UnitTest: shared two-captain camera (Chunk 46) ===");

		// Parameters chosen so the framing growth stays well inside [Min, Max] (no clamp masking it).
		var cam = new FollowCamera
		{
			DynamicZoom = true,
			MinDistance = 10f, MaxDistance = 80f,
			FitScale = 1.5f, ZoomMargin = 10f,
		};
		// In the tree so the captains' GlobalPosition resolves; the test is synchronous (no physics
		// frame awaited) so the camera's _PhysicsProcess never runs before everything is freed.
		AddChild(cam);
		var capA = new Node3D();
		var capB = new Node3D();
		AddChild(capA);
		AddChild(capB);

		// Single target only: focus is exactly that target, and HalfSeparation is 0.
		cam.Target = capA;
		cam.Target2 = null;
		capA.GlobalPosition = new Vector3(4f, 0f, -3f);
		bool soloFocus = cam.FocusPoint().DistanceTo(capA.GlobalPosition) < 0.001f;
		bool soloNoSep = Mathf.IsZeroApprox(cam.HalfSeparation());
		GD.Print($"solo: focus={cam.FocusPoint()} (onTarget={soloFocus}), halfSep={cam.HalfSeparation():0.00} (zero={soloNoSep})");

		// Two targets: focus is their midpoint.
		cam.Target2 = capB;
		capA.GlobalPosition = new Vector3(-5f, 0f, 0f);
		capB.GlobalPosition = new Vector3(5f, 0f, 0f);          // 10 m apart -> midpoint at origin, halfSep 5
		Vector3 mid = cam.FocusPoint();
		bool midpointOk = mid.DistanceTo(Vector3.Zero) < 0.001f;
		bool halfSepOk = Mathf.IsEqualApprox(cam.HalfSeparation(), 5f, 0.01f);
		float nearDist = cam.DesiredDistance(cam.HalfSeparation());   // 5*1.5 + 10 = 17.5
		GD.Print($"close: focus={mid} (midpoint={midpointOk}), halfSep={cam.HalfSeparation():0.00} (ok={halfSepOk}), dist={nearDist:0.0}");

		// Pull the captains apart: the framing distance must GROW (more spread to cover).
		capA.GlobalPosition = new Vector3(-20f, 0f, 0f);
		capB.GlobalPosition = new Vector3(20f, 0f, 0f);        // 40 m apart -> halfSep 20
		Vector3 mid2 = cam.FocusPoint();
		bool stillMidpoint = mid2.DistanceTo(Vector3.Zero) < 0.001f;
		float farDist = cam.DesiredDistance(cam.HalfSeparation());   // 20*1.5 + 10 = 40
		bool grew = farDist > nearDist;
		GD.Print($"apart: halfSep={cam.HalfSeparation():0.00}, dist={farDist:0.0} (grew={grew}, stillMidpoint={stillMidpoint})");

		cam.Free();
		capA.Free();
		capB.Free();

		bool pass = soloFocus && soloNoSep && midpointOk && halfSepOk && stillMidpoint && grew;
		GD.Print(pass
			? "PASS: two targets focus on their midpoint and the camera pulls back as they separate"
			: $"FAIL: soloFocus={soloFocus}, soloNoSep={soloNoSep}, midpoint={midpointOk}, halfSep={halfSepOk}, stillMidpoint={stillMidpoint}, grew={grew}");
		return pass;
	}

	// Chunk 64: the terrain-following camera DAMPS the focus height instead of snapping to the
	// target's Y as the captain climbs/descends, and an optional lift raises the framed point. The
	// flat path stays byte-identical: a constant target Y with zero lift eases to itself every frame.
	// Pure math on FollowCamera.EaseFocusHeight — no tree or input needed.
	private bool TestTerrainCamera()
	{
		GD.Print("=== UnitTest: terrain-following camera focus height (Chunk 64) ===");
		double dt = 1.0 / 60.0;

		// Eases toward the target's Y rather than snapping: one step from 0 toward 10 lands strictly
		// between, and many steps converge on it.
		var cam = new FollowCamera { FocusHeightLerp = 4.0f, FocusHeightLift = 0f };
		float oneStep = cam.EaseFocusHeight(0f, 10f, dt);
		bool eases = oneStep > 0f && oneStep < 10f;
		float h = 0f;
		for (int i = 0; i < 600; i++) h = cam.EaseFocusHeight(h, 10f, dt);
		bool converges = Mathf.IsEqualApprox(h, 10f, 0.01f);
		GD.Print($"climb: oneStep={oneStep:0.000} (between 0 and 10={eases}), settled={h:0.000} (converges={converges})");

		// Flat invariant: a constant target Y with zero lift is a no-op every frame (byte-identical).
		float flat = cam.EaseFocusHeight(1f, 1f, dt);
		bool flatUnchanged = flat == 1f;
		GD.Print($"flat: ease(1,1)={flat:0.000} (unchanged={flatUnchanged})");

		// Lift raises the settled focus height by exactly FocusHeightLift.
		var lifted = new FollowCamera { FocusHeightLerp = 4.0f, FocusHeightLift = 2.5f };
		float lh = 0f;
		for (int i = 0; i < 600; i++) lh = lifted.EaseFocusHeight(lh, 3f, dt);
		bool liftedOk = Mathf.IsEqualApprox(lh, 5.5f, 0.01f);   // 3 + 2.5
		GD.Print($"lift: settled={lh:0.000} (target 3 + lift 2.5 = 5.5, ok={liftedOk})");

		cam.Free();
		lifted.Free();

		bool pass = eases && converges && flatUnchanged && liftedOk;
		GD.Print(pass
			? "PASS: focus height eases toward the target Y, is a no-op when flat, and respects the lift"
			: $"FAIL: eases={eases}, converges={converges}, flatUnchanged={flatUnchanged}, liftedOk={liftedOk}");
		return pass;
	}

	// Chunk 47 (M12.7): the co-op lose rule. With GameManager.RequireAllPlayersDead = true the match
	// is lost only once EVERY captain in the "player" group is down — a single captain falling leaves
	// the game going (its partner fights on). Mirrors the co-op scene: captains set
	// ShowGameOverOnDeath = false, so only GameManager reveals "game_over", and only on the LAST death.
	private async Task<bool> TestCoopLose()
	{
		GD.Print("=== UnitTest: co-op lose rule (RequireAllPlayersDead, Chunk 47) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// Stand in for ResultMenu's GAME OVER UI: a node in the "game_over" group, hidden to start.
		var label = new Label { Visible = false };
		label.AddToGroup("game_over");
		AddChild(label);

		var capScene = GD.Load<PackedScene>("res://scenes/Captain.tscn");
		var cap1 = capScene.Instantiate<Player>();
		cap1.ShowGameOverOnDeath = false;        // co-op: leave the reveal to GameManager
		AddChild(cap1);
		cap1.GlobalPosition = new Vector3(-5f, 1f, 0f);
		var cap2 = capScene.Instantiate<Player>();
		cap2.ShowGameOverOnDeath = false;
		AddChild(cap2);
		cap2.GlobalPosition = new Vector3(5f, 1f, 0f);

		var gm = new GameManager { RequireAllPlayersDead = true };
		AddChild(gm);

		// Both captains up: no loss, UI stays hidden.
		for (int i = 0; i < 3; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		bool aliveNoLose = !label.Visible;

		// One captain down: STILL no loss in co-op (the other plays on).
		cap1.TakeDamage(9999f);
		for (int i = 0; i < 3; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		bool oneDownNoLose = cap1.IsDead && !cap2.IsDead && !label.Visible;

		// Both down: now it's a loss — GameManager reveals GAME OVER.
		cap2.TakeDamage(9999f);
		for (int i = 0; i < 3; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		bool bothDownLose = cap2.IsDead && label.Visible;

		GD.Print($"both up -> hidden={aliveNoLose}; one down -> still hidden={oneDownNoLose}; both down -> GAME OVER={bothDownLose}");

		label.QueueFree();
		cap1.QueueFree();
		cap2.QueueFree();
		gm.QueueFree();

		bool pass = aliveNoLose && oneDownNoLose && bothDownLose;
		GD.Print(pass
			? "PASS: co-op loses only when BOTH captains fall"
			: $"FAIL: aliveNoLose={aliveNoLose}, oneDownNoLose={oneDownNoLose}, bothDownLose={bothDownLose}");
		return pass;
	}

	// Chunk 48 (M7): ally commands. Default is Follow (existing behaviour). Hold plants the ally on a
	// fixed point and engages foes within LeashRadius of THAT point; Attack-move advances it to a far
	// point, engaging foes within AggroRange en route. Each mode is exercised on a bare fists Ally.
	private async Task<bool> TestAllyCommands()
	{
		GD.Print("=== UnitTest: ally commands (Follow / Hold / Attack-move, Chunk 48) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var allyScene = GD.Load<PackedScene>("res://scenes/Ally.tscn");

		// (1) Default command is Follow — every existing level spawns this, untouched.
		var def = allyScene.Instantiate<Ally>();
		AddChild(def);
		bool defaultFollow = def.Command == Ally.CommandMode.Follow;
		def.QueueFree();

		// (2) HOLD: plant the ally on a point with no foe nearby; it walks to the point and settles.
		var holder = allyScene.Instantiate<Ally>();
		AddChild(holder);
		holder.GlobalPosition = Vector3.Zero;
		Vector3 holdPoint = new Vector3(6f, 0f, 0f);
		holder.HoldAt(holdPoint);
		for (int i = 0; i < 200; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		float holdDist = holder.GlobalPosition.DistanceTo(holdPoint);
		bool held = holder.Command == Ally.CommandMode.Hold && holdDist < 0.5f;
		GD.Print($"hold: {holdDist:0.00} m from the planted point (held={held})");
		holder.QueueFree();

		// (3) ATTACK-MOVE: send the ally to a distant point with no foe; it advances toward it.
		var mover = allyScene.Instantiate<Ally>();
		AddChild(mover);
		mover.GlobalPosition = Vector3.Zero;
		Vector3 movePoint = new Vector3(0f, 0f, -20f);
		mover.AttackMoveTo(movePoint);
		float moveStart = mover.GlobalPosition.DistanceTo(movePoint);
		for (int i = 0; i < 120; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		float moveEnd = mover.GlobalPosition.DistanceTo(movePoint);
		bool advanced = moveEnd < moveStart - 5f;
		GD.Print($"attack-move: {moveStart:0.0} -> {moveEnd:0.0} m to target (advanced={advanced})");
		mover.QueueFree();

		// (4) HOLD ENGAGE: a foe within LeashRadius of the held point gets attacked.
		var guard = allyScene.Instantiate<Ally>();
		AddChild(guard);
		guard.GlobalPosition = Vector3.Zero;
		guard.HoldAt(Vector3.Zero);
		var foe = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		AddChild(foe);
		foe.GlobalPosition = new Vector3(3f, 0f, 0f);   // within LeashRadius (6) of the held point
		float foeHp0 = foe.Health;
		for (int i = 0; i < 200; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		bool engaged = foe.Health < foeHp0;
		GD.Print($"hold-engage: foe HP {foeHp0} -> {foe.Health} (engaged={engaged})");
		guard.QueueFree();
		foe.QueueFree();

		bool pass = defaultFollow && held && advanced && engaged;
		GD.Print(pass
			? "PASS: Follow default; Hold plants; Attack-move advances; Hold engages a near foe"
			: $"FAIL: defaultFollow={defaultFollow}, held={held}, advanced={advanced}, engaged={engaged}");
		return pass;
	}

	// Chunk 49 (M7): a captain dispatches commands to ITS OWN squad only. Hold plants each owned ally
	// where it stands; Attack-move targets a point AttackMoveDistance ahead of the captain's facing;
	// Follow recalls. An ally bound to a DIFFERENT captain is left untouched (per-captain in co-op).
	private async Task<bool> TestSquadCommands()
	{
		GD.Print("=== UnitTest: captain issues squad commands (Chunk 49) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var capScene = GD.Load<PackedScene>("res://scenes/Captain.tscn");
		var allyScene = GD.Load<PackedScene>("res://scenes/Ally.tscn");

		var cap = capScene.Instantiate<Player>();
		cap.AttackMoveDistance = 12f;
		AddChild(cap);
		cap.GlobalPosition = Vector3.Zero;        // yaw 0 -> faces -Z
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var mine1 = allyScene.Instantiate<Ally>();
		mine1.CaptainPath = cap.GetPath();
		AddChild(mine1);
		mine1.GlobalPosition = new Vector3(-2f, 0f, 2f);
		var mine2 = allyScene.Instantiate<Ally>();
		mine2.CaptainPath = cap.GetPath();
		AddChild(mine2);
		mine2.GlobalPosition = new Vector3(2f, 0f, 2f);

		// A second captain + its ally — cap's orders must NOT reach this squad.
		var other = capScene.Instantiate<Player>();
		AddChild(other);
		other.GlobalPosition = new Vector3(40f, 0f, 0f);
		var theirs = allyScene.Instantiate<Ally>();
		theirs.CaptainPath = other.GetPath();
		AddChild(theirs);
		theirs.GlobalPosition = new Vector3(40f, 0f, 2f);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// HOLD: my allies plant where they stand; the other squad is untouched.
		cap.IssueSquadCommand(Ally.CommandMode.Hold);
		bool holdMine = mine1.Command == Ally.CommandMode.Hold && mine2.Command == Ally.CommandMode.Hold
			&& mine1.CommandPoint.DistanceTo(mine1.GlobalPosition) < 0.01f;
		bool otherUntouched = theirs.Command == Ally.CommandMode.Follow;

		// ATTACK-MOVE: target a point AttackMoveDistance ahead of the captain (-Z at yaw 0). Re-pin the
		// captain's pose first: this checks the command-dispatch math (mode + target relative to the
		// captain), not physics settling, and two ground-less captains repositioned in the same idle
		// frames occasionally swap transforms via the physics-server sync race (a harness artifact —
		// real levels place captains in the scene over solid ground and never hit it).
		cap.GlobalPosition = Vector3.Zero;
		cap.Rotation = Vector3.Zero;
		cap.IssueSquadCommand(Ally.CommandMode.AttackMove);
		Vector3 expect = new Vector3(0f, 0f, -12f);
		bool attackMine = mine1.Command == Ally.CommandMode.AttackMove
			&& mine1.CommandPoint.DistanceTo(expect) < 0.5f;

		// FOLLOW: recall to formation.
		cap.IssueSquadCommand(Ally.CommandMode.Follow);
		bool followMine = mine1.Command == Ally.CommandMode.Follow && mine2.Command == Ally.CommandMode.Follow;

		GD.Print($"hold: mine={holdMine}, otherUntouched={otherUntouched}; attack pt={mine1.CommandPoint} (ok={attackMine}); follow={followMine}");

		cap.QueueFree();
		other.QueueFree();
		mine1.QueueFree();
		mine2.QueueFree();
		theirs.QueueFree();

		bool pass = holdMine && otherUntouched && attackMine && followMine;
		GD.Print(pass
			? "PASS: a captain's order reaches only its own squad, with the right mode + target"
			: $"FAIL: holdMine={holdMine}, otherUntouched={otherUntouched}, attackMine={attackMine}, followMine={followMine}");
		return pass;
	}

	// Chunk 60 (M14): the Scenery terrain is now SOLID. Build a Scenery node, then cast downward
	// rays at a few world (x,z) points and assert each ray lands on the surface at the height the
	// height function (SampleHeight) predicts — once on the flat play centre, once up on a hill —
	// proving the generated HeightMapShape3D collision matches the mesh. Points sit on grid corners
	// so the linearly-interpolated collision surface equals the sampled height exactly.
	private async Task<bool> TestTerrainCollision()
	{
		GD.Print("=== UnitTest: terrain collision (heightmap, Chunk 60) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// A modest landscape with an even cell count (so x=0 and ±30 land on grid corners) and no
		// trees (collision only). step = 120/48 = 2.5, so 0 and 30 are exact corners.
		var scenery = new Scenery
		{
			FieldHalf = 20f,
			RampWidth = 8f,
			TerrainHalf = 60f,
			CellSize = 2.5f,
			PlayAmplitude = 2f,
			RidgeHeight = 4f,
			BackdropHeight = 10f,
			TreeCount = 0,
		};
		AddChild(scenery);                                   // _Ready builds the mesh + collider
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		var space = GetWorld3D().DirectSpaceState;

		// Cast a ray straight down at (x,z) and return the hit Y, or NaN if it missed.
		float RayGroundY(float x, float z)
		{
			var from = new Vector3(x, 100f, z);
			var to = new Vector3(x, -100f, z);
			var q = PhysicsRayQueryParameters3D.Create(from, to);
			var hit = space.IntersectRay(q);
			if (hit.Count == 0)
				return float.NaN;
			return ((Vector3)hit["position"]).Y;
		}

		// (a) Play centre: ray should land at the height function's value there (collider matches mesh).
		float centreExpect = scenery.SampleHeight(0f, 0f);
		float centreHit = RayGroundY(0f, 0f);
		bool centreOk = !float.IsNaN(centreHit) && Mathf.Abs(centreHit - centreExpect) < 0.3f;
		GD.Print($"centre (0,0): ray Y={centreHit:0.000}, predicted={centreExpect:0.000} (ok={centreOk})");

		// (b) Up on a hill, well past the field edge — non-zero elevation the collider must match.
		float hillExpect = scenery.SampleHeight(30f, 30f);
		float hillHit = RayGroundY(30f, 30f);
		bool hillRaised = hillExpect > 1f;   // sanity: the sample point is genuinely uphill
		bool hillOk = !float.IsNaN(hillHit) && Mathf.Abs(hillHit - hillExpect) < 0.3f;
		GD.Print($"hill (30,30): ray Y={hillHit:0.000}, predicted={hillExpect:0.000} (raised={hillRaised}, ok={hillOk})");

		scenery.QueueFree();

		bool pass = centreOk && hillRaised && hillOk;
		GD.Print(pass
			? "PASS: terrain collision matches the height function on the flat centre and the hills"
			: $"FAIL: centreOk={centreOk}, hillRaised={hillRaised}, hillOk={hillOk}");
		return pass;
	}

	// Chunk 61 (M14): grounded movement. Three checks over one gentle landscape:
	//   (a) NON-GROUNDED (Grounded off, today's behaviour) — a skeleton parked in the air with no
	//       foe never falls: gravity is off and its motion is byte-identical to the flat-plane levels.
	//   (b) GROUNDED SETTLE — a grounded skeleton dropped above a hill falls, lands ON the terrain
	//       (IsOnFloor) and comes to rest near the height function's surface (capsule sits ~0.9 m up).
	//   (c) GROUNDED CLIMB — a grounded skeleton marching uphill advances AND gains elevation, so it
	//       genuinely walks up the slope rather than skimming a fixed plane.
	private async Task<bool> TestGroundedMovement()
	{
		GD.Print("=== UnitTest: grounded movement (M14, Chunk 61) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		// A gentle landscape: low rolling field, a wide shallow backdrop ramp — every slope walkable.
		var scenery = new Scenery
		{
			FieldHalf = 10f,
			RampWidth = 24f,
			TerrainHalf = 60f,
			CellSize = 2.5f,
			PlayAmplitude = 2f,
			RidgeHeight = 0f,
			BackdropHeight = 6f,
			TreeCount = 0,
		};
		AddChild(scenery);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		Enemy Spawn(Vector3 pos)
		{
			var e = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
			AddChild(e);
			e.GlobalPosition = pos;
			return e;
		}

		// (a) Non-grounded skeleton floating high over the flat centre (terrain ≈ 0, no collider to
		// depenetrate against) with no opponents — must NOT fall: gravity is off when Grounded is.
		var floater = Spawn(new Vector3(0f, 8f, 3f));   // Grounded defaults to false
		float floatStartY = floater.GlobalPosition.Y;

		// (b) Grounded skeleton dropped just above a hillside — should settle onto the surface.
		float settleX = -25f, settleZ = -25f;
		float settleTerrain = scenery.SampleHeight(settleX, settleZ);
		var settler = Spawn(new Vector3(settleX, settleTerrain + 1.5f, settleZ));
		settler.Grounded = true;

		// (c) Grounded marcher placed on the ramp, marching toward +X (uphill) with no foe in range.
		float climbX = 14f;
		var climber = Spawn(new Vector3(climbX, scenery.SampleHeight(climbX, 0f) + 1.5f, 0f));
		climber.Grounded = true;
		climber.MarchMode = true;
		climber.MarchGoalDirection = new Vector3(1f, 0f, 0f);

		// Let everyone settle onto (or float above) the terrain.
		for (int i = 0; i < 60; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		bool floatStayedUp = Mathf.Abs(floater.GlobalPosition.Y - floatStartY) < 0.05f;
		GD.Print($"(a) non-grounded floater Y {floatStartY:0.00} -> {floater.GlobalPosition.Y:0.00} (stayedUp={floatStayedUp})");

		float settleRest = settler.GlobalPosition.Y;
		bool settledOnFloor = settler.IsOnFloor();
		bool settledHeight = settleRest > settleTerrain + 0.3f && settleRest < settleTerrain + 1.6f;
		GD.Print($"(b) grounded settler rest Y={settleRest:0.00}, terrain={settleTerrain:0.00}, onFloor={settledOnFloor} (heightOk={settledHeight})");

		// Baseline the climber after it has settled on the slope, then let it climb.
		float climbY0 = climber.GlobalPosition.Y;
		float climbX0 = climber.GlobalPosition.X;
		for (int i = 0; i < 150; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		float climbY1 = climber.GlobalPosition.Y;
		float climbX1 = climber.GlobalPosition.X;
		bool advanced = climbX1 - climbX0 > 2f;     // walked forward up the ramp
		bool climbed = climbY1 - climbY0 > 0.5f;    // and genuinely gained elevation
		GD.Print($"(c) grounded climber: X {climbX0:0.00}->{climbX1:0.00} (advanced={advanced}), Y {climbY0:0.00}->{climbY1:0.00} (climbed={climbed})");

		scenery.QueueFree();

		bool pass = floatStayedUp && settledOnFloor && settledHeight && advanced && climbed;
		GD.Print(pass
			? "PASS: grounded units fall/settle/climb terrain; a non-grounded unit ignores gravity (flat-level behaviour intact)"
			: $"FAIL: floatStayedUp={floatStayedUp}, settledOnFloor={settledOnFloor}, settledHeight={settledHeight}, advanced={advanced}, climbed={climbed}");
		return pass;
	}

	// Chunk 62 (M14): spawn / formation / command points sample the terrain so grounded units sit ON
	// the surface, not the scene's flat plane. Four checks over one gentle landscape:
	//   (a) SPAWN SNAP — a grounded unit instanced high over a hill lands at terrain + GroundedSpawnLift
	//       the instant it enters the tree (no waiting on gravity), instead of materialising buried/airborne.
	//   (b) FORMATION SLOT — a grounded ally's formation slot on a slope resolves to the terrain height
	//       at the slot's XZ (the named requirement), so it never steers at a point hanging in the air.
	//   (c) COMMAND POINT — a Hold order on a grounded ally drops the anchor onto the surface.
	//   (d) FLAT INTACT — a NON-grounded ally's Hold anchor keeps the exact Y it was given (no terrain pull).
	private async Task<bool> TestSpawnFormationHeight()
	{
		GD.Print("=== UnitTest: spawn / formation / command height (M14, Chunk 62) ===");

		foreach (Node c in GetChildren())
			c.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		var scenery = new Scenery
		{
			FieldHalf = 10f, RampWidth = 24f, TerrainHalf = 60f, CellSize = 2.5f,
			PlayAmplitude = 2f, RidgeHeight = 0f, BackdropHeight = 6f, TreeCount = 0,
		};
		AddChild(scenery);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		// (a) Spawn snap: instance a grounded skeleton high above a hill BEFORE adding it (so Grounded is
		// set when _Ready runs), then confirm it lands on the terrain the moment it enters the tree.
		float snapX = 15f, snapZ = 0f;
		float snapTerrain = scenery.SampleHeight(snapX, snapZ);
		var dropped = GD.Load<PackedScene>("res://scenes/Skeleton.tscn").Instantiate<Enemy>();
		dropped.Grounded = true;
		dropped.Position = new Vector3(snapX, 50f, snapZ);   // local == global under the identity-root test
		AddChild(dropped);                                   // _Ready -> SnapToGround
		float snappedY = dropped.GlobalPosition.Y;
		bool spawnSnapped = Mathf.IsEqualApprox(snappedY, snapTerrain + dropped.GroundedSpawnLift, 0.05f);
		GD.Print($"(a) spawn snap: Y 50.00 -> {snappedY:0.00}, terrain={snapTerrain:0.00}+lift={dropped.GroundedSpawnLift:0.00} (snapped={spawnSnapped})");

		// A captain (formation anchor) on the flat centre, facing default (FormationYaw 0 -> identity basis).
		var cap = GD.Load<PackedScene>("res://scenes/Captain.tscn").Instantiate<Player>();
		AddChild(cap);   // joins the "player" group so the ally resolves it
		cap.GlobalPosition = new Vector3(0f, scenery.SampleHeight(0f, 0f) + 1f, 0f);

		// (b) Grounded ally whose slot offset puts the slot out on the slope.
		var slotAlly = GD.Load<PackedScene>("res://scenes/Ally.tscn").Instantiate<Ally>();
		slotAlly.Grounded = true;
		slotAlly.FormationOffset = new Vector3(20f, 0f, 0f);
		AddChild(slotAlly);   // _Ready resolves the captain from the "player" group
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		Vector3 slot = slotAlly.SlotWorldPosition();
		float slotTerrain = scenery.SampleHeight(slot.X, slot.Z);
		bool slotOnTerrain = Mathf.IsEqualApprox(slot.Y, slotTerrain, 0.01f);
		GD.Print($"(b) formation slot at ({slot.X:0.0},{slot.Z:0.0}): Y={slot.Y:0.00}, terrain={slotTerrain:0.00} (onTerrain={slotOnTerrain})");

		// (c) Hold command on the grounded ally: the anchor is dropped to the surface.
		slotAlly.HoldAt(new Vector3(18f, 50f, 0f));
		float cmdTerrain = scenery.SampleHeight(18f, 0f);
		bool cmdOnTerrain = Mathf.IsEqualApprox(slotAlly.CommandPoint.Y, cmdTerrain, 0.01f);
		GD.Print($"(c) hold anchor Y={slotAlly.CommandPoint.Y:0.00}, terrain={cmdTerrain:0.00} (onTerrain={cmdOnTerrain})");

		// (d) Flat (non-grounded) ally: the Hold anchor keeps its given Y exactly — flat levels untouched.
		var flatAlly = GD.Load<PackedScene>("res://scenes/Ally.tscn").Instantiate<Ally>();
		AddChild(flatAlly);   // Grounded defaults to false
		flatAlly.HoldAt(new Vector3(18f, 7f, 0f));
		bool flatUntouched = Mathf.IsEqualApprox(flatAlly.CommandPoint.Y, 7f, 0.0001f);
		GD.Print($"(d) non-grounded hold anchor Y={flatAlly.CommandPoint.Y:0.00} (kept given 7.00 = {flatUntouched})");

		scenery.QueueFree();

		bool pass = spawnSnapped && slotOnTerrain && cmdOnTerrain && flatUntouched;
		GD.Print(pass
			? "PASS: grounded spawns/slots/command points sit on the terrain; flat-level points keep their Y"
			: $"FAIL: spawnSnapped={spawnSnapped}, slotOnTerrain={slotOnTerrain}, cmdOnTerrain={cmdOnTerrain}, flatUntouched={flatUntouched}");
		return pass;
	}

	// Chunk 63 (M14): ballistic projectiles on grounded levels arc to a target's REAL height, while
	// flat levels keep the dead-level skim. Four checks:
	//   (a) UPHILL arc — solving + integrating a stone's velocity reaches a target 6 m ABOVE launch.
	//   (b) DOWNHILL arc — same, to a target 5 m BELOW launch.
	//   (c) FLAT INTACT — a Stone aimed with the old Launch() keeps a constant Y (no gravity).
	//   (d) ARROW arc — the Arrow solver reaches an uphill target too (shared math).
	private async Task<bool> TestBallisticProjectiles()
	{
		GD.Print("=== UnitTest: ballistic projectiles (M14, Chunk 63) ===");

		const float g = Ballistics.Gravity;

		// Integrate a solved arc and return the closest distance it ever passes to `to`.
		float ClosestApproach(Vector3 from, Vector3 to, float speed)
		{
			Vector3 v = Ballistics.SolveArcVelocity(from, to, speed, g);
			Vector3 p = from;
			float best = p.DistanceTo(to);
			const float dt = 1f / 120f;
			for (int i = 0; i < 600; i++)   // up to 5 s of flight
			{
				v.Y -= g * dt;
				p += v * dt;
				best = Mathf.Min(best, p.DistanceTo(to));
			}
			return best;
		}

		// (a) Uphill: target 6 m above and 20 m out.
		float upMiss = ClosestApproach(new Vector3(0f, 1f, 0f), new Vector3(20f, 7f, 0f), 18f);
		bool uphill = upMiss < 0.5f;
		GD.Print($"(a) uphill arc closest approach = {upMiss:0.00} m (hit={uphill})");

		// (b) Downhill: target 5 m below and 16 m out.
		float downMiss = ClosestApproach(new Vector3(0f, 8f, 0f), new Vector3(16f, 3f, 0f), 18f);
		bool downhill = downMiss < 0.5f;
		GD.Print($"(b) downhill arc closest approach = {downMiss:0.00} m (hit={downhill})");

		// (c) Flat Launch keeps level flight: a stone fired straight stays at its launch Y.
		var stone = GD.Load<PackedScene>("res://scenes/Stone.tscn").Instantiate<Stone>();
		AddChild(stone);
		stone.GlobalPosition = new Vector3(0f, 3f, 0f);
		stone.Launch(new Vector3(1f, 0f, 0f), Unit.TeamId.Player);
		float y0 = stone.GlobalPosition.Y;
		for (int i = 0; i < 5; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		float y1 = IsInstanceValid(stone) ? stone.GlobalPosition.Y : y0;
		bool flatLevel = Mathf.IsEqualApprox(y0, y1, 0.001f) && Mathf.IsEqualApprox(y0, 3f, 0.001f);
		GD.Print($"(c) flat stone Y {y0:0.00} -> {y1:0.00} (level={flatLevel})");
		if (IsInstanceValid(stone)) stone.QueueFree();

		// (d) Arrow shares the solver — uphill target 24 m out, 8 m up, at arrow speed.
		float arrowMiss = ClosestApproach(new Vector3(0f, 1f, 0f), new Vector3(24f, 9f, 0f), 24f);
		bool arrowArc = arrowMiss < 0.5f;
		GD.Print($"(d) arrow arc closest approach = {arrowMiss:0.00} m (hit={arrowArc})");

		bool pass = uphill && downhill && flatLevel && arrowArc;
		GD.Print(pass
			? "PASS: arcs reach up/downhill targets; flat shots stay level"
			: $"FAIL: uphill={uphill}, downhill={downhill}, flatLevel={flatLevel}, arrowArc={arrowArc}");
		return pass;
	}
}
