using Godot;

// Reusable in-game pause overlay (Esc / gamepad Start). Drop this scene into a real-time
// battle level as a CanvasLayer that PROCESSES while the tree is frozen (ProcessMode = Always),
// so it keeps receiving input after it pauses everything else.
//
// Esc toggles the overlay. Opening remembers whatever pause state was already in effect and
// then freezes the battlefield (GetTree().Paused = true); closing restores that prior state, so
// the overlay nests cleanly without clobbering a pause some other system set. Resume closes it;
// Level Select always lifts the pause before leaving (GetTree().Paused survives a scene change,
// so the menu would otherwise open frozen).
public partial class PauseMenu : CanvasLayer
{
	[Export(PropertyHint.File, "*.tscn")] public string MenuScenePath = "res://scenes/Menu/LevelSelect.tscn";

	private Control _overlay;
	private bool _open;
	private bool _wasPaused;

	public override void _Ready()
	{
		_overlay = GetNode<Control>("Overlay");
		_overlay.Visible = false;
		GetNode<Button>("Overlay/Panel/VBox/ResumeButton").Pressed += Close;
		GetNode<Button>("Overlay/Panel/VBox/MenuButton").Pressed += ToMenu;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.IsActionPressed("pause") && !@event.IsEcho())
		{
			if (_open) Close(); else Open();
			GetViewport().SetInputAsHandled();
		}
	}

	private void Open()
	{
		_open = true;
		_wasPaused = GetTree().Paused;
		GetTree().Paused = true;
		_overlay.Visible = true;
	}

	private void Close()
	{
		_open = false;
		_overlay.Visible = false;
		GetTree().Paused = _wasPaused;
	}

	private void ToMenu()
	{
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(MenuScenePath);
	}
}
