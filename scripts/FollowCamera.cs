using Godot;

// Top-down-ish follow camera for twin-stick. It tracks the target's POSITION
// at a fixed offset but keeps its own authored angle, so when the player rotates
// to aim, the world does NOT spin — movement stays screen-relative.
public partial class FollowCamera : Camera3D
{
	[Export] public Node3D Target;                            // the player to follow
	[Export] public Vector3 Offset = new Vector3(0, 13, 14);  // up & back from target
	[Export] public float FollowLerp = 12.0f;                 // higher = snappier follow

	public override void _PhysicsProcess(double delta)
	{
		if (Target == null)
			return;

		Vector3 desired = Target.GlobalPosition + Offset;
		// Frame-rate-independent smoothing toward the desired spot.
		float t = 1f - Mathf.Exp(-FollowLerp * (float)delta);
		GlobalPosition = GlobalPosition.Lerp(desired, t);
		// Rotation is left exactly as authored in the scene (the 45° downward tilt).
	}
}
