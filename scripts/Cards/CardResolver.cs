using Godot;

// Bridges the pure card MODEL (Card / Deck) to the live battlefield (M12, Chunk 33). This is the
// ONLY card-mode code that touches Godot/units, which keeps Card/Deck plain C# + headless-testable.
//
//   • A UNIT card -> SpawnUnit(): instantiate its friendly unit at the chosen battlefield location.
//   • An ACTION card -> PerformAction(): make the chosen friendly unit ACT, using the same public
//     Unit APIs (AddKnockback / TakeDamage) and target service (UnitRegistry) the rest of the game
//     already uses, so a card-driven charge / strike reads exactly like a normal one.
//
// Unit cards deploy ALLIES (the player-team fighter): the Pikeman scene is already an Ally; the
// Swordman / Bowman cards reuse the generic Ally scene skinned with a melee / ranged weapon so the
// deployed unit actually fights for you (the enemy-scripted Swordman/Bowman scenes are hard-locked
// to the enemy team). Real archetype allies can be split out later.
public static class CardResolver
{
	// --- Tuning (per-action effect strength). Int/Str scaling lands in Chunk 34. ---
	public const float ChargeImpulse = 9.0f;   // forward self-shove speed (m/s) a Charge grants
	public const float AttackDamage  = 12.0f;  // damage a Rally/Attack lands on the nearest foe
	public const float BraceDamage   = 6.0f;   // damage a Brace lands while digging in
	public const float BraceRepel    = 5.0f;   // shove (m/s) a Brace flings the struck foe back with
	public const float FireboltDamage = 16.0f; // damage a Firebolt deals (magic; Int-scaled later)
	public const float ActionRange   = 14.0f;  // a foe must be within this of the actor to be struck

	private static PackedScene _pikemanScene;
	private static PackedScene _allyScene;

	// Instantiate the friendly unit a Unit card deploys, and report which Ally weapon to skin it with.
	private static Unit MakeUnit(Card.UnitKind kind, out Ally.WeaponType weapon)
	{
		weapon = Ally.WeaponType.Fists;
		switch (kind)
		{
			case Card.UnitKind.Pikeman:
				_pikemanScene ??= GD.Load<PackedScene>("res://scenes/Pikeman.tscn");
				weapon = Ally.WeaponType.Pike;
				return _pikemanScene.Instantiate<Unit>();
			case Card.UnitKind.Swordman:
				_allyScene ??= GD.Load<PackedScene>("res://scenes/Ally.tscn");
				weapon = Ally.WeaponType.Fists;     // a melee bruiser
				return _allyScene.Instantiate<Unit>();
			case Card.UnitKind.Bowman:
				_allyScene ??= GD.Load<PackedScene>("res://scenes/Ally.tscn");
				weapon = Ally.WeaponType.Stones;    // a ranged skirmisher
				return _allyScene.Instantiate<Unit>();
			default:
				return null;
		}
	}

	// Play a UNIT card: spawn its friendly unit at `location` under `parent`. Returns the new unit
	// (null if the card isn't a Unit card / has no unit kind). The unit registers itself with the
	// UnitRegistry on _Ready, so it's immediately a valid target/ally on the battlefield.
	public static Unit SpawnUnit(Card card, Vector3 location, Node parent)
	{
		if (card == null || card.Kind != Card.CardKind.Unit || parent == null)
			return null;

		Unit unit = MakeUnit(card.Spawns, out Ally.WeaponType weapon);
		if (unit == null)
			return null;

		// Allies read their Weapon (and load the stone scene) in _Ready, so set it before AddChild.
		if (unit is Ally allyBefore)
			allyBefore.Weapon = weapon;

		parent.AddChild(unit);                 // _Ready: Team set, joins "units" + UnitRegistry
		location.Y = unit.GlobalPosition.Y;    // keep it on the unit's own ground height
		unit.GlobalPosition = location;

		// Hold roughly where it was deployed: with no captain anchor (group "player") the Ally's
		// formation slot resolves to its own position, so it stands its ground and engages nearby
		// foes. With an anchor present, anchoring the slot here keeps it near where you placed it.
		if (unit is Ally allyAfter)
			allyAfter.FormationOffset = location;

		GD.Print($"[CardResolver] played '{card.Title}' -> spawned {unit.Name} at {location}");
		return unit;
	}

	// Play an ACTION card on `target` (a friendly unit), which then performs the action on the
	// battlefield. Returns true if the unit actually acted (false if there was no valid target, or
	// no foe in range for a strike action — so the caller can refuse the play / refund the card).
	public static bool PerformAction(Card card, Unit target)
	{
		if (card == null || card.Kind != Card.CardKind.Action
			|| target == null || !GodotObject.IsInstanceValid(target) || target.IsDead)
			return false;

		switch (card.Action)
		{
			case Card.ActionKind.Charge:
			{
				Vector3 fwd = -target.GlobalTransform.Basis.Z;   // the unit's facing on the flat plane
				fwd.Y = 0f;
				if (fwd.LengthSquared() < 0.0001f)
					fwd = Vector3.Forward;
				target.AddKnockback(fwd.Normalized() * ChargeImpulse);
				GD.Print($"[CardResolver] '{card.Title}': {target.Name} charges forward");
				return true;
			}
			case Card.ActionKind.Attack:
				return StrikeNearest(card, target, AttackDamage, 0f);
			case Card.ActionKind.Brace:
				return StrikeNearest(card, target, BraceDamage, BraceRepel);
			case Card.ActionKind.Firebolt:
				return StrikeNearest(card, target, FireboltDamage, 0f);
			default:
				return false;
		}
	}

	// The unit strikes its nearest foe (within ActionRange) for `damage`, optionally shoving it back
	// with `knockback`. Returns false if no foe is in range to hit.
	private static bool StrikeNearest(Card card, Unit attacker, float damage, float knockback)
	{
		Unit foe = UnitRegistry.FindNearestOpponent(attacker.Team, attacker.GlobalPosition, ActionRange);
		if (foe == null)
		{
			GD.Print($"[CardResolver] '{card.Title}': {attacker.Name} found no foe in range");
			return false;
		}
		Vector3 dir = foe.GlobalPosition - attacker.GlobalPosition;
		foe.TakeDamage(damage, dir, knockback);
		GD.Print($"[CardResolver] '{card.Title}': {attacker.Name} struck {foe.Name} for {damage} (knockback {knockback})");
		return true;
	}
}
