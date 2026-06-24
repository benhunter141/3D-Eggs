using Godot;

// Procedural chicken-egg mesh: a surface of revolution that is a sphere modulated
// by a vertical taper, so the BOTTOM is fat/round and the TOP tapers to a point —
// the asymmetric profile a plain SphereMesh (a symmetric ellipsoid) can't give.
// Attach to a MeshInstance3D; it rebuilds the mesh on ready. Color comes from the
// node's material_override, so each unit keeps its team color.
[Tool]
public partial class EggMesh : MeshInstance3D
{
    [Export] public float Width = 1.0f;          // diameter at the widest point
    [Export] public float Height = 1.7f;         // full top-to-bottom height
    [Export] public float Taper = 0.22f;         // asymmetry: >0 = fatter bottom, pointier top
    [Export] public int RadialSegments = 24;     // around
    [Export] public int Rings = 24;              // top-to-bottom

    // Toon outline (M16, Chunk 77): a back-face inverted hull of this same mesh, grown
    // along the normals + drawn flat dark, so every egg gets a crisp cartoon outline.
    [Export] public bool ShowOutline = true;
    [Export] public float OutlineWidth = 0.045f;

    private static Shader _outlineShader;

    public override void _Ready()
    {
        BuildEgg();
        BuildOutline();
    }

    // Hang a child MeshInstance3D that re-draws this egg's mesh as a dark inverted hull.
    // Runtime only (skipped in the editor) so .tscn files stay clean and never accumulate
    // duplicate children across editor reloads; sharing the same Mesh resource keeps it cheap.
    private void BuildOutline()
    {
        if (!ShowOutline || Engine.IsEditorHint() || Mesh == null)
            return;
        if (GetNodeOrNull("Outline") != null)
            return;

        _outlineShader ??= GD.Load<Shader>("res://materials/outline.gdshader");
        if (_outlineShader == null)
            return;

        var mat = new ShaderMaterial { Shader = _outlineShader };
        mat.SetShaderParameter("outline_width", OutlineWidth);

        AddChild(new MeshInstance3D
        {
            Name = "Outline",
            Mesh = Mesh,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    private void BuildEgg()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        float rx = Width * 0.5f;
        float ry = Height * 0.5f;
        int cols = RadialSegments + 1;
        int point = 0;
        int prevrow = 0;

        // Rows run top (r=0) -> bottom (r=Rings), matching Godot's SphereMesh winding
        // so default back-face culling shows the outer surface.
        for (int r = 0; r <= Rings; r++)
        {
            int thisrow = point;
            float v = (float)r / Rings;          // 0 top .. 1 bottom
            float phi = v * Mathf.Pi;            // 0 top .. PI bottom
            float yUnit = Mathf.Cos(phi);        // 1 top .. -1 bottom
            float baseR = Mathf.Sin(phi);        // 0 at poles, 1 at equator
            float taper = 1.0f - Taper * yUnit;  // top (yUnit=1) narrower, bottom (yUnit=-1) fatter
            float ringR = baseR * taper;

            for (int c = 0; c <= RadialSegments; c++)
            {
                float u = (float)c / RadialSegments;
                float theta = u * Mathf.Tau;
                // Match Godot's SphereMesh radial convention (x=sin, z=cos) so the
                // shared triangle winding yields OUTWARD normals, not inverted ones.
                float x = rx * ringR * Mathf.Sin(theta);
                float z = rx * ringR * Mathf.Cos(theta);
                float y = ry * yUnit;

                st.SetUV(new Vector2(u, v));
                st.AddVertex(new Vector3(x, y, z));

                if (r > 0 && c > 0)
                {
                    st.AddIndex(prevrow + c - 1);
                    st.AddIndex(prevrow + c);
                    st.AddIndex(thisrow + c - 1);

                    st.AddIndex(prevrow + c);
                    st.AddIndex(thisrow + c);
                    st.AddIndex(thisrow + c - 1);
                }
                point++;
            }
            prevrow = thisrow;
        }

        st.GenerateNormals();
        st.GenerateTangents();
        Mesh = st.Commit();
    }
}
