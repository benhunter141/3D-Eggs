using Godot;

// Card data model (M12 "Slay the Eggs", Chunk 32). A card is either a UNIT card (played
// onto a battlefield location to spawn that unit for your team) or an ACTION card (played
// onto a friendly unit, who then performs the action). The play-targeting behaviour itself
// (Unit→location spawn, Action→friendly-unit behaviour) is bridged to the battlefield by
// CardResolver (Chunk 33), driven by the Spawns / Action enums below; HP/Str/Int and energy
// gating arrive in Chunks 34/35.
//
// A plain C# class (not a Godot object): the deck stays pure model, fully headless-testable.
// What a card DOES is just data here — which unit to spawn, which behaviour to trigger — so
// CardResolver (the only Godot-touching card code) can act on it without the model importing
// any engine types.
public class Card
{
	public enum CardKind { Unit, Action }

	// UNIT cards: which friendly unit this card deploys at the chosen location. (None on Action cards.)
	public enum UnitKind { None, Pikeman, Swordman, Bowman }

	// ACTION cards: what the chosen friendly unit does when the card is played on it. (None on Unit cards.)
	//   Charge   — the unit lunges forward along its facing (a self-shove).
	//   Attack   — the unit strikes its nearest foe (weapon damage, no knockback).
	//   Brace    — the unit digs in, striking + repelling its nearest foe (damage + a small shove).
	//   Firebolt — the unit hurls a magic bolt at its nearest foe (Int-scaled later, Chunk 34).
	public enum ActionKind { None, Charge, Attack, Brace, Firebolt }

	public string Title;
	public CardKind Kind;
	public int EnergyCost;
	public string Description;
	public UnitKind Spawns;     // Unit cards: which unit to deploy at the target location
	public ActionKind Action;   // Action cards: which behaviour the target friendly unit performs

	public Card(string title, CardKind kind, int energyCost, string description = "",
		UnitKind spawns = UnitKind.None, ActionKind action = ActionKind.None)
	{
		Title = title;
		Kind = kind;
		EnergyCost = energyCost;
		Description = description;
		Spawns = spawns;
		Action = action;
	}

	// An independent copy. Decks hold many duplicate cards; cloning the starter list keeps
	// the piles from aliasing the same Card instances (so future per-card state stays local).
	public Card Clone() => new Card(Title, Kind, EnergyCost, Description, Spawns, Action);
}
