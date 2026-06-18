using Godot;
using System.Collections.Generic;

// The player capsule. Twin-stick movement on the flat XZ plane.
// WASD (or left stick) moves; mouse aims; left-click THRUSTS the held weapon straight ahead.
// Extends Unit so it shares the team/health/damage pipeline with allies and enemies.
//
// Weapon swap (M9, Chunks 26–27): the captain carries one of several weapons, cycled live
// with `swap_weapon` (Q / gamepad). Each weapon is a PROFILE — reach, per-hit damage +
// knockback, and thrust/cooldown feel, plus which mesh is shown. The profiles live in a
// table keyed by WeaponType (built from the Inspector exports in _Ready), so adding a weapon
// is one enum entry + one table row + one mesh; the swing code reads one set of runtime
// fields (filled by ApplyWeapon) regardless of which weapon is up — the same plumbing any
// unit could be skinned with later.
//   • Spear — LONG reach, NO knockback: a phalanx poker that out-ranges its foe.
//   • Sword — SHORT reach, strong KNOCKBACK: the classic sword that flings what it hits.
//   • Axe   — heavy & SLOW, the biggest single hit, with a modest shove.
//   • Mace  — the KNOCKBACK specialist: middling damage, the hardest fling of all.
public partial class Player : Unit
{
	public enum WeaponType { Spear, Sword, Axe, Mace }

	// One weapon's resolved numbers. The Inspector exports below feed these in _Ready so each
	// weapon stays tunable while the swing code reads a single uniform set.
	private readonly struct WeaponProfile
	{
		public readonly float Damage, Knockback, Reach, ThrustDistance, SwingDuration, SwingCooldown;
		public WeaponProfile(float damage, float knockback, float reach, float thrustDistance, float swingDuration, float swingCooldown)
		{
			Damage = damage; Knockback = knockback; Reach = reach;
			ThrustDistance = thrustDistance; SwingDuration = swingDuration; SwingCooldown = swingCooldown;
		}
	}

	// --- Tunable feel knobs (editable in the Inspector too) ---
	[Export] public float Speed = 6.0f;         // top movement speed (m/s) — a notch above the AI, not double
	[Export] public float Acceleration = 60.0f; // how fast we ramp up to Speed
	[Export] public float Friction = 50.0f;     // how fast we slow to a stop
	[Export] public float TurnSpeed = 480.0f;   // max aim turn rate (deg/s) — caps how fast we (and the phalanx) can pivot

	// --- Weapon loadout (Chunk 26) ---
	[Export] public WeaponType StartingWeapon = WeaponType.Spear;  // which weapon the captain spawns holding (per-level default)

	// Spear profile — long reach, no knockback.
	[Export] public float SpearDamage = 40.0f;
	[Export] public float SpearKnockback = 0.0f;       // a pike pokes; it doesn't fling
	[Export] public float SpearReach = 3.0f;           // hitbox forward length (m) — out-ranges melee
	[Export] public float SpearThrustDistance = 0.9f;  // extra lunge at full extension
	[Export] public float SpearSwingDuration = 0.2f;
	[Export] public float SpearSwingCooldown = 0.35f;

	// Sword profile — short reach, strong knockback (the classic sword rules).
	[Export] public float SwordDamage = 40.0f;
	[Export] public float SwordKnockback = 10.0f;      // shove speed (m/s) flung along the hit direction
	[Export] public float SwordReach = 1.4f;           // hitbox forward length (m) — much shorter than the spear
	[Export] public float SwordThrustDistance = 0.6f;
	[Export] public float SwordSwingDuration = 0.18f;
	[Export] public float SwordSwingCooldown = 0.30f;

	// Axe profile — heavy & slow: the biggest single hit, with a modest shove. The long
	// cooldown is the heavy-weapon tax (you commit to each swing).
	[Export] public float AxeDamage = 70.0f;
	[Export] public float AxeKnockback = 4.0f;
	[Export] public float AxeReach = 1.6f;
	[Export] public float AxeThrustDistance = 0.6f;
	[Export] public float AxeSwingDuration = 0.28f;
	[Export] public float AxeSwingCooldown = 0.55f;    // slow

	// Mace profile — the knockback specialist: middling damage, the hardest fling of all.
	[Export] public float MaceDamage = 28.0f;
	[Export] public float MaceKnockback = 14.0f;       // strongest shove of any weapon
	[Export] public float MaceReach = 1.5f;
	[Export] public float MaceThrustDistance = 0.6f;
	[Export] public float MaceSwingDuration = 0.22f;
	[Export] public float MaceSwingCooldown = 0.42f;

