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
	// Unit = spawn a unit at a location; Action = a friendly unit performs an effect;
	// PlayerBuff (M15, Chunk 68) = a Co-op Card Brawl card resolved against the TRIGGERING
	// player's egg (equip a weapon / grant an ability / spawn a subordinate for them — no target click).
	public enum CardKind { Unit, Action, PlayerBuff }

	// What a played card resolves against. Derived from Kind — Unit cards drop a unit on the
	// ground (a Location), Action cards empower one of your own units (a FriendlyUnit), and a
	// PlayerBuff card lands on whoever played it (Self — the triggering egg, no aim).
	public enum TargetKind { Location, FriendlyUnit, Self }

	// The concrete effect an Action card runs when its target unit performs it (Chunk 33).
	// None = a Unit card (it spawns instead of acting). Real units map these to behaviour in
	// Unit.PerformAction; the headless test only checks the right card reaches the right unit.
	public enum ActionKind { None, Charge, Rally, Firebolt, Brace }

	// What a PlayerBuff card grants to the triggering egg (M15, Chunk 68). Weapon arms it (a runtime
	// EquipWeapon, Chunk 66); Ability grants a castable spell (GrantAbility, Chunk 67); Soldier spawns
	// a subordinate on that egg's team (SpawnPath). None = not a buff card.
	public enum BuffKind { None, Weapon, Ability, Soldier }

	public string Title;
	public CardKind Kind;
	public int EnergyCost;
	public string Description;

	// Unit cards (and Soldier buff cards): the scene instanced for the spawned (friendly) unit.
	public string SpawnPath;
	// Action cards only: which effect the targeted friendly unit performs.
	public ActionKind Action;

	// PlayerBuff cards only (M15, Chunk 68): what to grant the triggering egg, and the weapon/ability
	// it grants (used only for Buff == Weapon / Ability; Soldier uses SpawnPath).
	public BuffKind Buff;
	public Player.WeaponType BuffWeapon;
	public Player.AbilityType BuffAbility;

	public Card(string title, CardKind kind, int energyCost, string description = "",
		string spawnPath = null, ActionKind action = ActionKind.None,
		BuffKind buff = BuffKind.None,
		Player.WeaponType buffWeapon = Player.WeaponType.Spear,
		Player.AbilityType buffAbility = Player.AbilityType.None)
	{
		Title = title;
		Kind = kind;
		EnergyCost = energyCost;
		Description = description;
		SpawnPath = spawnPath;
		Action = action;
		Buff = buff;
		BuffWeapon = buffWeapon;
		BuffAbility = buffAbility;
	}

	// What this card resolves against: Unit → a location, Action → a friendly unit, PlayerBuff → self.
	public TargetKind Target => Kind switch
	{
		CardKind.Unit => TargetKind.Location,
		CardKind.PlayerBuff => TargetKind.Self,
		_ => TargetKind.FriendlyUnit,
	};

	// An independent copy. Decks hold many duplicate cards; cloning the starter list keeps
	// the piles from aliasing the same Card instances (so future per-card state stays local).
	public Card Clone() => new Card(Title, Kind, EnergyCost, Description, SpawnPath, Action,
		Buff, BuffWeapon, BuffAbility);
}
