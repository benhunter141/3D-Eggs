using Godot;
using System;
using System.Threading.Tasks;

// Chunk 18 (M5 — crowds): a headless stress harness that spawns N fighting units and
// reports the per-physics-frame cost, so we can see whether a crowd holds the 60 FPS
// budget (16.67 ms/frame) and measure the effect of the staggered target re-scan.
//
// Run:
//   godot --headless --path . res://scenes/Tests/Crowd.tscn
//
// For each crowd size it runs the SAME scene twice — once with every unit re-scanning
// UnitRegistry every physics frame (TargetRescanInterval = 1, the pre-throttle baseline),
// once with the staggered re-scan (interval 6) — so the A/B is measured under identical
// conditions. Every unit runs its real AI (Swordman/Bowman chase + kite, Pikeman leash),
// so each one queries the registry, the per-frame hotspot Chunk 17 optimised.
//
// Measurement notes: the engine's Performance.TimePhysicsProcess monitor reports the true
// CPU cost of the physics step regardless of headless real-time pacing, but C# GC pauses
// land on random frames and inflate the MEAN. We therefore report the MEDIAN (and min/p90)
// over a large sample — the median reflects the real steady compute cost, the p90/peak the
// GC tail. A GC.Collect() before each window clears pending garbage so a collection is less
// likely to fire mid-sample. The two armies start ~40 m apart with the pikemen on their
// slots, so units scan + hold rather than collide or spam combat logs during the window.
public partial class CrowdTest : Node3D
{
	private const int WarmupFrames = 30;
	private const int SampleFrames = 180;
	private const double BudgetMs = 1000.0 / 60.0;   // 16.67 ms = one 60 FPS frame
	private static readonly int[] Counts = { 50, 100 };

	private readonly PackedScene _swordman = GD.Load<PackedScene>("res://scenes/Swordman.tscn");
	private readonly PackedScene _bowman = GD.Load<PackedScene>("res://scenes/Bowman.tscn");
	private readonly PackedScene _pikeman = GD.Load<PackedScene>("res://scenes/Pikeman.tscn");

	public override void _Ready() => _ = RunSweep();

	private async Task RunSweep()
	{
		GD.Print("=== CrowdTest: frame-budget sweep (headless) ===");
		GD.Print($"warmup={WarmupFrames}, sample={SampleFrames} frames, budget={BudgetMs:0.00} ms/frame (60 FPS)");
		GD.Print("reporting MEDIAN physics-step ms (min..p90); mean is GC-noisy at this scale.");

		// A single config can be pinned from the command line so each point is measured from a
		// COLD process (the only reliable A/B — within one process the medians drift with run
		// order). Pass user args after a `++` separator, e.g.:
		//   godot --headless --path . res://scenes/Tests/Crowd.tscn ++ --count=100 --rescan=6
		int oneCount = ArgInt("--count=", 0);
		int oneRescan = ArgInt("--rescan=", 6);
		if (oneCount > 0)
		{
			await MeasureCrowd(oneCount, oneRescan);
		}
		else
		{
			// No args: in-process sweep (handy as a one-shot, but trust the min, not the median).
			foreach (int n in Counts)
			{
				await MeasureCrowd(n, rescanInterval: 1);   // baseline: every unit scans every frame
				await MeasureCrowd(n, rescanInterval: 6);   // throttled: staggered re-scan
			}
		}

		GD.Print("=== CrowdTest: done ===");
		GetTree().Quit();
	}

	// Parse an integer command-line user arg of the form "--name=123", or `fallback` if absent.
	private static int ArgInt(string prefix, int fallback)
	{
		foreach (string arg in OS.GetCmdlineUserArgs())
			if (arg.StartsWith(prefix) && int.TryParse(arg.Substring(prefix.Length), out int v))
				return v;
		return fallback;
	}

	private async Task MeasureCrowd(int count, int rescanInterval)
	{
		var crowd = new Node3D { Name = $"Crowd{count}_r{rescanInterval}" };
		AddChild(crowd);

		// Player-team anchor: pikemen read it from the "player" group for their slot origin.
		var anchor = new Node3D { Name = "Anchor" };
		crowd.AddChild(anchor);
		anchor.AddToGroup("player");
		anchor.GlobalPosition = new Vector3(0f, 0f, 20f);

		int perTeam = count / 2;
		// Enemy army near z=-20, player (pikemen) near z=+20 -> ~40 m apart: they scan + hold
		// without colliding or fighting during the short sample window.
		SpawnArmy(crowd, perTeam, new Vector3(0f, 0f, -20f), enemy: true, rescanInterval);
		SpawnArmy(crowd, perTeam, new Vector3(0f, 0f, 20f), enemy: false, rescanInterval);
		int spawned = perTeam * 2;

		// Let the scene settle and the registry fill before timing.
		for (int i = 0; i < WarmupFrames; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		// Clear pending garbage so a collection is less likely to fire mid-window.
		GC.Collect();
		GC.WaitForPendingFinalizers();

		var phys = new double[SampleFrames];
		for (int i = 0; i < SampleFrames; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			phys[i] = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
		}

		Array.Sort(phys);
		double min = phys[0];
		double median = phys[SampleFrames / 2];
		double p90 = phys[(int)(SampleFrames * 0.9)];
		bool underBudget = median <= BudgetMs;
		GD.Print($"[N={spawned}, rescan every {rescanInterval}f] physics median={median:0.000} ms " +
			$"({min:0.000}..p90 {p90:0.000}) -> {(underBudget ? "WITHIN" : "OVER")} {BudgetMs:0.00} ms budget");

		// Tear down before the next run so the registry buckets and counts reset cleanly.
		crowd.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	// Spawn `n` units in a square grid around `center`. Enemy armies alternate Swordman/Bowman;
	// player armies are Pikemen placed on their formation slots (so they hold, not clump).
	private void SpawnArmy(Node3D parent, int n, Vector3 center, bool enemy, int rescanInterval)
	{
		int cols = Mathf.CeilToInt(Mathf.Sqrt(n));
		const float spacing = 1.5f;
		for (int i = 0; i < n; i++)
		{
			int row = i / cols, col = i % cols;
			float x = (col - cols * 0.5f) * spacing;
			float z = (enemy ? -1f : 1f) * row * spacing;

			Unit u = enemy
				? (i % 2 == 0 ? _swordman : _bowman).Instantiate<Unit>()
				: _pikeman.Instantiate<Unit>();
			u.MaxHealth = 1_000_000f;            // survive the window so the population stays fixed
			u.TargetRescanInterval = rescanInterval;

			// Pikemen hold a slot relative to the anchor; placing the slot at the spawn grid
			// cell means they start settled and don't pile onto a single point.
			if (u is Ally ally)
				ally.FormationOffset = new Vector3(x, 0f, z);

			parent.AddChild(u);
			u.GlobalPosition = center + new Vector3(x, 0f, z);
		}
	}
}
