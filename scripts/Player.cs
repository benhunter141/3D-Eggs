using Godot;
using System.Collections.Generic;

// The player capsule. Twin-stick movement on the flat XZ plane.
// WASD (or left stick) moves; mouse aims; left-click THRUSTS the held weapon straight ahead.
// Extends Unit so it shares the team/health/damage pipeline with allies and enemies.
//
// Weapon swap (M9, Chunks 26–27): the captain carries one of several weapons, cycled live
// with `swap_weapon` (Q / gamepad). Each weapon is a PROFILE — reach, per-hit damage +
// knockback, and thrust/cooldown feel, plus which mesh is shown. The profiles live in a
// table keyed by WeaponType (built from the Inspector exports in _Ready), so adding a weapon
// is one enum entry + one table row + one mesh; the swing code reads one set of runtime
// fields (filled by ApplyWeapon) regardless of which weapon is up — the same plumbing any
// unit could be skinned with later.
//   • Spear — LONG reach, NO knockback: a phalanx poker that out-ranges its foe.
//   • Sword — SHORT reach, strong KNOCKBACK: the classic sword that flings what it hits.
//   • Axe   — heavy & SLOW, the biggest single hit, with a modest shove.
//   • Mace  — the KNOCKBACK specialist: middling damage, the hardest fling of all.
//   • Punch — the unarmed "basic egg" loadout (M15): the weakest hit, short reach, no knockback.
//             Never part of the Q-swap cycle; only ever set via StartUnarmed / EquipWeapon so cards
//             are the only way an egg gets stronger. Keep it LAST in the enum (SwappableWeaponCount
//             assumes the unarmed punch is the final value, excluded from cycling).
public partial class Player : Unit, ICardPlayer
{
	public enum WeaponType { Spear, Sword, Axe, Mace, Punch }

	// How a weapon SWINGS (M18, Chunk 94). Today every weapon pokes with the same straight thrust;
	// this gives each its own recognizable motion so the move tells you which weapon you hold. The
	// style is a per-weapon column on the WeaponProfile table and drives `AnimateSwing` (the swing
	// pose) — later chunks add Sweep/Chop/Swing routines + shape the hitbox per style. For now every
	// weapon is Thrust (spear/sword/axe/mace) or Jab (punch), both the existing slide, so behaviour is
	// byte-identical until each later chunk flips its weapon's style.
	//   • Thrust — straight jab out and back (spear/pike): today's poke.
	//   • Sweep  — horizontal left→right arc across the front (sword): multi-hit.   [Chunk 95]
	//   • Chop   — slow overhead top-down swing (axe): one big committed hit.         [Chunk 96]
	//   • Swing  — wide circular round-house (mace): broad multi-hit + strong shove.  [Chunk 97 — done]
	//   • Jab    — short fast thrust (unarmed punch): the basic egg's weak poke.
	public enum AttackStyle { Thrust, Sweep, Chop, Swing, Jab }

	// Castable abilities the egg can be GRANTED at runtime (M15). `None` (the default) = nothing, so
	// every existing captain spawns exactly as before — only a card-granted egg in the Co-op Card Brawl
	// ever carries any. An egg holds a BAR of these (granted by cards, replayable each turn); each maps to
	// a hotkey (P1: keys 1–4; P2: gamepad buttons) and a per-ability cooldown. Two flavours:
	//   • TARGETED (Fireball, Dash) — P1 presses the hotkey to AIM (a ground reticle follows the mouse),
	//     then left-clicks a spot to cast there (right-click cancels). P2 (no mouse) casts toward its aim.
	//   • INSTANT  (Enrage, Heal)   — fires immediately on the hotkey (cast on self, no aim).
	// Fireball = INT-scaled magic bolt; Enrage = briefly doubles attack power; Heal = restore HP; Dash =
	// blink toward the target point. Add an ability by extending this enum + AbilityIsTargeted + a case
	// in CastSlot (and a cooldown export).
	public enum AbilityType { None, Fireball, Enrage, Heal, Dash }

	// --- Control scheme (M12.7, Chunk 44: two-player couch co-op) ---
	// Which input device drives THIS captain. `Any` (default) is today's blended read —
	// keyboard+gamepad move, mouse aim — so every existing single-player level is untouched.
	// The co-op scene assigns one captain `KeyboardMouse` and the other `Gamepad` so two
	// players on one machine never cross-talk: `KeyboardMouse` reads only the keyboard + mouse;
	// `Gamepad` reads ONE pad (`DeviceId`) — left stick moves, right stick aims, that pad's
	// buttons attack/brace/swap/mount. All reads funnel through the public accessors below so
	// allies (Chunk 45) can consult their captain's brace, etc.
	// `Ai` (M15 Co-op Card Brawl redesign): a subordinate egg spawned by a Soldier card. It carries the
	// full Player weapon/ability machinery (so weapon/ability cards attach to it exactly like a hero egg)
	// but reads NO device — a tiny chase-and-strike AI fills in move/aim/attack each frame, and it stays
	// OUT of the "player" group so it isn't a captain for the camera, squad anchor, or the lose check.
	public enum ControlScheme { Any, KeyboardMouse, Gamepad, Ai }
	[Export] public ControlScheme Control = ControlScheme.Any;
	[Export] public int DeviceId = 0;            // which gamepad to read when Control == Gamepad

	// Co-op (M12.7, Chunk 47): in single-player a captain's death IS the loss, so OnDeath reveals
	// the shared "game_over" UI immediately (default true). In the couch co-op scene BOTH captains
	// must fall before it's a loss, so each co-op captain sets this false — a downed captain just
	// freezes in place and GameManager (RequireAllPlayersDead) reveals GAME OVER only when the LAST
	// one falls. Off-by-default keeps every existing level byte-identical.
	[Export] public bool ShowGameOverOnDeath = true;
	[Export] public float MoveDeadzone = 0.2f;   // left-stick deadzone (Gamepad move)
	[Export] public float AimDeadzone = 0.4f;    // right stick must clear this to re-aim (else hold facing)

	// Gamepad button indices — mirror the joypad events in project.godot's input map so the
	// explicit Gamepad scheme matches what a single pad already does in Any mode.
	private const JoyButton AttackButton = (JoyButton)7;
	private const JoyButton SwapButton   = JoyButton.X;    // 2
	private const JoyButton MountButton  = JoyButton.B;    // 1
	private const JoyButton BraceButton  = JoyButton.Back; // 4

	// Squad-command buttons (M7, Chunk 49) — mirror the command_* actions in project.godot so the
	// explicit schemes match what a single pad already does in Any mode.
	private const JoyButton CmdFollowButton = JoyButton.LeftShoulder; // 9
	private const JoyButton CmdHoldButton   = JoyButton.DpadLeft;     // 13
	private const JoyButton CmdAttackButton = JoyButton.DpadRight;    // 14

	// Ability-bar hotkeys (M15 redesign). P1 (keyboard) fires bar slots 0–3 with the number keys 1–4;
	// P2 (gamepad) fires the same slots with these buttons (A / Y / R1 / D-pad-up — all otherwise free for
	// the captain during a live wave; the brawl ignores the pad while the world is frozen). Slot i's hotkey
	// is AbilityKeys[i] / GamepadAbilityButtons[i].
	private static readonly Key[] AbilityKeys = { Key.Key1, Key.Key2, Key.Key3, Key.Key4 };
	private static readonly JoyButton[] GamepadAbilityButtons =
		{ JoyButton.A, JoyButton.Y, JoyButton.RightShoulder, JoyButton.DpadUp };

	// Prev-frame pressed state for the device-scoped schemes, so a raw "is pressed" bool becomes
	// a one-frame "just pressed" (the action system gives this for free, but only un-scoped).
	private bool _attackHeldPrev, _swapHeldPrev, _mountHeldPrev;
	private bool _cmdFollowPrev, _cmdHoldPrev, _cmdAttackPrev;
	private readonly bool[] _abilityHeldPrev = new bool[4];   // one per bar slot hotkey
	private bool _aimCancelPrev;                               // P1 right-click (cancel-aim) edge

	// One weapon's resolved numbers. The Inspector exports below feed these in _Ready so each
	// weapon stays tunable while the swing code reads a single uniform set.
	private readonly struct WeaponProfile
	{
		public readonly float Damage, Knockback, Reach, ThrustDistance, SwingDuration, SwingCooldown;
		public readonly AttackStyle Style;   // how this weapon swings (M18, Chunk 94)
		public WeaponProfile(float damage, float knockback, float reach, float thrustDistance, float swingDuration, float swingCooldown, AttackStyle style)
		{
			Damage = damage; Knockback = knockback; Reach = reach;
			ThrustDistance = thrustDistance; SwingDuration = swingDuration; SwingCooldown = swingCooldown;
			Style = style;
		}
	}

