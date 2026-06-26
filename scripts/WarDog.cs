using Godot;

// War Dog — the tier-1 PACK hunter of the M17 bestiary (Chunk 85). The fast counterpart to the
// slow Zombie horde: very low HP and very fast, with a short bite cooldown so a pack overwhelms by
// closing distance and chewing in numbers rather than by any single dog being dangerous. Like the
// Zombie everything it does is already the base `Enemy` behaviour (chase the nearest opponent, melee
// on contact, no knockback); WarDog only swaps in the fast-fragile-fast-bite stat block. Its archetype
// numbers live on the scene root (`Dog.tscn`) so they stay editor-tunable; this class exists for the
// visual/stat identity and so waves can target it (and place it as a pack) by type.
public partial class WarDog : Enemy
{
}
