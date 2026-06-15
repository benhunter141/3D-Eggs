using Godot;

// Shared base for every fighter — Player, Ally, Enemy all extend this so combat
// (team, health, damage, death, knockback) works identically for everyone.
// Knockback lives here and decays each frame; the player's sword feeds it on hit.
//
// Hit feedback (Chunk 10 juice) is centralised here because TakeDamage is the single
// chokepoint every hit flows through (sword, fists, stones, skeleton melee): each hit
// pops a body-colour flash and fires a short impact sound. Subclasses only override
// _PhysicsProcess, so the flash decay rides on _Process without conflict.
public partial class Unit : CharacterBody3D
{
	public enum TeamId { Player, Enemy }

	[Export] public TeamId Team = TeamId.Player;
	[Export] public float MaxHealth = 100.0f;
	[Export] public float KnockbackDecay = 14.0f;   // how fast a shove bleeds off (m/s per s)

	// --- Pinball collision response (M6, Chunk 20) ---
	// A unit carrying a real shove (> MinBounceSpeed) turns its MoveAndSlide collisions into
	// billiard chaos: it hands part of its momentum to any unit it rams (KnockbackTransfer)
	// and BOUNCES the rest off whatever it hit — wall or body (KnockbackBounce restitution).
	// One sword-fling thus scatters a packed line in a chain. Below MinBounceSpeed nothing
	// fires, so idle units packed in formation just jostle quietly. MaxKnockback caps the
	// accumulated shove so chains can't blow up to silly speeds.
	[Export] public float KnockbackBounce = 0.5f;    // restitution: fraction of speed kept after a bounce
	[Export] public float KnockbackTransfer = 0.5f;  // fraction of speed handed to a unit we ram
	[Export] public float MinBounceSpeed = 2.5f;     // shoves slower than this don't bounce/transfer
	[Export] public float MaxKnockback = 18.0f;      // cap on accumulated shove speed (0 = uncapped)

	// --- Staggered target re-acquisition (M5 crowds) ---
	// Re-scanning UnitRegistry for the nearest foe EVERY physics frame is the crowd hotspot:
	// it is O(opponents) per unit, so a 100-unit battle is ~O(n^2) of distance checks per
	// frame. Instead each AI unit refreshes its pick only every TargetRescanInterval frames,
	// spread across frames by a per-unit phase so the cost never spikes on one frame, and
	// keeps tracking the cached target's LIVE position in between (only the "who is nearest"
	// decision is throttled, not the chase). A dead/freed target forces an immediate re-scan.
	[Export] public int TargetRescanInterval = 6;   // physics frames between nearest-foe re-scans

	protected Unit CachedTarget;     // last picked target; reused between re-scans
	private int _rescanCounter = -1; // counts down to the next re-scan; <0 until phase is seeded

	// --- Hit feedback (juice) ---
	[Export] public Color FlashColor = new Color(1f, 1f, 1f); // colour the body pops toward on a hit
	[Export] public float FlashDuration = 0.12f;              // seconds for one flash to fade out
	[Export] public float DeathLinger = 0.4f;                 // corpse lingers this long so its death cue can play

	public float Health { get; private set; }
	public bool IsDead { get; private set; }

	// Lingering shove velocity; subclasses fold this into their MoveAndSlide.
	protected Vector3 KnockbackVelocity = Vector3.Zero;

	// Read-only view for debugging / headless tests.
	public Vector3 CurrentKnockback => KnockbackVelocity;

	// Flash state: a per-instance copy of the body material, driven each frame.
	private MeshInstance3D _bodyMesh;
	private StandardMaterial3D _bodyMat;
	private Color _baseColor;
	private float _flash;   // 1 = full flash, decays to 0

	// One generated impact sound, shared by every unit; each unit owns a 3D player.
	private static AudioStreamWav _hitSound;
	private AudioStreamPlayer3D _audio;

	public override void _Ready()
	{
		Health = MaxHealth;
		// Every fighter joins this group (GameManager counts it for win/lose) AND registers
		// with UnitRegistry, which is what AI/projectiles scan for the nearest foe — the
		// registry avoids a per-frame GetNodesInGroup marshal. Team is set by subclasses
		// before this base call, so we bucket into the right side here.
		AddToGroup("units");
		UnitRegistry.Register(this);
		SetupHitFeedback();
	}

	// Leave the registry the moment we leave the tree (death-free, scene reload, etc.) so
	// the team buckets always reflect exactly the units currently in play.
	public override void _ExitTree()
	{
		UnitRegistry.Unregister(this);
	}

