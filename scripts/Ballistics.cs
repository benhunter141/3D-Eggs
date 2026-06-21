using Godot;

// Pure projectile-arc math (M14, Chunk 63: traversable terrain → ballistic projectiles).
//
// On flat levels stones/arrows fly dead level (direction flattened, no gravity). On grounded
// (terrain) levels a level shot misses any target up or down a slope, so the projectile instead
// LOBS in a gravity arc that passes through the target's REAL position (height included). This
// helper is the one piece of that — given launch + target points it returns the initial velocity
// — kept as plain math (no node state) so it's headless-testable.
public static class Ballistics
{
	// Default downward acceleration for lobbed projectiles (m/s²). Tuned heavier than real gravity
	// so arcs are snappy at battlefield scale; projectiles expose it as an export to override.
	public const float Gravity = 20.0f;

	// Initial velocity so a projectile launched from `from` reaches `to` under `gravity`, with its
	// HORIZONTAL speed ≈ `horizontalSpeed`. Flight time T = horizontalDistance / horizontalSpeed
	// (clamped to a small minimum so near-vertical shots still arc), then the vertical component is
	// solved to land exactly at the target's height after T: dy = vy·T − ½·g·T². The arc therefore
	// passes through `to` regardless of the up/downhill height difference.
	public static Vector3 SolveArcVelocity(Vector3 from, Vector3 to, float horizontalSpeed, float gravity)
	{
		Vector3 d = to - from;
		Vector3 flat = new Vector3(d.X, 0f, d.Z);
		float horiz = flat.Length();

		float t = horiz > 0.001f ? horiz / Mathf.Max(0.001f, horizontalSpeed) : 0.001f;
		t = Mathf.Max(t, 0.15f);   // floor so a straight-up shot doesn't divide by ~0

		Vector3 v = horiz > 0.001f ? flat / t : Vector3.Zero;          // horizontal component
		v.Y = (d.Y + 0.5f * gravity * t * t) / t;                      // vertical to hit dy at time t
		return v;
	}
}
