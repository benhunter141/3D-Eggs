using Godot;

// A rideable mount (M10, Chunk 28). The player walks up to it and presses `mount` (the Player
// owns that input, so the decision is read once and never double-fires across mounts) to climb
// on. While ridden the mount is CARRIED as part of the player's silhouette: its own collision is
// switched off and each physics frame it snaps directly under the rider, matching its facing, so
// captain + steed read as one body that turns and moves together. It lends the rider its
// MountSpeed for as long as it's ridden. Dismounting stands it back up and re-enables collision.
//
// Not a Unit — mounts don't fight or take damage (for now). The base holds ALL of this; concrete
// mounts (Donkey now, Chocobo next) are just scenes with their own MountSpeed and look. Mounts
// join the "mounts" group so the Player can find the nearest one in range.
public partial class Mount : CharacterBody3D
{
	[Export] public float MountSpeed = 9.0f;    // rider's top speed while riding this mount (foot speed is ~6)
	[Export] public float MountRange = 3.0f;    // how close the player must be to climb on
	[Export] public float RiderHeight = 1.6f;   // vertical gap from the mount's origin up to the seated rider's origin

	public Player Rider { get; private set; }
	public bool IsRidden => Rider != null && IsInstanceValid(Rider);

	private CollisionShape3D _collision;

	public override void _Ready()
	{
		AddToGroup("mounts");
		_collision = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
	}

	// Called by the Player when it climbs on: take the rider and stop being a separate obstacle
	// (we're now part of the rider's silhouette, so a second capsule would just fight the player's).
	public void OnMounted(Player rider)
	{
		Rider = rider;
		if (_collision != null)
			_collision.Disabled = true;
	}

	// Called by the Player when it climbs off: stand where we are and become solid again.
	public void OnDismounted()
	{
		Rider = null;
		if (_collision != null)
			_collision.Disabled = false;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Rider == null)
			return;

		// Rider was freed (scene reload, death-free) without a clean dismount — recover gracefully.
		if (!IsInstanceValid(Rider))
		{
			OnDismounted();
			return;
		}

		// Sit directly under the rider, one RiderHeight down, sharing its yaw — captain + mount
		// move and turn as one. (The rider drives the movement; we just mirror it.)
		Vector3 pos = Rider.GlobalPosition;
		pos.Y -= RiderHeight;
		GlobalPosition = pos;

		Vector3 rot = Rotation;
		rot.Y = Rider.Rotation.Y;
		Rotation = rot;
	}
}