	// Build the per-instance flash material (so one unit can flash without lighting up
	// every other instance sharing the scene's material) and the 3D impact-sound player.
	private void SetupHitFeedback()
	{
		_bodyMesh = GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
		if (_bodyMesh != null && _bodyMesh.GetActiveMaterial(0) is StandardMaterial3D src)
		{
			_bodyMat = (StandardMaterial3D)src.Duplicate();
			_baseColor = _bodyMat.AlbedoColor;
			_bodyMat.EmissionEnabled = true;        // emission drives the "pop"; energy 0 at rest
			_bodyMat.Emission = FlashColor;
			_bodyMat.EmissionEnergyMultiplier = 0f;
			_bodyMesh.SetSurfaceOverrideMaterial(0, _bodyMat);
		}

		_audio = new AudioStreamPlayer3D
		{
			Stream = GetHitSound(),
			// Disable distance attenuation: keeps every hit at a steady, audible volume for
			// the far-off top-down camera, while the 3D player still pans hits left/right.
			AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
			VolumeDb = -3f,
		};
		AddChild(_audio);
	}

	// Fade the active flash. Only Unit defines _Process (subclasses use _PhysicsProcess),
	// so this never clashes with their per-frame logic.
	public override void _Process(double delta)
	{
		if (_bodyMat == null || _flash <= 0f)
			return;

		_flash = Mathf.Max(0f, _flash - (float)delta / FlashDuration);
		_bodyMat.AlbedoColor = _baseColor.Lerp(FlashColor, _flash * 0.85f);
		_bodyMat.EmissionEnergyMultiplier = _flash * 2.0f;
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
				AddKnockback(hitDirection.Normalized() * knockbackStrength);
		}

