using Godot;

// Floating health bar (M16, Chunk 79). A small billboarded two-quad bar that hangs above a
// unit: a dark background plus a team-coloured fill that shrinks from the LEFT as HP drops.
// Pure visual, parented to the unit root. Built procedurally (no asset files) and sized off
// the body's egg so a fat captain gets a wider bar than a skeleton.
//
// Cheap-for-crowds choices: the background quad + the two unshaded team materials are shared
// static resources across every unit; only the FILL quad is a per-instance QuadMesh (so its
// width can be resized without touching other instances). Both quads billboard toward the
// camera via their material (BillboardMode), draw unshaded with depth-test off + a render
// priority so the bar always reads on top of the bodies, and never cast shadows.
public partial class HealthBar3D : Node3D
{
    // Shared assets so 100 units cost one bg mesh + three materials, not 400.
    private static QuadMesh _bgMesh;
    private static StandardMaterial3D _bgMat;
    private static StandardMaterial3D _playerFillMat;
    private static StandardMaterial3D _enemyFillMat;

    private float _barWidth;
    private float _barHeight;
    private QuadMesh _fillMesh;       // per-instance so the fill can be resized
    private float _fraction = 1f;

    // Build a bar `width`×`height` units big, coloured for `team`. Call once after adding the
    // node to the unit and positioning it; resize later with SetFraction.
    public void Build(float width, float height, Unit.TeamId team)
    {
        _barWidth = width;
        _barHeight = height;

        // Background: full-size shared quad, slightly behind the fill.
        AddChild(new MeshInstance3D
        {
            Name = "Bg",
            Mesh = BgMesh(width, height),
            MaterialOverride = BgMat(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // Fill: per-instance quad, anchored to the LEFT edge so it shrinks rightward.
        _fillMesh = new QuadMesh { Size = new Vector2(width, height) };
        AddChild(new MeshInstance3D
        {
            Name = "Fill",
            Mesh = _fillMesh,
            MaterialOverride = team == Unit.TeamId.Player ? PlayerFillMat() : EnemyFillMat(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        SetFraction(1f);
    }

    // Set the fill to `fraction` (0..1) of full HP. The quad is resized and its centre offset so
    // it stays pinned to the bar's left edge (drains right→left, like a classic health bar).
    public void SetFraction(float fraction)
    {
        _fraction = Mathf.Clamp(fraction, 0f, 1f);
        if (_fillMesh == null)
            return;
        float w = _barWidth * _fraction;
        _fillMesh.Size = new Vector2(w, _barHeight);
        // Anchor left: shift the (centre-origin) quad right by half its missing width.
        _fillMesh.CenterOffset = new Vector3(-_barWidth * 0.5f + w * 0.5f, 0f, 0f);
    }

    private static QuadMesh BgMesh(float width, float height)
    {
        // The bg is the same for every unit at a given size; cache the common case and only
        // build a fresh one if a unit asks for an off-size bar (rare).
        if (_bgMesh != null && Mathf.IsEqualApprox(_bgMesh.Size.X, width) && Mathf.IsEqualApprox(_bgMesh.Size.Y, height))
            return _bgMesh;
        var mesh = new QuadMesh { Size = new Vector2(width, height) };
        _bgMesh ??= mesh;
        return mesh;
    }

    private static StandardMaterial3D BgMat() =>
        _bgMat ??= MakeBarMat(new Color(0.06f, 0.06f, 0.08f), 0);

    private static StandardMaterial3D PlayerFillMat() =>
        _playerFillMat ??= MakeBarMat(new Color(0.30f, 0.80f, 0.35f), 1);   // friendly green

    private static StandardMaterial3D EnemyFillMat() =>
        _enemyFillMat ??= MakeBarMat(new Color(0.90f, 0.25f, 0.25f), 1);    // hostile red

    // One billboarded, unshaded, depth-test-off bar material. `priority` lifts the fill over
    // the background; depth-off + priority keeps the whole bar legible in front of the bodies.
    private static StandardMaterial3D MakeBarMat(Color color, int priority) => new StandardMaterial3D
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        RenderPriority = priority,
    };
}
