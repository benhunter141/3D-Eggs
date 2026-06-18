using Godot;

// Pure input math for the captain's control schemes (M12.7, Chunk 44). Kept Godot-type-light
// (just Vector2 + Mathf) and side-effect-free so it's headless-testable without any nodes/Input.
//
// The two-player couch level (M12.7) gives each captain its own control scheme: P1 on
// keyboard+mouse, P2 on a gamepad whose RIGHT STICK aims (no mouse). This helper converts that
// right-stick vector into a desired world yaw so the gamepad captain turns exactly the way the
// mouse captain does — the rate-limited turn toward the yaw lives back in Player.
public static class CaptainInput
{
	// Convert a right-stick vector (screen-space: +X right, +Y down) to a desired world yaw.
	// Returns false (and leaves yaw untouched) while the stick sits inside the deadzone, so a
	// resting stick doesn't fight the captain's facing.
	//
	// The mapping mirrors BOTH conventions already in Player: WASD/left-stick map screen-up to
	// −Z (away from camera), and mouse-aim resolves a target to yaw = Atan2(−dirX, −dirZ) with
	// forward = −Z, yaw 0. Treating the stick like a screen-space direction (sx → +X, sy → +Z)
	// gives the identical formula, so pushing the stick up aims the captain away from the camera
	// and pushing it right aims east — consistent with how the cursor would.
	public static bool TryStickToYaw(Vector2 stick, float deadzone, out float yaw)
	{
		yaw = 0f;
		if (stick.LengthSquared() < deadzone * deadzone)
			return false;
		yaw = Mathf.Atan2(-stick.X, -stick.Y);
		return true;
	}

	// Apply a radial deadzone to a movement stick and rescale the live region to 0..1 so motion
	// eases in just past the deadzone rather than snapping to a floor speed. Returns zero inside
	// the deadzone. Pure — used for the gamepad captain's left-stick move.
	public static Vector2 DeadzoneStick(Vector2 stick, float deadzone)
	{
		float len = stick.Length();
		if (len < deadzone)
			return Vector2.Zero;
		float scaled = Mathf.Clamp((len - deadzone) / (1f - deadzone), 0f, 1f);
		return stick / len * scaled;
	}
}
