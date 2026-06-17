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

	// Optional bouncy gait (Chunk 29). When HopAmplitude > 0 and the mount is being ridden at speed,
	// a child Node3D named "Visual" bobs up and down — a springy hop the donkey doesn't have but the
	// chocobo does. Cosmetic only: it bobs the VISUAL, never the rider or the collision body, so it
	// can't shove the captain or break the carried-silhouette mirroring.
	[Export] public float HopAmplitude = 0.0f;  // peak vertical bob of the visual while moving (0 = no hop, e.g. donkey)
	[Export] public float HopFrequency = 9.0f;  // bobs per second of travel

	public Player Rider { get; private set; }
	public bool IsRidden => Rider != null && IsInstanceValid(Rider);

	private CollisionShape3D _collision;
	private Node3D _visual;       // optional "Visual" child that hops; null on mounts without one
	private float _visualBaseY;   // the visual's rest height, restored when not hopping
	private float _hopPhase;      // advances with travel distance so the bob ties to motion, not wall-clock

	public override void _Ready()
	{
		AddToGroup("mounts");
		_collision = GetNodeOrNull<CollisionShape3D>("CollisionShape3D");
		_visual = GetNodeOrNull<Node3D>("Visual");
		if (_visual != null)
			_visualBaseY = _visual.Position.Y;
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

		// Springy gait: while we're actually travelling, bob the visual. Tying the phase to the
		// horizontal speed makes the hop quicken with the gallop and settle when standing still.
		if (_visual != null && HopAmplitude > 0f)
		{
			Vector3 vel = Rider.Velocity;
			float groundSpeed = new Vector2(vel.X, vel.Z).Length();
			if (groundSpeed > 0.5f)
				_hopPhase += (float)delta * HopFrequency;
			// abs(sin) so the body kicks UP off the ground and lands, never dipping below rest.
			float bob = groundSpeed > 0.5f ? Mathf.Abs(Mathf.Sin(_hopPhase)) * HopAmplitude : 0f;
			Vector3 vp = _visual.Position;
			vp.Y = _visualBaseY + bob;
			_visual.Position = vp;
		}
	}
}
