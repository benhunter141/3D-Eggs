using Godot;

// Card data model (M12 "Slay the Eggs", Chunk 32). A card is either a UNIT card (played
// onto a battlefield location to spawn that unit for your team) or an ACTION card (played
// onto a friendly unit, who then performs the action). Chunk 32 only needs the data the
// piles + hand UI display — title, kind, energy cost, description. The play-targeting
// behaviour (Unit→location, Action→friendly unit) wires up in Chunk 33; HP/Str/Int and
// energy gating arrive in Chunks 34/35.
//
// A plain C# class (not a Godot object): the deck is pure model, fully headless-testable.
public class Card
{
	public enum CardKind { Unit, Action }

	public string Title;
	public CardKind Kind;
	public int EnergyCost;
	public string Description;

	public Card(string title, CardKind kind, int energyCost, string description = "")
	{
		Title = title;
		Kind = kind;
		EnergyCost = energyCost;
		Description = description;
	}

	// An independent copy. Decks hold many duplicate cards; cloning the starter list keeps
	// the piles from aliasing the same Card instances (so future per-card state stays local).
	public Card Clone() => new Card(Title, Kind, EnergyCost, Description);
}
