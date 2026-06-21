using Godot;
using System.Collections.Generic;

// A friendly fighter that follows the player in formation and brawls on a LOOSE LEASH:
// it engages enemies only while one is within LeashRadius of its formation slot, then
// falls back to its slot when none are near. Measuring the leash from the SLOT (not the
// ally) is what stops the squad from scattering — an ally never wanders further than one
// leash from where it belongs.
// Weapon types (Weapon): Fists rush into melee range and punch; Stones hang back and
// lob a projectile on a cooldown; Pikes reach far and can BRACE. All deal damage with NO
// knockback — only the player's sword shoves — with ONE exception: a braced pike repels
// (small shove) whatever charges its front (Chunk 12). Formation is from Chunk 6; fists
// from Chunk 7; stones added in Chunk 8; pike + brace in Chunk 12.
public partial class Ally : Unit
{
	public enum WeaponType { Fists, Stones, Pike }

	[Export] public float MoveSpeed = 7.5f;      // top follow speed (m/s); a margin over player Speed so it can catch up
	[Export] public float Acceleration = 60.0f;  // how fast it ramps toward the target velocity
	[Export] public float ArriveRadius = 1.5f;   // begin slowing within this distance of the slot
	[Export] public float StopRadius = 0.12f;    // close enough — hold position
	[Export] public float TurnSpeed = 120.0f;    // max turn rate (deg/s) — a subordinate pivots deliberately, never snaps (180° ≈ 1.5 s)

	// --- Combat (loose leash) ---
	[Export] public WeaponType Weapon = WeaponType.Fists; // fist-fighter or stone-thrower
	[Export] public float LeashRadius = 6.0f;    // engage enemies within this distance of the slot
	[Export] public float AttackRange = 1.6f;    // (fists) stop & swing once this close to the target
	[Export] public float AttackDamage = 8.0f;   // (fists) damage per punch (no knockback)
	[Export] public float AttackCooldown = 0.8f; // (fists) seconds between punches

	// --- Ranged (stones) ---
	[Export] public float ThrowRange = 7.0f;     // (stones) throw at targets within this distance, else close in
	[Export] public float StoneDamage = 12.0f;   // (stones) damage carried by each thrown stone (no knockback)
	[Export] public float ThrowCooldown = 1.2f;  // (stones) seconds between throws
	[Export] public PackedScene StoneScene;      // (stones) projectile to spawn; falls back to res://scenes/Stone.tscn

	// --- Pike (long reach + brace) ---
	[Export] public float PikeReach = 3.0f;      // (pike) thrust / brace reach distance
	[Export] public float PikeDamage = 10.0f;    // (pike) damage per thrust / brace pulse
	[Export] public float PikeCooldown = 0.9f;   // (pike) seconds between thrusts / brace pulses
	[Export] public float BraceRepel = 4.0f;     // (pike, braced ONLY) small shove flung at enemies in the pike-front
	[Export] public float BraceFrontDot = 0.4f;  // (pike, braced) front cone: enemy must be at least this far "ahead" to be repelled

	// Local-space slot offset relative to the player (forward is -Z, so +Z trails behind).
	// Set per-ally in the scene; rotated by the player's yaw each frame to get the world slot.
	[Export] public Vector3 FormationOffset = Vector3.Zero;

	// Squad ownership (M12.7, Chunk 45). When set, this ally anchors its formation slot AND
	// facing to THAT captain instead of `GetFirstNodeInGroup("player")` — so two captains can
	// each lead their own squad on one screen. Unset (the default) = today's first-player-in-group
	// behaviour, so every single-player level is untouched.
	[Export] public NodePath CaptainPath;

