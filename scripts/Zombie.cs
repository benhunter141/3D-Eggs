using Godot;

// Zombie — the tier-1 horde shambler of the M17 bestiary (Chunk 84). The easiest foe: slow,
// fragile, and armed only with a contact melee that deals no knockback. Everything it does is
// already the base `Enemy` behaviour (chase the nearest opponent, melee on contact, no shove) —
// Zombie only swaps in the horde-shambler stat block (low MoveSpeed, low HP) so a big crowd of
// them reads as a slow tide rather than a threat per body. Its archetype numbers live on the
// scene root (`Zombie.tscn`) so they stay editor-tunable; this class exists for the visual/stat
// identity and so waves can target it by type.
public partial class Zombie : Enemy
{
}
