using Godot;

// Procedural rolling-hills backdrop + scattered woodland for a large outdoor level.
//
// M14 (Chunk 60): the terrain is now SOLID, not just a backdrop. This node builds a hilly
// LANDSCAPE — the centre (within FieldHalf) stays flat at ground level, the ground RISES into
// rolling hills past the field edge — AND generates matching collision from the SAME height
// function (a HeightMapShape3D under a StaticBody3D), so units can walk + fight on the slopes
// once they opt into grounded movement (Unit.Grounded, Chunk 61). Flat levels never use Scenery,
// so they're untouched. Boundary walls still live in the scene.
//
// Everything here is generated in _Ready from a height function (no asset files): a single
// ArrayMesh for the hills (vertex-coloured grass→highland green), a HeightMapShape3D collider
// sampled on the same grid (visuals + collision match exactly), and two MultiMeshes for the
// trees (trunks + foliage), so a few hundred trees cost a handful of draw calls.
public partial class Scenery : Node3D
{
	[Export] public float FieldHalf = 40.0f;       // half-width of the play field (gentle rolling terrain within)
	[Export] public float PlayAmplitude = 2.5f;    // how much the playable field gently swells up/down
	[Export] public float RidgeHeight = 5.0f;      // crest height of the couple of ridges crossing the field
	[Export] public float RidgeWidth = 11.0f;      // half-thickness of each ridge band (wider = gentler slope)
	[Export] public float RampWidth = 30.0f;       // distance past the field over which the backdrop rises
	[Export] public float BackdropHeight = 10.0f;  // how high the distant scenery hills rise beyond the field
	[Export] public float TerrainHalf = 120.0f;    // half-width of the whole generated landscape
	[Export] public float CellSize = 2.5f;         // grid spacing of the hill mesh (smaller = smoother/heavier)

	[Export] public bool Collidable = true;       // build a solid HeightMapShape3D collider matching the mesh
	[Export] public int TreeCount = 220;          // decorative trees scattered across the hills
	[Export] public float TreeBandInner = 4.0f;   // trees start this far OUTSIDE the field edge
	[Export] public int Seed = 1337;

	private System.Random _rng;

	// The level's active terrain, so grounded units / formation slots / mounts (Chunk 62) can sample
	// the surface height WITHOUT a node path or group walk: there is one landscape per level, and a
	// flat level has none. Set on _Ready, cleared on _ExitTree (guarded by identity so a reload that
	// builds the next terrain before freeing the old one can't null the new one).
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

	// Public height probe of the landscape at world (x,z). Spawns/formation slots/camera (Chunks
	// 62, 64) sample this so they sit ON the terrain instead of a fixed plane. Matches the mesh
	// + collider exactly (all three read HeightAt).
	public float SampleHeight(float x, float z) => HeightAt(x, z);

	// Height of the ACTIVE level terrain at world (x,z), or `fallback` when there is no terrain (every
	// flat level). The single chokepoint Unit/Mount call for grounded placement (Chunk 62) — off a
	// terrain level it returns the fallback unchanged, so flat levels stay byte-identical.
	public static float SampleActiveHeight(float x, float z, float fallback)
		=> _active != null && IsInstanceValid(_active) ? _active.SampleHeight(x, z) : fallback;

