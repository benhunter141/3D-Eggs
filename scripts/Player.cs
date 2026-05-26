using Godot;
using System.Collections.Generic;

// The player capsule. Twin-stick movement on the flat XZ plane.
// WASD (or left stick) moves; mouse aims; left-click swings the sword.
// Extends Unit so it shares the team/health/damage pipeline with allies and enemies.
public partial class Player : Unit
{
	// --- Tunable feel knobs (editable in the Inspector too) ---
	[Export] public float Speed = 8.0f;         // top movement speed (m/s)
	[Export] public float Acceleration = 60.0f; // how fast we ramp up to Speed
	[Export] public float Friction = 50.0f;     // how fast we slow to a stop

	// --- Sword swing ---
	[Export] public float SwordDamage = 40.0f;       // damage per enemy hit per swing
	[Export] public float SwordKnockback = 10.0f;    // shove speed (m/s) flung along the hit direction
	[Export] public float SwingArcDegrees = 150.0f;  // total right-to-left sweep
	[Export] public float SwingDuration = 0.2f;      // seconds the blade is sweeping
	[Export] public float SwingCooldown = 0.35f;     // delay after a swing before the next
	[Export] public float SwordRestDegrees = -55.0f; // idle angle (negative = player's right side)
	[Export] public float SwordReturnLerp = 7.0f;    // how fast the blade eases back to rest

	private Node3D _swordPivot;
	private Area3D _hitbox;
	private CollisionShape3D _hitboxShape;

	// Bodies already reported this swing, so each gets one print per swing.
	private readonly HashSet<Node3D> _hitThisSwing = new();

	private bool _swinging;
	private float _swingTimer;    // counts up 0..SwingDuration while swinging
	private float _cooldownTimer; // counts down; > 0 blocks a new swing

	public override void _Ready()
	{
		base._Ready();   // init Health from MaxHealth
		AddToGroup("player");   // allies find their formation anchor through this group

		_swordPivot = GetNode<Node3D>("SwordPivot");
		_hitbox = GetNode<Area3D>("SwordPivot/Hitbox");
		_hitboxShape = GetNode<CollisionShape3D>("SwordPivot/Hitbox/CollisionShape3D");

		SetSwordAngle(SwordRestDegrees);
		SetHitboxActive(false);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		if (IsDead)
		{
			// Game over: ignore input/aim, just bleed off momentum in place.
			Vector3 stop = new Vector3(Velocity.X, 0f, Velocity.Z).MoveToward(Vector3.Zero, Friction * dt);
			Velocity = stop;
			MoveAndSlide();
			return;
		}

		// GetVector auto-normalizes, so diagonals aren't faster.
		// Screen-up (W) maps to -Z (away from the camera).
		Vector2 input = Input.GetVector("move_left", "move_right", "move_up", "move_down");
		Vector3 direction = new Vector3(input.X, 0f, input.Y);

		Vector3 horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);
		Vector3 target = direction * Speed;

		horizontal = direction != Vector3.Zero
			? horizontal.MoveToward(target, Acceleration * dt)
			: horizontal.MoveToward(Vector3.Zero, Friction * dt);

		// Flat ground for now — no gravity/vertical motion.
		Velocity = new Vector3(horizontal.X, 0f, horizontal.Z);
		MoveAndSlide();

		AimAtMouse();
		UpdateSwing(dt);
	}

	// Drive the swing state machine: trigger on attack, sweep the blade across a
	// timed arc, keep the hitbox live during the sweep, then cool down.
	private void UpdateSwing(float dt)
	{
		if (_cooldownTimer > 0f)
			_cooldownTimer -= dt;

		if (!_swinging && _cooldownTimer <= 0f && Input.IsActionJustPressed("attack"))
			StartSwing();

		if (!_swinging)
		{
			// Idle / post-swing: ease the blade back to its rest pose on the right.
			float current = _swordPivot.RotationDegrees.Y;
			float t2 = 1f - Mathf.Exp(-SwordReturnLerp * dt);
			SetSwordAngle(Mathf.Lerp(current, SwordRestDegrees, t2));
			return;
		}

		_swingTimer += dt;
		float t = Mathf.Clamp(_swingTimer / SwingDuration, 0f, 1f);
		float half = SwingArcDegrees * 0.5f;
		SetSwordAngle(Mathf.Lerp(-half, half, t)); // sweep from the right (-) to the left (+)

		// Poll overlaps each frame — bodies can enter mid-sweep as the box rotates.
		// Each body is struck at most once per swing; only damage enemy-team Units.
		foreach (Node3D body in _hitbox.GetOverlappingBodies())
		{
			if (body == this || !_hitThisSwing.Add(body))
				continue;

			if (body is Unit unit && unit.Team != Team)
			{
				Vector3 hitDir = unit.GlobalPosition - GlobalPosition;
				unit.TakeDamage(SwordDamage, hitDir, SwordKnockback);  // shove flings them away
				GD.Print($"[Sword] {Name} hit {unit.Name} for {SwordDamage} (knockback {SwordKnockback})");
			}
		}

		if (_swingTimer >= SwingDuration)
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
		_cooldownTimer = SwingCooldown;
		SetHitboxActive(false);
		// Angle is left where the swing ended (far left); the idle branch eases it home.
	}

	private void SetSwordAngle(float degrees)
	{
		Vector3 rot = _swordPivot.RotationDegrees;
		rot.Y = degrees;
		_swordPivot.RotationDegrees = rot;
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

	// Rotate the player to face the mouse cursor. We shoot a ray from the camera
	// through the cursor and intersect it with the horizontal plane at the player's
	// height, then LookAt that point (the player's forward is -Z).
	private void AimAtMouse()
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

		// Skip degenerate look-at when the cursor is right on top of us.
		if (GlobalPosition.DistanceSquaredTo(target) < 0.0025f)
			return;

		LookAt(target, Vector3.Up);
	}
}
