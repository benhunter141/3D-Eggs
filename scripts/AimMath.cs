using Godot;

// Pure aim math shared by the twin-stick captain (M12.7, Chunk 44). Kept Godot-light (only
// Vector2 / Mathf) and side-effect-free so the gamepad-aim conversion is headless-testable.
//
// The right stick aims like the mouse: its 2-D vector maps to a ground heading and resolves to
// a desired YAW. Screen-up on the stick (Godot reports the Y axis NEGATIVE when pushed up) aims
// AWAY from the camera (−Z, forward = yaw 0), matching the move mapping new Vector3(x, 0, y).
// The captain then turns toward that yaw, rate-limited exactly like the mouse aim.
public static class AimMath
{
	// Right-stick vector → desired yaw (radians). Forward is −Z, so a stick of (0, −1) → yaw 0.
	// Mirrors AimAtMouse's atan2(-dir.X, -dir.Z) with dir = (stick.X, 0, stick.Y).
	public static float StickToYaw(Vector2 stick) => Mathf.Atan2(-stick.X, -stick.Y);
}
