using Godot;

// Restart button shown on the game-over screen. It's in the "game_over" group,
// so Player.OnDeath() flips it visible alongside the GAME OVER label. Pressing it
// reloads the current scene from scratch — a clean reset of the whole match.
public partial class RestartButton : Button
{
	public override void _Pressed()
	{
		GetTree().ReloadCurrentScene();
	}
}
