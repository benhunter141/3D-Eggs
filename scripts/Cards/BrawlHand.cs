using Godot;
using System.Collections.Generic;

// Shared-hand play routing for the Co-op Card Brawl (M15, Chunk 69). Both eggs draw from ONE Deck and
// spend ONE EnergyPool; what differs per play is WHO triggered it — the buff lands on that egg. This is
// the pure routing core, lifted out of the view so it's headless-testable: given a hand index and a
// player index, it gates on energy, resolves the card on the right player via CardPlay, and (on success)
// discards the card + spends its cost. The CardBrawl view is a thin shell over this (UI + two-device input).
public class BrawlHand
{
	public Deck Deck { get; }
	public EnergyPool Energy { get; }

	public BrawlHand(Deck deck, EnergyPool energy)
	{
		Deck = deck;
		Energy = energy;
	}

	// Play hand[handIndex], resolving it on players[playerIndex] (the egg that triggered it). Returns
	// true only when the play happened — an out-of-range index, an unaffordable card, or a rejected
	// resolution leaves everything unchanged so the card stays in hand. `field` is for the (unused in the
	// brawl) Unit-card path; brawl cards are all PlayerBuff, so it may be null.
	public bool Play(int handIndex, int playerIndex, IReadOnlyList<ICardPlayer> players, ICardField field = null)
	{
		if (handIndex < 0 || handIndex >= Deck.Hand.Count)
			return false;
		if (players == null || playerIndex < 0 || playerIndex >= players.Count)
			return false;

		Card card = Deck.Hand[handIndex];
		if (!Energy.CanAfford(card))
			return false;

		if (!CardPlay.Play(card, field, Vector3.Zero, null, players[playerIndex]))
			return false;

		Deck.Discard(card);
		Energy.Spend(card);
		return true;
	}
}
