using Godot;

// Procedural rolling-hills landscape + scattered woodland for the Highlands level.
//
// M14: the terrain is SOLID, not just a backdrop — units walk and fight on the slopes (opt-in via
// Unit.Grounded). This node builds, in _Ready and entirely from a height function (no asset files):
//   * one vertex-coloured ArrayMesh for the hills (grass→rock by height + slope),
//   * a HeightMapShape3D collider sampled from the SAME height field (visuals + collision line up),
//   * two MultiMeshes for the trees (trunks + foliage) so the whole forest is a couple of draw calls.
//
// Rebuilt for cost + looks (the previous version was a regular-sine field ringed by a hill-wall, with a
// 300 m collider that tanked FPS on low-end GPUs):
//   * The COLLIDER only covers the walled play field (ColliderHalf, ≈ the ±40 walls + a margin), NOT
//     the whole visual landscape — units can't leave the field, so collision past it is wasted. This is
//     the big perf win (a ~100 m collider grid instead of a ~300 m one).
//   * Height is smooth layered VALUE NOISE (organic rolling ground) instead of pure sines, plus a couple
//     of gentle ridges and a distant backdrop rise for a highland horizon.
//   * Colour blends grass→highland→rock by height AND slope, so the ground reads as terrain.
//
// Flat levels never instance Scenery, so they're untouched. Boundary walls live in the scene.
public partial class Scenery : Node3D
{
	[Export] public float FieldHalf = 40.0f;       // half-width of the play field (gentle rolling terrain within)
	[Export] public float PlayAmplitude = 2.5f;    // how much the playable field gently swells up/down
	[Export] public float RidgeHeight = 5.0f;      // crest height of the couple of ridges crossing the field
	[Export] public float RidgeWidth = 11.0f;      // half-thickness of each ridge band (wider = gentler slope)
	[Export] public float RampWidth = 30.0f;       // distance past the field over which the backdrop rises
	[Export] public float BackdropHeight = 10.0f;  // how high the distant scenery hills rise beyond the field
	[Export] public float TerrainHalf = 90.0f;     // half-width of the VISUAL landscape (horizon)
	[Export] public float ColliderHalf = 0.0f;     // half-width of the SOLID collider; 0 = use TerrainHalf (only the play field needs collision)
	[Export] public float CellSize = 3.0f;         // grid spacing of the hill mesh (smaller = smoother/heavier)

	[Export] public bool Collidable = true;       // build a solid HeightMapShape3D collider matching the mesh
	[Export] public int TreeCount = 160;          // decorative trees scattered across the hills
	[Export] public float TreeBandInner = 4.0f;   // trees start this far OUTSIDE the field edge
	[Export] public int Seed = 1337;

	private System.Random _rng;

	// The level's active terrain, so grounded units / formation slots / mounts can sample the surface
	// height WITHOUT a node path or group walk: one landscape per level, and a flat level has none.
	// Set on _Ready, cleared on _ExitTree (guarded by identity so a reload that builds the next terrain
	// before freeing the old one can't null the new one).
	private static Scenery _active;

	public override void _Ready()
	{
		_active = this;
		_rng = new System.Random(Seed);
		BuildHills();
		if (Collidable)
			BuildCollision();
		ScatterTrees();
	}

	public override void _ExitTree()
	{
		if (_active == this)
			_active = null;
	}

	// Public height probe of the landscape at world (x,z). Spawns / formation slots / camera /
	// projectiles sample this so they sit ON the terrain. Matches the mesh + collider exactly.
	public float SampleHeight(float x, float z) => HeightAt(x, z);

	// Height of the ACTIVE level terrain at world (x,z), or `fallback` when there is no terrain (every
	// flat level). The single chokepoint Unit/Mount/projectile call for grounded placement — off a
	// terrain level it returns the fallback unchanged, so flat levels stay byte-identical.
	public static float SampleActiveHeight(float x, float z, float fallback)
		=> _active != null && IsInstanceValid(_active) ? _active.SampleHeight(x, z) : fallback;

	// --- Height field -------------------------------------------------------------------------------

