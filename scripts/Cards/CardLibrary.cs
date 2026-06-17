using System.Collections.Generic;

// Card definitions for Slay the Eggs (M12). For now just the default opening deck — a spread of
// Unit cards (spawn a unit at a location) and Action cards (a friendly unit performs the action).
// Each card carries WHAT it does as data (Spawns / Action), which CardResolver turns into a real
// battlefield spawn / unit behaviour (Chunk 33); energy gating arrives in Chunk 35. Kept in one
// place so the UI and the headless test build the same starter deck.
public static class CardLibrary
{
	public static List<Card> StarterDeck() => new()
	{
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location.", spawns: Card.UnitKind.Pikeman),
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location.", spawns: Card.UnitKind.Pikeman),
		new Card("Recruit Swordman", Card.CardKind.Unit, 2, "Spawn a Swordman at a location.", spawns: Card.UnitKind.Swordman),
		new Card("Recruit Bowman", Card.CardKind.Unit, 2, "Spawn a Bowman at a location.", spawns: Card.UnitKind.Bowman),
		new Card("Charge", Card.CardKind.Action, 1, "A friendly unit charges forward.", action: Card.ActionKind.Charge),
		new Card("Charge", Card.CardKind.Action, 1, "A friendly unit charges forward.", action: Card.ActionKind.Charge),
		new Card("Rally", Card.CardKind.Action, 1, "A friendly unit attacks the nearest foe.", action: Card.ActionKind.Attack),
		new Card("Rally", Card.CardKind.Action, 1, "A friendly unit attacks the nearest foe.", action: Card.ActionKind.Attack),
		new Card("Firebolt", Card.CardKind.Action, 2, "A friendly unit hurls a bolt (Int).", action: Card.ActionKind.Firebolt),
		new Card("Brace", Card.CardKind.Action, 0, "A friendly unit braces, repelling its nearest foe.", action: Card.ActionKind.Brace),
	};
}
