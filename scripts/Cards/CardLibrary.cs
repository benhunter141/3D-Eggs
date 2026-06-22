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

	// ── Co-op Card Brawl (M15, Chunk 68) ────────────────────────────────────────────────────────────
	// The Brawl is a different game: two weak eggs share ONE hand of PLAYER-BUFF cards. Each card is
	// resolved against whoever played it (no target click) — a weapon arms that egg, an ability grants it
	// a spell, a soldier spawns a subordinate to fight beside it. The opening deck is soldier-heavy (you
	// need bodies to survive the first wave) with a couple of weapons + a spell to start arming up.

	// A weapon-grant card: arms the triggering egg with `weapon` (runtime EquipWeapon).
	private static Card WeaponCard(string title, int cost, Player.WeaponType weapon, string desc) =>
		new Card(title, Card.CardKind.PlayerBuff, cost, desc,
			buff: Card.BuffKind.Weapon, buffWeapon: weapon);

	// An ability-grant card: hands the triggering egg a castable `ability` (GrantAbility).
	private static Card AbilityCard(string title, int cost, Player.AbilityType ability, string desc) =>
		new Card(title, Card.CardKind.PlayerBuff, cost, desc,
			buff: Card.BuffKind.Ability, buffAbility: ability);

	// A soldier card: spawns a subordinate on the triggering egg's team.
	private static Card SoldierCard(int cost = 1) =>
		new Card("Soldier", Card.CardKind.PlayerBuff, cost, "Spawn a soldier to fight beside you.",
			spawnPath: "res://scenes/Ally.tscn", buff: Card.BuffKind.Soldier);

	// The shared opening deck for the Co-op Card Brawl. Abilities are granted PER TURN (they wipe at the end
	// of each wave), so an ability card is a recurring choice — replay it to re-arm the spell next round.
	public static List<Card> BrawlDeck() => new()
	{
		SoldierCard(), SoldierCard(), SoldierCard(), SoldierCard(), SoldierCard(),
		WeaponCard("Sword", 2, Player.WeaponType.Sword, "Arm your egg with a sword (strong knockback)."),
		WeaponCard("Spear", 2, Player.WeaponType.Spear, "Arm your egg with a long-reach spear."),
		WeaponCard("Mace",  2, Player.WeaponType.Mace,  "Arm your egg with a mace (hardest fling)."),
		AbilityCard("Fireball", 2, Player.AbilityType.Fireball, "Ability (this turn): aim + hurl a magic bolt. Hotkey 1–4 / gamepad."),
		AbilityCard("Enrage",   2, Player.AbilityType.Enrage,   "Ability (this turn): instantly double your attack power for a few seconds."),
		AbilityCard("Heal",     2, Player.AbilityType.Heal,     "Ability (this turn): instantly restore a chunk of your HP."),
		AbilityCard("Dash",     1, Player.AbilityType.Dash,     "Ability (this turn): aim + blink toward a spot. Hotkey 1–4 / gamepad."),
	};

	// Cards the Brawl could offer as upgrades (kept distinct). Not yet wired to a reward screen —
	// here so the pool of brawl cards lives in one place (parity with the StS RewardPool).
	public static List<Card> BrawlPool() => new()
	{
		SoldierCard(),
		WeaponCard("Sword", 2, Player.WeaponType.Sword, "Arm your egg with a sword (strong knockback)."),
		WeaponCard("Axe",   3, Player.WeaponType.Axe,   "Arm your egg with an axe (biggest single hit)."),
		WeaponCard("Mace",  2, Player.WeaponType.Mace,  "Arm your egg with a mace (hardest fling)."),
		AbilityCard("Fireball", 2, Player.AbilityType.Fireball, "Ability (this turn): aim + hurl a magic bolt. Hotkey 1–4 / gamepad."),
		AbilityCard("Enrage",   2, Player.AbilityType.Enrage,   "Ability (this turn): instantly double your attack power for a few seconds."),
		AbilityCard("Heal",     2, Player.AbilityType.Heal,     "Ability (this turn): instantly restore a chunk of your HP."),
		AbilityCard("Dash",     1, Player.AbilityType.Dash,     "Ability (this turn): aim + blink toward a spot. Hotkey 1–4 / gamepad."),
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
