using Godot;

// Procedural rolling-hills backdrop + scattered woodland for a large outdoor level.
//
// Why visual-only terrain: every fighter (Player/Ally/Enemy/...) moves on FLAT ground
// with no gravity or floor-snapping (see Player.cs "Flat ground for now"), so real sloped
// collision would make units float/clip. Instead this node builds a hilly LANDSCAPE that
// rings the battlefield: the centre (within FieldHalf) stays flat at ground level so units
// fight on solid flat ground, and the terrain only RISES into rolling hills past the field
// edge — purely for looks. A separate flat collision plane + boundary walls (in the scene)
// keep the actual play on the level centre.
//
// Everything here is generated in _Ready from a height function (no asset files): a single
// ArrayMesh for the hills (vertex-coloured grass→highland green) and two MultiMeshes for the
// trees (trunks + foliage), so a few hundred trees cost a handful of draw calls.
public partial class Scenery : Node3D
{
	[Export] public float FieldHalf = 40.0f;     // half-width of the flat play field (kept level)
	[Export] public float RampWidth = 16.0f;     // how far past the field the ground ramps up into hills
	[Export] public float TerrainHalf = 120.0f;  // half-width of the whole generated landscape
	[Export] public float CellSize = 2.5f;       // grid spacing of the hill mesh (smaller = smoother/heavier)
	[Export] public float RimHeight = 2.5f;       // base lift the hills sit at once past the ramp
	[Export] public float HillAmplitude = 7.0f;   // extra height of the rolling sine hills on top of the rim
	[Export] public float CenterDrop = 0.15f;     // sink the flat centre just below the play plane (no z-fight)

	[Export] public int TreeCount = 220;          // decorative trees scattered across the hills
	[Export] public float TreeBandInner = 4.0f;   // trees start this far OUTSIDE the field edge
	[Export] public int Seed = 1337;

	private System.Random _rng;

	public override void _Ready()
	{
		_rng = new System.Random(Seed);
		BuildHills();
		ScatterTrees();
	}

	// Height of the landscape at (x,z): flat (slightly sunk) inside the field, smoothly ramping
	// up past the edge into rolling sine-noise hills. A square distance metric (max of |x|,|z|)
	// makes the flat region match the square play field + its boundary walls.
	private float HeightAt(float x, float z)
	{
		float d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
		float t = Mathf.Clamp((d - FieldHalf) / Mathf.Max(0.001f, RampWidth), 0f, 1f);
		float ramp = Mathf.SmoothStep(0f, 1f, t);     // 0 inside the field, 1 once fully past the ramp

		// Rolling hills: layered sines, normalised to 0..1 then scaled.
		float h = Mathf.Sin(x * 0.06f) * Mathf.Cos(z * 0.05f)
				+ 0.5f * Mathf.Sin(x * 0.13f + 1.7f) * Mathf.Cos(z * 0.11f - 0.4f)
				+ 0.3f * Mathf.Sin((x + z) * 0.09f + 3.1f);
		float hills = (h * 0.4f + 0.5f) * HillAmplitude;

		return ramp * (RimHeight + hills) - CenterDrop;
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

		float ColorT(float y) => Mathf.Clamp(y / (RimHeight + HillAmplitude), 0f, 1f);
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
