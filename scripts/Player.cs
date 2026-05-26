using Godot;

// The player capsule. Twin-stick movement on the flat XZ plane.
// WASD (or left stick) moves; accel/friction give it a tunable "feel".
public partial class Player : CharacterBody3D
{
	// --- Tunable feel knobs (editable in the Inspector too) ---
	[Export] public float Speed = 8.0f;         // top movement speed (m/s)
	[Export] public float Acceleration = 60.0f; // how fast we ramp up to Speed
	[Export] public float Friction = 50.0f;     // how fast we slow to a stop

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

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
	}
}
