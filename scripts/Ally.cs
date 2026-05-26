using Godot;

// A friendly fighter that follows the player in formation and brawls on a LOOSE LEASH:
// it engages enemies only while one is within LeashRadius of its formation slot, chasing
// and punching with fists (damage, no knockback), then falls back to its slot when none
// are near. Measuring the leash from the SLOT (not the ally) is what stops the squad from
// scattering — an ally never wanders further than one leash from where it belongs.
// Movement/formation logic is from Chunk 6; combat is layered on top here (Chunk 7).
public partial class Ally : Unit
{
	[Export] public float MoveSpeed = 10.0f;     // top follow speed (m/s); > player Speed so it can catch up
	[Export] public float Acceleration = 60.0f;  // how fast it ramps toward the target velocity
	[Export] public float ArriveRadius = 1.5f;   // begin slowing within this distance of the slot
	[Export] public float StopRadius = 0.12f;    // close enough — hold position
	[Export] public float TurnLerp = 10.0f;      // how fast it rotates to match the player's facing

	// --- Combat (fists, loose leash) ---
	[Export] public float LeashRadius = 6.0f;    // engage enemies within this distance of the slot
	[Export] public float AttackRange = 1.6f;    // stop & swing once this close to the target
	[Export] public float AttackDamage = 8.0f;   // fist damage per hit (no knockback)
	[Export] public float AttackCooldown = 0.8f; // seconds between fist hits

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

		Unit target = FindTargetInLeash();
		if (target != null)
		{
			// Combat: chase the in-leash enemy and punch on contact (fists -> no knockback).
			Vector3 toTarget = target.GlobalPosition - GlobalPosition;
			toTarget.Y = 0f;
			float dist = toTarget.Length();

			FaceTowards(target.GlobalPosition, dt);
			facingHandled = true;

			if (dist > AttackRange)
			{
				desiredVel = toTarget.Normalized() * MoveSpeed;
			}
			else if (_attackTimer <= 0f)
			{
				target.TakeDamage(AttackDamage);   // fists: damage only, no shove
				_attackTimer = AttackCooldown;
				GD.Print($"[Ally] {Name} punched {target.Name} for {AttackDamage}");
			}
		}
		else if (_player != null)
		{
			// No enemy in leash: fall back to the formation slot (Chunk 6 arrive behaviour).
			Vector3 toSlot = SlotWorldPosition() - GlobalPosition;
			toSlot.Y = 0f;
			float dist = toSlot.Length();

			if (dist > StopRadius)
			{
				// Full speed when far, scaled down inside ArriveRadius so it settles
				// into its slot instead of overshooting it.
				float speed = MoveSpeed * Mathf.Min(1f, dist / ArriveRadius);
				desiredVel = toSlot.Normalized() * speed;
			}
		}

		Vector3 horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);
		horizontal = horizontal.MoveToward(desiredVel, Acceleration * dt);

		Velocity = horizontal + KnockbackVelocity;
		MoveAndSlide();

		// Out of combat, face wherever the player is aiming so the squad points together;
		// in combat, FaceTowards already aimed us at the target.
		if (!facingHandled && _player != null)
			FacePlayerYaw(dt);
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
		foreach (Node n in GetTree().GetNodesInGroup("units"))
		{
			if (n is Unit u && !u.IsDead && u.Team != Team)
			{
				if (slot.DistanceSquaredTo(u.GlobalPosition) > leashSq)
					continue;   // too far from our post — leave it alone
				float d = GlobalPosition.DistanceSquaredTo(u.GlobalPosition);
				if (d < bestSq)
				{
					bestSq = d;
					best = u;
				}
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