	// Height of the landscape at (x,z): smooth organic rolling ground across the play field, a couple of
	// gentle crossing ridges, and a distant backdrop rise (only well past the field edge) for a highland
	// horizon. A square distance metric (max of |x|,|z|) keeps the backdrop ramp aligned to the square
	// field + its boundary walls. Continuous everywhere, so grounded units don't jitter.
	private float HeightAt(float x, float z)
	{
		// Layered value noise → soft, irregular swells (no regular wave pattern). ~[-1.4,1.4].
		float n = Noise2(x * 0.06f, z * 0.06f)
				+ 0.4f * Noise2(x * 0.13f + 5.2f, z * 0.13f - 3.1f);
		float h = n * PlayAmplitude;

		// A couple of distinct ridges crossing the field, each a raised band along a line.
		h += Ridge(x, z, 0.25f, 1.0f, -8.0f);    // a low ridge running roughly E–W near z ≈ -8
		h += Ridge(x, z, 1.0f, -0.35f, 14.0f);   // a cross ridge running roughly N–S near x ≈ 14

		// Distant backdrop: ground swells upward only well outside the play field, so the horizon reads
		// as highlands while the field edge by the walls stays low.
		float d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
		float t = Mathf.Clamp((d - FieldHalf) / Mathf.Max(0.001f, RampWidth), 0f, 1f);
		h += Mathf.SmoothStep(0f, 1f, t) * BackdropHeight;

		return h;
	}

	// A ridge: a raised band along the line (nx·x + nz·z = offset), peaking RidgeHeight at the line and
	// falling off over RidgeWidth (gaussian), so it reads as a smooth climbable crest.
	private float Ridge(float x, float z, float nx, float nz, float offset)
	{
		float len = Mathf.Sqrt(nx * nx + nz * nz);
		float dist = Mathf.Abs(nx * x + nz * z - offset) / Mathf.Max(0.001f, len);
		float f = dist / Mathf.Max(0.001f, RidgeWidth);
		return RidgeHeight * Mathf.Exp(-f * f);
	}

	// Smooth value noise in [-1,1]: hash the four lattice corners and interpolate with smoothstep, so
	// the surface is C1-continuous (no creases for floor-snap to fight). Deterministic — same shape
	// every run, so collider and mesh always agree.
	private static float Noise2(float x, float z)
	{
		int x0 = Mathf.FloorToInt(x);
		int z0 = Mathf.FloorToInt(z);
		float fx = x - x0, fz = z - z0;
		float u = fx * fx * (3f - 2f * fx);
		float v = fz * fz * (3f - 2f * fz);
		float a = Hash(x0, z0), b = Hash(x0 + 1, z0);
		float c = Hash(x0, z0 + 1), d = Hash(x0 + 1, z0 + 1);
		return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
	}

	// Integer hash → [-1,1].
	private static float Hash(int ix, int iz)
	{
		int h = ix * 374761393 + iz * 668265263;
		h = (h ^ (h >> 13)) * 1274126177;
		h ^= h >> 16;
		return (h & 0x7fffffff) / (float)0x3fffffff - 1f;
	}

	// --- Mesh / collision / trees -------------------------------------------------------------------

