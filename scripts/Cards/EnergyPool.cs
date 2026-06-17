using System;

// Card energy economy for Slay the Eggs (M12, Chunk 37). In King-of-the-Hill the ground you hold IS
// your economy: each round's card energy is a base allowance plus a bonus for every capture point your
// team holds at the pause. Energy GATES plays — a card can only be played if you can afford its cost.
//
// Pure model (no Godot types) so the economy is deterministic and headless-testable. CardBattle is a
// thin view: it Refill()s this from the live count of player-held capture points at each pause, and
// Spend()s from it on every successful play.
public class EnergyPool
{
	// Energy granted every round before counting any held ground — keeps the opening hand playable
	// even with no territory (matches the old StartingEnergy of 3).
	public int BaseEnergy { get; }
	// Extra energy per capture point held at the pause — territory pays.
	public int PerPoint { get; }

	// Flat run-long bonus from relics (M12, Chunk 39): added to every round's grant on top of the
	// base allowance and the territory bonus. Set by CardBattle from the run inventory before Refill.
	public int BonusEnergy { get; set; }

	// Energy available to spend this round, and the amount granted this round (the "/ max" denominator).
	public int Energy { get; private set; }
	public int Granted { get; private set; }

	public EnergyPool(int baseEnergy = 3, int perPoint = 1)
	{
		BaseEnergy = Math.Max(0, baseEnergy);
		PerPoint = Math.Max(0, perPoint);
		Energy = Granted = BaseEnergy;
	}

	// Energy a round grants for holding `pointsHeld` capture points: base + relic bonus + per-point.
	public int EnergyFor(int pointsHeld) =>
		BaseEnergy + Math.Max(0, BonusEnergy) + Math.Max(0, pointsHeld) * PerPoint;

	// Refill at the pause: set this round's energy from the capture points held (territory = economy).
	public void Refill(int pointsHeld) => Energy = Granted = EnergyFor(pointsHeld);

	// Add energy mid-round on top of the round's grant (a one-shot energy Potion, Chunk 39). Only ever
	// raises the spendable pool — never the denominator — so the readout can show e.g. 5 / 3.
	public void Add(int amount)
	{
		if (amount > 0)
			Energy += amount;
	}

	// Can this card be played right now? (A null card, or one costing more than we hold, cannot.)
	public bool CanAfford(Card card) => card != null && Energy >= card.EnergyCost;

	// Spend a card's cost if affordable; returns false and changes nothing when it isn't (the gate).
	public bool Spend(Card card)
	{
		if (!CanAfford(card))
			return false;
		Energy -= card.EnergyCost;
		return true;
	}
}
