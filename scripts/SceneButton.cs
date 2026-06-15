using Godot;

// Reusable navigation button for the front-end and result menus.
//   ScenePath set   → switch to that scene (Level Select → a level, or a level's
//                     "Level Select" button → back to the menu).
//   ScenePath empty → reload the current scene (the "Retry" button).
// One script covers every menu jump, so menus are pure scene + exported-path data.
public partial class SceneButton : Button
{
	[Export(PropertyHint.File, "*.tscn")] public string ScenePath = "";

	public override void _Pressed()
	{
		if (string.IsNullOrEmpty(ScenePath))
			GetTree().ReloadCurrentScene();
		else
			GetTree().ChangeSceneToFile(ScenePath);
	}
}
