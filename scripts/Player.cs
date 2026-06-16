using Godot;
using System.Collections.Generic;

// The player capsule. Twin-stick movement on the flat XZ plane.
// WASD (or left stick) moves; mouse aims; left-click THRUSTS the held weapon straight ahead.
// Extends Unit so it shares the team/health/damage pipeline with allies and enemies.
//
// Weapon swap (M9, Chunk 26): the captain carries one of two weapons, toggled live with
// `swap_weapon` (Q / gamepad). Each weapon is a profile that drives the hitbox REACH, the
// per-hit DAMAGE + KNOCKBACK, and the thrust/cooldown feel, plus which mesh is shown:
//   • Spear — LONG reach, NO knockback: a phalanx poker that out-ranges its foe.
//   • Sword — SHORT reach, strong KNOCKBACK: the classic sword that flings what it hits.
// The active values are copied into runtime fields by ApplyWeapon so the swing code reads
// one set of numbers regardless of which weapon is up.
public partial class Player : Unit
{
	public enum WeaponType { Spear, Sword }

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

	// Read-only views (used by the headless weapon-swap test).
	public WeaponType CurrentWeapon => _weapon;
	public float CurrentDamage => _damage;
	public float CurrentWeaponKnockback => _knockback;   // NOTE: Unit.CurrentKnockback is the live shove Vector3
	public float CurrentReach => _reach;
	public float EffectiveReach => _reach + _thrustDistance;            // how far the tip lands at full lunge
	public float HitboxLength => _hitboxShape?.Shape is BoxShape3D b ? b.Size.Z : 0f;

	private Node3D _swordPivot;
	private Node3D _spearMesh;
	private Node3D _swordMesh;
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

	public override void _Ready()
	{
		base._Ready();   // init Health from MaxHealth
		AddToGroup("player");   // allies find their formation anchor through this group

		_swordPivot = GetNode<Node3D>("SwordPivot");
		_spearMesh = GetNodeOrNull<Node3D>("SwordPivot/Spear");
		_swordMesh = GetNodeOrNull<Node3D>("SwordPivot/SwordMesh");
		_hitbox = GetNode<Area3D>("SwordPivot/Hitbox");
		_hitboxShape = GetNode<CollisionShape3D>("SwordPivot/Hitbox/CollisionShape3D");

		// Own the hitbox box so resizing it for a weapon never mutates a shared resource.
		if (_hitboxShape.Shape is BoxShape3D box)
			_hitboxShape.Shape = (BoxShape3D)box.Duplicate();

		// The weapon points straight forward (-Z) at rest; the thrust slides it out and back.
		_swordPivot.Rotation = Vector3.Zero;
		ApplyWeapon(StartingWeapon);   // sets reach/damage/knockback/feel + shows the right mesh
		SetThrustOffset(0f);
		SetHitboxActive(false);
	}

	// Toggle between the two weapons (bound to `swap_weapon`).
	public void SwapWeapon()
	{
		ApplyWeapon(_weapon == WeaponType.Spear ? WeaponType.Sword : WeaponType.Spear);
		GD.Print($"[Player] swapped to {_weapon} (reach {_reach:0.0} m, knockback {_knockback:0.0})");
	}

	// Load a weapon's profile into the active fields, show its mesh, and resize the thrust
	// hitbox so its forward length matches the weapon's reach (the box grows out along -Z from
	// the pivot). Called on spawn and on every swap.
	private void ApplyWeapon(WeaponType weapon)
	{
		_weapon = weapon;
		bool sword = weapon == WeaponType.Sword;

		_damage         = sword ? SwordDamage : SpearDamage;
		_knockback      = sword ? SwordKnockback : SpearKnockback;
		_reach          = sword ? SwordReach : SpearReach;
		_thrustDistance = sword ? SwordThrustDistance : SpearThrustDistance;
		_swingDuration  = sword ? SwordSwingDuration : SpearSwingDuration;
		_swingCooldown  = sword ? SwordSwingCooldown : SpearSwingCooldown;

		if (_spearMesh != null) _spearMesh.Visible = !sword;
		if (_swordMesh != null) _swordMesh.Visible = sword;

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
				unit.TakeDamage(_damage, hitDir, _knockback);  // sword shoves; spear deals no knockback
				GD.Print($"[{_weapon}] {Name} thrust {unit.Name} for {_damage} (knockback {_knockback})");
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