	// --- Shared thrust feel ---
	[Export] public float ThrustExtendFrac = 0.4f;   // fraction of the duration spent jabbing out (rest is the retract)
	[Export] public float SwordReturnLerp = 12.0f;   // how fast the weapon eases back to rest if interrupted

	// Active profile values, set by ApplyWeapon — the swing code reads only these.
	private WeaponType _weapon;
	private float _damage;
	private float _knockback;
	private float _reach;
	private float _thrustDistance;
	private float _swingDuration;
	private float _swingCooldown;

	// The weapon table + the mesh per weapon, built in _Ready.
	private Dictionary<WeaponType, WeaponProfile> _profiles;
	private Dictionary<WeaponType, Node3D> _weaponMeshes;

	// Read-only views (used by the headless weapon tests).
	public WeaponType CurrentWeapon => _weapon;
	public float CurrentDamage => _damage;                               // weapon BASE damage (pre-stat)
	public float CurrentAttackDamage => ScaledWeaponDamage(_damage);     // what a hit actually deals (Strength-scaled)
	public float CurrentWeaponKnockback => _knockback;   // NOTE: Unit.CurrentKnockback is the live shove Vector3
	public float CurrentReach => _reach;
	public float CurrentSwingCooldown => _swingCooldown;                // higher = slower (the axe's tax)
	public float EffectiveReach => _reach + _thrustDistance;            // how far the tip lands at full lunge
	public float HitboxLength => _hitboxShape?.Shape is BoxShape3D b ? b.Size.Z : 0f;
	public static int WeaponCount => System.Enum.GetValues<WeaponType>().Length;

	private Node3D _swordPivot;
	private Area3D _hitbox;
	private CollisionShape3D _hitboxShape;

	// Bodies already reported this swing, so each gets one print per swing.
	private readonly HashSet<Node3D> _hitThisSwing = new();

	private bool _swinging;
	private float _swingTimer;    // counts up 0..SwingDuration while swinging
	private float _cooldownTimer; // counts down; > 0 blocks a new swing

	// The captain's OWN ground velocity, kept separate from the knockback shove so a bumper/
	// sword fling doesn't feed back into itself frame to frame (we add KnockbackVelocity in
	// only at the MoveAndSlide). The captain is now susceptible to shoves like everyone else.
	private Vector3 _moveVel = Vector3.Zero;

	// --- Mount (M10, Chunk 28) ---
	// The captain can climb onto a nearby Mount (Donkey, later Chocobo) to ride faster. While
	// mounted the player keeps its full move/aim/attack pipeline — only the top Speed changes and
	// the body is lifted onto the mount's back; the Mount mirrors us underneath as one silhouette.
	[Export] public float MountRange = 3.0f;   // how close a mount must be to climb on (the mount's own range can extend this)
	private Mount _mount;                        // current mount, or null on foot
	private float _footSpeed;                    // top Speed on foot, restored on dismount
	private float _footY;                        // ground Y we stand at on foot, restored on dismount
	public bool IsMounted => _mount != null;
	public Mount CurrentMount => _mount;

	public override void _Ready()
	{
		base._Ready();   // init Health from MaxHealth
		AddToGroup("player");   // allies find their formation anchor through this group

		_swordPivot = GetNode<Node3D>("SwordPivot");
		_hitbox = GetNode<Area3D>("SwordPivot/Hitbox");
		_hitboxShape = GetNode<CollisionShape3D>("SwordPivot/Hitbox/CollisionShape3D");

		// Build the weapon table from the Inspector exports, and find each weapon's mesh node
		// (missing ones are fine — GetNodeOrNull keeps a weapon usable even without its mesh).
		_profiles = new Dictionary<WeaponType, WeaponProfile>
		{
			[WeaponType.Spear] = new WeaponProfile(SpearDamage, SpearKnockback, SpearReach, SpearThrustDistance, SpearSwingDuration, SpearSwingCooldown),
			[WeaponType.Sword] = new WeaponProfile(SwordDamage, SwordKnockback, SwordReach, SwordThrustDistance, SwordSwingDuration, SwordSwingCooldown),
			[WeaponType.Axe]   = new WeaponProfile(AxeDamage,   AxeKnockback,   AxeReach,   AxeThrustDistance,   AxeSwingDuration,   AxeSwingCooldown),
			[WeaponType.Mace]  = new WeaponProfile(MaceDamage,  MaceKnockback,  MaceReach,  MaceThrustDistance,  MaceSwingDuration,  MaceSwingCooldown),
		};
		_weaponMeshes = new Dictionary<WeaponType, Node3D>
		{
			[WeaponType.Spear] = GetNodeOrNull<Node3D>("SwordPivot/Spear"),
			[WeaponType.Sword] = GetNodeOrNull<Node3D>("SwordPivot/SwordMesh"),
			[WeaponType.Axe]   = GetNodeOrNull<Node3D>("SwordPivot/AxeMesh"),
			[WeaponType.Mace]  = GetNodeOrNull<Node3D>("SwordPivot/MaceMesh"),
		};

		// Own the hitbox box so resizing it for a weapon never mutates a shared resource.
		if (_hitboxShape.Shape is BoxShape3D box)
			_hitboxShape.Shape = (BoxShape3D)box.Duplicate();

		// The weapon points straight forward (-Z) at rest; the thrust slides it out and back.
		_swordPivot.Rotation = Vector3.Zero;
		ApplyWeapon(StartingWeapon);   // sets reach/damage/knockback/feel + shows the right mesh
		SetThrustOffset(0f);
		SetHitboxActive(false);
	}

