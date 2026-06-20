using Godot;

// An enemy bowman — a kiting ranged attacker (the foil to the charging Swordman). It never
// melees and never knocks back (only the captain's pike/sword shoves). Each frame it:
//   1. FLEES if the nearest player-team unit is closer than FleeRange (a charge got through) —
//      it backpedals straight away to re-open distance.
//   2. HOLDS A RANGE BAND otherwise: advances if the target is beyond PreferredRangeMax,
//      backs off if inside PreferredRangeMin, and stands its ground within the band.
//   3. FIRES an Arrow on FireCooldown at any target within PreferredRangeMax (it can loose
//      while kiting), aimed at the target's current position.
// Still obeys any knockback the captain applies, decaying it each frame (shared Unit logic).
public partial class Bowman : Unit
{
	[Export] public float MoveSpeed = 4.0f;          // walk/kite speed (m/s)
	[Export] public float PreferredRangeMin = 10.0f; // back off if a target gets closer than this
	[Export] public float PreferredRangeMax = 14.0f; // advance if the target is farther than this
	[Export] public float FleeRange = 5.0f;          // a unit this close triggers a fast backpedal
	[Export] public float FireCooldown = 1.5f;       // seconds between arrows
	[Export] public float ArrowDamage = 10.0f;       // damage carried by each arrow (no knockback)
	[Export] public float TurnLerp = 10.0f;          // how fast it rotates to face its target
	[Export] public PackedScene ArrowScene;          // projectile to spawn; falls back to res://scenes/Arrow.tscn

	private float _fireTimer; // counts down; > 0 blocks the next shot

	public override void _Ready()
	{
		Team = TeamId.Enemy;   // a bowman is always on the enemy team
		base._Ready();

		// Bowmen need a projectile scene; auto-load one if none was wired in.
		if (ArrowScene == null)
			ArrowScene = GD.Load<PackedScene>("res://scenes/Arrow.tscn");
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		DecayKnockback(dt);

		if (IsDead)
		{
			// Dead bodies are freed shortly after; ride out any lingering shove meanwhile.
			Velocity = ComposeMovement(KnockbackVelocity, dt);
			MoveAndSlide();
			return;
		}

		if (_fireTimer > 0f)
			_fireTimer -= dt;

		Vector3 move = Vector3.Zero;
		// Throttled nearest-foe pick (M5): re-scan only every few frames, but keep kiting the
		// cached target's live position every frame.
		if (ShouldRescanTarget())
			CachedTarget = ScanNearestOpponent();
		Unit target = LiveTarget;
		if (target != null)
		{
			Vector3 toTarget = target.GlobalPosition - GlobalPosition;
			toTarget.Y = 0f;
			float dist = toTarget.Length();
			Vector3 dir = dist > 0.0001f ? toTarget / dist : Vector3.Forward;

			FaceTowards(target.GlobalPosition, dt);

			// Kite: keep inside the range band, fleeing hard if something closed to melee.
			if (dist < FleeRange)
				move = -dir * MoveSpeed;          // backpedal away from the charger
			else if (dist > PreferredRangeMax)
				move = dir * MoveSpeed;           // too far — close into firing range
			else if (dist < PreferredRangeMin)
				move = -dir * MoveSpeed;          // too close — open the gap
			// else: comfortably in the band, stand and shoot.

			// Loose an arrow on cooldown at anything within firing range (even while kiting).
			if (dist <= PreferredRangeMax && _fireTimer <= 0f)
			{
				FireArrowAt(target);
				_fireTimer = FireCooldown;
			}
		}
		else if (MarchMode)
		{
			// Football-pitch auto-battler (Chunk 42): no foe in aggro range — advance to the player endzone.
			move = MarchVelocity(MoveSpeed);
			FaceTowards(GlobalPosition + MarchGoalDirection, dt);
		}

		// A strong shove takes over (ride it out and slow down); as it decays the unit eases its
		// kite back in (OwnMovementScale) instead of snapping it on — no spurious second bump.
		// ComposeMovement folds in gravity on grounded terrain (flat levels: Y stays 0, unchanged).
		Velocity = ComposeMovement(move * OwnMovementScale + KnockbackVelocity, dt);
		MoveAndSlide();
		ResolveKnockbackBounce();   // pinball: pass on / bounce a real shove off whatever we rammed
	}

	// Spawn an Arrow at our position aimed at the target and let it fly. Added to our parent so
	// it lives independently of us, and tagged with our team so it never hits friendlies.
	private void FireArrowAt(Unit target)
	{
		if (ArrowScene == null)
		{
			GD.PrintErr($"[Bowman] {Name} has no ArrowScene");
			return;
		}
		var arrow = ArrowScene.Instantiate<Arrow>();
		arrow.Damage = ArrowDamage;
		GetParent().AddChild(arrow);
		arrow.GlobalPosition = GlobalPosition;
		arrow.Launch(target.GlobalPosition - GlobalPosition, Team);
		GD.Print($"[Bowman] {Name} loosed an arrow at {target.Name}");
	}

	// Smoothly rotate to face a world point on the flat plane (forward is -Z).
	private void FaceTowards(Vector3 worldPos, float dt)
	{
		Vector3 flat = new Vector3(worldPos.X, GlobalPosition.Y, worldPos.Z);
		if (GlobalPosition.DistanceSquaredTo(flat) < 0.0025f)
			return;

		Vector3 dir = flat - GlobalPosition;
		float desiredYaw = Mathf.Atan2(-dir.X, -dir.Z);
		float t = 1f - Mathf.Exp(-TurnLerp * dt);
		Vector3 rot = Rotation;
		rot.Y = Mathf.LerpAngle(rot.Y, desiredYaw, t);
		Rotation = rot;
	}
}
