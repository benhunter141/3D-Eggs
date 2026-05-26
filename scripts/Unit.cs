using Godot;

// Shared base for every fighter — Player, Ally, Enemy all extend this so combat
// (team, health, damage, death, knockback) works identically for everyone.
// Knockback lives here and decays each frame; the player's sword feeds it on hit.
public partial class Unit : CharacterBody3D
{
	public enum TeamId { Player, Enemy }

	[Export] public TeamId Team = TeamId.Player;
	[Export] public float MaxHealth = 100.0f;
	[Export] public float KnockbackDecay = 14.0f;   // how fast a shove bleeds off (m/s per s)

	public float Health { get; private set; }
	public bool IsDead { get; private set; }

	// Lingering shove velocity; subclasses fold this into their MoveAndSlide.
	protected Vector3 KnockbackVelocity = Vector3.Zero;

	// Read-only view for debugging / headless tests.
	public Vector3 CurrentKnockback => KnockbackVelocity;

	public override void _Ready()
	{
		Health = MaxHealth;
		// Every fighter joins this group so others can find targets cheaply
		// (skeletons scan it for the nearest enemy-team unit).
		AddToGroup("units");
	}

	// Take `amount` damage. `hitDirection` points the way we'd be shoved (attacker→us);
	// knockbackStrength 0 means no shove — only the player's sword passes >0.
	public virtual void TakeDamage(float amount, Vector3 hitDirection = default, float knockbackStrength = 0.0f)
	{
		if (IsDead)
			return;

		Health = Mathf.Max(0f, Health - amount);
		GD.Print($"[Unit] {Name} took {amount} dmg -> {Health}/{MaxHealth} HP");

		if (knockbackStrength > 0f)
		{
			hitDirection.Y = 0f;
			if (hitDirection.LengthSquared() > 0.0001f)
				KnockbackVelocity += hitDirection.Normalized() * knockbackStrength;
		}

		if (Health <= 0f)
			Die();
	}

	protected virtual void Die()
	{
		IsDead = true;
		GD.Print($"[Unit] {Name} died");
		OnDeath();
	}

	// What actually happens to the body on death. Default: vanish (skeletons).
	// The player overrides this to stay in the scene and show a game-over state.
	protected virtual void OnDeath()
	{
		QueueFree();
	}

	// Frame-rate-independent decay of the knockback impulse toward zero.
	protected void DecayKnockback(float dt)
	{
		KnockbackVelocity = KnockbackVelocity.MoveToward(Vector3.Zero, KnockbackDecay * dt);
	}
}