	// Ally commands (M7, Chunk 48). The player can direct a squad beyond the default loose leash:
	//   • Follow (default) — today's behaviour: leash to the moving formation slot, re-form when idle.
	//   • Hold — plant on a fixed world point; engage foes within LeashRadius of THAT point, else hold it.
	//   • AttackMove — advance to a fixed world point, engaging any foe within AggroRange en route, then hold.
	// OFF by default (every unit spawns Follow) so existing levels AND the MarchMode auto-battler are
	// untouched — MarchMode is resolved before the command branches, so a pitch unit ignores commands.
	// Chunk 49 wires the captain's input to issue these to its squad.
	public enum CommandMode { Follow, Hold, AttackMove }
	public CommandMode Command { get; private set; } = CommandMode.Follow;
	private Vector3 _commandPoint;                  // world anchor for Hold / AttackMove
	public Vector3 CommandPoint => _commandPoint;   // read for tests / HUD

	// Issue a command (called by the captain in Chunk 49, or directly in tests). On grounded terrain the
	// anchor is dropped onto the surface (Chunk 62) so the leash distance + arrive logic reference a
	// point ON the ground, not one floating in the air above/below a slope.
	public void HoldAt(Vector3 worldPoint)       { Command = CommandMode.Hold;       _commandPoint = OnGround(worldPoint); }
	public void AttackMoveTo(Vector3 worldPoint) { Command = CommandMode.AttackMove; _commandPoint = OnGround(worldPoint); }
	public void FollowCaptain()                  { Command = CommandMode.Follow; }

	// Drop a world point onto the terrain surface when grounded (a no-op on flat levels — Grounded off,
	// or no terrain, returns the point unchanged), so anchors/slots sit on the ground (Chunk 62).
	private Vector3 OnGround(Vector3 p)
	{
		if (Grounded)
			p.Y = SampleGroundHeight(p.X, p.Z, p.Y);
		return p;
	}

	private Node3D _player;
	private float _attackTimer; // counts down; > 0 blocks the next fist hit

	// The captain this ally answers to (resolved in _Ready from CaptainPath or the player group).
	// A captain reads this to dispatch commands only to ITS own squad (M7, Chunk 49).
	public Node3D Captain => _player;

