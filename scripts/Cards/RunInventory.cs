using System.Collections.Generic;

// The run's collected RELICS and POTIONS (M12, Chunk 39). A run carries this alongside its card
// Collection (RunMap.Inventory); rooms grant a relic or potion as a bonus, and the battle reads
// the aggregate modifiers off it. Relics are permanent passives summed into run-long modifiers
// (BonusEnergy / BonusHandSize / SpawnStrengthBonus); potions are one-shot items the player pops.
//
// Pure model (plain C#) so the whole accumulate-and-aggregate cycle is headless-testable.
public class RunInventory
{
	public List<Relic> Relics { get; } = new();
	public List<Potion> Potions { get; } = new();

	public void AddRelic(Relic relic) { if (relic != null) Relics.Add(relic); }
	public void AddPotion(Potion potion) { if (potion != null) Potions.Add(potion); }

	// Sum every relic of a kind into its run-long modifier. The battle folds these in each round:
	//   BonusEnergy       -> added to the EnergyPool's per-round grant
	//   BonusHandSize     -> extra cards drawn each round
	//   SpawnStrengthBonus-> added to the Strength of every Unit card spawned
	public int BonusEnergy => SumRelics(Relic.RelicKind.BonusEnergy);
	public int BonusHandSize => SumRelics(Relic.RelicKind.BonusHandSize);
	public int SpawnStrengthBonus => SumRelics(Relic.RelicKind.SpawnStrength);

	private int SumRelics(Relic.RelicKind kind)
	{
		int total = 0;
		foreach (Relic r in Relics)
			if (r.Kind == kind)
				total += r.Magnitude;
		return total;
	}
}
