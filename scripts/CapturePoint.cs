using Godot;

// A King-of-the-Hill capture zone (M11, Chunk 30). An Area3D tracking which team has
// living units inside. Every PeriodSeconds it awards a point to the team that HOLDS
// the zone (sole occupant); contested (both teams present) = no award. Exposes state,
// scores, and timer for the KotH HUD (Chunk 31) and the card-energy hook (M12).
//
// Detection uses GetOverlappingBodies() each physics frame — robust against deaths
// (IsDead filtered out), freed units, and disabled collision shapes, with no stale
// counts from missed enter/exit signals. A level typically has 1-3 zones, so the per-
// frame cost is negligible.
public partial class CapturePoint : Area3D
{
	[Export] public float PeriodSeconds = 15.0f;

	public int PlayerScore { get; private set; }
	public int EnemyScore { get; private set; }

	public enum ZoneState { Neutral, PlayerHeld, EnemyHeld, Contested }
	public ZoneState State { get; private set; } = ZoneState.Neutral;

	public float PeriodTimer { get; private set; }
	public int PeriodCount { get; private set; }

	[Signal] public delegate void PeriodEndedEventHandler(int playerScore, int enemyScore, string holder);
	[Signal] public delegate void StateChangedEventHandler(string newState);

	private StandardMaterial3D _zoneMat;

	private static readonly Color NeutralColor = new(0.5f, 0.5f, 0.5f, 0.35f);
	private static readonly Color PlayerColor = new(0.2f, 0.4f, 1.0f, 0.45f);
	private static readonly Color EnemyColor = new(1.0f, 0.2f, 0.2f, 0.45f);
	private static readonly Color ContestedColor = new(1.0f, 0.9f, 0.2f, 0.45f);

	public override void _Ready()
	{
		PeriodTimer = PeriodSeconds;

		var mesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		if (mesh?.GetActiveMaterial(0) is StandardMaterial3D src)
		{
			_zoneMat = (StandardMaterial3D)src.Duplicate();
			mesh.SetSurfaceOverrideMaterial(0, _zoneMat);
		}
		UpdateVisual();
	}

	public override void _PhysicsProcess(double delta)
	{
		EvaluateState();

		PeriodTimer -= (float)delta;
		if (PeriodTimer <= 0f)
		{
			AwardPeriod();
			PeriodTimer += PeriodSeconds;
			PeriodCount++;
		}
	}

	private void EvaluateState()
	{
		int players = 0, enemies = 0;
		foreach (Node3D body in GetOverlappingBodies())
		{
			if (body is Unit u && !u.IsDead)
			{
				if (u.Team == Unit.TeamId.Player)
					players++;
				else
					enemies++;
			}
		}

		ZoneState prev = State;
		if (players > 0 && enemies > 0)
			State = ZoneState.Contested;
		else if (players > 0)
			State = ZoneState.PlayerHeld;
		else if (enemies > 0)
			State = ZoneState.EnemyHeld;
		else
			State = ZoneState.Neutral;

		if (State != prev)
		{
			UpdateVisual();
			EmitSignal(SignalName.StateChanged, State.ToString());
			GD.Print($"[CapturePoint] {Name} -> {State}");
		}
	}

	private void AwardPeriod()
	{
		string holder = "none";
		if (State == ZoneState.PlayerHeld)
		{
			PlayerScore++;
			holder = "Player";
		}
		else if (State == ZoneState.EnemyHeld)
		{
			EnemyScore++;
			holder = "Enemy";
		}

		GD.Print($"[CapturePoint] {Name} period {PeriodCount + 1}: state={State}, holder={holder}, score P={PlayerScore} E={EnemyScore}");
		EmitSignal(SignalName.PeriodEnded, PlayerScore, EnemyScore, holder);
	}

	private void UpdateVisual()
	{
		if (_zoneMat == null) return;

		_zoneMat.AlbedoColor = State switch
		{
			ZoneState.PlayerHeld => PlayerColor,
			ZoneState.EnemyHeld => EnemyColor,
			ZoneState.Contested => ContestedColor,
			_ => NeutralColor,
		};
	}
}
