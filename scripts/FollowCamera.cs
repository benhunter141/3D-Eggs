using Godot;
using System.Collections.Generic;

// Top-down-ish follow camera for twin-stick. It tracks the target's POSITION
// at a fixed offset/angle and always aims at the target, so the player stays
// centered but the world does NOT spin when the player rotates to aim — movement
// stays screen-relative.
//
// Optional DYNAMIC ZOOM: in chaotic levels (pinball) units get flung far past any
// sensible FIXED zoom and leave the frame. When DynamicZoom is on, the camera keeps
// the target centered (so controls stay intuitive) but slides its distance along the
// SAME offset direction to fit the farthest tracked unit — zooming out when the fight
// spreads, back in when it tightens.
public partial class FollowCamera : Camera3D
{
	[Export] public Node3D Target;                            // the player to follow
	[Export] public Vector3 Offset = new Vector3(0, 13, 14);  // up & back from target; also sets the view ANGLE
	[Export] public float FollowLerp = 12.0f;                 // higher = snappier position follow
	[Export] public float FieldOfView = 80.0f;                // wider = more ground visible (but >85 starts to stretch the edges)

	[ExportGroup("Dynamic Zoom")]
	[Export] public bool DynamicZoom = false;                 // off = classic fixed offset (uses Offset as-is)
	[Export] public float MinDistance = 28.0f;                // closest the camera zooms in (tight fight)
	[Export] public float MaxDistance = 58.0f;                // farthest it zooms out (units flung wide)
	[Export] public float FitScale = 1.5f;                    // camera distance per unit of spread — bigger = zoom out sooner
	[Export] public float ZoomMargin = 10.0f;                 // extra distance so the farthest unit isn't right on the edge
	[Export] public float ZoomLerp = 3.5f;                    // how fast the zoom eases (lower = smoother/laggier)
	[Export] public float MaxTrackRadius = 40.0f;             // ignore units farther than this so one escapee can't max-zoom

	private Vector3 _offsetDir;     // normalized Offset — the fixed view angle
	private float _currentDistance; // smoothed camera distance along _offsetDir

	public override void _Ready()
	{
		Fov = FieldOfView;
		_offsetDir = Offset.Length() > 0.001f ? Offset.Normalized() : Vector3.Up;
		_currentDistance = Offset.Length();

		// Robust target acquisition: if the scene's NodePath didn't resolve, fall back to
		// the player group (the captain joins "player" in Player._Ready). Without a Target
		// the camera sits frozen at its authored transform — the bug behind "view doesn't move".
		if (Target == null)
			Target = GetTree().GetFirstNodeInGroup("player") as Node3D;

		GD.Print($"[FollowCamera] Ready. Target = {(Target == null ? "NULL (camera will not follow!)" : Target.Name)}, current = {Current}");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Target == null)
			return;

		// Decide how far back to sit this frame.
		float distance;
		if (DynamicZoom)
		{
			float spread = FarthestTrackedUnit(Target.GlobalPosition);
			float desired = Mathf.Clamp(spread * FitScale + ZoomMargin, MinDistance, MaxDistance);
			float zt = 1f - Mathf.Exp(-ZoomLerp * (float)delta);
			_currentDistance = Mathf.Lerp(_currentDistance, desired, zt);
			distance = _currentDistance;
		}
		else
		{
			distance = Offset.Length();
		}

		Vector3 desiredPos = Target.GlobalPosition + _offsetDir * distance;
		// Frame-rate-independent smoothing toward the desired spot.
		float t = 1f - Mathf.Exp(-FollowLerp * (float)delta);
		GlobalPosition = GlobalPosition.Lerp(desiredPos, t);

		// Always aim at the target — keeps it centered for any distance/angle.
		LookAt(Target.GlobalPosition, Vector3.Up);
	}

	// Distance from `center` to the farthest LIVING unit within MaxTrackRadius (0 if none).
	// Walks both team buckets once — single scanner, so the per-frame group-scan cost the
	// UnitRegistry was built to avoid doesn't apply here.
	private float FarthestTrackedUnit(Vector3 center)
	{
		float capSq = MaxTrackRadius * MaxTrackRadius;
		float maxSq = 0f;
		AccumulateFarthest(UnitRegistry.OnTeam(Unit.TeamId.Player), center, capSq, ref maxSq);
		AccumulateFarthest(UnitRegistry.OnTeam(Unit.TeamId.Enemy), center, capSq, ref maxSq);
		return Mathf.Sqrt(maxSq);
	}

	private static void AccumulateFarthest(IReadOnlyList<Unit> units, Vector3 center, float capSq, ref float maxSq)
	{
		for (int i = 0; i < units.Count; i++)
		{
			Unit u = units[i];
			if (u == null || !GodotObject.IsInstanceValid(u) || u.IsDead)
				continue;
			float d = center.DistanceSquaredTo(u.GlobalPosition);
			if (d <= capSq && d > maxSq)
				maxSq = d;
		}
	}
}
