using Godot;

// Floating health bar (M16, Chunk 79). A single billboarded quad drawn as a rounded toon
// "pill" by materials/healthbar.gdshader: a dark frame, a team-coloured fill that drains
// right→left, and a soft top highlight so it reads cartoony rather than a flat rectangle.
// Pure visual, parented to the unit root, sized off the body's egg.
//
// Cheap-for-crowds: ONE shared unit (1×1) quad mesh + ONE shared shader across every unit;
// the node's scale sizes the bar and only the per-instance ShaderMaterial carries this unit's
// fill fraction + team colour. Unshaded, depth-test-off (always reads over the bodies) and
// never casts shadows.
public partial class HealthBar3D : Node3D
{
    private static QuadMesh _quad;     // shared 1×1 quad; node scale sizes it
    private static Shader _shader;

    private ShaderMaterial _mat;       // per-instance: this unit's fill + colour

    // Build a bar `width`×`height` units big, coloured for `team`. Call once after adding the
    // node to the unit and positioning it; update later with SetFraction.
    public void Build(float width, float height, Unit.TeamId team)
    {
        _shader ??= GD.Load<Shader>("res://materials/healthbar.gdshader");
        Scale = new Vector3(width, height, 1f);

        _mat = new ShaderMaterial { Shader = _shader };
        _mat.SetShaderParameter("aspect", height > 0f ? width / height : 9f);
        _mat.SetShaderParameter("fill_color", team == Unit.TeamId.Player
            ? new Color(0.34f, 0.82f, 0.40f)    // friendly green
            : new Color(0.92f, 0.30f, 0.28f));  // hostile red
        _mat.SetShaderParameter("fill", 1f);

        AddChild(new MeshInstance3D
        {
            Name = "Pill",
            Mesh = Quad(),
            MaterialOverride = _mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    // Set the fill to `fraction` (0..1) of full HP.
    public void SetFraction(float fraction)
    {
        _mat?.SetShaderParameter("fill", Mathf.Clamp(fraction, 0f, 1f));
    }

    private static QuadMesh Quad() => _quad ??= new QuadMesh { Size = Vector2.One };
}
