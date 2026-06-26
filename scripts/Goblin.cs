using Godot;

// Goblin Cutter — the tier-2 skirmisher of the M17 bestiary (Chunk 86). A small, fast little green
// egg clutching a crude blade: quicker than a Skeleton and a touch tougher than a Zombie, so it darts
// in to slash rather than shambling. Everything it does is already the base `Enemy` behaviour (chase
// the nearest opponent, melee on contact, no knockback); Goblin only swaps in the fast-fragile-quick-
// slash stat block. Its archetype numbers live on the scene root (`Goblin.tscn`) so they stay editor-
// tunable; this class exists for the visual/stat identity and so waves can target it by type.
public partial class Goblin : Enemy
{
}
