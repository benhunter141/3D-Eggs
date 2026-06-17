using System.Collections.Generic;

// Card definitions for Slay the Eggs (M12). The default opening deck — a spread of Unit cards
// (spawn a FRIENDLY unit at a location) and Action cards (a friendly unit performs the action).
// Unit cards point at friendly scenes (Pikeman / Ally) so a played unit fights on your side;
// Action cards carry the effect their target unit runs (Chunk 33). Energy gating arrives in
// Chunk 35. Kept in one place so the UI and the headless test build the same starter deck.
public static class CardLibrary
{
	public static List<Card> StarterDeck() => new()
	{
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location.",
			spawnPath: "res://scenes/Pikeman.tscn"),
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location.",
			spawnPath: "res://scenes/Pikeman.tscn"),
		new Card("Recruit Fighter", Card.CardKind.Unit, 2, "Spawn a Fighter at a location.",
			spawnPath: "res://scenes/Ally.tscn"),
		new Card("Recruit Fighter", Card.CardKind.Unit, 2, "Spawn a Fighter at a location.",
			spawnPath: "res://scenes/Ally.tscn"),
		new Card("Charge", Card.CardKind.Action, 1, "A friendly unit charges forward.",
			action: Card.ActionKind.Charge),
		new Card("Charge", Card.CardKind.Action, 1, "A friendly unit charges forward.",
			action: Card.ActionKind.Charge),
		new Card("Rally", Card.CardKind.Action, 1, "A friendly unit strikes the nearest foe.",
			action: Card.ActionKind.Rally),
		new Card("Rally", Card.CardKind.Action, 1, "A friendly unit strikes the nearest foe.",
			action: Card.ActionKind.Rally),
		new Card("Firebolt", Card.CardKind.Action, 2, "A friendly unit hurls a bolt at the nearest foe.",
			action: Card.ActionKind.Firebolt),
		new Card("Brace", Card.CardKind.Action, 0, "A friendly unit braces (a brief defiant pop).",
			action: Card.ActionKind.Brace),
	};

	// Candidate cards offered as post-room rewards (M12, Chunk 38). After a fight the run offers a few
	// of these for the player to pick one to ADD to the deck — a spread of friendly units and actions
	// to grow the deck across the run. RunMap clones from this pool, so callers may mutate the returned
	// list freely. Relics & potions (the non-card drops) arrive in Chunk 39.
	public static List<Card> RewardPool() => new()
	{
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location.",
			spawnPath: "res://scenes/Pikeman.tscn"),
		new Card("Recruit Fighter", Card.CardKind.Unit, 2, "Spawn a Fighter at a location.",
			spawnPath: "res://scenes/Ally.tscn"),
		new Card("Charge", Card.CardKind.Action, 1, "A friendly unit charges forward.",
			action: Card.ActionKind.Charge),
		new Card("Rally", Card.CardKind.Action, 1, "A friendly unit strikes the nearest foe.",
			action: Card.ActionKind.Rally),
		new Card("Firebolt", Card.CardKind.Action, 2, "A friendly unit hurls a bolt at the nearest foe.",
			action: Card.ActionKind.Firebolt),
		new Card("Brace", Card.CardKind.Action, 0, "A friendly unit braces (a brief defiant pop).",
			action: Card.ActionKind.Brace),
	};
}
