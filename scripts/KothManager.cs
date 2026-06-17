using Godot;

// King-of-the-Hill mode controller + HUD (M11, Chunk 31). A CanvasLayer that owns the
// mode's match state on top of a CapturePoint (Chunk 30): it shows a live score / period
// countdown / contest readout, and decides the match —
//   WIN  — the player's team reaches WinScore captured periods, OR every enemy unit is gone.
//   LOSE — the player dies, OR the enemy team reaches WinScore periods.
// Lose is checked first each frame (same rule as GameManager) so a last-gasp ally can't
// flash VICTORY over a death. Once ended it only listens for the "restart" action.
//
// It finds the first CapturePoint in the scene (a KotH level has exactly one hill) and
// drives the win/lose UI through the shared "victory" / "game_over" groups, so the same
// ResultMenu.tscn works here as in every other level.
public partial class KothManager : CanvasLayer
{
	[Export] public int WinScore = 3;

	private CapturePoint _zone;
	private Label _scoreLabel;
	private Label _timerLabel;
	private Label _stateLabel;
	private Label _bannerLabel;

	private bool _ended;
	private bool _sawEnemies;

	public override void _Ready()
	{
		_scoreLabel  = GetNode<Label>("Panel/VBox/ScoreLabel");
		_timerLabel  = GetNode<Label>("Panel/VBox/TimerLabel");
		_stateLabel  = GetNode<Label>("Panel/VBox/StateLabel");
		_bannerLabel = GetNode<Label>("Panel/VBox/BannerLabel");

		_bannerLabel.Text = $"KING OF THE HILL — first to {WinScore} holds";

		_zone = FindCapturePoint(GetTree().Root);
		if (_zone != null)
		{
			_zone.PeriodEnded += OnPeriodEnded;
			_zone.StateChanged += _ => RefreshState();
		}
		else
		{
			GD.PrintErr("[KothManager] No CapturePoint found in scene.");
		}

		RefreshScore();
		RefreshState();
	}

	public override void _Process(double delta)
	{
		if (_zone != null)
		{
			int secs = Mathf.CeilToInt(Mathf.Max(0f, _zone.PeriodTimer));
			_timerLabel.Text = $"Next point in  {secs}s";
		}

		if (_ended)
		{
			if (Input.IsActionJustPressed("restart"))
				GetTree().ReloadCurrentScene();
			return;
		}

		// Lose takes precedence over a simultaneous win, mirroring GameManager.
		Player player = GetTree().GetFirstNodeInGroup("player") as Player;
		if (player == null || player.IsDead)
		{
			Lose("player died");
			return;
		}

		// Bonus win: wipe out the enemy host entirely (after at least one has existed).
		int enemies = 0;
		foreach (Node n in GetTree().GetNodesInGroup("units"))
			if (n is Unit u && !u.IsDead && u.Team == Unit.TeamId.Enemy)
				enemies++;

		if (enemies > 0)
			_sawEnemies = true;
		else if (_sawEnemies)
			Win("enemy host routed");
	}

	private void OnPeriodEnded(int playerScore, int enemyScore, string holder)
	{
		RefreshScore();
		if (_ended)
			return;

		if (playerScore >= WinScore)
			Win($"held {WinScore} periods");
		else if (enemyScore >= WinScore)
			Lose($"enemy held {WinScore} periods");
	}

	private void RefreshScore()
	{
		if (_zone == null) return;
		_scoreLabel.Text = $"YOU  {_zone.PlayerScore}    —    {_zone.EnemyScore}  ENEMY";
	}

	private void RefreshState()
	{
		if (_zone == null) return;
		(string text, Color color) = _zone.State switch
		{
			CapturePoint.ZoneState.PlayerHeld => ("HILL: you hold it",  new Color(0.55f, 0.7f, 1f)),
			CapturePoint.ZoneState.EnemyHeld  => ("HILL: enemy holds it", new Color(1f, 0.45f, 0.45f)),
			CapturePoint.ZoneState.Contested  => ("HILL: CONTESTED",     new Color(1f, 0.9f, 0.35f)),
			_                                 => ("HILL: neutral",       new Color(0.75f, 0.77f, 0.8f)),
		};
		_stateLabel.Text = text;
		_stateLabel.AddThemeColorOverride("font_color", color);
	}

	private void Win(string why)
	{
		_ended = true;
		foreach (Node n in GetTree().GetNodesInGroup("victory"))
			if (n is CanvasItem ci)
				ci.Visible = true;
		GD.Print($"[KothManager] VICTORY ({why})");
	}

	private void Lose(string why)
	{
		_ended = true;
		foreach (Node n in GetTree().GetNodesInGroup("game_over"))
			if (n is CanvasItem ci)
				ci.Visible = true;
		GD.Print($"[KothManager] DEFEAT ({why})");
	}

	private static CapturePoint FindCapturePoint(Node root)
	{
		if (root is CapturePoint cp)
			return cp;
		foreach (Node child in root.GetChildren())
		{
			CapturePoint found = FindCapturePoint(child);
			if (found != null)
				return found;
		}
		return null;
	}
}
