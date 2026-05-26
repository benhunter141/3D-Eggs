using Godot;

// Match-state authority for the 5v5 slice. Each frame it decides the match's fate:
//   LOSE — the player has died (or vanished).
//   WIN  — every enemy-team unit is gone (after at least one ever existed).
// Whichever fires first ends the match; the other can no longer trigger. Checking
// the lose condition *first* is what stops a stray VICTORY from appearing after the
// allies mop up the last skeletons in the seconds following the player's death.
//
// The win/lose UI lives in the "victory" / "game_over" groups (the shared Restart
// button is in both). Player.OnDeath already reveals "game_over" on death; this node
// reveals "victory" on a win. Once ended, the "restart" action reloads the scene.
public partial class GameManager : Node
{
	private bool _ended;       // match decided — stop judging, just listen for restart
	private bool _sawEnemies;  // don't "win" on frame 0 before any enemy exists

	public override void _Process(double delta)
	{
		if (_ended)
		{
			// Mirror of the Restart button, for keyboard/gamepad players.
			if (Input.IsActionJustPressed("restart"))
				GetTree().ReloadCurrentScene();
			return;
		}

		// Lose takes precedence: if the player is dead, the match is over regardless
		// of how many skeletons the allies are still chewing through.
		Player player = GetPlayer();
		if (player == null || player.IsDead)
		{
			Lose();
			return;
		}

		// Win: all enemy-team units cleared. Gate on having seen at least one so we
		// don't declare victory on the first frame before the scene is populated.
		int enemies = 0;
		foreach (Node n in GetTree().GetNodesInGroup("units"))
			if (n is Unit u && !u.IsDead && u.Team == Unit.TeamId.Enemy)
				enemies++;

		if (enemies > 0)
		{
			_sawEnemies = true;
			return;
		}

		if (_sawEnemies)
			Win();
	}

	private Player GetPlayer()
	{
		foreach (Node n in GetTree().GetNodesInGroup("player"))
			if (n is Player p)
				return p;
		return null;
	}

	private void Win()
	{
		_ended = true;
		foreach (Node n in GetTree().GetNodesInGroup("victory"))
			if (n is CanvasItem ci)
				ci.Visible = true;
		GD.Print("[GameManager] VICTORY");
	}

	// The GAME OVER label + Restart button are revealed by Player.OnDeath (the
	// "game_over" group). Here we just latch the match closed so Win() can't fire.
	private void Lose()
	{
		_ended = true;
		GD.Print("[GameManager] DEFEAT");
	}
}
