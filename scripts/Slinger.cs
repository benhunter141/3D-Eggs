using Godot;

// Bandit Slinger — the tier-3 ranged kiter of the M17 bestiary (Chunk 87). Unlike every melee
// foe so far it never closes to contact: it whirls a sling and lobs Stones from range, then
// backpedals the moment an egg charges into its face. Mechanically it is the enemy mirror of the
// player-side stone-throwing Ally / the Bowman's kite, but armed with a Stone (reused projectile)
// instead of an arrow. Each physics frame it:
//   1. FLEES if the nearest player-team unit is closer than FleeRange (someone got in its face) —
//      backpedals straight away to re-open distance.
//   2. HOLDS A RANGE BAND otherwise: advances if the target is past PreferredRangeMax, backs off
//      if inside PreferredRangeMin, stands its ground within the band.
//   3. SLINGS a Stone on FireCooldown at any target within PreferredRangeMax (it can loose while
//      kiting), aimed at the target's current position (ballistic arc on grounded terrain).
// Still obeys any knockback applied to it, decaying each frame (shared Unit logic). Like all foes
// its own hits carry no knockback — only the captains shove. Co-op-Card-Brawl scope (opts in there).
public partial class Slinger : Unit
{
	[Export] public float MoveSpeed = 3.5f;          // walk/kite speed (m/s) — a touch slower than a Bowman
	[Export] public float PreferredRangeMin = 8.0f;  // back off if a target gets closer than this
	[Export] public float PreferredRangeMax = 12.0f; // advance if the target is farther than this
	[Export] public float FleeRange = 4.0f;          // a unit this close triggers a fast backpedal
	[Export] public float FireCooldown = 1.8f;       // seconds between slung stones
	[Export] public float StoneDamage = 9.0f;        // damage carried by each stone (no knockback)
	[Export] public float TurnLerp = 10.0f;          // how fast it rotates to face its target
	[Export] public PackedScene StoneScene;          // projectile to spawn; falls back to res://scenes/Stone.tscn

	private float _fireTimer; // counts down; > 0 blocks the next sling

	public override void _Ready()
	{
		Team = TeamId.Enemy;   // a bandit slinger is always on the enemy team
		base._Ready();

		// Slingers need a projectile scene; auto-load one if none was wired in.
		if (StoneScene == null)
			StoneScene = GD.Load<PackedScene>("res://scenes/Stone.tscn");
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

			// Kite: keep inside the range band, fleeing hard if something closed in.
			if (dist < FleeRange)
				move = -dir * MoveSpeed;          // backpedal away from the charger
			else if (dist > PreferredRangeMax)
				move = dir * MoveSpeed;           // too far — close into firing range
			else if (dist < PreferredRangeMin)
				move = -dir * MoveSpeed;          // too close — open the gap
			// else: comfortably in the band, stand and sling.

			// Sling a stone on cooldown at anything within firing range (even while kiting).
			if (dist <= PreferredRangeMax && _fireTimer <= 0f)
			{
				FireStoneAt(target);
				_fireTimer = FireCooldown;
			}
		}
		else if (MarchMode)
		{
			// Football-pitch auto-battler parity (Chunk 42): no foe in aggro range — advance to the goal.
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

	// Spawn a Stone at our position aimed at the target and let it fly. Added to our parent so it
	// lives independently of us, and tagged with our team so it never hits friendlies.
	private void FireStoneAt(Unit target)
	{
		if (StoneScene == null)
		{
			GD.PrintErr($"[Slinger] {Name} has no StoneScene");
			return;
		}
		var stone = StoneScene.Instantiate<Stone>();
		stone.Damage = StoneDamage;
		GetParent().AddChild(stone);
		stone.GlobalPosition = GlobalPosition;
		// Grounded levels lob a gravity arc onto the target's real (up/downhill) position; flat
		// levels keep the straight level skim (Chunk 63 parity).
		if (Grounded)
			stone.LaunchBallistic(stone.GlobalPosition, target.GlobalPosition, Team);
		else
			stone.Launch(target.GlobalPosition - GlobalPosition, Team);
		GD.Print($"[Slinger] {Name} slung a stone at {target.Name}");
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
