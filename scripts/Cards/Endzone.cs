using Godot;

// Endzone bounds for the football-pitch card mode (M12.5, Chunk 41). A rectangle on the ground
// plane (XZ): x ∈ [−HalfWidth, HalfWidth], z ∈ [FarZ, NearZ]. Unit cards may only be deployed
// inside the player's endzone, so the placement click is validated against this rectangle.
//
// Pure value type — the only Godot dependency is Vector3 for the convenience overload — so the
// Contains test is fully headless-testable. CardBattle builds one from its exported field bounds.
public readonly struct Endzone
{
	public readonly float HalfWidth;   // X extent (±) of the pitch
	public readonly float FarZ;        // inner (−Z) edge
	public readonly float NearZ;       // near (+Z) edge (toward the camera)

	public Endzone(float halfWidth, float farZ, float nearZ)
	{
		HalfWidth = halfWidth;
		// Tolerate the bounds being passed in either order so callers don't have to sort them.
		FarZ = Mathf.Min(farZ, nearZ);
		NearZ = Mathf.Max(farZ, nearZ);
	}

	// Is this ground point inside the endzone rectangle? Only X and Z matter (Y ignored).
	public bool Contains(float x, float z) =>
		x >= -HalfWidth && x <= HalfWidth && z >= FarZ && z <= NearZ;

	public bool Contains(Vector3 point) => Contains(point.X, point.Z);
}