	// --- Tunable feel knobs (editable in the Inspector too) ---
	[Export] public float Speed = 6.0f;         // top movement speed (m/s) — a notch above the AI, not double
	[Export] public float Acceleration = 60.0f; // how fast we ramp up to Speed
	[Export] public float Friction = 50.0f;     // how fast we slow to a stop
	[Export] public float TurnSpeed = 480.0f;   // max aim turn rate (deg/s) — the CAPTAIN's body/weapon tracks the cursor fast
	// The phalanx wheels on its OWN slow yaw, decoupled from the captain's fast aim (M7 turn-feel pass).
	// FormationYaw lags Rotation.Y at this rate, and the allies anchor their slots + facing to it
	// (via FormationBasis), so the wall of pikemen can't snap 180° when you flick the mouse — it
	// wheels deliberately like a real formation while your sword still points where you aim instantly.
	[Export] public float FormationTurnSpeed = 90.0f;   // max formation wheel rate (deg/s) — 90 ≈ 2 s for a 180° turn
	private float _formationYaw;                          // the squad's current facing; eases toward Rotation.Y

	// --- Weapon loadout (Chunk 26) ---
	[Export] public WeaponType StartingWeapon = WeaponType.Spear;  // which weapon the captain spawns holding (per-level default)

	// M15 basic egg (Chunk 66): when true the captain spawns wielding the weak unarmed Punch
	// instead of StartingWeapon — the Co-op Card Brawl eggs start helpless and get armed only by
	// cards (EquipWeapon at runtime). Default false so every existing scene spawns as today.
	[Export] public bool StartUnarmed = false;

	// --- Castable abilities (M15 redesign) ---
	// The egg can be GRANTED castable abilities at runtime (ability cards). None by default, so every
	// existing captain is untouched; cards are the only way an egg gains any. Abilities live in a BAR
	// (slots 0–3) fired by the number keys / gamepad buttons above, each gated by its own cooldown. In
	// the brawl they're wiped each turn (ClearAbilities) so a card grants its spell for that wave only.
	[Export] public AbilityType StartingAbility = AbilityType.None; // ability the egg spawns with (per-scene default)

	// Fireball — INT-scaled magic bolt, aimed at the target point (or facing for a gamepad/AI cast).
	[Export] public float FireballDamage = 16.0f;   // BASE magic damage (pre-Int-scale) of a cast fireball
	[Export] public float FireballCooldown = 1.2f;  // seconds between fireball casts
	[Export] public PackedScene FireballScene;       // projectile to spawn; falls back to res://scenes/Fireball.tscn

	// Enrage — instant self-buff: multiply attack power for a short window.
	[Export] public float EnrageFactor = 2.0f;       // attack-damage multiplier while enraged ("double atk power")
	[Export] public float EnrageDuration = 5.0f;     // seconds the buff lasts
	[Export] public float EnrageCooldown = 8.0f;

	// Heal — instant self-restore.
	[Export] public float HealAmount = 50.0f;        // HP restored per cast
	[Export] public float HealCooldown = 10.0f;

	// Dash — blink toward the target point (or facing for a gamepad/AI cast), capped at DashRange.
	[Export] public float DashRange = 8.0f;          // max blink distance (m)
	[Export] public float DashCooldown = 4.0f;

	// One bar slot: an ability + its live cooldown timer.
	private class AbilitySlot { public AbilityType Type; public float Cooldown; public float Timer; public bool Ready => Timer <= 0f; }
	private readonly List<AbilitySlot> _abilities = new();

	private float _enrageTimer;        // > 0 while the Enrage buff is active
	private int _aimingSlot = -1;      // ability slot P1 is currently aiming a targeted cast for, else -1
	private bool _castConfirmedThisFrame;   // a targeted cast fired this frame — suppress the swing on that click
	private Node3D _reticle;           // P1's ground aim ring, built lazily on first aim

	public bool IsEnraged => _enrageTimer > 0f;

	// Read-only views (HUD + headless tests). CurrentAbility/HasAbility/AbilityReady stay first-slot views
	// for back-compat with the Chunk-67/68 tests; the bar accessors below drive the HUD per slot.
	public AbilityType CurrentAbility => _abilities.Count > 0 ? _abilities[0].Type : AbilityType.None;
	public bool HasAbility => _abilities.Count > 0;
	public bool AbilityReady => _abilities.Count > 0 && _abilities[0].Ready;
	public int AbilityCount => _abilities.Count;
	public AbilityType AbilityTypeAt(int i) => i >= 0 && i < _abilities.Count ? _abilities[i].Type : AbilityType.None;
	public bool AbilityReadyAt(int i) => i >= 0 && i < _abilities.Count && _abilities[i].Ready;
	public float AbilityCooldownLeft(int i) => i >= 0 && i < _abilities.Count ? _abilities[i].Timer : 0f;
	public bool IsAiming => _aimingSlot >= 0;

	// Which abilities need a target point (P1 aims; P2/AI cast toward facing). The rest are instant self-casts.
	public static bool AbilityIsTargeted(AbilityType a) => a == AbilityType.Fireball || a == AbilityType.Dash;

	// Spear profile — long reach, no knockback.
	[Export] public float SpearDamage = 40.0f;
	[Export] public float SpearKnockback = 0.0f;       // a pike pokes; it doesn't fling
	[Export] public float SpearReach = 3.0f;           // hitbox forward length (m) — out-ranges melee
	[Export] public float SpearThrustDistance = 0.9f;  // extra lunge at full extension
	[Export] public float SpearSwingDuration = 0.2f;
	[Export] public float SpearSwingCooldown = 0.35f;

	// Sword profile — short reach, strong knockback (the classic sword rules).
	[Export] public float SwordDamage = 40.0f;
	[Export] public float SwordKnockback = 10.0f;      // shove speed (m/s) flung along the hit direction
	[Export] public float SwordReach = 1.4f;           // hitbox forward length (m) — much shorter than the spear
	[Export] public float SwordThrustDistance = 0.6f;
	[Export] public float SwordSwingDuration = 0.18f;
	[Export] public float SwordSwingCooldown = 0.30f;

	// Axe profile — heavy & slow: the biggest single hit, with a modest shove. The long
	// cooldown is the heavy-weapon tax (you commit to each swing).
	[Export] public float AxeDamage = 70.0f;
	[Export] public float AxeKnockback = 4.0f;
	[Export] public float AxeReach = 1.6f;
	[Export] public float AxeThrustDistance = 0.6f;
	[Export] public float AxeSwingDuration = 0.28f;
	[Export] public float AxeSwingCooldown = 0.55f;    // slow

	// Mace profile — the knockback specialist: middling damage, the hardest fling of all.
	[Export] public float MaceDamage = 28.0f;
	[Export] public float MaceKnockback = 14.0f;       // strongest shove of any weapon
	[Export] public float MaceReach = 1.5f;
	[Export] public float MaceThrustDistance = 0.6f;
	[Export] public float MaceSwingDuration = 0.22f;
	[Export] public float MaceSwingCooldown = 0.42f;

	// Punch profile (M15, Chunk 66) — the unarmed basic-egg loadout: the weakest hit of all,
	// the shortest reach (under the sword), and NO knockback. Deliberately feeble so a card-granted
	// weapon is a clear upgrade. Excluded from the Q-swap cycle (only set via StartUnarmed/EquipWeapon).
	[Export] public float PunchDamage = 6.0f;
	[Export] public float PunchKnockback = 0.0f;
	[Export] public float PunchReach = 1.0f;            // shorter than the sword (1.4) — you have to get close
	[Export] public float PunchThrustDistance = 0.4f;
	[Export] public float PunchSwingDuration = 0.16f;
	[Export] public float PunchSwingCooldown = 0.35f;

	// --- Shared thrust feel ---
	[Export] public float ThrustExtendFrac = 0.4f;   // fraction of the duration spent jabbing out (rest is the retract)
	[Export] public float SwordReturnLerp = 12.0f;   // how fast the weapon eases back to rest if interrupted

	// --- Sweep feel (M18, Chunk 95) ---
	[Export] public float SweepArcDegrees = 140.0f;  // sword: total left→right arc the blade fans across the front

	// --- Swing feel (M18, Chunk 97) ---
	[Export] public float SwingArcDegrees = 250.0f;  // mace: total round-house arc — WIDER than the sword sweep, sweeping nearly all the way around

	// --- Hitbox shaping (M18, Chunk 98) ---
	// The active hitbox region should match the attack STYLE, not be one fixed box for every weapon. A
	// thrust/chop is a narrow forward LINE (you poke/chop straight ahead); a sweep/swing is a broad swept
	// ARC, so its box is widened to a fan that covers the cluster the blade arcs through — so multi-hit
	// reads fairly instead of slipping between the per-frame angular steps. The box's forward length still
	// follows weapon reach; only its WIDTH (the egg's X) is shaped here, set in ApplyWeapon from the style.
	[Export] public float ThrustHitboxWidth = 0.7f;  // narrow forward line for Thrust / Chop / Jab (the scene's base width)
	[Export] public float SweptHitboxWidth  = 1.8f;   // wide fan for Sweep / Swing so the arc connects across a cluster

