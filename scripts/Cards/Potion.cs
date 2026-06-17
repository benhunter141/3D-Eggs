// Potions for Slay the Eggs (M12, Chunk 39). A potion is a ONE-SHOT consumable: collected through
// the run, held until you choose to pop it, and spent for an immediate effect (a burst of energy
// this round, or a few extra cards drawn). Unlike a Relic (permanent passive), a potion fires once
// and is gone — Consumed flips true and Apply refuses to run again.
//
// Pure model (plain C#): a potion APPLIES ITS OWN EFFECT against the battle's two mutable pure-model
// pieces — the EnergyPool and the Deck — so "consume + trigger" is fully headless-testable without
// any Godot tree. CardBattle just offers a button per potion and calls Apply.
public class Potion
{
	// Which one-shot effect this potion fires when popped. Magnitude scales it.
	public enum PotionKind
	{
		Energy,   // +Magnitude card energy right now (on top of the round's grant)
		Draw,     // draw Magnitude extra cards into the hand right now
	}

	public string Title;
	public string Description;
	public PotionKind Kind;
	public int Magnitude;
	public bool Consumed { get; private set; }

	public Potion(string title, PotionKind kind, int magnitude, string description = "")
	{
		Title = title;
		Kind = kind;
		Magnitude = magnitude;
		Description = description;
	}

	// Fire this potion's effect once against the live battle pieces, then mark it spent. Returns
	// false (changing nothing) if it was already consumed — a potion is strictly one-shot.
	public bool Apply(EnergyPool energy, Deck deck)
	{
		if (Consumed)
			return false;
		switch (Kind)
		{
			case PotionKind.Energy:
				energy?.Add(Magnitude);
				break;
			case PotionKind.Draw:
				deck?.Draw(Magnitude);
				break;
		}
		Consumed = true;
		return true;
	}

	// An independent copy, so a potion taken into the run isn't aliased to the pool definition.
	public Potion Clone() => new Potion(Title, Kind, Magnitude, Description);
}