	// Build the hill landscape as one vertex-coloured ArrayMesh. SurfaceTool generates the normals so the
	// rolling surface shades smoothly; colour blends grass (low/flat) → highland green (high) → rock
	// (steep), with a touch of noise tint to break up flat shading.
	private void BuildHills()
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);

		int cells = Mathf.Max(2, (int)(TerrainHalf * 2f / CellSize));
		float step = TerrainHalf * 2f / cells;

		float heightRange = Mathf.Max(1f, PlayAmplitude * 1.4f + RidgeHeight + BackdropHeight);
		Color lowGrass = new Color(0.24f, 0.42f, 0.20f);
		Color highGrass = new Color(0.44f, 0.50f, 0.30f);
		Color rock = new Color(0.46f, 0.43f, 0.40f);

		// One vertex per grid corner, then two triangles per cell referencing them.
		void Emit(int ix, int iz)
		{
			float x = -TerrainHalf + ix * step;
			float z = -TerrainHalf + iz * step;
			float y = HeightAt(x, z);

			// Slope from a central finite difference of the height field.
			float e = step * 0.5f;
			float gx = HeightAt(x + e, z) - HeightAt(x - e, z);
			float gz = HeightAt(x, z + e) - HeightAt(x, z - e);
			float slope = Mathf.Sqrt(gx * gx + gz * gz) / (2f * e);

			float heightT = Mathf.Clamp((y + PlayAmplitude) / heightRange, 0f, 1f);
			float steepT = Mathf.Clamp(slope * 1.3f, 0f, 1f);
			Color c = lowGrass.Lerp(highGrass, heightT).Lerp(rock, steepT);
			float tint = Noise2(x * 0.35f, z * 0.35f) * 0.04f;
			c = new Color(c.R + tint, c.G + tint, c.B + tint);

			st.SetColor(c);
			st.SetUV(new Vector2(ix * 0.25f, iz * 0.25f));
			st.AddVertex(new Vector3(x, y, z));
		}

		for (int iz = 0; iz < cells; iz++)
		{
			for (int ix = 0; ix < cells; ix++)
			{
				// Two triangles (CCW, so normals point up) for the quad at (ix,iz).
				Emit(ix, iz);     Emit(ix, iz + 1);     Emit(ix + 1, iz);
				Emit(ix + 1, iz); Emit(ix, iz + 1);     Emit(ix + 1, iz + 1);
			}
		}

		st.GenerateNormals();
		var mat = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			Roughness = 1.0f,
		};
		st.SetMaterial(mat);

		AddChild(new MeshInstance3D { Name = "Hills", Mesh = st.Commit() });
	}

	// Solid collision for the landscape: a HeightMapShape3D sampled from the same HeightAt the mesh draws.
	// Only the play field needs collision (units can't pass the boundary walls), so this covers ColliderHalf
	// — much smaller than the visual TerrainHalf, the main perf win. A HeightMapShape3D's samples are spaced
	// ONE unit apart in local space and a SCALED shape is unreliable under GodotPhysics, so we sample at
	// native 1 m spacing (no shape scaling): local index i maps straight to world coord (i - half).
	private void BuildCollision()
	{
		float reach = ColliderHalf > 0f ? ColliderHalf : TerrainHalf;
		int span = Mathf.Max(2, (int)(reach * 2f));   // world width covered (1 m per cell)
		int points = span + 1;                         // grid corners along each axis
		float half = span / 2f;                        // centre the map on the origin

		var data = new float[points * points];
		for (int iz = 0; iz < points; iz++)
			for (int ix = 0; ix < points; ix++)
				data[iz * points + ix] = HeightAt(ix - half, iz - half);

		var shape = new HeightMapShape3D
		{
			MapWidth = points,
			MapDepth = points,
			MapData = data,
		};

		var body = new StaticBody3D { Name = "TerrainBody" };
		body.AddChild(new CollisionShape3D { Name = "TerrainCollision", Shape = shape });
		AddChild(body);
	}

	// Scatter trees across the hill band (outside the play field) as two MultiMeshes — a brown trunk and a
	// green foliage blob per instance — so the whole forest is two draw calls.
	private void ScatterTrees()
	{
		var trunkMesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.28f, Height = 2.4f, RadialSegments = 6 };
		var foliageMesh = new SphereMesh { Radius = 1.4f, Height = 2.6f, RadialSegments = 7, Rings = 5 };

		var trunkMat = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.25f, 0.16f), Roughness = 1f };
		var foliageMat = new StandardMaterial3D { AlbedoColor = new Color(0.20f, 0.40f, 0.18f), Roughness = 1f };

		var trunkMM = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = trunkMesh };
		var foliageMM = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = foliageMesh };
		trunkMM.InstanceCount = TreeCount;
		foliageMM.InstanceCount = TreeCount;

		float innerEdge = FieldHalf + TreeBandInner;
		float outerEdge = TerrainHalf - 4f;

		for (int i = 0; i < TreeCount; i++)
		{
			// Reject-sample a spot in the square ring outside the field.
			float x, z;
			int guard = 0;
			do
			{
				x = (float)(_rng.NextDouble() * 2.0 - 1.0) * outerEdge;
				z = (float)(_rng.NextDouble() * 2.0 - 1.0) * outerEdge;
			}
			while (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < innerEdge && guard++ < 8);

			float y = HeightAt(x, z);
			float scale = 1.1f + (float)_rng.NextDouble() * 1.7f;
			float yaw = (float)(_rng.NextDouble() * Mathf.Tau);
			var basis = new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale);

			// Trunk sits on the ground; foliage rides above the trunk top.
			trunkMM.SetInstanceTransform(i, new Transform3D(basis, new Vector3(x, y + 1.2f * scale, z)));
			foliageMM.SetInstanceTransform(i, new Transform3D(basis, new Vector3(x, y + 3.1f * scale, z)));
		}

		AddChild(new MultiMeshInstance3D { Name = "TreeTrunks", Multimesh = trunkMM, MaterialOverride = trunkMat });
		AddChild(new MultiMeshInstance3D { Name = "TreeFoliage", Multimesh = foliageMM, MaterialOverride = foliageMat });
	}
}