	// --- Chop feel (M18, Chunk 96) ---
	[Export] public float ChopRaiseFrac = 0.45f;     // axe: fraction of the window spent raising the axe overhead (rest = the chop down)
	[Export] public float ChopRaiseDegrees = 75.0f;  // how far the axe head tilts UP/back at the top of the raise
	[Export] public float ChopDipDegrees = 15.0f;    // how far past horizontal the chop follows through (down) at the end

	// Active profile values, set by ApplyWeapon — the swing code reads only these.
	private WeaponType _weapon;
	private float _damage;
	private float _knockback;
	private float _reach;
	private float _thrustDistance;
	private float _swingDuration;
	private float _swingCooldown;
	private AttackStyle _style;   // active weapon's swing motion (M18, Chunk 94)

	// The weapon table + the mesh per weapon, built in _Ready.
	private Dictionary<WeaponType, WeaponProfile> _profiles;
	private Dictionary<WeaponType, Node3D> _weaponMeshes;

	// Read-only views (used by the headless weapon tests).
	public WeaponType CurrentWeapon => _weapon;
	public float CurrentDamage => _damage;                               // weapon BASE damage (pre-stat)
	public float CurrentAttackDamage => ScaledWeaponDamage(_damage);     // Strength-scaled weapon damage (no enrage)
	public float EffectiveAttackDamage => ScaledWeaponDamage(_damage) * (_enrageTimer > 0f ? EnrageFactor : 1f); // what a hit deals NOW (incl. Enrage)
	public float CurrentWeaponKnockback => _knockback;   // NOTE: Unit.CurrentKnockback is the live shove Vector3
	public float CurrentReach => _reach;
	public float CurrentSwingCooldown => _swingCooldown;                // higher = slower (the axe's tax)
	public AttackStyle CurrentAttackStyle => _style;                    // how the active weapon swings (M18)
	public float EffectiveReach => _reach + _thrustDistance;            // how far the tip lands at full lunge
	public float HitboxLength => _hitboxShape?.Shape is BoxShape3D b ? b.Size.Z : 0f;
	public float HitboxWidth => _hitboxShape?.Shape is BoxShape3D b ? b.Size.X : 0f;   // shaped per style (M18, Chunk 98)
	// A swept style (sword sweep / mace round-house) arcs the box across the front, so it gets the WIDE fan
	// hitbox; a thrust/chop/jab pokes straight ahead, so it keeps the narrow forward line.
	public static bool IsSweptStyle(AttackStyle s) => s == AttackStyle.Sweep || s == AttackStyle.Swing;
	public static int WeaponCount => System.Enum.GetValues<WeaponType>().Length;
	// How many weapons the Q-swap cycles through — every WeaponType EXCEPT the unarmed Punch (the
	// basic-egg loadout, M15), which is the LAST enum value and only ever set via EquipWeapon/StartUnarmed.
	public static int SwappableWeaponCount => WeaponCount - 1;

	private Node3D _swordPivot;
	private Area3D _hitbox;
	private CollisionShape3D _hitboxShape;

	// Bodies already reported this swing, so each gets one print per swing.
	private readonly HashSet<Node3D> _hitThisSwing = new();

	private bool _swinging;
	private float _swingTimer;    // counts up 0..SwingDuration while swinging
	private float _cooldownTimer; // counts down; > 0 blocks a new swing

	// The captain's OWN ground velocity, kept separate from the knockback shove so a bumper/
	// sword fling doesn't feed back into itself frame to frame (we add KnockbackVelocity in
	// only at the MoveAndSlide). The captain is now susceptible to shoves like everyone else.
	private Vector3 _moveVel = Vector3.Zero;

	// --- Mount (M10, Chunk 28) ---
	// The captain can climb onto a nearby Mount (Donkey, later Chocobo) to ride faster. While
	// mounted the player keeps its full move/aim/attack pipeline — only the top Speed changes and
	// the body is lifted onto the mount's back; the Mount mirrors us underneath as one silhouette.
	// --- Squad commands (M7, Chunk 49) ---
	[Export] public float AttackMoveDistance = 12.0f;  // how far ahead of the captain an attack-move order targets

	[Export] public float MountRange = 3.0f;   // how close a mount must be to climb on (the mount's own range can extend this)
	private Mount _mount;                        // current mount, or null on foot
	private float _footSpeed;                    // top Speed on foot, restored on dismount
	private float _footY;                        // ground Y we stand at on foot, restored on dismount
	public bool IsMounted => _mount != null;
	public Mount CurrentMount => _mount;

	public override void _Ready()
	{
		base._Ready();   // init Health from MaxHealth
		// AI subordinates (M15) are NOT captains: they stay out of "player" so the camera, ally formation
		// anchor, and GameManager's both-captains-dead lose check only ever see the real hero eggs.
		if (Control != ControlScheme.Ai)
			AddToGroup("player");   // allies find their formation anchor through this group

		_swordPivot = GetNode<Node3D>("SwordPivot");
		_hitbox = GetNode<Area3D>("SwordPivot/Hitbox");
		_hitboxShape = GetNode<CollisionShape3D>("SwordPivot/Hitbox/CollisionShape3D");

		// Build the weapon table from the Inspector exports, and find each weapon's mesh node
		// (missing ones are fine — GetNodeOrNull keeps a weapon usable even without its mesh).
		_profiles = new Dictionary<WeaponType, WeaponProfile>
		{
			// The Style column (M18): spear/pike stay Thrust; Sword SWEEPS (Chunk 95); Axe CHOPS (Chunk 96);
			// Mace now SWINGS a wide round-house (Chunk 97); punch = Jab. Thrust/Jab keep the byte-identical slide.
			[WeaponType.Spear] = new WeaponProfile(SpearDamage, SpearKnockback, SpearReach, SpearThrustDistance, SpearSwingDuration, SpearSwingCooldown, AttackStyle.Thrust),
			[WeaponType.Sword] = new WeaponProfile(SwordDamage, SwordKnockback, SwordReach, SwordThrustDistance, SwordSwingDuration, SwordSwingCooldown, AttackStyle.Sweep),
			[WeaponType.Axe]   = new WeaponProfile(AxeDamage,   AxeKnockback,   AxeReach,   AxeThrustDistance,   AxeSwingDuration,   AxeSwingCooldown,   AttackStyle.Chop),
			[WeaponType.Mace]  = new WeaponProfile(MaceDamage,  MaceKnockback,  MaceReach,  MaceThrustDistance,  MaceSwingDuration,  MaceSwingCooldown,  AttackStyle.Swing),
			[WeaponType.Punch] = new WeaponProfile(PunchDamage, PunchKnockback, PunchReach, PunchThrustDistance, PunchSwingDuration, PunchSwingCooldown, AttackStyle.Jab),
		};
		_weaponMeshes = new Dictionary<WeaponType, Node3D>
		{
			[WeaponType.Spear] = GetNodeOrNull<Node3D>("SwordPivot/Spear"),
			[WeaponType.Sword] = GetNodeOrNull<Node3D>("SwordPivot/SwordMesh"),
			[WeaponType.Axe]   = GetNodeOrNull<Node3D>("SwordPivot/AxeMesh"),
			[WeaponType.Mace]  = GetNodeOrNull<Node3D>("SwordPivot/MaceMesh"),
		};

		// Own the hitbox box so resizing it for a weapon never mutates a shared resource.
		if (_hitboxShape.Shape is BoxShape3D box)
			_hitboxShape.Shape = (BoxShape3D)box.Duplicate();

		// The weapon points straight forward (-Z) at rest; the thrust slides it out and back.
		_swordPivot.Rotation = Vector3.Zero;
		// A basic egg (M15) spawns with the weak Punch; everyone else with their per-level StartingWeapon.
		ApplyWeapon(StartUnarmed ? WeaponType.Punch : StartingWeapon);   // sets reach/damage/knockback/feel + shows the right mesh
		SetThrustOffset(0f);
		SetHitboxActive(false);

		if (StartingAbility != AbilityType.None)
			GrantAbility(StartingAbility);   // usually None; a brawl scene / card grants abilities (M15)

		_formationYaw = Rotation.Y;   // squad starts wheeled to wherever we spawn facing
	}

	// The squad's current facing yaw, and the basis allies rotate their formation offset by. Public so
	// every Ally bound to this captain anchors its slot + facing to the SLOW wheel rather than the
	// captain's instant aim (M7 turn-feel pass).
	public float FormationYaw => _formationYaw;
	public Basis FormationBasis => new Basis(Vector3.Up, _formationYaw);

