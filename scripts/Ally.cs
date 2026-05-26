using Godot;

// A friendly fighter that follows the player in formation. CHUNK 6 IS MOVEMENT ONLY:
// each ally steers toward an assigned slot — a fixed local offset rotated by the
// player's facing, so the whole formation turns with the player — and eases to a stop
// as it arrives. Combat (loose-leash engagement, fists, stones) lands in Chunks 7-8.
public partial class Ally : Unit
{
	[Export] public float MoveSpeed = 10.0f;     // top follow speed (m/s); > player Speed so it can catch up
	[Export] public float Acceleration = 60.0f;  // how fast it ramps toward the target velocity
	[Export] public float ArriveRadius = 1.5f;   // begin slowing within this distance of the slot
	[Export] public float StopRadius = 0.12f;    // close enough — hold position
	[Export] public float TurnLerp = 10.0f;      // how fast it rotates to face its travel direction

	// Local-space slot offset relative to the player (forward is -Z, so +Z trails behind).
	// Set per-ally in the scene; rotated by the player's yaw each frame to get the world slot.
	[Export] public Vector3 FormationOffset = Vector3.Zero;

	private Node3D _player;

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

		Vector3 desiredVel = Vector3.Zero;
		if (_player != null)
		{
			Vector3 toSlot = SlotWorldPosition() - GlobalPosition;
			toSlot.Y = 0f;
			float dist = toSlot.Length();

			if (dist > StopRadius)
			{
				// Arrive behaviour: full speed when far, scaled down inside ArriveRadius
				// so the ally settles into its slot instead of overshooting it.
				float speed = MoveSpeed * Mathf.Min(1f, dist / ArriveRadius);
				desiredVel = toSlot.Normalized() * speed;
			}
		}

		Vector3 horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);
		horizontal = horizontal.MoveToward(desiredVel, Acceleration * dt);

		Velocity = horizontal + KnockbackVelocity;
		MoveAndSlide();

		// Face the way we're actually heading (only while moving enough to matter).
		if (horizontal.LengthSquared() > 0.04f)
			FaceTowards(GlobalPosition + horizontal, dt);
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

		float desiredYaw = Mathf.Atan2(flat.X - GlobalPosition.X, flat.Z - GlobalPosition.Z);
		float t = 1f - Mathf.Exp(-TurnLerp * dt);
		Vector3 rot = Rotation;
		rot.Y = Mathf.LerpAngle(rot.Y, desiredYaw, t);
		Rotation = rot;
	}
}
