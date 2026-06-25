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
    private static BoxMesh _shardMesh;                 // shell fragment (egg-break, Chunk 82)
    private static SphereMesh _yolkMesh;               // yolk blob (egg-break, Chunk 82)
    private static StandardMaterial3D _additiveMat;   // sparks: glowing, billboarded
    private static StandardMaterial3D _alphaMat;       // poof / dust: soft alpha billboard
    private static StandardMaterial3D _shellMat;       // shell shards: lit, per-spawn colour
    private static StandardMaterial3D _yolkMat;        // yolk goop: unshaded yellow billboard
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

    // Violent egg-break on death (M16, Chunk 82). The shell SHATTERS apart instead of just
    // poofing: a fan of shell-shard fragments tumbles outward (spin + gravity), a yolk splat
    // bursts from the core, and a sharp bright pop punches at the impact — so a death reads as
    // an egg cracking open. `direction` biases the throw the way the killing shove pushed (so a
    // pinball-fling death sprays that way) and `force` (≈ shove speed over MinBounceSpeed) scales
    // how hard it bursts, so a heavy knockback kill explodes harder than a quiet expiry. Shell
    // shards take the unit's own `shellColor`; the yolk is always egg-yolk yellow. Headless-safe.
    public static void EggBurst(Node parent, Vector3 position, Color shellColor, Vector3 direction, float force)
    {
        if (Headless || parent == null || !GodotObject.IsInstanceValid(parent))
            return;

        Vector3 bias = direction;
        bias.Y = 0f;
        bias = bias.LengthSquared() > 0.0001f ? bias.Normalized() : Vector3.Zero;
        float boost = 1f + Mathf.Clamp(force, 0f, 2f);   // 1..3x throw on a hard shove kill

        // 1) Shell shards — chunky fragments of the cracked shell, flung up + outward, tumbling.
        var shards = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.3f,
            Direction = (Vector3.Up * 1.2f + bias).Normalized(),
            Spread = 70f,
            Gravity = new Vector3(0f, -16f, 0f),
            InitialVelocityMin = 4.5f * boost,
            InitialVelocityMax = 8.5f * boost,
            AngularVelocityMin = -720f,   // degrees/sec — shards tumble as they fly
            AngularVelocityMax = 720f,
            ScaleMin = 0.7f,
            ScaleMax = 1.5f,
            Color = shellColor,
        };
        Spawn(parent, position, ShardMesh(), shards, ShellMat(), count: 12, lifetime: 0.85f);

        // 2) Yolk splat — a goopy yellow burst from the core that arcs down and splats.
        var yolk = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.14f,
            Direction = (Vector3.Up * 0.6f + bias).Normalized(),
            Spread = 80f,
            Gravity = new Vector3(0f, -18f, 0f),
            InitialVelocityMin = 2.5f * boost,
            InitialVelocityMax = 5.5f * boost,
            ScaleMin = 0.9f,
            ScaleMax = 2.0f,
            Color = new Color(1f, 0.82f, 0.2f),   // egg-yolk yellow
            AlphaCurve = FadeCurve(),
        };
        Spawn(parent, position, YolkMesh(), yolk, YolkMat(), count: 9, lifetime: 0.6f);

        // 3) Sharp pop — a quick bright additive flash punch at the moment of the break.
        var pop = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.05f,
            Direction = Vector3.Up,
            Spread = 90f,
            Gravity = Vector3.Zero,
            InitialVelocityMin = 5f,
            InitialVelocityMax = 9f,
            ScaleMin = 1.0f,
            ScaleMax = 2.2f,
            Color = new Color(1f, 0.96f, 0.82f),
            AlphaCurve = FadeCurve(),
        };
        Spawn(parent, position, SparkMesh(), pop, AdditiveMat(), count: 10, lifetime: 0.22f);
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

    // A flattened box reads as a curved shell fragment when it tumbles (Chunk 82).
    private static BoxMesh ShardMesh() =>
        _shardMesh ??= new BoxMesh { Size = new Vector3(0.16f, 0.05f, 0.16f) };

    // A small low-poly blob for the yolk splat.
    private static SphereMesh YolkMesh() =>
        _yolkMesh ??= new SphereMesh { Radius = 0.1f, Height = 0.2f, RadialSegments = 6, Rings = 3 };

    // Shell shards are LIT (not billboarded) so a tumbling fragment catches the light and reads
    // as a solid 3D piece of shell; per-spawn shell colour comes from the process Color.
    private static StandardMaterial3D ShellMat() => _shellMat ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.85f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,   // thin shards visible from both faces
    };

    // Yolk blobs: flat unshaded glossy-yellow goop, billboarded so the splat always faces camera.
    private static StandardMaterial3D YolkMat() => _yolkMat ??= new StandardMaterial3D
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

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