	// Height of the landscape at (x,z). Unlike the old "flat field ringed by a wall of hills" this is
	// GENTLE PLAYABLE terrain: the whole field rolls up/down by a few metres (low-amplitude layered
	// sines) with a couple of distinct ridges crossing it, so units climb and fight over real but mild
	// elevation. Only the DISTANT backdrop (well past the field edge) rises higher, for a highland
	// horizon. A square distance metric (max of |x|,|z|) keeps the backdrop ramp aligned to the square
	// field + its boundary walls.
	private float HeightAt(float x, float z)
	{
		// Gentle rolling swells across the whole field — low-frequency layered sines give soft
		// up-and-down ground (roll ≈ ±1.5, scaled to about ±PlayAmplitude).
		float roll = Mathf.Sin(x * 0.05f + 0.4f) * Mathf.Cos(z * 0.045f)
				   + 0.5f * Mathf.Sin(x * 0.11f - 1.2f) * Mathf.Cos(z * 0.09f + 0.7f);
		float h = roll * 0.66f * PlayAmplitude;

		// A couple of distinct ridges crossing the field, each a raised band along a line.
		h += Ridge(x, z, 0.25f, 1.0f, -8.0f);    // a low ridge running roughly E–W near z ≈ -8
		h += Ridge(x, z, 1.0f, -0.35f, 14.0f);   // a cross ridge running roughly N–S near x ≈ 14

		// Distant backdrop: ground swells upward only well outside the play field, so the horizon
		// reads as highlands while the field edge by the walls stays low.
		float d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
		float t = Mathf.Clamp((d - FieldHalf) / Mathf.Max(0.001f, RampWidth), 0f, 1f);
		h += Mathf.SmoothStep(0f, 1f, t) * BackdropHeight;

		return h;
	}

	// A ridge: a raised band along the line (nx·x + nz·z = offset), peaking RidgeHeight at the line
	// and falling off over RidgeWidth (gaussian), so it reads as a smooth climbable crest.
	private float Ridge(float x, float z, float nx, float nz, float offset)
	{
		float len = Mathf.Sqrt(nx * nx + nz * nz);
		float dist = Mathf.Abs(nx * x + nz * z - offset) / Mathf.Max(0.001f, len);
		float f = dist / Mathf.Max(0.001f, RidgeWidth);
		return RidgeHeight * Mathf.Exp(-f * f);
	}

	// Build the hill landscape as one vertex-coloured ArrayMesh. SurfaceTool generates the
	// normals so the rolling surface shades smoothly; colour blends grass-green (low) to a
	// paler highland green (high) by height.
	private void BuildHills()
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);

		int cells = Mathf.Max(2, (int)(TerrainHalf * 2f / CellSize));
		float step = TerrainHalf * 2f / cells;

		float ColorT(float y) => Mathf.Clamp(y / (PlayAmplitude + RidgeHeight + BackdropHeight), 0f, 1f);
		Color grass = new Color(0.24f, 0.42f, 0.20f);
		Color high = new Color(0.55f, 0.62f, 0.42f);

		// One vertex per grid corner, then two triangles per cell referencing them.
		void Emit(int ix, int iz)
		{
			float x = -TerrainHalf + ix * step;
			float z = -TerrainHalf + iz * step;
			float y = HeightAt(x, z);
			st.SetColor(grass.Lerp(high, ColorT(y)));
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

		var mi = new MeshInstance3D { Name = "Hills", Mesh = st.Commit() };
		AddChild(mi);
	}

	// Solid collision for the landscape: a HeightMapShape3D sampled from the same HeightAt the mesh
	// draws, so collision and visuals line up. A HeightMapShape3D's samples are spaced ONE unit apart
	// in local space and a SCALED HeightMapShape3D is unreliable under GodotPhysics, so we sample at
	// native 1 m spacing (no shape scaling): local index i maps straight to world coord (i - half).
	// This is finer than the 2.5 m mesh grid, but both read the same continuous height field so they
	// still match. The flat centre comes out at HeightAt's flat value (≈ -CenterDrop), giving units a
	// level floor on the play field.
	private void BuildCollision()
	{
		int span = Mathf.Max(2, (int)(TerrainHalf * 2f));   // world width covered (1 m per cell)
		int points = span + 1;                              // grid corners along each axis
		float half = span / 2f;                             // centre the map on the origin

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

	// Scatter trees across the hill band (outside the play field) as two MultiMeshes — a brown
	// trunk and a green foliage blob per instance — so the whole forest is two draw calls.
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
