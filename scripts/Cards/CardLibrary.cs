using System.Collections.Generic;

// Card definitions for Slay the Eggs (M12). The default opening deck — mostly Unit cards (M12.5,
// Chunk 43): the endzone auto-battler is about DEPLOYING a force, so the starter is unit-heavy
// (units the clear majority) with just a couple of Action cards for flavour. Unit cards point at
// friendly scenes (Pikeman / Ally) so a played unit fights on your side; Action cards carry the
// effect their target unit runs (Chunk 33). Kept in one place so the UI and the headless test
// build the same starter deck.
public static class CardLibrary
{
	public static List<Card> StarterDeck() => new()
	{
		// Unit cards — the bulk of the opening deck (deploy a force).
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location.",
			spawnPath: "res://scenes/Pikeman.tscn"),
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location.",
			spawnPath: "res://scenes/Pikeman.tscn"),
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location.",
			spawnPath: "res://scenes/Pikeman.tscn"),
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location.",
			spawnPath: "res://scenes/Pikeman.tscn"),
		new Card("Recruit Fighter", Card.CardKind.Unit, 2, "Spawn a Fighter at a location.",
			spawnPath: "res://scenes/Ally.tscn"),
		new Card("Recruit Fighter", Card.CardKind.Unit, 2, "Spawn a Fighter at a location.",
			spawnPath: "res://scenes/Ally.tscn"),
		new Card("Recruit Fighter", Card.CardKind.Unit, 2, "Spawn a Fighter at a location.",
			spawnPath: "res://scenes/Ally.tscn"),
		// A couple of Action cards for flavour.
		new Card("Charge", Card.CardKind.Action, 1, "A friendly unit charges forward.",
			action: Card.ActionKind.Charge),
		new Card("Rally", Card.CardKind.Action, 1, "A friendly unit strikes the nearest foe.",
			action: Card.ActionKind.Rally),
	};

	// Cards offered as post-room REWARDS in a run (M12, Chunk 38). RunMap draws a few of these at
	// random after each room; picking one adds a copy to the run deck. Kept distinct (no duplicates)
	// so a single reward never offers the same card twice. A spread of the starter archetypes plus a
	// couple of beefier options so the deck can actually GROW in power across a run.
	public static List<Card> RewardPool() => new()
	{
		new Card("Recruit Pikeman", Card.CardKind.Unit, 1, "Spawn a Pikeman at a location.",
			spawnPath: "res://scenes/Pikeman.tscn"),
		new Card("Recruit Fighter", Card.CardKind.Unit, 2, "Spawn a Fighter at a location.",
			spawnPath: "res://scenes/Ally.tscn"),
		new Card("Vanguard", Card.CardKind.Unit, 3, "Spawn a sturdy Pikeman at a location.",
			spawnPath: "res://scenes/Pikeman.tscn"),
		new Card("Charge", Card.CardKind.Action, 1, "A friendly unit charges forward.",
			action: Card.ActionKind.Charge),
		new Card("Rally", Card.CardKind.Action, 1, "A friendly unit strikes the nearest foe.",
			action: Card.ActionKind.Rally),
		new Card("Firebolt", Card.CardKind.Action, 2, "A friendly unit hurls a bolt at the nearest foe.",
			action: Card.ActionKind.Firebolt),
		new Card("Brace", Card.CardKind.Action, 0, "A friendly unit braces (a brief defiant pop).",
			action: Card.ActionKind.Brace),
	};

	// Relic pool (M12, Chunk 39) — permanent, run-long passives. A boss room grants one of these at
	// random; RunInventory sums their kinds into the modifiers each battle reads. Kept distinct so a
	// single grant never picks the same relic twice.
	public static System.Collections.Generic.List<Relic> RelicPool() => new()
	{
		new Relic("Egg of Plenty", Relic.RelicKind.BonusEnergy, 1, "+1 card energy every round."),
		new Relic("Captain's Banner", Relic.RelicKind.BonusHandSize, 1, "Draw +1 card every round."),
		new Relic("Warlord's Crest", Relic.RelicKind.SpawnStrength, 2, "Spawned units gain +2 Strength."),
	};

	// Potion pool (M12, Chunk 39) — one-shot consumables. An event room grants one of these; the
	// player pops it for an immediate effect (extra energy now, or extra cards now).
	public static System.Collections.Generic.List<Potion> PotionPool() => new()
	{
		new Potion("Energy Draught", Potion.PotionKind.Energy, 2, "Gain +2 energy now."),
		new Potion("Scroll of Insight", Potion.PotionKind.Draw, 2, "Draw 2 cards now."),
	};
}
