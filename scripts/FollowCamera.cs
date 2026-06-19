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
//
// LIVE PLAYER BIAS (Chunk 23): on top of whatever the mode picks, the player nudges a
// persistent ZoomBias (metres) live with the mouse wheel / zoom_in / zoom_out — pull the
// camera out or push it in WITHOUT turning auto-zoom off. The bias is added to the base
// distance and the result is clamped, so in DynamicZoom mode the auto framing keeps
// working, just shifted by the player's preference.
// TWO-TARGET MODE (Chunk 46, M12.7): for couch co-op the camera frames BOTH captains. Set
// Target2 and the camera centres on the MIDPOINT of the two and grows its distance to keep
// both (plus the crowd spread) in shot, reusing the dynamic-zoom fit — half the captains'
// separation feeds the same framing spread as a flung unit. Leaving Target2 null is the
// classic single-target path, byte-for-byte unchanged, so every other level is untouched.
public partial class FollowCamera : Camera3D
{
	[Export] public Node3D Target;                            // the player to follow
	[Export] public Node3D Target2;                           // optional 2nd captain (co-op); null = single-target
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

	[ExportGroup("Player Zoom Bias")]
	[Export] public float ZoomStep = 3.0f;                    // metres added/removed per wheel notch / key press
	[Export] public float ZoomBiasMin = -24.0f;              // most the player can pull IN  (negative = closer)
	[Export] public float ZoomBiasMax = 24.0f;               // most the player can push OUT (positive = farther)
	[Export] public float MinFixedDistance = 6.0f;           // hard floor in non-dynamic levels so a hard zoom-in can't pass through the player

	// Player's live zoom preference in metres, clamped to [ZoomBiasMin, ZoomBiasMax].
	// Negative pulls the camera in, positive pushes it out. Persists across frames.
	public float ZoomBias { get; private set; } = 0f;

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

		// Live player bias: one step per wheel notch / key press / gamepad press. The
		// wheel naturally pulses (pressed + released same frame), so JustPressed reads it.
		if (Input.IsActionJustPressed("zoom_in"))
			StepZoom(-1f);
		if (Input.IsActionJustPressed("zoom_out"))
			StepZoom(+1f);

		// Centre on the focus point: the single target, or the midpoint of both captains in co-op.
		Vector3 focus = FocusPoint();

		// Decide how far back to sit this frame.
		float distance;
		if (DynamicZoom)
		{
			float spread = FramingSpread(focus);
			float zt = 1f - Mathf.Exp(-ZoomLerp * (float)delta);
			_currentDistance = Mathf.Lerp(_currentDistance, DesiredDistance(spread), zt);
			distance = _currentDistance;
		}
		else
		{
			// Fixed angle/zoom level, plus the player's live bias. Position lerp smooths the step.
			distance = DesiredDistance(0f);
		}

		Vector3 desiredPos = focus + _offsetDir * distance;
		// Frame-rate-independent smoothing toward the desired spot.
		float t = 1f - Mathf.Exp(-FollowLerp * (float)delta);
		GlobalPosition = GlobalPosition.Lerp(desiredPos, t);

		// Always aim at the focus — keeps it centered for any distance/angle.
		LookAt(focus, Vector3.Up);
	}

	// The point the camera centres on: the lone Target, or the MIDPOINT of both captains when a
	// valid Target2 is set (co-op). Pure read of the targets' positions.
	public Vector3 FocusPoint()
	{
		if (HasTwoTargets())
			return (Target.GlobalPosition + Target2.GlobalPosition) * 0.5f;
		return Target.GlobalPosition;
	}

	// Half the distance between the two captains (0 in single-target mode). That's how far each
	// captain sits from the focus midpoint, so feeding it into the framing spread guarantees the
	// frame opens up enough to keep BOTH on screen as they split apart.
	public float HalfSeparation()
	{
		return HasTwoTargets()
			? Target.GlobalPosition.DistanceTo(Target2.GlobalPosition) * 0.5f
			: 0f;
	}

	private bool HasTwoTargets() =>
		Target != null && Target2 != null && IsInstanceValid(Target) && IsInstanceValid(Target2);

	// The spread that drives the dynamic-zoom distance this frame: the LARGER of the farthest
	// tracked unit from the focus and half the captains' separation — so in co-op the camera
	// pulls back to fit both captains even when no unit is flung wide. Single-target: just the units.
	public float FramingSpread(Vector3 focus) =>
		Mathf.Max(FarthestTrackedUnit(focus), HalfSeparation());

	// Nudge the live player bias by `direction` steps (negative = zoom IN, positive = OUT),
	// clamped to [ZoomBiasMin, ZoomBiasMax]. Pure state change — safe to call headless.
	public void StepZoom(float direction)
	{
		ZoomBias = Mathf.Clamp(ZoomBias + direction * ZoomStep, ZoomBiasMin, ZoomBiasMax);
	}

	// The target camera distance this frame (BEFORE the position/zoom smoothing), given the
	// crowd `spread` (ignored when DynamicZoom is off). Dynamic mode auto-frames the fight inside
	// [MinDistance, MaxDistance], THEN adds the player's live ZoomBias ON TOP — so the wheel can
	// pull CLOSER than MinDistance (the default rests at the current max-in: the tightest auto
	// frame) or push past MaxDistance, instead of the bias being swallowed by the clamp. The
	// MinFixedDistance floor still stops a hard zoom-in from passing through the player. Fixed mode
	// hangs the bias off the authored Offset length down to the same floor. Pure function of the
	// camera's exports + ZoomBias, so the headless test can drive it without a tree.
	public float DesiredDistance(float spread)
	{
		if (DynamicZoom)
		{
			float baseDist = spread * FitScale + ZoomMargin;
			float framed = Mathf.Clamp(baseDist, MinDistance, MaxDistance);
			return Mathf.Max(framed + ZoomBias, MinFixedDistance);
		}
		return Mathf.Max(Offset.Length() + ZoomBias, MinFixedDistance);
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
