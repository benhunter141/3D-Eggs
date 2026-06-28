using Godot;
using System.Collections.Generic;

// Necromancer — the tier-4 summoner of the M17 bestiary (Chunk 90). A fragile hooded caster that hangs
// back behind the horde and does two things on their own cooldowns: lobs a ranged BOLT at the nearest egg
// (a reused Stone projectile, no knockback), and periodically RAISES the dead — summoning a small clutch of
// Zombies right beside itself. Ignore it and the horde keeps regrowing, so it's a priority kill; but it's
// frail, so a captain who reaches it ends the bleeding. It holds a loose range band like the Slinger (back
// off if a foe gets close, advance if the band opens up) but never flees outright — it leans on its summons
// as a screen. Summons are CAPPED (`SummonCap` living minions tracked) so it can't runaway-flood the field.
// Its stat block lives on the scene root (Necromancer.tscn) so it stays editor-tunable. Enemy team; its bolt
// and its minions all read the eggs/soldiers as foes via the shared Unit/UnitRegistry spine.
public partial class Necromancer : Unit
{
	[Export] public float MoveSpeed = 2.6f;          // slow caster shuffle (m/s)
	[Export] public float PreferredRangeMin = 9.0f;  // back off if a target gets closer than this
	[Export] public float PreferredRangeMax = 14.0f; // drift forward if the target is farther than this
	[Export] public float BoltCooldown = 2.2f;       // seconds between bolts
	[Export] public float BoltDamage = 8.0f;         // damage carried by each bolt (no knockback)
	[Export] public float SummonCooldown = 6.0f;     // seconds between raising the dead
	[Export] public int SummonCount = 2;             // zombies raised per cast
	[Export] public int SummonCap = 6;               // max LIVING summoned zombies it will keep on the field
	[Export] public float SummonRadius = 1.8f;       // how far from the caster the new zombies appear
	[Export] public float TurnLerp = 10.0f;          // how fast it rotates to face its target
	[Export] public PackedScene BoltScene;           // projectile to lob; falls back to res://scenes/Stone.tscn
	[Export] public PackedScene ZombieScene;         // minion to raise; falls back to res://scenes/Zombie.tscn

	private float _boltTimer;
	private float _summonTimer;
	private readonly List<Zombie> _minions = new();   // tracked summons, pruned of the dead/freed each cast

	public override void _Ready()
	{
		Team = TeamId.Enemy;
		base._Ready();
		BoltScene ??= GD.Load<PackedScene>("res://scenes/Stone.tscn");
		ZombieScene ??= GD.Load<PackedScene>("res://scenes/Zombie.tscn");
		_summonTimer = SummonCooldown * 0.5f;   // first raise comes a little sooner than the full cadence
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		DecayKnockback(dt);

		if (IsDead)
		{
			Velocity = ComposeMovement(KnockbackVelocity, dt);
			MoveAndSlide();
			return;
		}

		if (_boltTimer > 0f) _boltTimer -= dt;
		if (_summonTimer > 0f) _summonTimer -= dt;

		Vector3 move = Vector3.Zero;
		if (ShouldRescanTarget())
			CachedTarget = ScanNearestOpponent();
		Unit target = LiveTarget;
		if (target != null)
		{
			Vector3 toTarget = target.GlobalPosition - GlobalPosition;
			toTarget.Y = 0f;
			float dist = toTarget.Length();
			Vector3 dir = dist > 0.0001f ? toTarget / dist : Vector3.Forward;

			FaceTowards(target.GlobalPosition, dt);

			// Loose range band — keep the target between min and max; never panic-flee, the horde shields us.
			if (dist > PreferredRangeMax)
				move = dir * MoveSpeed;        // drifting too far behind — close in
			else if (dist < PreferredRangeMin)
				move = -dir * MoveSpeed;       // a foe got close — give ground

			// Bolt on cooldown at anything within firing range.
			if (dist <= PreferredRangeMax && _boltTimer <= 0f)
			{
				FireBoltAt(target);
				_boltTimer = BoltCooldown;
			}
		}
		else if (MarchMode)
		{
			move = MarchVelocity(MoveSpeed);
			FaceTowards(GlobalPosition + MarchGoalDirection, dt);
		}

		// Raise the dead on its own cadence whenever we're under the cap (no target needed — it builds a wall).
		if (_summonTimer <= 0f && LivingMinions() < SummonCap)
		{
			RaiseDead();
			_summonTimer = SummonCooldown;
		}

		Velocity = ComposeMovement(move * OwnMovementScale + KnockbackVelocity, dt);
		MoveAndSlide();
		ResolveKnockbackBounce();
	}

	// Spawn SummonCount Zombies in a ring around us (clamped so we never exceed SummonCap living minions).
	// Returns how many were actually raised — used by the headless test to assert the cadence + cap.
	public int RaiseDead()
	{
		if (ZombieScene == null)
			return 0;

		int room = SummonCap - LivingMinions();
		int toRaise = Mathf.Clamp(SummonCount, 0, room);
		Node parent = GetParent();
		for (int i = 0; i < toRaise; i++)
		{
			var z = ZombieScene.Instantiate<Zombie>();
			parent.AddChild(z);
			float angle = Mathf.Tau * i / Mathf.Max(1, toRaise);
			Vector3 ring = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SummonRadius;
			z.GlobalPosition = GlobalPosition + ring;
			_minions.Add(z);
		}
		if (toRaise > 0)
			GD.Print($"[Necromancer] {Name} raised {toRaise} zombie(s) ({LivingMinions()}/{SummonCap})");
		return toRaise;
	}

	// Count tracked summons that are still alive + in the tree, pruning any that died/were freed.
	public int LivingMinions()
	{
		for (int i = _minions.Count - 1; i >= 0; i--)
		{
			Zombie z = _minions[i];
			if (z == null || !IsInstanceValid(z) || z.IsDead)
				_minions.RemoveAt(i);
		}
		return _minions.Count;
	}

	private void FireBoltAt(Unit target)
	{
		if (BoltScene == null)
			return;
		var bolt = BoltScene.Instantiate<Stone>();
		bolt.Damage = BoltDamage;
		GetParent().AddChild(bolt);
		bolt.GlobalPosition = GlobalPosition;
		if (Grounded)
			bolt.LaunchBallistic(bolt.GlobalPosition, target.GlobalPosition, Team);
		else
			bolt.Launch(target.GlobalPosition - GlobalPosition, Team);
		GD.Print($"[Necromancer] {Name} hurled a bolt at {target.Name}");
	}

	private void FaceTowards(Vector3 worldPos, float dt)
	{
		Vector3 flat = new Vector3(worldPos.X, GlobalPosition.Y, worldPos.Z);
		if (GlobalPosition.DistanceSquaredTo(flat) < 0.0025f)
			return;
		Vector3 dir = flat - GlobalPosition;
		float desiredYaw = Mathf.Atan2(-dir.X, -dir.Z);
		float t = 1f - Mathf.Exp(-TurnLerp * dt);
		Vector3 rot = Rotation;
		rot.Y = Mathf.LerpAngle(rot.Y, desiredYaw, t);
		Rotation = rot;
	}
}