	// Cycle to the next weapon in enum order (bound to `swap_weapon`), wrapping around. Skips the
	// unarmed Punch so pressing Q only ever lands on a real weapon — existing armed captains cycle
	// the same four weapons as before, and a card-armed egg can't accidentally swap back to its fists.
	public void SwapWeapon()
	{
		WeaponType next = _weapon;
		do { next = (WeaponType)(((int)next + 1) % WeaponCount); }
		while (next == WeaponType.Punch);
		ApplyWeapon(next);
		GD.Print($"[Player] swapped to {_weapon} (reach {_reach:0.0} m, knockback {_knockback:0.0}, cooldown {_swingCooldown:0.00}s)");
	}

	// Jump straight to a specific weapon (per-level default / tests).
	public void SetWeapon(WeaponType weapon) => ApplyWeapon(weapon);

	// Arm the egg at runtime (M15, Chunk 66) — a card grants a weapon and the basic egg picks it up,
	// reusing the same profile plumbing as the per-level loadout/swap. Public so cards (and the headless
	// test) can upgrade an egg straight from its weak Punch to a real weapon's reach/damage/knockback.
	public void EquipWeapon(WeaponType weapon)
	{
		ApplyWeapon(weapon);
		GD.Print($"[Player] {Name} equipped {_weapon} (reach {_reach:0.0} m, dmg {_damage:0.0}, knockback {_knockback:0.0})");
	}

	// --- Castable abilities (M15 redesign) ---

	// The full per-ability cooldown (drives both the cast gate and the HUD readout).
	private float CooldownFor(AbilityType a) => a switch
	{
		AbilityType.Fireball => FireballCooldown,
		AbilityType.Enrage   => EnrageCooldown,
		AbilityType.Heal     => HealCooldown,
		AbilityType.Dash     => DashCooldown,
		_                    => 0f,
	};

	// Grant the egg an ability (an ability card). Public so cards (and the headless test) can hand one to a
	// specific egg. Adds a bar slot, ready to cast; re-granting an ability it already has just refreshes it
	// (resets the cooldown). AbilityType.None is a no-op. The bar caps at the number of hotkeys.
	public void GrantAbility(AbilityType kind)
	{
		if (kind == AbilityType.None)
			return;
		foreach (AbilitySlot s in _abilities)
			if (s.Type == kind) { s.Timer = 0f; GD.Print($"[Player] {Name} refreshed ability {kind}"); return; }
		if (_abilities.Count >= AbilityKeys.Length)
			return;
		_abilities.Add(new AbilitySlot { Type = kind, Cooldown = CooldownFor(kind), Timer = 0f });
		GD.Print($"[Player] {Name} granted ability {kind} (slot {_abilities.Count})");
	}

	// Wipe the bar (the brawl calls this each turn so abilities last only for the wave they were played
	// for). Also drops any in-progress aim. Public so the round loop / a test can reset an egg.
	public void ClearAbilities()
	{
		_abilities.Clear();
		_enrageTimer = 0f;
		CancelAiming();
	}

	// Cast bar slot `slot` at an optional target point (null = toward facing, used by gamepad/AI/instant).
	// Gated on being alive + the slot's cooldown. Returns true if a cast actually went out. Public so the
	// UI / headless test can drive a cast without faking input.
	public bool CastSlot(int slot, Vector3? targetPoint)
	{
		if (slot < 0 || slot >= _abilities.Count || IsDead)
			return false;
		AbilitySlot s = _abilities[slot];
		if (!s.Ready)
			return false;

		switch (s.Type)
		{
			case AbilityType.Fireball: CastFireball(targetPoint); break;
			case AbilityType.Enrage:   _enrageTimer = EnrageDuration;
			                           GD.Print($"[Player] {Name} ENRAGED (x{EnrageFactor:0.0} atk for {EnrageDuration:0.0}s)"); break;
			case AbilityType.Heal:     Heal(HealAmount);
			                           GD.Print($"[Player] {Name} healed {HealAmount:0.0} -> {Health:0.0}/{MaxHealth:0.0}"); break;
			case AbilityType.Dash:     CastDash(targetPoint); break;
			default: return false;
		}
		s.Timer = s.Cooldown;
		return true;
	}

	// Back-compat (Chunk 67 test): cast the first bar slot toward facing.
	public bool CastAbility() => CastSlot(0, null);

	// Lob a Fireball — toward `targetPoint` if given (P1's clicked spot), else along our facing (-Z, where a
	// gamepad/AI egg is aiming). Damage is baked in at cast time as a MAGIC hit scaled by our Intelligence,
	// so a smarter egg's bolts hit harder. Spawned on our parent so it outlives us, tagged with our team.
	private void CastFireball(Vector3? targetPoint)
	{
		FireballScene ??= GD.Load<PackedScene>("res://scenes/Fireball.tscn");
		if (FireballScene == null)
		{
			GD.PrintErr($"[Player] {Name} has no FireballScene");
			return;
		}

		Vector3 dir = AimDirection(targetPoint);

		var fb = FireballScene.Instantiate<Fireball>();
		fb.Damage = ScaledMagicDamage(FireballDamage);   // Intelligence scales magic (Chunk 36 lane)
		GetParent().AddChild(fb);
		fb.GlobalPosition = GlobalPosition + dir * 1.0f; // emerge just ahead of the egg
		fb.Launch(dir, Team);

		GD.Print($"[Player] {Name} cast Fireball for {fb.Damage:0.0} (Int x{IntelligenceMultiplier:0.00})");
	}

	// Blink toward `targetPoint` (capped at DashRange), or DashRange along our facing if none. Instant
	// reposition on the flat plane; snaps onto terrain on a grounded level.
	private void CastDash(Vector3? targetPoint)
	{
		Vector3 dir = AimDirection(targetPoint);
		float dist = DashRange;
		if (targetPoint.HasValue)
		{
			Vector3 to = targetPoint.Value - GlobalPosition; to.Y = 0f;
			dist = Mathf.Min(DashRange, to.Length());
		}
		Vector3 dest = GlobalPosition + dir * dist;
		dest.Y = Grounded ? SampleGroundHeight(dest.X, dest.Z, GlobalPosition.Y) + GroundedSpawnLift : GlobalPosition.Y;
		GlobalPosition = dest;
		GD.Print($"[Player] {Name} dashed {dist:0.0} m");
	}

	// Flat unit direction toward `targetPoint`, or our facing (-Z) when none is given / it's right on us.
	private Vector3 AimDirection(Vector3? targetPoint)
	{
		Vector3 dir;
		if (targetPoint.HasValue)
		{
			dir = targetPoint.Value - GlobalPosition; dir.Y = 0f;
			if (dir.LengthSquared() > 0.0001f)
				return dir.Normalized();
		}
		dir = -GlobalTransform.Basis.Z; dir.Y = 0f;
		return dir.LengthSquared() > 0.0001f ? dir.Normalized() : Vector3.Forward;
	}

	// --- Ability bar input (M15 redesign) ---

	// Per-frame ability driving: tick cooldowns + the Enrage buff, then read the bar's hotkeys for this
	// scheme. P1 (keyboard) presses 1–4: an INSTANT ability fires at once, a TARGETED one toggles aim mode
	// (a ground reticle follows the mouse; left-click casts at the spot, right-click cancels). P2 (gamepad)
	// fires its buttons straight away, targeted casts going toward its aim. An AI subordinate auto-casts its
	// first ready ability at its quarry.
	private void UpdateAbilityInput(float dt)
	{
		for (int i = 0; i < _abilities.Count; i++)
			if (_abilities[i].Timer > 0f)
				_abilities[i].Timer = Mathf.Max(0f, _abilities[i].Timer - dt);
		if (_enrageTimer > 0f)
			_enrageTimer -= dt;

		if (_abilities.Count == 0)
		{
			CancelAiming();
			return;
		}

		if (Control == ControlScheme.Ai)
		{
			if (_aiTarget != null && IsInstanceValid(_aiTarget))
				for (int i = 0; i < _abilities.Count; i++)
					if (_abilities[i].Ready) { CastSlot(i, _aiTarget.GlobalPosition); break; }
			return;
		}

		if (Control == ControlScheme.Gamepad)
		{
			int n = Mathf.Min(_abilities.Count, GamepadAbilityButtons.Length);
			for (int i = 0; i < n; i++)
				if (JustPressed(Input.IsJoyButtonPressed(DeviceId, GamepadAbilityButtons[i]), ref _abilityHeldPrev[i]))
					CastSlot(i, null);   // no mouse → cast toward our aim/facing
			return;
		}

		// KeyboardMouse / Any: number keys 1–4 select the slot; targeted ones then aim with the mouse.
		int slots = Mathf.Min(_abilities.Count, AbilityKeys.Length);
		for (int i = 0; i < slots; i++)
			if (JustPressed(Input.IsPhysicalKeyPressed(AbilityKeys[i]), ref _abilityHeldPrev[i]))
				OnAbilityHotkey(i);

		if (_aimingSlot >= 0)
			UpdateAiming();
	}

