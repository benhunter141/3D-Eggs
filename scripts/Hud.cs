using Godot;

// In-game HUD (M9): ONE always-visible panel that lists every control in a single place, and
// shows the captain's CURRENT weapon live at the top so the weapon-swap key (Q) is obvious in
// the moment — not just buried on the menu. Drop this scene into a level; it finds the player
// through the "player" group and refreshes the weapon line whenever the weapon changes.
public partial class Hud : CanvasLayer
{
	private Label _weaponLabel;
	private Player _player;
	private Player.WeaponType _lastWeapon;
	private bool _haveWeapon;

	public override void _Ready()
	{
		_weaponLabel = GetNode<Label>("Controls/VBox/WeaponLabel");
	}

	public override void _Process(double delta)
	{
		// The captain registers in the "player" group in its _Ready; resolve lazily and re-resolve
		// if it's freed (e.g. after a restart reloads the scene).
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as Player;
			_haveWeapon = false;
		}
		if (_player == null)
			return;

		Player.WeaponType w = _player.CurrentWeapon;
		if (!_haveWeapon || w != _lastWeapon)
		{
			_lastWeapon = w;
			_haveWeapon = true;
			// Show the weapon AND its attack motion (M18) so a swap reads as a new move, not just a new name.
			_weaponLabel.Text = $"WEAPON:  {w}  ({_player.CurrentAttackStyle})";
		}
	}
}
