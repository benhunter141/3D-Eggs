using Godot;

// Card PLAY targeting + resolution (M12, Chunk 33). A card is aimed at a target that follows
// from its Kind (Card.Target): a UNIT card spawns a unit at a LOCATION, an ACTION card makes a
// FRIENDLY UNIT perform its effect. CardPlay.Play is the single chokepoint both routes flow
// through, talking only to two small interfaces — so the rules ("unit cards need a field +
// location", "action cards need a living friendly unit") are pure C# and fully headless-testable:
// the test supplies fake implementations and asserts the right card reached the right place.
// The real battlefield (CardBattle) implements ICardField; the real Unit implements ICardUnit.

// A friendly unit an Action card can target. Real fighters (Unit) implement this; so do test fakes.
public interface ICardUnit
{
	// May an Action card be played onto this unit? (On your team and still alive.)
	bool IsFriendly { get; }
	// Perform the given Action card's effect. Only ever called for Action cards on a friendly unit.
	void PerformAction(Card action);
}

// The egg a PlayerBuff card lands on (M15, Chunk 68): the player who PLAYED the card. The real Player
// implements this (equip a weapon / grant an ability / spawn a subordinate for itself); test fakes record
// the card they were handed so routing can be asserted without a scene.
public interface ICardPlayer
{
	// Resolve a PlayerBuff card on THIS player's egg. Only ever called for PlayerBuff cards.
	void ApplyCard(Card card);
}

// The battlefield a Unit card spawns into. Implemented by CardBattle; faked in the test.
public interface ICardField
{
	// Spawn the (friendly) unit described by this Unit card at a world location. Returns the
	// spawned unit (so callers can chain), or null if it couldn't be created.
	ICardUnit SpawnUnit(Card card, Vector3 location);
}

public static class CardPlay
{
	// Resolve a card against its required target (legacy 4-arg path — no triggering player, so a
	// PlayerBuff card has nowhere to land and is rejected). Kept so existing Unit/Action callers are
	// byte-identical.
	public static bool Play(Card card, ICardField field, Vector3 location, ICardUnit unitTarget)
		=> Play(card, field, location, unitTarget, null);

	// Resolve a card against its required target. Unit cards use (field, location); Action cards use
	// unitTarget; PlayerBuff cards (M15, Chunk 68) use `player` (the egg that played it). Returns true
	// only when the play actually happened — a missing field/unit/player, or a non-friendly unit target,
	// is rejected (false) and nothing is changed, so callers leave the card in hand.
	public static bool Play(Card card, ICardField field, Vector3 location, ICardUnit unitTarget,
		ICardPlayer player)
	{
		if (card == null)
			return false;

		switch (card.Kind)
		{
			case Card.CardKind.Unit:
				// Unit card → drop a unit at the location. Needs a battlefield to spawn into.
				return field != null && field.SpawnUnit(card, location) != null;

			case Card.CardKind.PlayerBuff:
				// PlayerBuff card → buff/​spawn for the triggering egg. Needs that player.
				if (player == null)
					return false;
				player.ApplyCard(card);
				return true;

			default:
				// Action card → a friendly unit performs the effect. Needs a living friendly target.
				if (unitTarget == null || !unitTarget.IsFriendly)
					return false;
				unitTarget.PerformAction(card);
				return true;
		}
	}
}