	// P1 pressed bar slot `i`: instant abilities fire now; targeted ones toggle the aim reticle.
	private void OnAbilityHotkey(int i)
	{
		if (i < 0 || i >= _abilities.Count)
			return;
		if (AbilityIsTargeted(_abilities[i].Type))
		{
			if (_aimingSlot == i) CancelAiming();   // press again to put the spell away
			else { _aimingSlot = i; ShowReticle(true); }
		}
		else
		{
			CancelAiming();
			CastSlot(i, null);
		}
	}

	// While P1 is aiming a targeted ability: float the reticle under the cursor, cast on left-click (the
	// click is consumed here so it never also swings), cancel on right-click.
	private void UpdateAiming()
	{
		if (_aimingSlot < 0 || _aimingSlot >= _abilities.Count)
		{
			CancelAiming();
			return;
		}

		bool gotPoint = GroundPointUnderMouse(out Vector3 point);
		if (gotPoint)
			MoveReticle(point);

		bool rmb = Input.IsMouseButtonPressed(MouseButton.Right);
		bool rmbEdge = rmb && !_aimCancelPrev;
		_aimCancelPrev = rmb;
		if (rmbEdge)
		{
			CancelAiming();
			return;
		}

		if (gotPoint && AttackJustPressed())   // consumes the LMB edge so UpdateSwing won't swing
		{
			int slot = _aimingSlot;
			CancelAiming();
			if (CastSlot(slot, point))
				_castConfirmedThisFrame = true;
		}
	}

	private void CancelAiming()
	{
		_aimingSlot = -1;
		ShowReticle(false);
	}

