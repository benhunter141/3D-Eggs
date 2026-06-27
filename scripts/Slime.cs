using Godot;

// Slime — the slow blob horde of the M19 Co-op Phalanx level (Chunk 100). Like the Zombie it is
// a thin `Enemy` subclass that exists only for visual/stat identity: it inherits the whole base
// `Enemy` behaviour (chase the nearest opponent, contact melee, no knockback) and just wears the
// slow-blob stat block authored on the scene root (`Slime.tscn`). A dense block of them reads as a
// single shambling green mass that the twin phalanxes hold the line against. This class lets the
// level + tests target the type by name.
public partial class Slime : Enemy
{
}
