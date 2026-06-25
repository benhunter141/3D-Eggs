using Godot;

// Combat juice particles (M16, Chunk 80). One-shot GPUParticles3D bursts spawned at a world
// point that free themselves the moment they finish (via the Finished signal). Three flavours,
// all built procedurally (no asset files) and kept LOW-count for the 940MX:
//   • HitSpark  — a few bright motes flung off a non-lethal hit (sword / fist / stone / fireball,
//                 since every hit funnels through Unit.TakeDamage).
//   • DeathPoof — a soft puff of the unit's own colour when it dies.
//   • BounceDust— a low grey kick-up where a pinball shove rams something.
//
// Headless-safe: the logic tests run --headless where there is NO rendering device, so GPU
// particles can't be created. Every spawn early-outs when the display server is "headless",
// keeping UnitTest green — the visuals only fire in a real window. Shared draw materials + meshes
// keep a crowd cheap; the per-spawn ProcessMaterial is the only allocation and it's a one-shot.
public static class Particles
{
    private static bool Headless => DisplayServer.GetName() == "headless";

    private static QuadMesh _sparkMesh;
    private static SphereMesh _puffMesh;
    private static StandardMaterial3D _additiveMat;   // sparks: glowing, billboarded
    private static StandardMaterial3D _alphaMat;       // poof / dust: soft alpha billboard
    private static CurveTexture _fadeCurve;            // alpha 1 -> 0 over particle life (shared)

    // Bright spark fan on a non-lethal hit. `direction` points the way we were shoved
    // (attacker -> us); sparks spray that way + up. `color` tints the motes.
    public static void HitSpark(Node parent, Vector3 position, Vector3 direction, Color color)
    {
        if (Headless || parent == null || !GodotObject.IsInstanceValid(parent))
            return;

        Vector3 dir = direction;
        dir.Y = 0f;
        dir = dir.LengthSquared() > 0.0001f ? (dir.Normalized() + Vector3.Up * 0.8f).Normalized() : Vector3.Up;

        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.12f,
            Direction = dir,
            Spread = 50f,
            Gravity = new Vector3(0f, -10f, 0f),
            InitialVelocityMin = 4.5f,
            InitialVelocityMax = 8.5f,
            ScaleMin = 0.5f,
            ScaleMax = 1.1f,
            Color = color,
            AlphaCurve = FadeCurve(),
        };
        Spawn(parent, position, SparkMesh(), process, AdditiveMat(), count: 8, lifetime: 0.32f);
    }

    // Soft expanding puff of the unit's colour on death.
    public static void DeathPoof(Node parent, Vector3 position, Color color)
    {
        if (Headless || parent == null || !GodotObject.IsInstanceValid(parent))
            return;

        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.25f,
            Direction = Vector3.Up,
            Spread = 75f,
            Gravity = new Vector3(0f, 2.5f, 0f),   // drifts gently upward
            InitialVelocityMin = 1.8f,
            InitialVelocityMax = 4.0f,
            ScaleMin = 1.4f,
            ScaleMax = 2.4f,
            Color = color,
            AlphaCurve = FadeCurve(),
        };
        Spawn(parent, position, PuffMesh(), process, AlphaMat(), count: 14, lifetime: 0.55f);
    }

    // Low grey dust kick where a knockback shove rams a body or wall.
    public static void BounceDust(Node parent, Vector3 position)
    {
        if (Headless || parent == null || !GodotObject.IsInstanceValid(parent))
            return;

        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.2f,
            Direction = Vector3.Up,
            Spread = 80f,
            Gravity = new Vector3(0f, -3f, 0f),
            InitialVelocityMin = 1.5f,
            InitialVelocityMax = 3.2f,
            ScaleMin = 1.0f,
            ScaleMax = 1.8f,
            Color = new Color(0.78f, 0.74f, 0.66f, 0.8f),   // pale dust
            AlphaCurve = FadeCurve(),
        };
        Spawn(parent, position, PuffMesh(), process, AlphaMat(), count: 6, lifetime: 0.4f);
    }

    // Build the GPUParticles3D, drop it at `position` under `parent`, and self-free on Finished.
    private static void Spawn(Node parent, Vector3 position, Mesh mesh,
        ParticleProcessMaterial process, Material draw, int count, float lifetime)
    {
        var p = new GpuParticles3D
        {
            Amount = count,
            Lifetime = lifetime,
            OneShot = true,
            Explosiveness = 1.0f,    // emit the whole burst at once
            ProcessMaterial = process,
            DrawPass1 = mesh,
            MaterialOverride = draw,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(p);
        p.GlobalPosition = position;
        p.Finished += () => { if (GodotObject.IsInstanceValid(p)) p.QueueFree(); };
    }

    private static QuadMesh SparkMesh() => _sparkMesh ??= new QuadMesh { Size = new Vector2(0.16f, 0.16f) };

    private static SphereMesh PuffMesh() =>
        _puffMesh ??= new SphereMesh { Radius = 0.16f, Height = 0.32f, RadialSegments = 6, Rings = 3 };

    // Shared additive billboard for sparks: glows over the bodies, per-particle colour from the
    // process material's Color (vertex colour), unshaded so it reads cartoony.
    private static StandardMaterial3D AdditiveMat() => _additiveMat ??= new StandardMaterial3D
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // Shared soft-alpha billboard for poof / dust.
    private static StandardMaterial3D AlphaMat() => _alphaMat ??= new StandardMaterial3D
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // Shared alpha-over-life curve (opaque at birth, gone by death) so bursts fade out softly.
    private static CurveTexture FadeCurve()
    {
        if (_fadeCurve != null)
            return _fadeCurve;
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 1f));
        curve.AddPoint(new Vector2(0.6f, 0.7f));
        curve.AddPoint(new Vector2(1f, 0f));
        _fadeCurve = new CurveTexture { Curve = curve };
        return _fadeCurve;
    }
}