	// Ground point (y=0 plane) under the mouse cursor via a camera ray — the targeted-cast aim point.
	private bool GroundPointUnderMouse(out Vector3 point)
	{
		point = Vector3.Zero;
		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null)
			return false;
		Vector2 m = GetViewport().GetMousePosition();
		Vector3 from = cam.ProjectRayOrigin(m);
		Vector3 dir = cam.ProjectRayNormal(m);
		if (Mathf.IsZeroApprox(dir.Y))
			return false;
		float t = (0f - from.Y) / dir.Y;
		if (t < 0f)
			return false;
		point = from + dir * t;
		point.Y = 0f;
		return true;
	}

	// A flat ground ring that marks where a targeted cast will land, built once on first aim and parented to
	// the level so it sits in the world (not under the egg). Tinted by the aimed ability.
	private void EnsureReticle()
	{
		if (_reticle != null && IsInstanceValid(_reticle))
			return;
		var ring = new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = 0.7f, OuterRadius = 1.0f, RingSegments = 24, Rings = 8 },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};
		ring.MaterialOverride = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(1f, 0.55f, 0.2f),
		};
		_reticle = ring;
		GetParent().AddChild(_reticle);
	}

	private void ShowReticle(bool on)
	{
		if (on)
		{
			EnsureReticle();
			// Tint by the aimed ability (fireball orange, dash cyan).
			if (_reticle is MeshInstance3D mi && mi.MaterialOverride is StandardMaterial3D mat && _aimingSlot >= 0)
				mat.AlbedoColor = _abilities[_aimingSlot].Type == AbilityType.Dash
					? new Color(0.4f, 0.85f, 1f) : new Color(1f, 0.55f, 0.2f);
		}
		if (_reticle != null && IsInstanceValid(_reticle))
			_reticle.Visible = on;
	}

	private void MoveReticle(Vector3 point)
	{
		if (_reticle == null || !IsInstanceValid(_reticle))
			return;
		point.Y = 0.05f;   // hair above the ground to avoid z-fighting
		_reticle.GlobalPosition = point;
	}

	// --- Co-op Card Brawl: player-buff cards (M15, Chunk 68) ---

	// Fallback subordinate scene for a Soldier card that carries no SpawnPath.
	[Export] public PackedScene SoldierScene;

	// ICardPlayer: resolve a PlayerBuff card on THIS egg (the player who played it). Weapon arms the egg
	// (runtime EquipWeapon, Chunk 66), Ability grants a castable spell (GrantAbility, Chunk 67), and
	// Soldier spawns a subordinate that fights on our team. The card model routes here via CardPlay.Play.
	public void ApplyCard(Card card)
	{
		if (card == null)
			return;
		switch (card.Buff)
		{
			case Card.BuffKind.Weapon:  EquipWeapon(card.BuffWeapon); break;
			case Card.BuffKind.Ability: GrantAbility(card.BuffAbility); break;
			case Card.BuffKind.Soldier: SpawnSoldier(card); break;
		}
	}

	// Spawn a subordinate just ahead of the egg, on our team, so it fights the survival wave beside us.
	// Dropped into our parent (the level's Units container) so it lives independently of us.
	private void SpawnSoldier(Card card)
	{
		PackedScene scene = !string.IsNullOrEmpty(card.SpawnPath)
			? GD.Load<PackedScene>(card.SpawnPath)
			: (SoldierScene ??= GD.Load<PackedScene>("res://scenes/Ally.tscn"));
		if (scene == null || scene.Instantiate() is not Unit soldier)
		{
			GD.PrintErr($"[Player] {Name} couldn't spawn a soldier from {card.SpawnPath}");
			return;
		}
		soldier.Team = Team;   // fights on the egg's side (an Ally re-asserts Player team in _Ready)
		GetParent().AddChild(soldier);

		Vector3 fwd = -GlobalTransform.Basis.Z;
		fwd.Y = 0f;
		fwd = fwd.LengthSquared() > 0.0001f ? fwd.Normalized() : Vector3.Forward;
		soldier.GlobalPosition = GlobalPosition + fwd * 2.0f;
		GD.Print($"[Player] {Name} spawned a soldier ({soldier.Name})");
	}

	// --- Mount (Chunk 28) ---

	// `mount` toggles: off a mount if riding one, otherwise climb onto the nearest in range.
	public void ToggleMount()
	{
		if (_mount != null)
			Dismount();
		else
			TryMount();
	}

	// Climb onto the nearest unridden mount within range. Returns true if we mounted. Public so the
	// headless test can drive it without faking input. Riding raises our top Speed to the mount's,
	// lifts us onto its back, and hands the mount to us as our carried silhouette.
	public bool TryMount()
	{
		if (_mount != null)
			return false;

		Mount best = null;
		float bestSq = float.MaxValue;
		foreach (Node n in GetTree().GetNodesInGroup("mounts"))
		{
			if (n is not Mount m || m.IsRidden)
				continue;
			float range = Mathf.Max(MountRange, m.MountRange);
			float dSq = GlobalPosition.DistanceSquaredTo(m.GlobalPosition);
			if (dSq <= range * range && dSq < bestSq)
			{
				bestSq = dSq;
				best = m;
			}
		}
		if (best == null)
			return false;

		_mount = best;
		_footSpeed = Speed;
		_footY = GlobalPosition.Y;
		Speed = best.MountSpeed;

		// Hop onto the mount: snap to its spot, lifted one RiderHeight up. From here the Mount
		// mirrors our position/yaw each frame, so we ride as one.
		GlobalPosition = new Vector3(best.GlobalPosition.X, best.GlobalPosition.Y + best.RiderHeight, best.GlobalPosition.Z);
		best.OnMounted(this);

		GD.Print($"[Player] mounted {best.Name} (speed {_footSpeed:0.0} -> {Speed:0.0})");
		return true;
	}

	// Climb off: drop back to foot speed and ground height, a step to the side of the mount.
	public void Dismount()
	{
		if (_mount == null)
			return;

		Mount m = _mount;
		_mount = null;
		Speed = _footSpeed;
		m.OnDismounted();

		// Step off to our left so we don't stand inside the steed; back down to ground height.
		Vector3 side = GlobalTransform.Basis.X.Normalized();
		Vector3 p = m.GlobalPosition + side * Mathf.Max(1.0f, m.MountRange * 0.5f);
		p.Y = _footY;
		GlobalPosition = p;

		GD.Print($"[Player] dismounted {m.Name} (speed -> {Speed:0.0})");
	}

	// --- Squad commands (Chunk 49) ---

	// Push a command to every Ally that answers to THIS captain (its CaptainPath squad, or the
	// whole player squad in single-player). Hold plants each ally where it stands; Attack-move sends
	// them to a point ahead of our facing; Follow recalls them to formation. Public so a headless
	// test can drive it without faking input.
	public void IssueSquadCommand(Ally.CommandMode mode)
	{
		Vector3 attackTarget = AttackMoveTarget();
		int n = 0;
		foreach (Node node in GetTree().GetNodesInGroup("units"))
		{
			if (node is not Ally a || a.Captain != this)
				continue;
			switch (mode)
			{
				case Ally.CommandMode.Hold:       a.HoldAt(a.GlobalPosition); break;   // plant where it stands
				case Ally.CommandMode.AttackMove: a.AttackMoveTo(attackTarget); break; // push ahead of the captain
				default:                          a.FollowCaptain(); break;
			}
			n++;
		}
		GD.Print($"[Player] {Name} commanded squad ({n}): {mode}");
	}

	// A point AttackMoveDistance metres ahead of the captain's facing (forward is -Z), on the flat
	// plane — the rally point an attack-move order pushes the squad toward.
	private Vector3 AttackMoveTarget()
	{
		Vector3 fwd = -GlobalTransform.Basis.Z;
		fwd.Y = 0f;
		fwd = fwd.LengthSquared() > 0.0001f ? fwd.Normalized() : Vector3.Forward;
		Vector3 target = GlobalPosition + fwd * AttackMoveDistance;
		target.Y = 0f;   // rally point lives on the flat plane, not at the captain's capsule height
		return target;
	}

	// Load a weapon's profile into the active fields, show its mesh, and resize the thrust
	// hitbox so its forward length matches the weapon's reach (the box grows out along -Z from
	// the pivot). Called on spawn and on every swap.
	private void ApplyWeapon(WeaponType weapon)
	{
		_weapon = weapon;
		WeaponProfile p = _profiles[weapon];

		_damage         = p.Damage;
		_knockback      = p.Knockback;
		_reach          = p.Reach;
		_thrustDistance = p.ThrustDistance;
		_swingDuration  = p.SwingDuration;
		_swingCooldown  = p.SwingCooldown;
		_style          = p.Style;

		// Show only the active weapon's mesh.
		foreach (var (type, mesh) in _weaponMeshes)
			if (mesh != null)
				mesh.Visible = type == weapon;

		// Shape the hitbox to the weapon (M18, Chunk 98): the box spans 0..-_reach along the pivot's forward
		// axis (positioned at -_reach/2), and its WIDTH matches the style — a wide fan for a swept sword/mace
		// so the arc connects across a cluster, a narrow forward line for a thrust/chop/jab poking straight
		// ahead. The pivot's yaw (sweep/swing) then arcs this box across the front.
		if (_hitboxShape?.Shape is BoxShape3D box)
		{
			Vector3 size = box.Size;
			size.Z = _reach;
			size.X = IsSweptStyle(_style) ? SweptHitboxWidth : ThrustHitboxWidth;
			box.Size = size;

			Vector3 pos = _hitboxShape.Position;
			pos.Z = -_reach * 0.5f;
			_hitboxShape.Position = pos;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		DecayKnockback(dt);
		_castConfirmedThisFrame = false;

		if (IsDead)
		{
			// Game over: ignore input/aim, just bleed off momentum in place.
			_moveVel = _moveVel.MoveToward(Vector3.Zero, Friction * dt);
			Velocity = ComposeMovement(_moveVel + KnockbackVelocity, dt);
			MoveAndSlide();
			return;
		}

		// Subordinate AI (M15): compute this frame's synthetic move/aim/attack intent BEFORE the shared
		// pipeline reads it, so MoveInput()/Aim()/AttackJustPressed() route through the AI cases below.
		if (Control == ControlScheme.Ai)
			UpdateAi(dt);

		// Move read is scheme-aware (Chunk 44): Any = blended action, KeyboardMouse = WASD only,
		// Gamepad = this pad's left stick. Auto-normalized so diagonals aren't faster.
		// Screen-up (W / stick up) maps to -Z (away from the camera).
		Vector2 input = MoveInput();
		Vector3 direction = new Vector3(input.X, 0f, input.Y);

		// Keep tracking intended input velocity every frame (no special "shoved" branch). When a
		// hard shove is active its authority is scaled out below, but _moveVel stays current so
		// control returns smoothly as the shove decays — no snap.
		Vector3 target = direction * Speed;
		_moveVel = direction != Vector3.Zero
			? _moveVel.MoveToward(target, Acceleration * dt)
			: _moveVel.MoveToward(Vector3.Zero, Friction * dt);

		// A bumper / chained pinball hit reads as one clean launch: while the shove is strong
		// OwnMovementScale suppresses steering, then eases it back in PROPORTIONALLY as the shove
		// decays (no hard threshold snap that read as a second bump). Add in the lingering shove
		// only here. ComposeMovement folds in gravity on grounded terrain (Highlands); on every flat
		// level it leaves Y at 0 so motion is byte-identical to before.
		float own = OwnMovementScale;
		Velocity = ComposeMovement(new Vector3(_moveVel.X * own, 0f, _moveVel.Z * own) + KnockbackVelocity, dt);
		MoveAndSlide();
		ResolveKnockbackBounce();   // captain bounces off walls/bumpers/units like everyone else

		Aim(dt);   // mouse (Any/KeyboardMouse) or this pad's right stick (Gamepad)

		// Wheel the formation slowly toward our (fast) aim: the squad can't snap around (M7 turn-feel pass).
		float wheelStep = Mathf.DegToRad(FormationTurnSpeed) * dt;
		float wheelDiff = Mathf.Clamp(Mathf.Wrap(Rotation.Y - _formationYaw, -Mathf.Pi, Mathf.Pi), -wheelStep, wheelStep);
		_formationYaw += wheelDiff;

		// Climb onto / off a nearby mount (read once here so it never double-fires across mounts).
		if (MountJustPressed())
			ToggleMount();

		// Swap weapons between thrusts (not mid-swing, so the hitbox never resizes live).
		if (!_swinging && SwapJustPressed())
			SwapWeapon();

		// Direct the squad (M7): recall to formation / plant a hold / push an attack-move.
		if (CommandFollowPressed()) IssueSquadCommand(Ally.CommandMode.Follow);
		if (CommandHoldPressed())   IssueSquadCommand(Ally.CommandMode.Hold);
		if (CommandAttackPressed()) IssueSquadCommand(Ally.CommandMode.AttackMove);

		// Drive the ability bar (M15): tick cooldowns, fire hotkeys, run P1's targeted-aim state.
		UpdateAbilityInput(dt);

		UpdateSwing(dt);
	}

	// Drive the thrust state machine: trigger on attack, jab the pike straight out
	// along our facing and retract it over a timed window, keep the hitbox live during
	// the lunge, then cool down.
	private void UpdateSwing(float dt)
	{
		if (_cooldownTimer > 0f)
			_cooldownTimer -= dt;

		// Read the attack edge every frame so the device-scoped schemes tick their prev-state
		// even mid-swing/cooldown (else a held button could mis-fire on the frame cooldown ends).
		bool attackPressed = AttackJustPressed();
		// A click while aiming a targeted ability is the cast-confirm, not a swing (UpdateAbilityInput
		// runs first and consumes that click); suppress the swing on that frame.
		if (!_swinging && _cooldownTimer <= 0f && attackPressed && _aimingSlot < 0 && !_castConfirmedThisFrame)
			StartSwing();


		if (!_swinging)
		{
			// Idle / post-swing: ease the weapon back to its rest pose — fully retracted AND rotated
			// back to straight-ahead (yaw for a swept sword, pitch for a chopped axe), so any style
			// returns from its arc. For a thrust weapon yaw + pitch are always 0, so easing them toward
			// 0 is a no-op (byte-identical).
			float currentOffset = -_swordPivot.Position.Z;
			float t2 = 1f - Mathf.Exp(-SwordReturnLerp * dt);
			SetThrustOffset(Mathf.Lerp(currentOffset, 0f, t2));
			SetPivotYaw(Mathf.Lerp(_swordPivot.Rotation.Y, 0f, t2));
			SetPivotPitch(Mathf.Lerp(_swordPivot.Rotation.X, 0f, t2));
			return;
		}

		_swingTimer += dt;
		float t = Mathf.Clamp(_swingTimer / _swingDuration, 0f, 1f);
		// Pose the weapon for this style's motion (M18, Chunk 94). The hit-polling below stays
		// shared across styles for now; later chunks shape the hitbox region per style.
		AnimateSwing(t);

		// Poll overlaps each frame — bodies can enter as the pike extends.
		// Each body is struck at most once per thrust; only damage enemy-team Units.
		foreach (Node3D body in _hitbox.GetOverlappingBodies())
		{
			if (body == this || !_hitThisSwing.Add(body))
				continue;

			if (body is Unit unit && unit.Team != Team)
			{
				Vector3 hitDir = unit.GlobalPosition - GlobalPosition;
				// Strength scales weapon attack power (Chunk 36); Enrage briefly doubles it on top (M15).
				float dmg = EffectiveAttackDamage;
				unit.TakeDamage(dmg, hitDir, _knockback);      // sword shoves; spear deals no knockback
				GD.Print($"[{_weapon}] {Name} {_style} hit {unit.Name} for {dmg:0.0} (knockback {_knockback})");
			}
		}

		if (_swingTimer >= _swingDuration)
			EndSwing();
	}

	private void StartSwing()
	{
		_swinging = true;
		_swingTimer = 0f;
		_hitThisSwing.Clear();
		SetHitboxActive(true);
	}

	private void EndSwing()
	{
		_swinging = false;
		_cooldownTimer = _swingCooldown;
		SetHitboxActive(false);
		// Offset is ~0 where the thrust ended (fully retracted); the idle branch eases out any residual.
	}

	// === Attack-style dispatcher (M18, Chunk 94) ========================================
	// Pose the weapon pivot for the active weapon's AttackStyle over the swing window `t` (0..1).
	// Each style is its own routine that sets the pivot transform (translation + rotation). For now
	// every weapon resolves to the straight Thrust slide (spear/sword/axe/mace = Thrust, punch = Jab),
	// so the motion is byte-identical to before; later chunks add the Sweep/Chop/Swing routines and
	// flip each weapon's style. The default arm keeps any not-yet-implemented style on the thrust.
	private void AnimateSwing(float t)
	{
		switch (_style)
		{
			case AttackStyle.Sweep: AnimateSweep(t); break;       // sword (Chunk 95)
			case AttackStyle.Chop:  AnimateChop(t); break;        // axe (Chunk 96)
			case AttackStyle.Swing: AnimateWideSwing(t); break;   // mace (Chunk 97)
			default: AnimateThrust(t); break;                     // Thrust + Jab
		}
	}

	// Sweep (sword, M18, Chunk 95): instead of jabbing straight out, the blade ARCS across the front
	// from one side to the other over the swing window. The pivot sits at the egg's centre (offset 0) and
	// yaws from +half to -half, so the forward hitbox box sweeps a fan in front of the captain — a single
	// swing can catch MULTIPLE foes standing in the arc (each struck once via _hitThisSwing in the shared
	// hit-poll). Wider arc = more foes; SweepArcDegrees tunes it.
	private void AnimateSweep(float t)
	{
		SetThrustOffset(0f);                       // pivot at centre — the blade fans around us, not out
		float half = Mathf.DegToRad(SweepArcDegrees) * 0.5f;
		SetPivotYaw(Mathf.Lerp(half, -half, t));   // left -> right across the front
	}

	// Swing (mace, M18, Chunk 97): a wide circular ROUND-HOUSE. Like the sword sweep the pivot sits at the
	// egg centre (offset 0) and yaws across the front, but the arc is much WIDER (SwingArcDegrees, ~250° vs
	// the sword's 140°) so the mace sweeps nearly all the way around — a crowd-clearer that catches a whole
	// cluster of foes in one swing, each shoved hard along its own hit direction (the mace's strong knockback,
	// applied per-victim in the shared hit-poll). The Chunk-98 hitbox shaping widens the swept box so the
	// broad arc connects fairly across the cluster.
	private void AnimateWideSwing(float t)
	{
		SetThrustOffset(0f);                       // pivot at centre — the mace whirls around us, not out
		float half = Mathf.DegToRad(SwingArcDegrees) * 0.5f;
		SetPivotYaw(Mathf.Lerp(half, -half, t));   // sweep the broad arc across the front, left -> right
	}

	// Chop (axe, M18, Chunk 96): a slow, heavy OVERHEAD swing. The pivot stays at the egg centre
	// (offset 0) and PITCHES about the egg's right axis — raise the axe head up/back over the first
	// ChopRaiseFrac of the window, then chop it down through horizontal (and a touch past, ChopDip) over
	// the rest. The forward hitbox box rides the pitch, so it only overlaps a foe on the DOWNSTROKE as it
	// sweeps through horizontal — and because the box is never widened sideways (unlike the sweep), the
	// chop stays NARROW: a single committed hit straight ahead, not a fan. The axe's long SwingCooldown
	// (the heavy-weapon tax) is preserved by its profile. (Thrust weapons never pitch, so SetPivotPitch is
	// only ever non-zero for the axe.)
	private void AnimateChop(float t)
	{
		SetThrustOffset(0f);                       // pivot at centre — the axe swings about us, not out
		float raiseFrac = Mathf.Clamp(ChopRaiseFrac, 0.05f, 0.95f);
		float raise = Mathf.DegToRad(ChopRaiseDegrees);
		float dip = Mathf.DegToRad(ChopDipDegrees);
		float pitch = t < raiseFrac
			? Mathf.Lerp(0f, raise, t / raiseFrac)              // raise the head up/back
			: Mathf.Lerp(raise, -dip, (t - raiseFrac) / (1f - raiseFrac)); // chop down through horizontal
		SetPivotPitch(pitch);
	}

	// Thrust / Jab: jab the weapon straight out along -Z over the first ThrustExtendFrac of the
	// window, then retract over the remainder — a snappy poke, not a wide sweep. (The original
	// inline swing math, lifted verbatim so spear + punch stay byte-identical.)
	private void AnimateThrust(float t)
	{
		float extend = Mathf.Clamp(ThrustExtendFrac, 0.05f, 0.95f);
		float reach = t < extend
			? t / extend                       // 0 -> 1 (lunging out)
			: 1f - (t - extend) / (1f - extend); // 1 -> 0 (pulling back)
		SetThrustOffset(reach * _thrustDistance);
	}

	// Slide the whole pike (mesh + hitbox) forward along our local -Z by `offset` metres.
	private void SetThrustOffset(float offset)
	{
		Vector3 pos = _swordPivot.Position;
		pos.Z = -offset;
		_swordPivot.Position = pos;
	}

	// Yaw the whole weapon pivot (mesh + hitbox) about the egg's up axis; 0 = pointing straight ahead
	// (-Z). Used by the swept styles (sword sweep, M18) so the forward hitbox fans across the front.
	private void SetPivotYaw(float yaw)
	{
		Vector3 r = _swordPivot.Rotation;
		r.Y = yaw;
		_swordPivot.Rotation = r;
	}

	// Pitch the whole weapon pivot (mesh + hitbox) about the egg's right axis; 0 = level (pointing
	// straight ahead, -Z), positive = head raised up/back. Used by the chop style (axe, M18) so the
	// forward hitbox swings down through horizontal on the downstroke.
	private void SetPivotPitch(float pitch)
	{
		Vector3 r = _swordPivot.Rotation;
		r.X = pitch;
		_swordPivot.Rotation = r;
	}

	private void SetHitboxActive(bool active)
	{
		_hitbox.Monitoring = active;
		_hitboxShape.Disabled = !active;
	}

	// The player doesn't vanish on death — it freezes and flips on the game-over UI
	// (any CanvasItem in the "game_over" group). Restart comes later (Chunk 9).
	protected override void OnDeath()
	{
		Velocity = Vector3.Zero;
		SetHitboxActive(false);
		CancelAiming();
		if (_reticle != null && IsInstanceValid(_reticle))
			_reticle.QueueFree();
		// A subordinate egg (M15) isn't a captain — it just falls and is removed (no game-over reveal).
		if (Control == ControlScheme.Ai)
		{
			QueueFree();
			return;
		}
		// Co-op captains leave the reveal to GameManager (only when EVERY captain is down).
		if (ShowGameOverOnDeath)
			foreach (Node n in GetTree().GetNodesInGroup("game_over"))
				if (n is CanvasItem ci)
					ci.Visible = true;
		GD.Print("[Player] GAME OVER");
	}

	// === Subordinate AI (M15 Co-op Card Brawl) ==========================================
	// A Soldier-card egg has no device; this fills in its move/aim/attack intent each frame: hunt the
	// nearest opposing unit (via the same UnitRegistry the rest of the AI uses), close to weapon reach,
	// face it, strike on cooldown, and auto-cast a granted ability when it has a target. Cheap (a handful
	// of subordinates), so it rescans every frame — no caching needed.
	[Export] public float AiStopRangeFrac = 0.85f;   // close to this fraction of reach, then hold and strike
	private Vector2 _aiMoveInput;                      // synthetic MoveInput() for the Ai scheme
	private bool _aiWantAttack;                        // synthetic AttackJustPressed() for the Ai scheme
	private Unit _aiTarget;                            // current quarry (live position chased each frame)

	private void UpdateAi(float dt)
	{
		_aiTarget = UnitRegistry.FindNearestOpponent(Team, GlobalPosition);
		if (_aiTarget == null || !IsInstanceValid(_aiTarget) || _aiTarget.IsDead)
		{
			_aiMoveInput = Vector2.Zero;
			_aiWantAttack = false;
			_aiTarget = null;
			return;
		}

		Vector3 to = _aiTarget.GlobalPosition - GlobalPosition;
		to.Y = 0f;
		float dist = to.Length();
		float stop = EffectiveReach * AiStopRangeFrac;
		_aiMoveInput = dist > stop && dist > 0.001f
			? new Vector2(to.X, to.Z) / dist          // unit XZ toward the quarry
			: Vector2.Zero;                            // in range — stand and strike
		_aiWantAttack = dist <= EffectiveReach;        // UpdateSwing's cooldown gates the actual cadence
	}

	// Turn an AI subordinate to face its quarry (forward is -Z), rate-limited like every other aim.
	private void AimAi(float dt)
	{
		if (_aiTarget == null || !IsInstanceValid(_aiTarget))
			return;
		Vector3 to = _aiTarget.GlobalPosition - GlobalPosition;
		to.Y = 0f;
		if (to.LengthSquared() < 0.0025f)
			return;
		TurnTowardYaw(Mathf.Atan2(-to.X, -to.Z), dt);
	}

	// Rotate the player toward the mouse cursor. We shoot a ray from the camera
	// through the cursor and intersect it with the horizontal plane at the player's
	// height, then turn toward that point (forward is -Z). The turn is RATE-LIMITED
	// to TurnSpeed (deg/s) rather than snapping with LookAt: a real captain can't
	// pivot instantly, and because the phalanx's slots rotate with our facing, an
	// instant snap would whip the whole wall around in a single frame.
	private void AimAtMouse(float dt)
	{
		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null)
			return;

		Vector2 mousePos = GetViewport().GetMousePosition();
		Vector3 from = cam.ProjectRayOrigin(mousePos);
		Vector3 dir = cam.ProjectRayNormal(mousePos);

		if (Mathf.IsZeroApprox(dir.Y))   // ray parallel to ground — no hit
			return;

		float t = (GlobalPosition.Y - from.Y) / dir.Y;
		if (t < 0f)                      // plane is behind the camera
			return;

		Vector3 hit = from + dir * t;
		Vector3 target = new Vector3(hit.X, GlobalPosition.Y, hit.Z);

		// Skip degenerate aim when the cursor is right on top of us.
		if (GlobalPosition.DistanceSquaredTo(target) < 0.0025f)
			return;

		// Desired yaw toward the cursor (forward is -Z, so negate the delta).
		Vector3 toTarget = target - GlobalPosition;
		TurnTowardYaw(Mathf.Atan2(-toTarget.X, -toTarget.Z), dt);
	}

	// Aim dispatch (Chunk 44): the mouse drives Any / KeyboardMouse; the Gamepad scheme turns
	// toward the right stick instead. Either way the turn is rate-limited by TurnTowardYaw.
	private void Aim(float dt)
	{
		if (Control == ControlScheme.Ai)
		{
			AimAi(dt);
		}
		else if (Control == ControlScheme.Gamepad)
		{
			Vector2 stick = StickVector(JoyAxis.RightX, JoyAxis.RightY, AimDeadzone);
			if (stick != Vector2.Zero)                       // below deadzone = no re-aim, hold facing
				TurnTowardYaw(AimMath.StickToYaw(stick), dt);
		}
		else
		{
			AimAtMouse(dt);
		}
	}

	// Turn toward a desired yaw by at most TurnSpeed*dt this frame, shortest way around. Shared by
	// the mouse aim and the gamepad stick aim so both pivot at the same captain-can't-snap rate.
	private void TurnTowardYaw(float desiredYaw, float dt)
	{
		float maxStep = Mathf.DegToRad(TurnSpeed) * dt;
		float diff = Mathf.Wrap(desiredYaw - Rotation.Y, -Mathf.Pi, Mathf.Pi);
		diff = Mathf.Clamp(diff, -maxStep, maxStep);

		Vector3 rot = Rotation;
		rot.Y += diff;
		Rotation = rot;
	}

	// === Scheme-aware input accessors (Chunk 44) =========================================
	// One surface for THIS captain's inputs, switched on Control. Single-player keeps Any =
	// the global action reads; co-op captains read only their own device. Public so allies
	// bound to a captain (Chunk 45) can consult its brace, and the headless test can probe it.

	// Move intent on the XZ input plane (x = right, y = screen-down). Length ≤ 1.
	public Vector2 MoveInput() => Control switch
	{
		ControlScheme.Ai            => _aiMoveInput,
		ControlScheme.KeyboardMouse => KeyboardMove(),
		ControlScheme.Gamepad       => StickVector(JoyAxis.LeftX, JoyAxis.LeftY, MoveDeadzone),
		_                           => Input.GetVector("move_left", "move_right", "move_up", "move_down"),
	};

	// One-frame edges for attack / swap / mount; held state for brace. The Ai scheme never reads a
	// device (so a hero's keystroke can't drive every subordinate) — it returns its synthetic intent,
	// and false for the controls a subordinate has no use for (swap / mount / brace / squad commands).
	public bool AttackJustPressed() => Control switch
	{
		ControlScheme.Ai            => _aiWantAttack,
		ControlScheme.KeyboardMouse => JustPressed(Input.IsMouseButtonPressed(MouseButton.Left), ref _attackHeldPrev),
		ControlScheme.Gamepad       => JustPressed(Input.IsJoyButtonPressed(DeviceId, AttackButton), ref _attackHeldPrev),
		_                           => Input.IsActionJustPressed("attack"),
	};

	public bool SwapJustPressed() => Control switch
	{
		ControlScheme.Ai            => false,
		ControlScheme.KeyboardMouse => JustPressed(Input.IsPhysicalKeyPressed(Key.Q), ref _swapHeldPrev),
		ControlScheme.Gamepad       => JustPressed(Input.IsJoyButtonPressed(DeviceId, SwapButton), ref _swapHeldPrev),
		_                           => Input.IsActionJustPressed("swap_weapon"),
	};

	public bool MountJustPressed() => Control switch
	{
		ControlScheme.Ai            => false,
		ControlScheme.KeyboardMouse => JustPressed(Input.IsPhysicalKeyPressed(Key.E), ref _mountHeldPrev),
		ControlScheme.Gamepad       => JustPressed(Input.IsJoyButtonPressed(DeviceId, MountButton), ref _mountHeldPrev),
		_                           => Input.IsActionJustPressed("mount"),
	};

	public bool BraceHeld() => Control switch
	{
		ControlScheme.Ai            => false,
		ControlScheme.KeyboardMouse => Input.IsMouseButtonPressed(MouseButton.Right) || Input.IsPhysicalKeyPressed(Key.Space),
		ControlScheme.Gamepad       => Input.IsJoyButtonPressed(DeviceId, BraceButton),
		_                           => Input.IsActionPressed("brace"),
	};

	// Squad-command edges (M7, Chunk 49): F / H / G (keyboard) or left-shoulder / d-pad (gamepad).
	public bool CommandFollowPressed() => Control switch
	{
		ControlScheme.Ai            => false,
		ControlScheme.KeyboardMouse => JustPressed(Input.IsPhysicalKeyPressed(Key.F), ref _cmdFollowPrev),
		ControlScheme.Gamepad       => JustPressed(Input.IsJoyButtonPressed(DeviceId, CmdFollowButton), ref _cmdFollowPrev),
		_                           => Input.IsActionJustPressed("command_follow"),
	};

	public bool CommandHoldPressed() => Control switch
	{
		ControlScheme.Ai            => false,
		ControlScheme.KeyboardMouse => JustPressed(Input.IsPhysicalKeyPressed(Key.H), ref _cmdHoldPrev),
		ControlScheme.Gamepad       => JustPressed(Input.IsJoyButtonPressed(DeviceId, CmdHoldButton), ref _cmdHoldPrev),
		_                           => Input.IsActionJustPressed("command_hold"),
	};

	public bool CommandAttackPressed() => Control switch
	{
		ControlScheme.Ai            => false,
		ControlScheme.KeyboardMouse => JustPressed(Input.IsPhysicalKeyPressed(Key.G), ref _cmdAttackPrev),
		ControlScheme.Gamepad       => JustPressed(Input.IsJoyButtonPressed(DeviceId, CmdAttackButton), ref _cmdAttackPrev),
		_                           => Input.IsActionJustPressed("command_attack"),
	};

	// WASD / arrows as a normalized-ish move vector (KeyboardMouse scheme — keyboard only, so a
	// second player's gamepad can't bleed into this captain through the shared move actions).
	private static Vector2 KeyboardMove()
	{
		Vector2 v = Vector2.Zero;
		if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) v.X += 1f;
		if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left))  v.X -= 1f;
		if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down))  v.Y += 1f;
		if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up))    v.Y -= 1f;
		return v.Length() > 1f ? v.Normalized() : v;
	}

	// This pad's two-axis stick, deadzoned. Returns Vector2.Zero below `deadzone`; magnitude
	// preserved for analog feel, clamped to length 1.
	private Vector2 StickVector(JoyAxis ax, JoyAxis ay, float deadzone)
	{
		Vector2 v = new Vector2(Input.GetJoyAxis(DeviceId, ax), Input.GetJoyAxis(DeviceId, ay));
		if (v.Length() < deadzone)
			return Vector2.Zero;
		return v.Length() > 1f ? v.Normalized() : v;
	}

	// Rising-edge helper: true only on the frame `now` goes from up to down.
	private static bool JustPressed(bool now, ref bool prev)
	{
		bool fired = now && !prev;
		prev = now;
		return fired;
	}
}
