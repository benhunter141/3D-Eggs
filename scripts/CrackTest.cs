using Godot;
using System.Collections.Generic;

// Visual harness for the shell-crack LOOK (run windowed, NOT headless). Builds a row of
// eggs frozen at fixed HP levels so a crack style can be judged at a glance, and lets us
// flip between candidate styles live to compare. Pure visual — no Unit AI, no combat. It
// reuses the same pieces a real Unit uses for its shell: an EggMesh body + the EggCracks
// shader as a next_pass overlay, with `damage` pinned per egg.
//
//   godot --path . res://scenes/Tests/CrackTest.tscn
//
// Controls:  [1] Bold branching   [2] Voronoi (old)   [R] toggle spin
public partial class CrackTest : Node3D
{
	// Missing-health fraction per egg: 0 = full HP / pristine .. 0.9 = near death.
	private static readonly float[] Damages = { 0f, 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };

	// (shader style id, label). Index 0 is the current default look.
	private static readonly (int Id, string Name)[] Styles =
	{
		(1, "Bold branching"),
		(0, "Voronoi (old)"),
	};

	private int _styleIndex;
	private bool _rotate = true;
	private readonly List<Node3D> _eggHolders = new();
	private readonly List<ShaderMaterial> _crackMats = new();
	private Label _hud;

	// Bold-branching tuning knobs (live-adjustable; defaults match the shader).
	private float _thickness = 0.018f;
	private float _jitter = 0.9f;
	private float _segLen = 0.05f;
	private int _count = 4;
	private float _grow = 5f;
	private bool _branches = true;

	public override void _Ready()
	{
		BuildEnvironment();
		BuildEggs();
		BuildHud();
		ApplyStyle();
		PushParams();
	}

	private void BuildEnvironment()
	{
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.16f, 0.18f, 0.22f),
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.6f, 0.6f, 0.66f),
			AmbientLightEnergy = 1.0f,
		};
		AddChild(new WorldEnvironment { Environment = env });

		var light = new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -35f, 0f) };
		AddChild(light);

		var cam = new Camera3D { Position = new Vector3(0f, 1.1f, 9.0f) };
		AddChild(cam);
		cam.LookAt(new Vector3(0f, 0.95f, 0f), Vector3.Up);
	}

	private void BuildEggs()
	{
		var shader = GD.Load<Shader>("res://shaders/EggCracks.gdshader");
		const float spacing = 2.0f;
		float x0 = -(Damages.Length - 1) * spacing * 0.5f;

		for (int i = 0; i < Damages.Length; i++)
		{
			float d = Damages[i];
			var holder = new Node3D { Position = new Vector3(x0 + i * spacing, 0.95f, 0f) };
			AddChild(holder);
			_eggHolders.Add(holder);

			// Body: a pale eggshell, each egg its own material so it can carry its own crack pass.
			var body = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.84f, 0.78f) };

			// Crack overlay: a per-egg shader material with damage pinned for this column.
			var crack = new ShaderMaterial { Shader = shader };
			crack.SetShaderParameter("seed", new Vector2(i * 12.7f + 3f, i * 5.3f + 1f));
			crack.SetShaderParameter("damage", d);
			body.NextPass = crack;
			_crackMats.Add(crack);

			var egg = new EggMesh { Width = 1.0f, Height = 1.7f, MaterialOverride = body };
			holder.AddChild(egg);

			var label = new Label3D
			{
				Text = $"{Mathf.RoundToInt((1f - d) * 100f)}% HP",
				Position = new Vector3(0f, 1.25f, 0f),
				Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
				FontSize = 48,
				Modulate = new Color(1f, 1f, 1f),
				NoDepthTest = true,
			};
			holder.AddChild(label);
		}
	}

	private void BuildHud()
	{
		var layer = new CanvasLayer();
		AddChild(layer);
		_hud = new Label
		{
			Position = new Vector2(16f, 12f),
			Theme = null,
		};
		_hud.AddThemeFontSizeOverride("font_size", 20);
		layer.AddChild(_hud);
	}

	public override void _Process(double delta)
	{
		if (!_rotate)
			return;
		foreach (Node3D holder in _eggHolders)
			holder.RotateY((float)delta * 0.6f);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key)
			return;

		switch (key.Keycode)
		{
			case Key.Key1: _styleIndex = 0; ApplyStyle(); break;
			case Key.Key2: _styleIndex = 1; ApplyStyle(); break;
			case Key.R: _rotate = !_rotate; UpdateHud(); break;

			// Bold-branching knobs: upper key raises, lower key lowers.
			case Key.Q: _thickness = Mathf.Min(0.05f, _thickness + 0.002f); PushParams(); break;
			case Key.A: _thickness = Mathf.Max(0.004f, _thickness - 0.002f); PushParams(); break;
			case Key.W: _jitter = Mathf.Min(2.0f, _jitter + 0.1f); PushParams(); break;
			case Key.S: _jitter = Mathf.Max(0f, _jitter - 0.1f); PushParams(); break;
			case Key.E: _segLen = Mathf.Min(0.12f, _segLen + 0.005f); PushParams(); break;
			case Key.D: _segLen = Mathf.Max(0.02f, _segLen - 0.005f); PushParams(); break;
			case Key.Z: _count = Mathf.Min(6, _count + 1); PushParams(); break;
			case Key.X: _count = Mathf.Max(1, _count - 1); PushParams(); break;
			case Key.C: _grow = Mathf.Min(6f, _grow + 1f); PushParams(); break;
			case Key.V: _grow = Mathf.Max(2f, _grow - 1f); PushParams(); break;
			case Key.B: _branches = !_branches; PushParams(); break;
		}
	}

	private void ApplyStyle()
	{
		int id = Styles[_styleIndex].Id;
		foreach (ShaderMaterial mat in _crackMats)
			mat.SetShaderParameter("style", id);
		UpdateHud();
	}

	// Push the current tuning values into every egg's crack material.
	private void PushParams()
	{
		foreach (ShaderMaterial mat in _crackMats)
		{
			mat.SetShaderParameter("crack_thickness", _thickness);
			mat.SetShaderParameter("crack_jitter", _jitter);
			mat.SetShaderParameter("crack_seg_len", _segLen);
			mat.SetShaderParameter("crack_count", _count);
			mat.SetShaderParameter("crack_grow", _grow);
			mat.SetShaderParameter("crack_branches", _branches);
		}
		UpdateHud();
	}

	private void UpdateHud()
	{
		_hud.Text =
			$"Crack style: {Styles[_styleIndex].Name}    [1] branching  [2] voronoi  [R] spin {(_rotate ? "on" : "off")}\n" +
			"\n" +
			"Bold-branching knobs (UPPER raises / LOWER lowers):\n" +
			$"  thickness  [Q/A]  {_thickness:0.000}\n" +
			$"  jitter     [W/S]  {_jitter:0.0}   (jaggedness)\n" +
			$"  seg length [E/D]  {_segLen:0.000}\n" +
			$"  count      [Z/X]  {_count}\n" +
			$"  grow/len   [C/V]  {_grow:0}\n" +
			$"  branches   [B]    {(_branches ? "on" : "off")}";
	}
}