	// Cycle to the next weapon in enum order (bound to `swap_weapon`), wrapping around.
	public void SwapWeapon()
	{
		ApplyWeapon((WeaponType)(((int)_weapon + 1) % WeaponCount));
		GD.Print($"[Player] swapped to {_weapon} (reach {_reach:0.0} m, knockback {_knockback:0.0}, cooldown {_swingCooldown:0.00}s)");
	}

	// Jump straight to a specific weapon (per-level default / tests).
	public void SetWeapon(WeaponType weapon) => ApplyWeapon(weapon);

	// --- Mount (Chunk 28) ---

	// `mount` toggles: off a mount if riding one, otherwise climb onto the nearest in range.
	public void ToggleMount()
	{
		if (_mount != null)
			Dismount();
		else
			TryMount();
	}

	// Climb onto the nearest unridden mount within range. Returns true if we mounted. Public so the
	// headless test can drive it without faking input. Riding raises our top Speed to the mount's,
	// lifts us onto its back, and hands the mount to us as our carried silhouette.
	public bool TryMount()
	{
		if (_mount != null)
			return false;

		Mount best = null;
		float bestSq = float.MaxValue;
		foreach (Node n in GetTree().GetNodesInGroup("mounts"))
		{
			if (n is not Mount m || m.IsRidden)
				continue;
			float range = Mathf.Max(MountRange, m.MountRange);
			float dSq = GlobalPosition.DistanceSquaredTo(m.GlobalPosition);
			if (dSq <= range * range && dSq < bestSq)
			{
				bestSq = dSq;
				best = m;
			}
		}
		if (best == null)
			return false;

		_mount = best;
		_footSpeed = Speed;
		_footY = GlobalPosition.Y;
		Speed = best.MountSpeed;

		// Hop onto the mount: snap to its spot, lifted one RiderHeight up. From here the Mount
		// mirrors our position/yaw each frame, so we ride as one.
		GlobalPosition = new Vector3(best.GlobalPosition.X, best.GlobalPosition.Y + best.RiderHeight, best.GlobalPosition.Z);
		best.OnMounted(this);

		GD.Print($"[Player] mounted {best.Name} (speed {_footSpeed:0.0} -> {Speed:0.0})");
		return true;
	}

	// Climb off: drop back to foot speed and ground height, a step to the side of the mount.
	public void Dismount()
	{
		if (_mount == null)
			return;

		Mount m = _mount;
		_mount = null;
		Speed = _footSpeed;
		m.OnDismounted();

		// Step off to our left so we don't stand inside the steed; back down to ground height.
		Vector3 side = GlobalTransform.Basis.X.Normalized();
		Vector3 p = m.GlobalPosition + side * Mathf.Max(1.0f, m.MountRange * 0.5f);
		p.Y = _footY;
		GlobalPosition = p;

		GD.Print($"[Player] dismounted {m.Name} (speed -> {Speed:0.0})");
	}

	// Broadcast a standing order to every ally in the "allies" group (M7, Chunk 44).
	private void CommandAllies(Ally.Command cmd)
	{
		int n = 0;
		foreach (Node node in GetTree().GetNodesInGroup("allies"))
			if (node is Ally a)
			{
				a.SetCommand(cmd);
				n++;
			}
		GD.Print($"[Player] ordered {n} ally(s): {cmd}");
	}

