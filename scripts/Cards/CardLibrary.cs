using System.Collections.Generic;

// Card definitions for Slay the Eggs (M12). For now just the default opening deck — a spread of
// Unit cards (spawn a unit at a location) and Action cards (a friendly unit performs the action).
// Costs and kinds only drive the display + the pile cycle in Chunk 32; their play behaviour wires
// up in Chunk 33 and energy gating in Chunk 35. Kept in one place so the UI and the headless test
// build the same starter deck.
public static class CardLibrary
{
	public static List<Card> StarterDeck() => new()
	{
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location."),
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location."),
		new Card("Recruit Swordman", Card.CardKind.Unit, 2, "Spawn a Swordman at a location."),
		new Card("Recruit Bowman", Card.CardKind.Unit, 2, "Spawn a Bowman at a location."),
		new Card("Charge", Card.CardKind.Action, 1, "A friendly unit charges forward."),
		new Card("Charge", Card.CardKind.Action, 1, "A friendly unit charges forward."),
		new Card("Rally", Card.CardKind.Action, 1, "A friendly unit attacks the nearest foe."),
		new Card("Rally", Card.CardKind.Action, 1, "A friendly unit attacks the nearest foe."),
		new Card("Firebolt", Card.CardKind.Action, 2, "A friendly unit hurls a bolt (Int)."),
		new Card("Brace", Card.CardKind.Action, 0, "A friendly unit braces in place."),
	};
}
