// Relics for Slay the Eggs (M12, Chunk 39). A relic is a PASSIVE, run-long modifier: once
// collected it stays in the run inventory and tweaks every battle that follows (more energy each
// round, a bigger opening hand, sturdier spawned units). Relics never get consumed — they're the
// permanent power your run accumulates, the counterpart to one-shot Potions.
//
// Pure model (plain C#, no Godot types). A relic doesn't apply itself — it just carries WHAT it
// changes and by HOW MUCH; RunInventory sums them into aggregate modifiers the battle reads. That
// keeps the whole thing headless-testable.
public class Relic
{
	// Which run-long modifier this relic provides. Magnitude scales each one.
	public enum RelicKind
	{
		BonusEnergy,        // +Magnitude card energy granted every round (territory bonus stacks on top)
		BonusHandSize,      // +Magnitude extra cards drawn each round
		SpawnStrength,      // spawned Unit cards get +Magnitude Strength (hit harder)
	}

	public string Title;
	public string Description;
	public RelicKind Kind;
	public int Magnitude;

	public Relic(string title, RelicKind kind, int magnitude, string description = "")
	{
		Title = title;
		Kind = kind;
		Magnitude = magnitude;
		Description = description;
	}

	// An independent copy, so a relic taken into the run isn't aliased to the pool definition.
	public Relic Clone() => new Relic(Title, Kind, Magnitude, Description);
}