	// Load a weapon's profile into the active fields, show its mesh, and resize the thrust
	// hitbox so its forward length matches the weapon's reach (the box grows out along -Z from
	// the pivot). Called on spawn and on every swap.
	private void ApplyWeapon(WeaponType weapon)
	{
		_weapon = weapon;
		WeaponProfile p = _profiles[weapon];

		_damage         = p.Damage;
		_knockback      = p.Knockback;
		_reach          = p.Reach;
		_thrustDistance = p.ThrustDistance;
		_swingDuration  = p.SwingDuration;
		_swingCooldown  = p.SwingCooldown;

		// Show only the active weapon's mesh.
		foreach (var (type, mesh) in _weaponMeshes)
			if (mesh != null)
				mesh.Visible = type == weapon;

		// Box spans 0..-_reach along the pivot's forward axis, so position it at -_reach/2.
		if (_hitboxShape?.Shape is BoxShape3D box)
		{
			Vector3 size = box.Size;
			size.Z = _reach;
			box.Size = size;

			Vector3 pos = _hitboxShape.Position;
			pos.Z = -_reach * 0.5f;
			_hitboxShape.Position = pos;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		DecayKnockback(dt);

		if (IsDead)
		{
			// Game over: ignore input/aim, just bleed off momentum in place.
			_moveVel = _moveVel.MoveToward(Vector3.Zero, Friction * dt);
			Velocity = _moveVel + KnockbackVelocity;
			MoveAndSlide();
			return;
		}

		// GetVector auto-normalizes, so diagonals aren't faster.
		// Screen-up (W) maps to -Z (away from the camera).
		Vector2 input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
		Vector3 direction = new Vector3(input.X, 0f, input.Y);

		// Keep tracking intended input velocity every frame (no special "shoved" branch). When a
		// hard shove is active its authority is scaled out below, but _moveVel stays current so
		// control returns smoothly as the shove decays — no snap.
		Vector3 target = direction * Speed;
		_moveVel = direction != Vector3.Zero
			? _moveVel.MoveToward(target, Acceleration * dt)
			: _moveVel.MoveToward(Vector3.Zero, Friction * dt);

		// Flat ground for now — no gravity/vertical motion. A bumper / chained pinball hit reads as
		// one clean launch: while the shove is strong OwnMovementScale suppresses steering, then
		// eases it back in PROPORTIONALLY as the shove decays (no hard threshold snap that read as
		// a second bump). Add in the lingering shove only here.
		float own = OwnMovementScale;
		Velocity = new Vector3(_moveVel.X * own, 0f, _moveVel.Z * own) + KnockbackVelocity;
		MoveAndSlide();
		ResolveKnockbackBounce();   // captain bounces off walls/bumpers/units like everyone else

		AimAtMouse(dt);

		// Climb onto / off a nearby mount (read once here so it never double-fires across mounts).
		if (Input.IsActionJustPressed("mount"))
			ToggleMount();

		// Re-task the squad (M7, Chunk 44): Hold plants them where they stand, Follow resumes
		// the loose-leash formation. Broadcast to every ally so the whole wall obeys at once.
		if (Input.IsActionJustPressed("cmd_hold"))
			CommandAllies(Ally.Command.Hold);
		else if (Input.IsActionJustPressed("cmd_follow"))
			CommandAllies(Ally.Command.Follow);

		// Swap weapons between thrusts (not mid-swing, so the hitbox never resizes live).
		if (!_swinging && Input.IsActionJustPressed("swap_weapon"))
			SwapWeapon();

		UpdateSwing(dt);
	}

	// Drive the thrust state machine: trigger on attack, jab the pike straight out
	// along our facing and retract it over a timed window, keep the hitbox live during
	// the lunge, then cool down.
	private void UpdateSwing(float dt)
	{
		if (_cooldownTimer > 0f)
			_cooldownTimer -= dt;

		if (!_swinging && _cooldownTimer <= 0f && Input.IsActionJustPressed("attack"))
			StartSwing();


		if (!_swinging)
		{
			// Idle / post-thrust: ease the pike back to its rest (fully retracted) pose.
			float currentOffset = -_swordPivot.Position.Z;
			float t2 = 1f - Mathf.Exp(-SwordReturnLerp * dt);
			SetThrustOffset(Mathf.Lerp(currentOffset, 0f, t2));
			return;
		}

		_swingTimer += dt;
		float t = Mathf.Clamp(_swingTimer / _swingDuration, 0f, 1f);
		// Jab out quickly over the first ThrustExtendFrac of the window, then retract
		// over the remainder — a snappy poke, not a wide sweep.
		float extend = Mathf.Clamp(ThrustExtendFrac, 0.05f, 0.95f);
		float reach = t < extend
			? t / extend                       // 0 -> 1 (lunging out)
			: 1f - (t - extend) / (1f - extend); // 1 -> 0 (pulling back)
		SetThrustOffset(reach * _thrustDistance);

		// Poll overlaps each frame — bodies can enter as the pike extends.
		// Each body is struck at most once per thrust; only damage enemy-team Units.
		foreach (Node3D body in _hitbox.GetOverlappingBodies())
		{
			if (body == this || !_hitThisSwing.Add(body))
				continue;

			if (body is Unit unit && unit.Team != Team)
			{
				Vector3 hitDir = unit.GlobalPosition - GlobalPosition;
				float dmg = ScaledWeaponDamage(_damage);       // Strength scales weapon attack power (Chunk 36)
				unit.TakeDamage(dmg, hitDir, _knockback);      // sword shoves; spear deals no knockback
				GD.Print($"[{_weapon}] {Name} thrust {unit.Name} for {dmg:0.0} (knockback {_knockback})");
			}
		}

		if (_swingTimer >= _swingDuration)
			EndSwing();
	}

	private void StartSwing()
	{
		_swinging = true;
		_swingTimer = 0f;
		_hitThisSwing.Clear();
		SetHitboxActive(true);
	}

	private void EndSwing()
	{
		_swinging = false;
		_cooldownTimer = _swingCooldown;
		SetHitboxActive(false);
		// Offset is ~0 where the thrust ended (fully retracted); the idle branch eases out any residual.
	}

	// Slide the whole pike (mesh + hitbox) forward along our local -Z by `offset` metres.
	private void SetThrustOffset(float offset)
	{
		Vector3 pos = _swordPivot.Position;
		pos.Z = -offset;
		_swordPivot.Position = pos;
	}

	private void SetHitboxActive(bool active)
	{
		_hitbox.Monitoring = active;
		_hitboxShape.Disabled = !active;
	}

	// The player doesn't vanish on death — it freezes and flips on the game-over UI
	// (any CanvasItem in the "game_over" group). Restart comes later (Chunk 9).
	protected override void OnDeath()
	{
		Velocity = Vector3.Zero;
		SetHitboxActive(false);
		foreach (Node n in GetTree().GetNodesInGroup("game_over"))
			if (n is CanvasItem ci)
				ci.Visible = true;
		GD.Print("[Player] GAME OVER");
	}

	// Rotate the player toward the mouse cursor. We shoot a ray from the camera
	// through the cursor and intersect it with the horizontal plane at the player's
	// height, then turn toward that point (forward is -Z). The turn is RATE-LIMITED
	// to TurnSpeed (deg/s) rather than snapping with LookAt: a real captain can't
	// pivot instantly, and because the phalanx's slots rotate with our facing, an
	// instant snap would whip the whole wall around in a single frame.
	private void AimAtMouse(float dt)
	{
		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null)
			return;

		Vector2 mousePos = GetViewport().GetMousePosition();
		Vector3 from = cam.ProjectRayOrigin(mousePos);
		Vector3 dir = cam.ProjectRayNormal(mousePos);

		if (Mathf.IsZeroApprox(dir.Y))   // ray parallel to ground — no hit
			return;

		float t = (GlobalPosition.Y - from.Y) / dir.Y;
		if (t < 0f)                      // plane is behind the camera
			return;

		Vector3 hit = from + dir * t;
		Vector3 target = new Vector3(hit.X, GlobalPosition.Y, hit.Z);

		// Skip degenerate aim when the cursor is right on top of us.
		if (GlobalPosition.DistanceSquaredTo(target) < 0.0025f)
			return;

		// Desired yaw toward the cursor (forward is -Z, so negate the delta).
		Vector3 toTarget = target - GlobalPosition;
		float desiredYaw = Mathf.Atan2(-toTarget.X, -toTarget.Z);

		// Step toward it by at most TurnSpeed*dt this frame, taking the shortest way around.
		float maxStep = Mathf.DegToRad(TurnSpeed) * dt;
		float diff = Mathf.Wrap(desiredYaw - Rotation.Y, -Mathf.Pi, Mathf.Pi);
		diff = Mathf.Clamp(diff, -maxStep, maxStep);

		Vector3 rot = Rotation;
		rot.Y += diff;
		Rotation = rot;
	}
}