	public override void _Ready()
	{
		Team = TeamId.Player;   // allies fight on the player's side
		base._Ready();
		// Pick our captain once: an explicit CaptainPath binds us to ONE captain (co-op squads,
		// Chunk 45); otherwise fall back to the first node in the "player" group (single-player).
		if (CaptainPath != null && !string.IsNullOrEmpty(CaptainPath.ToString()))
			_player = GetNodeOrNull<Node3D>(CaptainPath);
		if (_player == null)
			_player = GetTree().GetFirstNodeInGroup("player") as Node3D;

		// Stone-throwers need a projectile scene; auto-load one if none was wired in.
		if (Weapon == WeaponType.Stones && StoneScene == null)
			StoneScene = GD.Load<PackedScene>("res://scenes/Stone.tscn");
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		DecayKnockback(dt);

		if (IsDead)
		{
			// Match the others: ride out any lingering shove on the death frame.
			Velocity = ComposeMovement(KnockbackVelocity, dt);
			MoveAndSlide();
			return;
		}

		if (_attackTimer > 0f)
			_attackTimer -= dt;

		Vector3 desiredVel = Vector3.Zero;
		bool facingHandled = false;

		// BRACE (pike only, polled each frame — never plumbed from the captain): plant the
		// line on the slot, face the captain's yaw, and damage + lightly repel anything in
		// the pike-front. This is the ONE exception to "only the captain's weapon shoves".
		// Brace overrides normal target-chasing — a braced wall holds, it doesn't pursue.
		if (Weapon == WeaponType.Pike && CaptainBraceHeld())
		{
			desiredVel = SlotArriveVelocity();   // hold the slot
			UpdateBracePulse();                  // impale/repel the front on cooldown
			// facingHandled stays false so FacePlayerYaw below aims the pike at the captain's yaw.
		}
		else
		{
			// Throttled leash scan (M5): re-pick our target only every few frames, but keep engaging
			// the cached target's live position every frame. The SCAN anchor depends on the command
			// (M7): Follow leashes to the moving formation slot, Hold leashes to its planted point,
			// while AttackMove (and the MarchMode auto-battler, Chunk 42) instead pick the nearest foe
			// within AggroRange of US — they push forward, so there's no fixed post to leash to.
			Vector3 anchor = CommandAnchor();
			bool aggroScan = MarchMode || Command == CommandMode.AttackMove;
			if (ShouldRescanTarget())
				CachedTarget = aggroScan
					? UnitRegistry.FindNearestOpponent(Team, GlobalPosition, AggroRange)
					: FindTargetInLeash(anchor);
			Unit target = LiveTarget;
			if (target != null)
			{
				// Combat: chase the in-leash enemy and strike on contact (no knockback).
				Vector3 toTarget = target.GlobalPosition - GlobalPosition;
				toTarget.Y = 0f;
				float dist = toTarget.Length();

				FaceTowards(target.GlobalPosition, dt);
				facingHandled = true;

				if (Weapon == WeaponType.Stones)
				{
					// Stones: hang back and pelt; only close in when the target is out of throw range.
					if (dist > ThrowRange)
						desiredVel = toTarget.Normalized() * MoveSpeed;
					else if (_attackTimer <= 0f)
					{
						ThrowStoneAt(target);
						_attackTimer = ThrowCooldown;
					}
				}
				else
				{
					// Fists (AttackRange) or Pike (PikeReach): rush into reach, then strike on cooldown.
					bool pike = Weapon == WeaponType.Pike;
					float reach = pike ? PikeReach : AttackRange;
					float dmg = pike ? PikeDamage : AttackDamage;
					float cd = pike ? PikeCooldown : AttackCooldown;

					if (dist > reach)
						desiredVel = toTarget.Normalized() * MoveSpeed;
					else if (_attackTimer <= 0f)
					{
						float hit = ScaledWeaponDamage(dmg);   // Strength scales weapon attack power (Chunk 36)
						target.TakeDamage(hit);                // unbraced strike: damage only, no shove
						_attackTimer = cd;
						GD.Print($"[Ally] {Name} struck {target.Name} for {hit:0.0}");
					}
				}
			}
			else if (MarchMode)
			{
				// Football-pitch auto-battler (Chunk 42): no foe in aggro range — advance to the enemy endzone.
				desiredVel = MarchVelocity(MoveSpeed);
				FaceTowards(GlobalPosition + MarchGoalDirection, dt);
				facingHandled = true;
			}
			else if (Command == CommandMode.AttackMove)
			{
				// Attack-move (M7): no foe in range — keep advancing to the commanded point (arrive-
				// slowdown, then hold it). Face the way we're pushing.
				desiredVel = ArriveVelocity(_commandPoint);
				FaceTowards(_commandPoint, dt);
				facingHandled = true;
			}
			else if (Command == CommandMode.Hold)
			{
				// Hold (M7): plant on the commanded point and keep it (no captain needed).
				desiredVel = ArriveVelocity(_commandPoint);
			}
			else if (_player != null)
			{
				// Follow: no enemy in leash — fall back to the formation slot (Chunk 6 arrive behaviour).
				desiredVel = SlotArriveVelocity();
			}
		}

		Vector3 horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);
		horizontal = horizontal.MoveToward(desiredVel, Acceleration * dt);

		// A strong shove takes over (ride it out and slow down); as it decays the unit eases its
		// steer back in (OwnMovementScale) instead of snapping it on — no spurious second bump.
		// ComposeMovement folds in gravity on grounded terrain (flat levels: Y stays 0, unchanged).
		Velocity = ComposeMovement(horizontal * OwnMovementScale + KnockbackVelocity, dt);
		MoveAndSlide();
		ResolveKnockbackBounce();   // pinball: pass on / bounce a real shove off whatever we rammed

