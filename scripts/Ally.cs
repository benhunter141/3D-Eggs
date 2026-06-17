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
	[Export] public float TurnLerp = 10.0f;      // how fast it rotates to match the player's facing

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

	private Node3D _player;
	private float _attackTimer; // counts down; > 0 blocks the next fist hit

	public override void _Ready()
	{
		Team = TeamId.Player;   // allies fight on the player's side
		base._Ready();
		// The player tags itself into the "player" group on ready; grab it once.
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
			Velocity = KnockbackVelocity;
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
		if (Weapon == WeaponType.Pike && Input.IsActionPressed("brace"))
		{
			desiredVel = SlotArriveVelocity();   // hold the slot
			UpdateBracePulse();                  // impale/repel the front on cooldown
			// facingHandled stays false so FacePlayerYaw below aims the pike at the captain's yaw.
		}
		else
		{
			// Throttled leash scan (M5): re-pick our in-leash target only every few frames,
			// but keep engaging the cached target's live position every frame.
			if (ShouldRescanTarget())
				CachedTarget = FindTargetInLeash();
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
			else if (_player != null)
			{
				// No enemy in leash: fall back to the formation slot (Chunk 6 arrive behaviour).
				desiredVel = SlotArriveVelocity();
			}
		}

		Vector3 horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);
		horizontal = horizontal.MoveToward(desiredVel, Acceleration * dt);

		// A strong shove takes over (ride it out and slow down); as it decays the unit eases its
		// steer back in (OwnMovementScale) instead of snapping it on — no spurious second bump.
		Velocity = horizontal * OwnMovementScale + KnockbackVelocity;
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
		Vector3 toSlot = SlotWorldPosition() - GlobalPosition;
		toSlot.Y = 0f;
		float dist = toSlot.Length();
		if (dist <= StopRadius)
			return Vector3.Zero;
		float speed = MoveSpeed * Mathf.Min(1f, dist / ArriveRadius);
		return toSlot.Normalized() * speed;
	}

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
		stone.Launch(target.GlobalPosition - GlobalPosition, Team);
		GD.Print($"[Ally] {Name} threw a stone at {target.Name}");
	}

	// Nearest living enemy-team unit whose distance to OUR SLOT is within LeashRadius.
	// Gating on the slot (not the ally) keeps the squad from chasing a fleeing enemy
	// across the map — it returns null once nothing is near the slot, and we re-form.
	private Unit FindTargetInLeash()
	{
		Vector3 slot = SlotWorldPosition();
		float leashSq = LeashRadius * LeashRadius;
		Unit best = null;
		float bestSq = float.MaxValue;
		IReadOnlyList<Unit> foes = UnitRegistry.Opponents(Team);
		for (int i = 0; i < foes.Count; i++)
		{
			Unit u = foes[i];
			if (u == null || !IsInstanceValid(u) || u.IsDead)
				continue;
			if (slot.DistanceSquaredTo(u.GlobalPosition) > leashSq)
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

	// World position of this ally's formation slot: the local offset rotated by the
	// player's current orientation, so the formation rotates as the player turns.
	public Vector3 SlotWorldPosition()
	{
		if (_player == null)
			return GlobalPosition;
		return _player.GlobalPosition + _player.GlobalTransform.Basis * FormationOffset;
	}

	// Smoothly rotate to face a world point on the flat plane (forward is -Z).
	private void FaceTowards(Vector3 worldPos, float dt)
	{
		Vector3 flat = new Vector3(worldPos.X, GlobalPosition.Y, worldPos.Z);
		if (GlobalPosition.DistanceSquaredTo(flat) < 0.0025f)
			return;

		// Forward is -Z, so negate the delta: atan2(dx,dz) would aim our BACK at the target.
		Vector3 dir = flat - GlobalPosition;
		float desiredYaw = Mathf.Atan2(-dir.X, -dir.Z);
		float t = 1f - Mathf.Exp(-TurnLerp * dt);
		Vector3 rot = Rotation;
		rot.Y = Mathf.LerpAngle(rot.Y, desiredYaw, t);
		Rotation = rot;
	}

	// Smoothly match the player's yaw so the ally faces wherever the player is aiming.
	private void FacePlayerYaw(float dt)
	{
		float t = 1f - Mathf.Exp(-TurnLerp * dt);
		Vector3 rot = Rotation;
		rot.Y = Mathf.LerpAngle(rot.Y, _player.Rotation.Y, t);
		Rotation = rot;
	}
}
