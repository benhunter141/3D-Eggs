using Godot;

// Card data model (M12 "Slay the Eggs", Chunk 32; play-targeting added Chunk 33). A card is
// either a UNIT card (played onto a battlefield LOCATION to spawn that unit for your team) or
// an ACTION card (played onto a FRIENDLY UNIT, who then performs the action). Which target a
// card needs follows from its Kind (see Target), so the play layer (CardPlay) can route a click
// without per-card flags. A Unit card carries the SCENE it spawns (SpawnPath); an Action card
// carries WHICH effect it runs (Action). HP/Str/Int and energy gating arrive in Chunks 34/35.
//
// A plain C# class (not a Godot node): the deck is pure model, fully headless-testable.
public class Card
{
	public enum CardKind { Unit, Action }

	// What a played card must be aimed at. Derived from Kind — Unit cards drop a unit on the
	// ground (a location), Action cards empower one of your own units (a friendly unit).
	public enum TargetKind { Location, FriendlyUnit }

	// The concrete effect an Action card runs when its target unit performs it (Chunk 33).
	// None = a Unit card (it spawns instead of acting). Real units map these to behaviour in
	// Unit.PerformAction; the headless test only checks the right card reaches the right unit.
	public enum ActionKind { None, Charge, Rally, Firebolt, Brace }

	public string Title;
	public CardKind Kind;
	public int EnergyCost;
	public string Description;

	// Unit cards only: the scene instanced for the spawned (friendly) unit.
	public string SpawnPath;
	// Action cards only: which effect the targeted friendly unit performs.
	public ActionKind Action;

	public Card(string title, CardKind kind, int energyCost, string description = "",
		string spawnPath = null, ActionKind action = ActionKind.None)
	{
		Title = title;
		Kind = kind;
		EnergyCost = energyCost;
		Description = description;
		SpawnPath = spawnPath;
		Action = action;
	}

	// What this card must be played onto: Unit cards → a location, Action cards → a friendly unit.
	public TargetKind Target => Kind == CardKind.Unit ? TargetKind.Location : TargetKind.FriendlyUnit;

	// An independent copy. Decks hold many duplicate cards; cloning the starter list keeps
	// the piles from aliasing the same Card instances (so future per-card state stays local).
	public Card Clone() => new Card(Title, Kind, EnergyCost, Description, SpawnPath, Action);
}
