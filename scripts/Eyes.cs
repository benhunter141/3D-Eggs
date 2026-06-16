using Godot;

// Cartoony eyes (Chunk 24): a pair of forward-facing eyeballs — a white sphere with a
// dark pupil poked out the front — built procedurally and parented to a unit so every
// fighter reads as a cute little character. PURE VISUAL, no logic.
//
// A unit's front is -Z (matches the FacingMarker / weapon meshes), so the eyes sit on
// the upper-front of the egg body and naturally turn with the unit as it rotates to face
// its target. Each archetype just sets EggWidth/EggHeight/EggTaper to match its EggMesh
// (and may tweak EyeHeight/EyeSpread/EyeRadius for its size); the eyes are then placed
// ON the egg's surface curve — mirroring EggMesh's profile — and protrude a touch so they
// read as bulging googly eyes from the top-down camera. Tool-enabled so they preview in
// the editor; runtime-generated children are owner-less so they're never saved into .tscn.
[Tool]
public partial class Eyes : Node3D
{
    [Export] public float EggWidth = 1.0f;     // match the body's EggMesh Width
    [Export] public float EggHeight = 1.7f;    // match the body's EggMesh Height
    [Export] public float EggTaper = 0.22f;    // match the body's EggMesh Taper
    [Export] public float EyeHeight = 0.38f;   // local Y where the eyes sit (0 = body centre)
    [Export] public float EyeSpread = 0.40f;   // horizontal gap between the two eye centres
    [Export] public float EyeRadius = 0.16f;   // white eyeball radius
    [Export] public float PupilScale = 0.5f;   // pupil radius as a fraction of the eyeball
    [Export] public float Protrude = 0.5f;     // how far the eye pokes past the shell (× EyeRadius)

    public override void _Ready() => BuildEyes();

    private void BuildEyes()
    {
        // Drop any previously generated eyes (the editor re-runs _Ready on a [Tool] node).
        foreach (Node child in GetChildren())
            child.QueueFree();

        var white = new StandardMaterial3D { AlbedoColor = new Color(1f, 1f, 1f) };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.07f) };

        var eyeMesh = new SphereMesh
        {
            Radius = EyeRadius,
            Height = EyeRadius * 2f,
            RadialSegments = 12,
            Rings = 8,
        };
        float pr = EyeRadius * PupilScale;
        var pupilMesh = new SphereMesh
        {
            Radius = pr,
            Height = pr * 2f,
            RadialSegments = 10,
            Rings = 6,
        };

        float half = EyeSpread * 0.5f;
        AddEye(-half, eyeMesh, pupilMesh, white, dark);
        AddEye(half, eyeMesh, pupilMesh, white, dark);
    }

    private void AddEye(float x, Mesh eyeMesh, Mesh pupilMesh, Material white, Material dark)
    {
        float surfaceZ = FrontSurfaceZ(x, EyeHeight);     // negative = front (-Z)
        float z = surfaceZ - EyeRadius * Protrude;        // poke out past the shell

        var eye = new MeshInstance3D
        {
            Mesh = eyeMesh,
            MaterialOverride = white,
            Position = new Vector3(x, EyeHeight, z),
        };
        AddChild(eye);

        // Pupil sits on the front face of the eyeball (toward -Z) so it always looks forward.
        var pupil = new MeshInstance3D
        {
            Mesh = pupilMesh,
            MaterialOverride = dark,
            Position = new Vector3(0f, 0f, -EyeRadius * 0.7f),
        };
        eye.AddChild(pupil);
    }

    // Front (-Z) surface Z of the egg at horizontal offset x and height y — mirrors EggMesh's
    // surface-of-revolution profile so the eyes hug the body curve. Returns 0 (body centre
    // plane) if (x, y) falls outside the silhouette, which never happens for sane eye values.
    private float FrontSurfaceZ(float x, float y)
    {
        float ry = EggHeight * 0.5f;
        float rx = EggWidth * 0.5f;
        float yUnit = Mathf.Clamp(y / ry, -1f, 1f);
        float phi = Mathf.Acos(yUnit);
        float ringR = Mathf.Sin(phi) * (1f - EggTaper * yUnit);
        float radius = rx * ringR;                 // xz-plane radius of the egg at this height
        float inside = radius * radius - x * x;
        return inside > 0f ? -Mathf.Sqrt(inside) : 0f;
    }
}
