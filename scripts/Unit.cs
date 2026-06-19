using Godot;

// Shared base for every fighter — Player, Ally, Enemy all extend this so combat
// (team, health, damage, death, knockback) works identically for everyone.
// Knockback lives here and decays each frame; the player's sword feeds it on hit.
//
// Hit feedback (Chunk 10 juice) is centralised here because TakeDamage is the single
// chokepoint every hit flows through (sword, fists, stones, skeleton melee): each hit
// pops a body-colour flash and fires a short impact sound. Subclasses only override
// _PhysicsProcess, so the flash decay rides on _Process without conflict.
public partial class Unit : CharacterBody3D, ICardUnit
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
	[Export] public float KnockbackControlThreshold = 4.0f; // at/above this shove speed the unit rides ballistic (steering fully suppressed); as the shove decays from here to 0 it eases steering back in proportionally (see OwnMovementScale) — no hard snap

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

	// --- Forward-march AI (M12.5, Chunk 42) ---
	// An OPT-IN auto-battler behaviour for the football-pitch card mode: when MarchMode is on and no
	// opponent sits within AggroRange, the unit ADVANCES toward the opposing endzone (MarchGoalDirection,
	// set per team by CardBattle) instead of standing in formation / chasing the globally-nearest foe;
	// the instant a foe comes within AggroRange it engages with its normal chase/attack. OFF by default,
	// so every other level keeps its real formations + global chase exactly as before. Wired into
	// Ally/Enemy/Swordman/Bowman _PhysicsProcess as a fallback path that only fires when MarchMode is set.
	[Export] public bool MarchMode = false;
	[Export] public float AggroRange = 8.0f;             // (march only) break off the advance to engage a foe within this range
	public Vector3 MarchGoalDirection = Vector3.Zero;    // unit vector toward the enemy endzone (set per team by CardBattle)

	// --- Hit feedback (juice) ---
	[Export] public Color FlashColor = new Color(1f, 1f, 1f); // colour the body pops toward on a hit
	[Export] public float FlashDuration = 0.12f;              // seconds for one flash to fade out
	[Export] public float DeathLinger = 0.4f;                 // corpse lingers this long so its death cue can play

	// --- Cartoony eyes (M8, Chunk 24) ---
	// Every unit grows a pair of googly eyes on the front of its egg in _Ready — pure visual,
	// no logic. They're built procedurally (no asset files) and sized off the body's EggMesh,
	// so a fat Captain gets bigger eyes than a skeleton automatically; EyeScale nudges per
	// archetype if a scene wants to. Eyes are children of the unit root, which already rotates
	// to face -Z, so they always look where the unit is heading. Whites/pupils share static
	// unshaded materials + one sphere mesh across every unit to keep crowds cheap.
	[Export] public bool ShowEyes = true;
	[Export] public float EyeScale = 1.0f;   // per-archetype multiplier on eye size

	// --- Card-mode actions (M12, Chunk 33) ---
	// When an Action card is played onto this (friendly) unit it runs PerformAction below.
	// Effects are deliberately simple/visible; Str/Int scaling wired in Chunk 36 (see below).
	[Export] public float CardChargeImpulse = 12.0f; // Charge: forward lunge speed (via knockback)
	[Export] public float CardStrikeDamage = 12.0f;  // Rally: weapon strike on the nearest foe
	[Export] public float CardMagicDamage = 16.0f;   // Firebolt: magic hit on the nearest foe
	[Export] public float CardActionRange = 14.0f;   // Rally/Firebolt: max reach to the nearest foe

	// --- Unit stats: HP / Str / Int (M12, Chunk 36) ---
	// HP is MaxHealth/Health above. STRENGTH scales weapon attack power + strength-based card
	// actions (the Charge lunge, the Rally strike); INTELLIGENCE scales magic-based card actions
	// (Firebolt). Each point adds a flat fraction of the BASE value (StrengthScale / Intelligence-
	// Scale), so a stat of 0 resolves to exactly the base numbers every earlier chunk was tuned
	// around — a buff only ever adds on top, never re-tunes the floor.
	[Export] public int Strength = 0;
	[Export] public int Intelligence = 0;
	[Export] public float StrengthScale = 0.10f;       // +10% weapon/strength damage per Strength point
	[Export] public float IntelligenceScale = 0.10f;   // +10% magic damage per Intelligence point

	// Stat multipliers (1.0 at stat 0). Public so weapon code and headless tests can read them.
	public float StrengthMultiplier => 1f + Strength * StrengthScale;
	public float IntelligenceMultiplier => 1f + Intelligence * IntelligenceScale;

	// Scale a base hit by the relevant stat: weapon / strength hits use STR, magic uses INT. Every
	// damage source funnels through these (player swing, ally strike, card actions) so a stat buff
	// lands uniformly everywhere a unit deals damage.
	public float ScaledWeaponDamage(float baseDamage) => baseDamage * StrengthMultiplier;
	public float ScaledMagicDamage(float baseDamage) => baseDamage * IntelligenceMultiplier;

	public float Health { get; private set; }
	public bool IsDead { get; private set; }

	// Lingering shove velocity; subclasses fold this into their MoveAndSlide.
	protected Vector3 KnockbackVelocity = Vector3.Zero;

	// Read-only view for debugging / headless tests.
	public Vector3 CurrentKnockback => KnockbackVelocity;

	// How much authority the unit has over its OWN movement right now, 0..1. A shove at or above
	// KnockbackControlThreshold fully suppresses steering (scale 0) so a bumper/sword fling reads
	// as a clean launch; as the shove DECAYS the unit eases its steering back in PROPORTIONALLY
	// (scale climbs 0→1 as |knockback| falls threshold→0). This replaced a hard on/off at the
	// threshold: snapping steering back to full strength in a single frame jolted the unit (often
	// reversing it) ~half a second after a bumper hit, which read as a spurious SECOND bump. The
	// smooth blend turns a bumper touch back into one clean velocity change that decays away.
	// Subclasses fold this in as: Velocity = ownVelocity * OwnMovementScale + KnockbackVelocity.
	protected float OwnMovementScale =>
		KnockbackControlThreshold <= 0f
			? 1f
			: Mathf.Clamp(1f - KnockbackVelocity.Length() / KnockbackControlThreshold, 0f, 1f);

	// Flash state: a per-instance copy of the body material, driven each frame.
	private MeshInstance3D _bodyMesh;
	private StandardMaterial3D _bodyMat;
	private Color _baseColor;
	private float _flash;   // 1 = full flash, decays to 0

	// --- Health-as-cracks (shell damage overlay) ---
	// A next_pass shader on the body material paints crack lines whose extent tracks
	// missing health: a couple of hairlines at ~90% HP spreading to a shattered shell
	// near death. The flash system above is untouched — this rides on top of it. The
	// Shader resource is shared by every unit; each unit owns a per-instance material
	// so it can carry its own random crack seed + live damage value. CrackDamage 0 at
	// full health -> 1 at death; updated each time TakeDamage changes Health.
	private static Shader _crackShader;
	private ShaderMaterial _crackMat;

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
		SetupEyes();
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
			SetupCracks();
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

	// Hang a crack overlay off the body material as a next_pass: same egg geometry drawn
	// again with the EggCracks shader, blended on top so the base colour + hit-flash are
	// untouched. The Shader is loaded once and shared; the ShaderMaterial is per-instance so
	// each egg cracks from its own random seed and carries its own live damage value.
	private void SetupCracks()
	{
		_crackShader ??= GD.Load<Shader>("res://shaders/EggCracks.gdshader");
		if (_crackShader == null)
			return;

		_crackMat = new ShaderMaterial { Shader = _crackShader };
		// Per-unit random offset so no two shells crack identically.
		_crackMat.SetShaderParameter("seed", new Vector2(
			(float)GD.RandRange(0.0, 100.0), (float)GD.RandRange(0.0, 100.0)));
		_bodyMat.NextPass = _crackMat;
		UpdateCrackDamage();
	}

	// Push the current missing-health fraction (0 pristine -> 1 near death) into the crack
	// shader. Called on spawn and after every hit; cheap, so no per-frame work is needed.
	private void UpdateCrackDamage()
	{
		if (_crackMat == null)
			return;
		float damage = MaxHealth > 0f ? Mathf.Clamp(1f - Health / MaxHealth, 0f, 1f) : 0f;
		_crackMat.SetShaderParameter("damage", damage);
	}

	// Shared, build-once eye assets so 100 units cost two materials + one sphere, not 600.
	private static SphereMesh _eyeMesh;
	private static StandardMaterial3D _eyeWhiteMat;
	private static StandardMaterial3D _eyePupilMat;

	// Grow a pair of cartoony eyes on the front of the egg. Placement is solved against the
	// egg's profile so the whites sit ON the curved surface (then bulge out a touch), with the
	// pupils poking forward — pure visual, added as a child of the unit root.
	private void SetupEyes()
	{
		if (!ShowEyes)
			return;

		// Derive size from the body's EggMesh so each archetype's eyes scale with its body.
		float width = 1.0f, height = 1.7f, taper = 0.22f;
		if (_bodyMesh is EggMesh egg)
		{
			width = egg.Width;
			height = egg.Height;
			taper = egg.Taper;
		}

		float rx = width * 0.5f;
		float ry = height * 0.5f;
		float eyeY = height * 0.16f;                       // a little above the egg's middle
		float yUnit = ry > 0f ? Mathf.Clamp(eyeY / ry, -1f, 1f) : 0f;
		float baseR = Mathf.Sqrt(Mathf.Max(0f, 1f - yUnit * yUnit));
		float ringR = baseR * (1.0f - taper * yUnit);      // matches EggMesh's revolved profile
		float surfR = rx * ringR;                          // egg radius (xz) at eye height

		float whiteR = width * 0.16f * EyeScale;
		float pupilR = whiteR * 0.55f;
		float eyeX = Mathf.Min(surfR * 0.5f, width * 0.20f);                 // sideways spread
		float frontZ = -Mathf.Sqrt(Mathf.Max(0f, surfR * surfR - eyeX * eyeX)); // front hemisphere (-Z)

		var eyes = new Node3D { Name = "Eyes" };
		AddChild(eyes);

		foreach (int side in new[] { -1, 1 })
		{
			Vector3 onSurface = new Vector3(side * eyeX, eyeY, frontZ);
			Vector3 outward = new Vector3(side * eyeX, 0f, frontZ);
			outward = outward.LengthSquared() > 0.0001f ? outward.Normalized() : Vector3.Forward;

			Vector3 whitePos = onSurface + outward * (whiteR * 0.35f);
			Vector3 pupilPos = whitePos + outward * (whiteR * 0.7f);

			eyes.AddChild(MakeEyePart(whitePos, whiteR, EyeWhiteMat()));
			eyes.AddChild(MakeEyePart(pupilPos, pupilR, EyePupilMat()));
		}
	}

	// One eyeball part: a scaled instance of the shared unit-radius sphere at `pos`.
	private static MeshInstance3D MakeEyePart(Vector3 pos, float radius, StandardMaterial3D mat)
	{
		// Shared sphere has radius 0.5, so a scale of 2*radius gives the wanted radius.
		var basis = Basis.Identity.Scaled(Vector3.One * (radius * 2f));
		return new MeshInstance3D
		{
			Mesh = EyeMesh(),
			MaterialOverride = mat,
			Transform = new Transform3D(basis, pos),
			// Eyes shouldn't cast/receive shadows — keeps them reading flat and cartoony.
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	private static SphereMesh EyeMesh()
	{
		if (_eyeMesh == null)
			_eyeMesh = new SphereMesh { Radius = 0.5f, Height = 1.0f, RadialSegments = 8, Rings = 4 };
		return _eyeMesh;
	}

	private static StandardMaterial3D EyeWhiteMat()
	{
		if (_eyeWhiteMat == null)
			_eyeWhiteMat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.98f, 0.98f, 0.98f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, // flat cartoony white
			};
		return _eyeWhiteMat;
	}

	private static StandardMaterial3D EyePupilMat()
	{
		if (_eyePupilMat == null)
			_eyePupilMat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.05f, 0.05f, 0.05f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			};
		return _eyePupilMat;
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
		UpdateCrackDamage();   // shell cracks spread as HP drops
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

	// --- ICardUnit (M12, Chunk 33): the card battler treats a Unit as a targetable fighter ---

	// An Action card may target this unit only while it's ours and still alive.
	public bool IsFriendly => Team == TeamId.Player && !IsDead;

	// Run an Action card's effect on this unit. Stats route the power (Chunk 36): the Charge lunge
	// and Rally strike are STRENGTH actions (scaled by Str), Firebolt is a MAGIC action (scaled by
	// Int). Energy gating (Chunk 37) layers on later. Virtual so archetypes can specialise.
	// Foe-targeting actions reuse UnitRegistry — never a per-frame group scan.
	public virtual void PerformAction(Card action)
	{
		if (IsDead || action == null)
			return;

		switch (action.Action)
		{
			case Card.ActionKind.Charge:
				// Strength lunge along the way we're facing (-Z is forward); rides the pinball path.
				AddKnockback(-GlobalTransform.Basis.Z.Normalized() * CardChargeImpulse * StrengthMultiplier);
				break;

			case Card.ActionKind.Rally:
				StrikeNearestFoe(ScaledWeaponDamage(CardStrikeDamage));   // strength-scaled weapon strike
				break;

			case Card.ActionKind.Firebolt:
				StrikeNearestFoe(ScaledMagicDamage(CardMagicDamage));     // intelligence-scaled magic hit
				break;

			case Card.ActionKind.Brace:
				// No combat effect yet — just a defiant pop so the play reads on screen.
				break;
		}

		Flash(1f);          // every card action pops a flash so the targeted unit is obvious
		GD.Print($"[Unit] {Name} performed card action {action.Action}");
	}

	// Deal `damage` to the nearest opposing unit within CardActionRange (no knockback). Used by
	// the Rally / Firebolt actions; silently does nothing if no foe is in reach.
	private void StrikeNearestFoe(float damage)
	{
		Unit foe = UnitRegistry.FindNearestOpponent(Team, GlobalPosition, CardActionRange);
		foe?.TakeDamage(damage);
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

	// Re-scan UnitRegistry for our nearest opponent — globally as ever, or (in MarchMode) capped to
	// AggroRange so we only pick up a foe worth breaking the advance for. The shared scan body for the
	// AI subclasses: in march mode a null result means "nothing close — keep marching" (Chunk 42).
	protected Unit ScanNearestOpponent() =>
		MarchMode
			? UnitRegistry.FindNearestOpponent(Team, GlobalPosition, AggroRange)
			: UnitRegistry.FindNearestOpponent(Team, GlobalPosition);

	// Flat velocity toward the march goal (the opposing endzone) at `speed`; zero if no goal direction
	// was set. Subclasses use this as the no-foe fallback while MarchMode is on (Chunk 42).
	protected Vector3 MarchVelocity(float speed)
	{
		Vector3 dir = MarchGoalDirection;
		dir.Y = 0f;
		return dir.LengthSquared() > 0.0001f ? dir.Normalized() * speed : Vector3.Zero;
	}

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