		// Out of combat, face wherever the player is aiming so the squad points together;
		// in combat, FaceTowards already aimed us at the target.
		if (!facingHandled && _player != null)
			FacePlayerYaw(dt);
	}

	// Velocity that walks us toward our formation slot with arrive-slowdown — full speed
	// when far, scaled down inside ArriveRadius so we settle instead of overshooting, and
	// zero once within StopRadius. Shared by the re-form fallback and the brace stance.
	private Vector3 SlotArriveVelocity()
	{
		if (_player == null)
			return Vector3.Zero;
		return ArriveVelocity(SlotWorldPosition());
	}

	// Arrive-slowdown velocity toward a flat world point: full speed when far, eased down inside
	// ArriveRadius so we settle instead of overshoot, zero within StopRadius. Shared by the formation
	// re-form, the brace stance, and the Hold / Attack-move commands (M7).
	private Vector3 ArriveVelocity(Vector3 worldPoint)
	{
		Vector3 to = worldPoint - GlobalPosition;
		to.Y = 0f;
		float dist = to.Length();
		if (dist <= StopRadius)
			return Vector3.Zero;
		float speed = MoveSpeed * Mathf.Min(1f, dist / ArriveRadius);
		return to.Normalized() * speed;
	}

	// The point our leash/return logic anchors to this frame: the moving formation slot (Follow),
	// or the planted command point (Hold / Attack-move). Pure read.
	private Vector3 CommandAnchor() => Command switch
	{
		CommandMode.Hold => _commandPoint,
		CommandMode.AttackMove => _commandPoint,
		_ => SlotWorldPosition(),
	};

	// Braced-pike pulse: on cooldown, damage AND lightly repel every enemy that sits within
	// PikeReach inside our front cone. The repel (small knockback) is the phalanx's whole
	// point and the one sanctioned exception to "only the captain's weapon knocks back".
	private void UpdateBracePulse()
	{
		if (_attackTimer > 0f)
			return;

		Vector3 fwd = -GlobalTransform.Basis.Z;   // pikeman's facing on the flat plane
		fwd.Y = 0f;
		bool struck = false;
		IReadOnlyList<Unit> foes = UnitRegistry.Opponents(Team);
		for (int i = 0; i < foes.Count; i++)
		{
			Unit u = foes[i];
			if (u == null || !IsInstanceValid(u) || u.IsDead)
				continue;
			Vector3 to = u.GlobalPosition - GlobalPosition;
			to.Y = 0f;
			float d = to.Length();
			if (d > PikeReach || d < 0.0001f)
				continue;                       // out of reach
			if (fwd.Dot(to.Normalized()) < BraceFrontDot)
				continue;                       // not in the pike-front cone
			u.TakeDamage(ScaledWeaponDamage(PikeDamage), to, BraceRepel);   // braced pike: Str-scaled damage + small repel
			struck = true;
		}

		if (struck)
		{
			_attackTimer = PikeCooldown;
			GD.Print($"[Ally] {Name} braced pike impaled the front for {PikeDamage} (+repel {BraceRepel})");
		}
	}

	// Spawn a Stone at our feet aimed at the target and let it fly (Chunk 8). Added to our
	// parent so it lives independently of us, and tagged with our team so it never hits
	// friendlies. Stones deal damage with no knockback.
	private void ThrowStoneAt(Unit target)
	{
		if (StoneScene == null)
		{
			GD.PrintErr($"[Ally] {Name} is a stone-thrower but has no StoneScene");
			return;
		}
		var stone = StoneScene.Instantiate<Stone>();
		stone.Damage = StoneDamage;
		GetParent().AddChild(stone);
		stone.GlobalPosition = GlobalPosition;
		// Grounded levels lob a gravity arc onto the target's real (up/downhill) position; flat
		// levels keep the straight level skim (Chunk 63).
		if (Grounded)
			stone.LaunchBallistic(stone.GlobalPosition, target.GlobalPosition, Team);
		else
			stone.Launch(target.GlobalPosition - GlobalPosition, Team);
		GD.Print($"[Ally] {Name} threw a stone at {target.Name}");
	}

	// Nearest living enemy-team unit whose distance to our ANCHOR (formation slot for Follow, the
	// planted point for Hold) is within LeashRadius. Gating on the anchor (not the ally) keeps the
	// squad from chasing a fleeing enemy across the map — it returns null once nothing is near the
	// anchor, and we re-form / re-settle.
	private Unit FindTargetInLeash(Vector3 anchor)
	{
		float leashSq = LeashRadius * LeashRadius;
		Unit best = null;
		float bestSq = float.MaxValue;
		IReadOnlyList<Unit> foes = UnitRegistry.Opponents(Team);
		for (int i = 0; i < foes.Count; i++)
		{
			Unit u = foes[i];
			if (u == null || !IsInstanceValid(u) || u.IsDead)
				continue;
			if (anchor.DistanceSquaredTo(u.GlobalPosition) > leashSq)
				continue;   // too far from our post — leave it alone
			float d = GlobalPosition.DistanceSquaredTo(u.GlobalPosition);
			if (d < bestSq)
			{
				bestSq = d;
				best = u;
			}
		}
		return best;
	}

	// Is OUR captain holding brace? Route through the captain's scheme-aware BraceHeld() so a
	// co-op squad braces only with ITS captain's device (Chunk 44/47). For a single-player captain
	// (Control = Any) this resolves to the same global "brace" action as before — byte-identical —
	// and a non-Player captain (or none) falls back to the global action.
	private bool CaptainBraceHeld() =>
		_player is Player p ? p.BraceHeld() : Input.IsActionPressed("brace");

	// World position of this ally's formation slot: the local offset rotated by the captain's
	// FORMATION yaw (the slow wheel), not its instant aim — so the whole wall of slots wheels
	// deliberately when the captain flicks the mouse instead of teleporting to the far side. Falls
	// back to a plain Node3D captain's live basis (non-Player anchors) so other rigs still work.
	public Vector3 SlotWorldPosition()
	{
		if (_player == null)
			return GlobalPosition;
		Basis basis = _player is Player p ? p.FormationBasis : _player.GlobalTransform.Basis;
		// On grounded terrain the slot rides the surface height at its XZ (Chunk 62) so an ally never
		// steers at — or measures its leash from — a point hanging in the air over a slope.
		return OnGround(_player.GlobalPosition + basis * FormationOffset);
	}

	// Rotate to face a world point on the flat plane (forward is -Z), capped to TurnSpeed.
	private void FaceTowards(Vector3 worldPos, float dt)
	{
		Vector3 flat = new Vector3(worldPos.X, GlobalPosition.Y, worldPos.Z);
		if (GlobalPosition.DistanceSquaredTo(flat) < 0.0025f)
			return;

		// Forward is -Z, so negate the delta: atan2(dx,dz) would aim our BACK at the target.
		Vector3 dir = flat - GlobalPosition;
		TurnTowardYaw(Mathf.Atan2(-dir.X, -dir.Z), dt);
	}

	// Match the captain's FORMATION yaw (the slow wheel), not its instant aim, so an idle squad
	// wheels together deliberately. Falls back to a plain captain's body yaw for non-Player anchors.
	private void FacePlayerYaw(float dt)
	{
		float yaw = _player is Player p ? p.FormationYaw : _player.Rotation.Y;
		TurnTowardYaw(yaw, dt);
	}

	// Turn toward a desired yaw by at most TurnSpeed*dt this frame, shortest way around — a hard rate
	// cap (not an exponential lerp), so a subordinate can never snap-spin to a new facing.
	private void TurnTowardYaw(float desiredYaw, float dt)
	{
		float maxStep = Mathf.DegToRad(TurnSpeed) * dt;
		float diff = Mathf.Clamp(Mathf.Wrap(desiredYaw - Rotation.Y, -Mathf.Pi, Mathf.Pi), -maxStep, maxStep);
		Vector3 rot = Rotation;
		rot.Y += diff;
		Rotation = rot;
	}
}
