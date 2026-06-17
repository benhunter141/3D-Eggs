using System;
using System.Collections.Generic;

// Draw / hand / discard piles for the card battler (M12, Chunk 32), Slay-the-Spire style.
// Cards flow:  DrawPile → Hand (Draw),  Hand → DiscardPile (Discard / DiscardHand),  and when
// the draw pile runs dry mid-draw the DiscardPile is shuffled back under it (Reshuffle). The
// CONSERVATION INVARIANT: the total number of cards across the three piles never changes — cards
// only ever move between piles, never appear or vanish. The headless test (Chunk 32) leans on it.
//
// Pure model (plain C# + System.Random) so the whole cycle is headless-testable; the on-screen
// UI (CardBattle) is a thin view over this.
public class Deck
{
	public List<Card> DrawPile { get; } = new();
	public List<Card> Hand { get; } = new();
	public List<Card> DiscardPile { get; } = new();

	private readonly Random _rng;

	// Total cards in play across all three piles — invariant under every operation below.
	public int TotalCount => DrawPile.Count + Hand.Count + DiscardPile.Count;

	// Pass a seed for deterministic shuffles (tests); omit for a fresh random run.
	public Deck(int? seed = null)
	{
		_rng = seed.HasValue ? new Random(seed.Value) : new Random();
	}

	// (Re)seed every pile from a starter list, cloned so the source isn't aliased, then shuffle
	// the draw pile. The hand and discard start empty.
	public void LoadStarter(IEnumerable<Card> cards)
	{
		DrawPile.Clear();
		Hand.Clear();
		DiscardPile.Clear();
		foreach (Card c in cards)
			DrawPile.Add(c.Clone());
		Shuffle(DrawPile);
	}

	// Draw up to n cards from the top of the draw pile into the hand. When the draw pile empties
	// mid-draw, the discard pile is reshuffled back in and drawing continues. Returns how many were
	// actually drawn (fewer than n only when every pile is exhausted).
	public int Draw(int n)
	{
		int drawn = 0;
		for (int i = 0; i < n; i++)
		{
			if (DrawPile.Count == 0)
			{
				if (DiscardPile.Count == 0)
					break;               // nothing left anywhere to draw
				Reshuffle();
			}
			int top = DrawPile.Count - 1;
			Hand.Add(DrawPile[top]);
			DrawPile.RemoveAt(top);
			drawn++;
		}
		return drawn;
	}

	// Move one specific card from the hand to the discard pile. Returns false if it isn't in hand.
	public bool Discard(Card card)
	{
		if (!Hand.Remove(card))
			return false;
		DiscardPile.Add(card);
		return true;
	}

	// Discard the whole hand at once (end of turn).
	public void DiscardHand()
	{
		DiscardPile.AddRange(Hand);
		Hand.Clear();
	}

	// Permanently add a card to the deck (deck-building — the Chunk 38 post-room rewards). It lands in
	// the discard pile so it shuffles into the draw pile on the next Reshuffle. NOTE: unlike
	// Draw/Discard/Reshuffle this DELIBERATELY changes TotalCount — a brand-new card enters the deck.
	// The conservation invariant governs the pile-to-pile moves, not deck-building.
	public void AddCard(Card card)
	{
		if (card != null)
			DiscardPile.Add(card);
	}

	// Shuffle the discard pile back into the draw pile. Called automatically by Draw when the draw
	// pile empties, but exposed for explicit refills too.
	public void Reshuffle()
	{
		DrawPile.AddRange(DiscardPile);
		DiscardPile.Clear();
		Shuffle(DrawPile);
	}

	// In-place Fisher–Yates shuffle.
	private void Shuffle(List<Card> pile)
	{
		for (int i = pile.Count - 1; i > 0; i--)
		{
			int j = _rng.Next(i + 1);
			(pile[i], pile[j]) = (pile[j], pile[i]);
		}
	}
}