		if (Health <= 0f)
		{
			Die();   // Die() plays the heavier death cue
		}
		else
		{
			Flash(0.85f);
			PlaySound(1f);
		}
	}

	protected virtual void Die()
	{
		IsDead = true;
		GD.Print($"[Unit] {Name} died");
		Flash(1f);            // full white pop on death
		PlaySound(0.7f);      // lower pitch reads as a heavier death thud
		OnDeath();
	}

	// What happens to the body on death. Default: drop collision so corpses don't jostle
	// the living, then vanish after DeathLinger so the death flash + sound aren't cut off.
	// The player overrides this to stay in the scene and show a game-over state.
	protected virtual void OnDeath()
	{
		if (GetNodeOrNull<CollisionShape3D>("CollisionShape3D") is CollisionShape3D shape)
			shape.Disabled = true;

		SceneTreeTimer timer = GetTree().CreateTimer(DeathLinger);
		timer.Timeout += () => { if (IsInstanceValid(this)) QueueFree(); };
	}

	// Frame-rate-independent decay of the knockback impulse toward zero.
	protected void DecayKnockback(float dt)
	{
		KnockbackVelocity = KnockbackVelocity.MoveToward(Vector3.Zero, KnockbackDecay * dt);
	}

	// Inject a shove with NO damage — the canonical way to add knockback (the sword routes
	// here too). Flattened onto the ground plane and clamped to MaxKnockback so chained
	// pinball impacts can't accumulate into runaway speeds. The dead take no shove.
	public void AddKnockback(Vector3 impulse)
	{
		if (IsDead)
			return;
		impulse.Y = 0f;
		KnockbackVelocity += impulse;
		if (MaxKnockback > 0f && KnockbackVelocity.Length() > MaxKnockback)
			KnockbackVelocity = KnockbackVelocity.Normalized() * MaxKnockback;
	}

	// Pinball collision response (M6, Chunk 20). Call once right AFTER MoveAndSlide: if we're
	// carrying a real shove, every body we drove into this frame gets part of our momentum
	// along the impact line (a billiard break), and the shove that remains BOUNCES off the
	// first surface we hit. One reflection per frame is enough — chains build over successive
	// frames on their own.
	//
	// We can't trust KinematicCollision3D.GetNormal()/positions for body-vs-body: two equal
	// capsules standing on the same plane resolve to a near-VERTICAL contact normal and Godot
	// slides the mover "over" the obstacle, so the post-move positions read with the bodies in
	// the wrong order (a capsule artifact). What IS reliable is our own travel direction — we
	// only get here because we drove into something this frame, and it sits along the way we
	// were heading. So for a unit we hit, the impact axis is our knockback DIRECTION: shove it
	// that way and reverse our own shove. Static walls keep their clean surface normal. The
	// response is kept on the ground plane (Y zeroed) so nothing gets launched into the air.
	protected void ResolveKnockbackBounce()
	{
		float speed = KnockbackVelocity.Length();
		if (speed < MinBounceSpeed)
			return;
		Vector3 travelDir = KnockbackVelocity / speed;   // unit vector along our shove

		bool bounced = false;
		int count = GetSlideCollisionCount();
		for (int i = 0; i < count; i++)
		{
			KinematicCollision3D col = GetSlideCollision(i);

			// Contact axis pointing from the obstacle back toward us (opposes our motion), so
			// Bounce() reflects the shove outward.
			Vector3 contactNormal;
			if (col.GetCollider() is Unit other && !other.IsDead)
			{
				other.AddKnockback(travelDir * speed * KnockbackTransfer);   // pass momentum on (no damage)
				contactNormal = -travelDir;
			}
			else
			{
				contactNormal = col.GetNormal();
				contactNormal.Y = 0f;
				if (contactNormal.LengthSquared() < 0.0001f)
					continue;                              // floor/ceiling contact — ignore
				contactNormal = contactNormal.Normalized();
				if (KnockbackVelocity.Dot(contactNormal) > 0f)
					contactNormal = -contactNormal;        // make it oppose our motion
			}

			if (!bounced)
			{
				KnockbackVelocity = KnockbackVelocity.Bounce(contactNormal) * KnockbackBounce;
				bounced = true;
			}
		}
	}

	// True on the frames this unit should re-scan UnitRegistry for its nearest target. Call
	// once per physics frame; scan and store into CachedTarget only when it returns true.
	// Seeds a random phase on first use so a crowd's scans fan out across frames rather than
	// all landing together, and forces an immediate re-scan when the cached target just died
	// or left the tree so a chaser never lingers on a corpse. Allocation-free (no closures).
	protected bool ShouldRescanTarget()
	{
		int interval = Mathf.Max(1, TargetRescanInterval);
		if (_rescanCounter < 0)
			_rescanCounter = (int)(GD.Randi() % (uint)interval);   // per-unit phase offset

		bool invalid = CachedTarget != null && (!IsInstanceValid(CachedTarget) || CachedTarget.IsDead);
		if (invalid || --_rescanCounter <= 0)
		{
			_rescanCounter = interval;
			return true;
		}
		return false;
	}

	// CachedTarget filtered to a still-living, valid unit — or null if it's gone. Use this
	// (not CachedTarget directly) so a target that died between re-scans reads as no target.
	protected Unit LiveTarget =>
		CachedTarget != null && IsInstanceValid(CachedTarget) && !CachedTarget.IsDead ? CachedTarget : null;

	// Kick the flash up to `amount` (keep the brighter value if already flashing).
	private void Flash(float amount)
	{
		_flash = Mathf.Max(_flash, amount);
	}

	// Fire the impact sound at a randomised pitch so repeated hits don't sound identical.
	private void PlaySound(float basePitch)
	{
		if (_audio == null)
			return;
		_audio.PitchScale = basePitch * (float)GD.RandRange(0.92, 1.12);
		_audio.Play();
	}

	// Generate a short percussive impact sound once (noise burst + low thud, fast decay)
	// so the project ships no audio asset files. The stream is read-only data, safe to
	// share across every unit; each unit just owns its own player for positioned playback.
	private static AudioStreamWav GetHitSound()
	{
		if (_hitSound != null)
			return _hitSound;

		const int rate = 22050;
		const float seconds = 0.14f;
		int samples = (int)(rate * seconds);
		var data = new byte[samples * 2];
		var rng = new System.Random(8675309);

		for (int i = 0; i < samples; i++)
		{
			float t = (float)i / samples;
			float env = Mathf.Exp(-t * 16f);                          // sharp attack, quick fade
			float noise = (float)(rng.NextDouble() * 2.0 - 1.0);      // crunch
			float thud = Mathf.Sin(2f * Mathf.Pi * 130f * i / rate);  // low body
			float s = (noise * 0.55f + thud * 0.55f) * env;

			short v = (short)Mathf.Clamp(s * 0.9f * short.MaxValue, short.MinValue, short.MaxValue);
			data[i * 2] = (byte)(v & 0xFF);
			data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
		}

		_hitSound = new AudioStreamWav
		{
			Format = AudioStreamWav.FormatEnum.Format16Bits,
			MixRate = rate,
			Stereo = false,
			Data = data,
		};
		return _hitSound;
	}
}
