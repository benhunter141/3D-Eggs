# 3D Eggs — Project Guide

Godot exe path: C:\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe

> This file is the single source of truth for the project. The conversation that
> created it will be cleared, so everything needed to continue lives here. When the
> user says **"go"**, build the **next unchecked chunk** in §7 — follow the WORKFLOW
> box there. Assume prior chunks already work; the user reports problems if any.
> Per-chunk implementation detail lives in the **code + git log**, not here.

---

## 1. The Vision

A **3D twin-stick action game** — **medieval fantasy**, mostly **melee**:
- Twin-stick movement + mouse aim; **click to attack** (player wields a sword).
- **Allies fight alongside you in formation**; some weapons apply knockback / effects.
- Large numbers of allies and enemies on screen (~50–100 units, eventually).
- **Pinball-like physics** — bouncy knockback, impacts, chaotic collisions.
- **Multiplayer** (play with friends) — last.
- Built entirely with **free** tools.

Theme: the folder is "3D Eggs" 🥚 but the game is **medieval fantasy** — egg theme
dropped. Use medieval/neutral names (`Player`, `Ally`, `Enemy` / `Skeleton`).

## 2. Locked-In Decisions

| Decision | Choice | Why |
|---|---|---|
| Engine | **Godot 4.6.x — .NET / C# build** | Free & OSS; text scene files Claude can edit directly; first-class headless mode. |
| Language | **C#** | User has C#/Unity experience — transfers directly. |
| .NET SDK | **8.0+** (required by Godot 4.4+ for C#) | Godot C# packages target `net8.0`. |
| Target platform | **Desktop download** (Windows first) | Simplest; best physics/multiplayer perf. |
| Controls | **Twin-stick:** WASD = move, mouse = aim, left-click = attack. Gamepad sticks too. | Classic desktop twin-stick feel. |

## 3. Developer Profile

- Comfortable with **C#** and **Unity** — but **new to Godot**.
- Explain Godot-specific concepts (nodes, scenes, signals, editor); don't over-explain
  general programming or C#.
- Wants Claude **hands-on** — build large chunks (scenes, scripts, tests), not just code
  to paste.

## 4. How Claude Works On This Project

- **Claude CANNOT see the running game.** Visual "feel" is judged only by the user
  playing. After building anything visual, ask the user to run it; always give the exact
  run command (§8).
- **What Claude CAN test:** logic/numbers (damage, counts, collision events via
  `GD.Print`), C# compilation (`dotnet build`), project/scene structure.
- **Godot files are text** — read & edit `.tscn`, `project.godot`, `.cs` directly.
- **Headless test:** run a scene with `--headless`; `GD.Print(...)` goes to stdout.
  `scenes/Tests/UnitTest.tscn` is the logic test harness (damage/death, knockback,
  ally follow + slot rotation, loose-leash fists, ranged stones).
- **Always leave the project ready-to-run for the user — never mention the build step.**
  The user does not know or care what `dotnet build` is. After ANY change (and as part of
  "pull"), silently compile the C# so the editor opens clean, and only surface the result
  ("ready to test" / the error). NEVER ask the user to build, NEVER ask whether to build,
  and NEVER use the words "dotnet build" in a reply to them. If it fails, fix it and retry —
  don't hand them a broken project.
- **"pull" means: pull the changes AND get everything ready to test** — fetch/fast-forward,
  then silently compile, then tell the user it's ready to open (and what landed). One word,
  zero follow-up questions.
- Keep iterations small: build one thing → user runs it → adjust.
- **Git — always land work on `master` directly (user-locked).** Never leave a change
  stranded on a feature branch. After any change, **stage only the exact files THIS session
  touched** (`git add <explicit paths>`), then `git commit` — **never `git add -A` / `git add .`**.
  Multiple Claude sessions may share this one working tree at the same time, and a blanket add
  sweeps another session's in-progress files into your commit. Then get it onto the repo's
  default branch **`master`** (fast-forward/merge `master` and push it) so it takes effect
  immediately. The user has **standing authorization** to push/merge to `master` — do NOT wait
  for a PR, review, or extra confirmation, and do NOT stop at a feature branch even if
  per-session task instructions name one. A change isn't "done" until `origin/master` contains it.
- **Parallel-session safety (shared working tree).** Assume another session may be editing files
  right now. So: (1) stage explicit paths, never `-A`; (2) **never run destructive/global git ops**
  (`git reset --hard`, `git checkout -- .`, `git stash`, `git clean`) — they can erase another
  session's uncommitted work irrecoverably; if you think you need one, stop and ask first; (3) before
  editing a file, a fresh `git status`/read tells you if someone else is mid-change in it. For
  deliberate heavy parallel work, prefer `git worktree add` (isolated dir + branch, merge to `master`
  when done) over sharing the tree.

## 5. Architecture & Invariants (the durable design law)

**Spine:** a `Unit` base (`CharacterBody3D`: `Team`, `Health`, `TakeDamage`, `Die`,
knockback-decay, virtual `OnDeath` hook) that `Player`, `Ally`, `Enemy` all extend, so
combat is uniform.

**Groups:** all units join `units` (GameManager counts it for win/lose); player also in
`player` (ally anchor); win/lose UI is in `victory` / `game_over` (Restart button is in
both).

**Target scanning = `UnitRegistry` (M5, NOT the group).** Units register/unregister with
the static `UnitRegistry` in `Unit._Ready`/`_ExitTree`, bucketed by team. AI and
projectiles call `UnitRegistry.FindNearestOpponent(team, pos[, maxRange])` /
`UnitRegistry.Opponents(team)` instead of `GetNodesInGroup("units")` — that group lookup
marshalled a fresh Godot array across the C++↔C# boundary every frame (O(n) alloc × n
scanners), the first wall for 50–100-unit crowds. Queries defensively skip dead/invalid
entries. **When adding any unit/projectile that needs targets, use the registry, never a
per-frame group scan.** AI units further **throttle** the pick (M5, Chunk 18): instead of
re-scanning every frame they call `Unit.ShouldRescanTarget()` → store into `CachedTarget`,
which re-scans only every `TargetRescanInterval` frames (default 6, phase-staggered per
unit so the cost spreads across frames) and reuses the cache in between while still chasing
its LIVE position. A dead/freed cached target forces an immediate re-scan. Allocation-free
(no per-frame closures). Cold-process A/B: ~halves the median physics step (100 units
9.4→4.4 ms). Projectiles (Stone/Arrow) still scan every frame — they need exact proximity.

**Match state:** `GameManager` (node in each level scene) is the single authority — each
frame it declares LOSE if the player is dead, else WIN once all enemy units are cleared.
**Lose is checked first** so a late ally kill can't flash VICTORY over GAME OVER. Once
ended it only listens for the `restart` action (R / gamepad) → `ReloadCurrentScene`.

**Combat rules:**
- **Only the player's sword knocks back** (`SwordKnockback` ≈ 10 m/s, along hit dir).
  Fists, thrown stones, and skeleton hits deal damage with **no** knockback.
- Damage: sword `SwordDamage` (skeleton dies in 3); fists 8; stones 12; skeleton
  `AttackDamage` on `AttackCooldown` (melee on contact).
- `Player.OnDeath` → freeze + show `GAME OVER`; `Enemy.OnDeath` → free itself.

**Pinball collision response (M6, Chunk 20).** Knockback no longer just decays — when a unit
carrying a real shove (> `MinBounceSpeed`, default 2.5 m/s) rams something during its
`MoveAndSlide`, it **hands part of its momentum on** (`KnockbackTransfer`, no damage) and
**bounces the rest back** (`KnockbackBounce` restitution). So one sword-fling chains through a
packed line — the chaotic soul. All injection routes through `Unit.AddKnockback` (clamped to
`MaxKnockback`); the sword's `TakeDamage` shove now goes through it too. Each AI unit calls
`ResolveKnockbackBounce()` right after its `MoveAndSlide`. **Gotcha:** two equal capsules
meeting head-on on the flat plane resolve to a near-vertical contact normal and Godot slides
them *through* each other for a frame, so `GetNormal()`/post-move positions are unreliable for
body-vs-body. The resolver therefore uses the **knockback travel direction** as the impact axis
for unit hits (shove the foe that way, reverse our own shove) and only trusts the surface
normal for **static walls**. The player is currently knockback-immune (its movement code never
folds in `KnockbackVelocity`) — revisit if pinball should toss the captain too.

**Allies = loose leash (user-locked):**
- Steer to a player-relative formation **slot**; offset rotates with player facing
  (`SlotWorldPosition`), so the squad turns with you. Arrive-slowdown on approach.
- Engage the nearest enemy within `LeashRadius` (6 m) **of the slot** (gating on the
  slot, not the ally, prevents scattering); else re-form.
- `Ally.Weapon` = `Fists` | `Stones`. Fists: chase to `AttackRange` and punch. Stones:
  hold near slot, lob a `Stone` every `ThrowCooldown`, close in only if past `ThrowRange`.
- `Stone` projectile: aimed straight on launch, proximity-hits enemy-team units, frees
  on hit or `MaxLifetime`.
- Direct player-issued ally commands come later (M7).

**Ally commands (M7, Chunk 48).** Beyond the loose leash, an `Ally` carries a `CommandMode`
(`Follow` default | `Hold` | `AttackMove`) + a `_commandPoint`. **Follow** = the leash behaviour
above (anchor = the moving formation slot). **Hold** anchors the leash to a planted world point and
re-settles on it. **AttackMove** advances to a world point, engaging foes within `AggroRange` en
route (the same aggro scan MarchMode uses), then holds. The engagement scan anchors on
`CommandAnchor()` and all re-settle/return motion on a shared `ArriveVelocity(point)`. **Off by
default** (everyone spawns `Follow`) and resolved AFTER the `MarchMode` branch, so existing levels and
the football auto-battler are byte-identical. **The captain dispatches them (M7, Chunk 49):**
`Player.IssueSquadCommand(mode)` walks the `units` group for allies whose `Captain == this` and sets
Hold (plant where they stand) / Attack-move (a point `AttackMoveDistance` ahead of the captain's
facing) / Follow. Reads are scheme-aware (`command_follow`/`command_hold`/`command_attack` actions;
keys F/H/G or gamepad left-shoulder/d-pad) so co-op captains command their squads independently.

**Mounts (M10, Chunk 28).** A `Mount` (`CharacterBody3D`, NOT a `Unit` — mounts don't fight/take
damage) joins the `mounts` group. The **Player** owns the `mount` input (read once, so it never
double-fires across mounts): `TryMount()` climbs onto the nearest unridden mount within range
(`Mathf.Max(player.MountRange, mount.MountRange)`), `Dismount()` steps off beside it. While ridden
the player's top `Speed` becomes the mount's `MountSpeed`, the player is lifted `RiderHeight` onto
the mount's back, and the mount mirrors the rider's position/yaw each frame with its own collision
DISABLED — captain + steed read as one silhouette, and the full move/aim/attack pipeline keeps
working (mounted combat). Dismount restores foot `Speed` + ground height. Concrete mounts are just
scenes with their own `MountSpeed`/look (Donkey now; Chocobo = faster, Chunk 29).

**Capture zones (M11, Chunk 30).** A `CapturePoint` (`Area3D`, NOT a `Unit`) counts living units
per team inside its collision cylinder each physics frame via `GetOverlappingBodies()`. Every
`PeriodSeconds` (default 15) it awards a point to the sole holder (one team present, the other
absent); contested (both present) or neutral (empty) awards nobody. Exposes `PlayerScore`,
`EnemyScore`, `State` (Neutral/PlayerHeld/EnemyHeld/Contested), `PeriodTimer`, `PeriodCount`
plus `PeriodEnded` and `StateChanged` signals. A translucent ground disc recolours per state
(gray → blue/red/yellow). M12 ties `PeriodEnded` to card energy.

**KotH mode (M11, Chunk 31).** `KingOfTheHill.tscn` puts the player's phalanx against
swordmen + bowmen around one central `CapturePoint`. `KothManager` (a `CanvasLayer`, the
mode's sole match authority — no `GameManager` in this scene) finds that lone CapturePoint,
shows a live HUD (score, period countdown, contest state) and decides the match: **WIN** at
`WinScore` (3) held periods *or* an enemy wipeout; **LOSE** on player death *or* the enemy
reaching `WinScore`. Lose is checked first each frame (GameManager's rule). It drives the
shared `victory`/`game_over` groups so the same `ResultMenu.tscn` works here. Listed as
battle 6 on LevelSelect.

**Cards: Deck = three piles (M12, Chunk 32).** `scripts/Cards/` is the card battler's PURE MODEL
(plain C#, no Godot types, fully headless-testable). A `Card` is a `Kind` (Unit | Action) + title +
`EnergyCost` + description (Unit cards spawn a unit at a location; Action cards make a friendly unit
act — targeting lands in Chunk 33). A `Deck` owns three `List<Card>` piles — `DrawPile`, `Hand`,
`DiscardPile` — and the only operations that move cards between them: `Draw(n)` (top of draw pile →
hand, **auto-`Reshuffle()`-ing** the discard back under the draw pile when it empties mid-draw),
`Discard(card)` / `DiscardHand()` (hand → discard), `LoadStarter(cards)` (clones a starter list into
a shuffled draw pile). **Invariant:** `TotalCount` (sum across the three piles) never changes — cards
only move, never appear/vanish; the headless test leans on it. Shuffles are seedable (`new Deck(seed)`)
for deterministic tests; `CardLibrary.StarterDeck()` is the shared opening deck. `CardBattle` (a
`CanvasLayer`, `CardBattle.tscn`) is a thin VIEW: draw-count panel (bottom-left), the live hand as
clickable card-buttons (centre, Unit=blue / Action=amber), discard-count panel (bottom-right), plus
Draw / End-Turn controls and an Energy readout. Clicking a card then a target plays it (Chunk 33);
the round/pause loop = Chunk 34, energy gating = Chunk 37.

**Unit stats: HP / Str / Int (M12, Chunk 36).** `Unit` carries `Strength` + `Intelligence` (ints,
default 0) beside its HP (`MaxHealth`/`Health`). **STRENGTH** scales weapon attack power and
strength-based card actions (the Charge lunge, the Rally strike); **INTELLIGENCE** scales magic
actions (Firebolt). Each point adds a flat fraction of the BASE value (`StrengthScale` /
`IntelligenceScale`, default +10% each), exposed as `StrengthMultiplier` / `IntelligenceMultiplier`
(both 1.0 at stat 0, so a stat of 0 resolves to exactly the numbers earlier chunks were tuned around —
buffs only add on top). **All damage funnels through `Unit.ScaledWeaponDamage(base)` (Str) or
`ScaledMagicDamage(base)` (Int)** so a buff lands uniformly: the player's swing, ally fist/pike
strikes, and `PerformAction`'s Rally/Firebolt all route through them. The two stats stay in their
lanes (Str never touches magic, Int never touches a weapon strike). Energy gating = Chunk 37.

**Energy from KotH (M12, Chunk 37).** `EnergyPool` (pure C# in `scripts/Cards/`) is the card economy:
each round's energy = `BaseEnergy` (default 3, keeps the opening hand playable with no ground) + a bonus
per capture point your team HOLDS at the pause (`PerPoint`, default 1) — **territory is your economy.**
`CardBattle` refills it at every pause (and at open) from `CountPlayerHeldPoints()` — capture points in
the `capture_points` group whose `State == PlayerHeld` (read while frozen, so it reflects the end-of-round
holding). Plays are **GATED**: `ResolvePlay` refuses a card unless `EnergyPool.CanAfford` (then `Spend`s
its cost), and unaffordable hand cards render disabled. Two `CapturePoint`s sit on `CardBattle.tscn`.

**Relics & potions (M12, Chunk 39).** The run accumulates two kinds of item, both pure C# in
`scripts/Cards/`. A **`Relic`** is a PERMANENT run-long passive (`RelicKind`: `BonusEnergy` /
`BonusHandSize` / `SpawnStrength` + a `Magnitude`); a **`Potion`** is a ONE-SHOT consumable
(`PotionKind`: `Energy` / `Draw`) that `Apply(EnergyPool, Deck)`s its effect once, then refuses
(`Consumed`). `RunMap.Inventory` (a **`RunInventory`**) carries both; rooms grant them as a guaranteed
bonus on the room reward — **boss rooms hand a relic, event rooms hand a potion** (`RoomReward.BonusRelic`
/ `BonusPotion`, added to the inventory in `TakeReward`). Relics never apply themselves: `RunInventory`
SUMS them by kind into `BonusEnergy` / `BonusHandSize` / `SpawnStrengthBonus`, and `CardBattle` folds
those in each round — `RefillEnergy()` sets `EnergyPool.BonusEnergy` before refilling, `EffectiveHandSize()`
adds to the draw, and `SpawnUnit` bumps each spawned unit's `Strength`. Potions are popped from a left-edge
inventory panel (built in code) → `Potion.Apply` → `Refresh`. `CardLibrary.RelicPool()` / `PotionPool()`
are the grant pools. All headless-testable (`TestRelicsPotions`): a relic's modifier applies, a potion
consumes its effect once, and the run grants both.

**Key files:** `scripts/` — `Player.cs`, `Unit.cs`, `UnitRegistry.cs`, `Ally.cs`,
`Enemy.cs`, `Swordman.cs`, `Bowman.cs`, `Stone.cs`, `Arrow.cs`, `Bumper.cs`, `Mount.cs`,
`CapturePoint.cs`, `KothManager.cs`, `FollowCamera.cs`, `GameManager.cs`, `SceneButton.cs`,
`CrowdTest.cs`, `Hud.cs`, `Cards/Card.cs`, `Cards/Deck.cs`, `Cards/CardLibrary.cs`, `Cards/CardBattle.cs`,
`Cards/EnergyPool.cs`, `Cards/RoundLoop.cs`, `Cards/CardPlay.cs`, `Cards/RunMap.cs`, `Cards/RunInventory.cs`, `Cards/Relic.cs`, `Cards/Potion.cs`.
`scenes/` — `Menu/LevelSelect.tscn` (entry/`main_scene`, carries the full CONTROLS list),
`Menu/ResultMenu.tscn` (reusable win/lose UI), `Hud.tscn` (reusable in-game controls panel +
live weapon readout — instanced in every level), `Levels/Level1_HoldTheLine.tscn`, `Levels/Level2_Pincer.tscn`,
`Levels/Level3_ArrowStorm.tscn`, `Levels/Level4_Onslaught.tscn`,
`Levels/Level5_PinballArena.tscn`, `Levels/KingOfTheHill.tscn`, `Captain.tscn`, `Pikeman.tscn`, `Swordman.tscn`,
`Bowman.tscn`, `Arrow.tscn`, `Skeleton.tscn`, `Ally.tscn`, `Stone.tscn`, `Bumper.tscn`,
`Donkey.tscn`, `Chocobo.tscn`, `CapturePoint.tscn`, `Cards/CardBattle.tscn`, `Tests/UnitTest.tscn`,
`Tests/Crowd.tscn`. (Legacy `Main.tscn` retired — git history keeps it.)

## 6. Roadmap (single-player fun first, multiplayer LAST)

Each milestone must be playable before moving on. **Don't start multiplayer until
M1–M5 feel great** — networking many physics bodies is the hardest part.

- [x] **M0 — Setup:** env verified, project builds, ground + player capsule visible.
- [~] **M1 — Twin-stick melee feel ⭐:** WASD move, mouse aim, click-swing sword (Chunks 1–2). Swing feel-check pending.
- [x] **M2 — Skeletons:** chase, take damage, die; sword knockback; player can die.
- [x] **M3 — Allies in formation:** loose-leash followers that fight (fists + stones) and re-form.
- [~] **M4 — 5v5 vertical slice:** player + 4 allies vs 5 skeletons; win/lose/restart (Chunk 9). Standalone juice (Chunk 10) deferred into per-level tuning.
- [x] **M4.5 — Level select + phalanx battles ⭐:** front-end menu; pikeman captain leads a braceable pike wall vs swordmen + bowmen across hand-designed levels (Chunks 11–16).
- [x] **M5 — Crowds:** `UnitRegistry` + staggered re-scan kill the O(n²) group scans; 50/100 units within the 60 FPS budget (Chunks 17–19). Onslaught balance feel-check pending.
- [~] **M6 — Deeper pinball physics:** bouncy/transferring knockback + `Bumper` posts + Pinball Arena (Chunks 20–22). Balance feel-check pending.
- [x] **M7 — Ally commands:** player directs allies (hold / follow / attack-move). Command model on
  `Ally` (Follow/Hold/AttackMove, off by default, Chunk 48) + per-captain input dispatch (Chunk 49).
  Optional polish (per-ally command HUD marker, attack-move ground reticle) deferred — revisit if asked.
- [x] **M8 — Camera & visual identity polish:** live zoom bias, googly eyes on every unit, recognizable weapon meshes (Chunks 23–25). Eyes/weapon feel-check pending.
- [x] **M9 — Weapons & loadouts:** data-driven `WeaponType→WeaponProfile`; `swap_weapon` (Q) cycles spear/sword/axe/mace with distinct reach/damage/knockback/mesh (Chunks 26–27). Archetype balance feel-check pending.
- [x] **M10 — Mounts:** rideable Donkey + faster Chocobo (mount/dismount via `mount`, mounted combat); both flank the Level 1 spawn (Chunks 28–29). Chocobo-speed feel-check pending.
- [x] **M11 — King of the Hill mode:** central `CapturePoint` scores its holder each period; `KothManager` HUD + match authority; battle 6 on LevelSelect (Chunks 30–31). Feeds M12 energy. Balance feel-check pending.
- [x] **M12 — Slay the Eggs (card battler mode):** StS-style PvE on the battlefield — draw/hand/discard
      piles; Unit cards spawn at a location, Action cards make a friendly unit act; a **round loop**
      runs real-time play for N sec (default 15) then **pauses** to play cards (End Turn resumes);
      units gain HP/Str/Int; energy from holding KotH points; a run of rooms with cards/relics/potions
      (Chunks 32–39, all done). Balance/feel-check pending.
- [x] **M12.5 — Endzone auto-battler reshape:** reshape Slay the Eggs into a football pitch —
      smaller fully on-screen field with two endzones; deploy units in your endzone and they
      march toward the enemy endzone unless aggro'd; 5 s turns; unit-heavy starter deck (Chunks 40–43, all done).
- [x] **M12.7 — Two-player couch co-op ⭐:** a local same-screen 2-player level — P1 on keyboard+mouse,
      P2 on gamepad — each captain leads 6 pikemen + 2 bowmen, fighting a shared AI enemy force; one
      shared camera frames both captains; lose only when BOTH captains fall. Per-captain control schemes +
      controller aim + squad ownership + the shared two-captain camera (Chunks 44–46) + the `CoopStand`
      scene with the both-captains-fall lose rule (Chunk 47). Removed the first four levels from the menu.
      *Local* couch co-op — NOT the networked M13. Feel-check pending (needs a gamepad for P2).
- [ ] **M12.8 — Slay-the-Spire-feel pass (Slay the Eggs look & UX):** make the card mode LOOK and (in the
      PAUSE phase) PLAY like StS without ripping out the real-time march. Real card frames (cost orb / title
      banner / art panel / desc), a compact fanned hover-lift hand that stops eating ~40% of the screen,
      board/tray separation + camera reframe, StS HUD chrome (energy orb + card-back pile stacks), and play
      juice (Chunks 55–59). **Visual reskin only** — two-click play and the auto-battler stay. Drag-to-target +
      enemy intent (was "Chunk F") and a full turn-based remake are explicitly OUT of scope unless asked.
- [~] **M13 — Multiplayer:** 2 players over the network, server-authoritative. Hardest, last.
  Now broken into Chunks 50–54 (net bootstrap → captain spawn/ownership → state replication →
  server-authoritative combat/input RPCs → networked co-op level + synced match state) — see §7.
  **Build gated** on M1–M5 feeling great (and the outstanding feel-checks); plan is laid out so
  the netcode build can start when the user is ready.
- [~] **M14 — Traversable terrain (fighting on slopes):** units walk + fight up and down real terrain
      elevation in the **Highlands** level — heightmap collision + gravity + floor-snapping, terrain-aware
      spawns/formation, ballistic projectiles, and a terrain-following camera (Chunks 60–65). **Highlands
      ONLY**, opt-in via a `Grounded` flag (default OFF), so every flat level (Pinball / KotH / Co-op / card
      battler) stays byte-identical. Highest-risk change so far — it touches the shared movement core of every
      unit type. Chunks 60–61 done: `Scenery` builds a solid `HeightMapShape3D` collider from its height function
      (+ public `SampleHeight`), and `Unit.Grounded` (default OFF) folds gravity + floor-snap into movement via the
      shared `ComposeMovement` chokepoint (every subclass routes through it; flat levels byte-identical). Chunk 62 done:
      `Scenery.SampleActiveHeight` (one active terrain per level) lets grounded units snap onto the surface on spawn
      (`GroundedSpawnLift`), formation slots + ally command points (`SlotWorldPosition`/`HoldAt`/`AttackMoveTo`) sit on the
      terrain, and a ridden `Mount` follows the ground under its rider — all NaN/fallback-guarded so flat levels are
      byte-identical. Chunk 63 done: grounded `Ally`/`Bowman` lob `Stone`/`Arrow` in a gravity arc (shared
      `Ballistics.SolveArcVelocity`) onto the target's REAL height — arrows tip along the arc, lobbed shots free on
      terrain impact; flat levels keep the straight level skim. Chunk 64 done: `FollowCamera` DAMPS its focus
      HEIGHT (eased toward the target/midpoint Y via `EaseFocusHeight`/`FocusHeightLerp`) so the view doesn't jolt
      as the captain climbs/descends, plus a `FocusHeightLift` to sit the frame above the slope; X/Z still track
      snappily and flat levels stay byte-identical (constant Y + zero lift = no-op ease). Chunk 65 done:
      `Scenery.HeightAt` reworked from a flat field ringed by a 26 m wall of hills into GENTLE PLAYABLE
      terrain — low rolling swells across the whole field (`PlayAmplitude`) + a couple of crossing ridges
      (`Ridge()`/`RidgeHeight`/`RidgeWidth`), only the distant backdrop rising for a highland horizon
      (`BackdropHeight`/`RampWidth`); every Highlands unit set `Grounded = true` so they walk/fight the
      slopes. All M14 chunks built. **Terrain REBUILT for cost + looks** (the first pass "looked bad +
      tanked FPS"): the collider now covers only the walled play field (`ColliderHalf`, ≈±50) instead of
      the whole 150 m landscape (a ~100 m heightmap grid vs ~300 m — the big perf win); the visual mesh
      shrank to `TerrainHalf = 90` at `CellSize = 3`; trees 460→160; `directional_shadow_max_distance = 90`.
      Height is now smooth layered **value noise** (`Noise2`/`Hash`) for organic rolling ground instead of
      regular sines, and the mesh colours by **height AND slope** (grass→highland→rock) so it reads as
      terrain. Headless terrain tests still pass (collider matches the new field). knockback-on-slopes
      tuning + Highlands feel-check still open.

## 7. Build Plan (chunks)  ← start here when user says "go"

> **WORKFLOW — what "go" means.** Find the **first unchecked `[ ]` chunk** below and
> build it **completely** (scenes + scripts), assuming all earlier chunks work. Then:
> 1. `dotnet build` — confirm 0 errors.
> 2. **Commit** the chunk (`git add -A && git commit`) as a roll-back point.
> 3. Tick its box `[x]` and update the M-line in §6.
> 4. Reply with a one-line summary + the exact run command (§8). Don't wait for
>    confirmation — the user says if something's off, else "go".
>
> **Always end EVERY reply with a one-sentence summary line** of the form
> `Done: Chunk N — <short name>.` (chunk number + its short name). It must be the very
> last sentence. For work that isn't a numbered chunk, name it succinctly instead
> (e.g. `Done: in-game controls HUD.`).
>
> Keep chunks self-contained and small. Headless-test logic where possible (§4).

**Done (detail in code + git log):**
- [x] **Chunk 1** — Mouse aim + follow camera.
- [x] **Chunk 2** — Sword swing (arc hitbox + cooldown).
- [x] **Chunk 3** — `Unit`/health foundation + damage dummy.
- [x] **Chunk 4** — Sword knockback.
- [x] **Chunk 5** — Skeleton AI (chase + melee) + player can die.
- [x] **Chunk 6** — Allies + formation (movement only).
- [x] **Chunk 7** — Ally combat (loose leash) + fists.
- [x] **Chunk 8** — Stone-throwing allies.
- [x] **Chunk 9** — 5v5 encounter + win/lose; `GameManager` match state.
- [~] **Chunk 10** — Juice & tuning. *Deferred — rolled into per-level tuning; revisit only if asked.*
- [x] **Chunk 11** — Level Select shell + scene routing; reusable Result menu.
- [x] **Chunk 12** — Pike + Pikeman + Brace; `brace` input action.
- [x] **Chunk 13** — Swordman (charge burst + flank-offset + melee, no knockback).
- [x] **Chunk 14** — Bowman + Arrow (range-band kite, flee melee, fire on cooldown).
- [x] **Chunk 15** — Level 1 "Hold the Line"; retired `Main.tscn`.
- [x] **Chunk 16** — Levels 2 "Pincer" & 3 "Arrow Storm"; objective labels; finalized menu.
- [x] **Chunk 17** — `UnitRegistry`: static bucketed target scanning; replaced all group scans.
- [x] **Chunk 18** — `Crowd.tscn` stress harness + staggered target re-scan; 50/100 units within budget.
- [x] **Chunk 19** — Level 4 "Onslaught" (~49-unit crowd battle).
- [x] **Chunk 20** — Bouncy knockback + impact transfer; `Unit.AddKnockback` / `ResolveKnockbackBounce`.
- [x] **Chunk 21** — `Bumper.tscn` static obstacles; `Area3D` kick via `Unit.AddKnockback`.
- [x] **Chunk 22** — Level 5 "Pinball Arena" (walled 44×44 arena + 8 bumpers).
- [x] **Chunk 23** — Adjustable dynamic zoom (`ZoomBias` on mouse wheel / keys).

**M7 — Ally commands:** now planned + in progress as Chunks 48–49 — see the dedicated section
near the end of §7 (it builds late, after the M12.x chunks, so its chunk numbers follow Chunk 47).

---

### ▶ PLANNED — M8 Camera & Visual Identity Polish (Chunks 24–25)

**Goal:** cartoony eyes and recognizable weapon meshes — cheap, high-impact identity wins.

- [x] **Chunk 24 — Cartoony eyes on units.** Give every `Unit` a pair of simple
  camera-/forward-facing eye visuals (white + pupil), sized per archetype, as a child node.
  Pure visual; no logic. User feel-check for cuteness.
- [x] **Chunk 25 — Visually distinct weapon meshes.** Replace placeholder weapon visuals with
  recognizable meshes per weapon (spear/pike shaft+tip, sword blade+guard, bow, stone, arrow)
  so a unit's weapon reads at a glance. Pure visual; reused across the matching scenes.

---

### ▶ PLANNED — M9 Weapons & Loadouts (Chunks 26–27)

**Goal:** make the weapon a real choice — the captain can wield a sword instead of the pike,
and weapons differ in reach / damage / knockback / look.

- [x] **Chunk 26 — Player weapon swap (spear ↔ sword).** Give `Player` a `WeaponType`
  (Spear | Sword) driving hitbox reach, damage, knockback, and thrust/swing feel + the
  Chunk-25 mesh. Add a `swap_weapon` input to toggle in-game (and/or per-level default).
  Sword = short reach + knockback (existing sword rules); spear = long reach, no knockback.
  Headless-test: each weapon's reach + knockback match its profile.
- [x] **Chunk 27 — More weapon archetypes.** Add a couple more weapons (e.g. axe = heavy/slow/
  high-damage, mace = knockback, bow = ranged) as `WeaponType` entries reusing the Chunk-26
  plumbing + Chunk-25 visuals, so allies/enemies can be skinned with them too. Headless-test:
  each archetype's stats resolve correctly.

---

### ▶ PLANNED — M10 Mounts (Chunks 28–29)

**Goal:** rideable mounts for speed & charm — a cute donkey and a chocobo.

- [x] **Chunk 28 — Mount base + Donkey.** `scripts/Mount.cs` + `scenes/Donkey.tscn`:
  mount/dismount (proximity + `mount` input), mounted state raises move speed & changes the
  player silhouette (rider on mount), mounted combat still works; dismount drops you beside the
  mount. Headless-test: mounting raises speed, dismount restores it.
- [x] **Chunk 29 — Chocobo mount.** `scenes/Chocobo.tscn` reusing `Mount.cs` — faster than the
  donkey (`MountSpeed` 13 vs 9), distinct look (taller upright yellow body, orange beak/legs,
  crest + tail feathers, googly eyes). Headless-test: chocobo `MountSpeed` > donkey and riding
  it tops the donkey's ride speed. One added beside the donkey in Level 1 to try.

---

### ▶ PLANNED — M11 King of the Hill Mode (Chunks 30–31)

**Goal:** a scoring mode where holding ground matters — and the foundation Slay the Eggs (M12)
draws energy from.

- [x] **Chunk 30 — Capture zone + period scoring.** `scripts/CapturePoint.cs` +
  `scenes/CapturePoint.tscn`: an `Area3D` zone tracking which team has units inside; every
  **period (15 s for now)** it awards a point to the team holding it at period end (contested =
  no award). Emits a signal / exposes state for HUD + energy hooks. Headless-test: holder at
  period end scores; contested scores nobody.
- [x] **Chunk 31 — KotH mode level + HUD.** `scenes/Levels/KingOfTheHill.tscn` (one central
  `CapturePoint` + player phalanx vs swordmen/bowmen) + `scripts/KothManager.cs` — a `CanvasLayer`
  HUD showing per-team score, period countdown, and contest state, and the mode's match authority:
  win at `WinScore` (3) held periods or an enemy wipeout, lose on player death or enemy 3. Added as
  battle 6 on LevelSelect.

---

### ▶ PLANNED — M12 Slay the Eggs — Card Battler Mode (Chunks 32–39)

**Goal:** a Slay-the-Spire-style PvE mode layered on the battlefield — build a deck and spend
energy (earned by holding KotH points, M11) to deploy units and trigger actions across a run of
rooms.

**New durable rules (promote into §5 as chunks land):**
- **Cards = Units or Actions.** A **Unit card** is played onto a **location** (a battlefield
  zone / `CapturePoint`-style slot) to spawn that unit for your team. An **Action card** is
  played onto a **friendly unit**, which then performs the action (move / attack / buff / spell).
- **Round = timed real-time play + tactical pause.** The mode **starts PAUSED** with an opening hand;
  **End Turn begins a round** — play runs in real time for `RoundSeconds` (default 15, dev-tunable),
  units move/fight and the player may play cards live — then at **timeout** the mode **pauses**
  (battlefield frozen) **and redeals** a fresh hand (discard + refill energy + draw 5); the player
  sets up again and hits **End Turn** to play on. The pause is also where per-period energy is
  awarded (Chunk 37).
- **Unit stats: HP / Str / Int.** **Str** scales weapon attack power + strength-based actions;
  **Int** scales magic-based actions. Stats live on `Unit` (or a card-mode component).
- **Energy from holding ground.** Card energy each round = KotH points your team holds at the
  pause (M11) — territory *is* your economy.
- **Run = rooms.** PvE like StS: a series of rooms (combat / event), collecting **cards**,
  **relics**, and **potions** between them.

- [x] **Chunk 32 — Card model + piles + hand UI.** `scripts/Cards/` data model (Card, Deck) +
  draw / hand / discard piles with reshuffle; on-screen UI showing all three piles (StS-style:
  draw count, hand, discard). Headless-test: draw/discard/reshuffle cycle conserves the deck.
- [x] **Chunk 33 — Unit & Action cards (play targeting).** Unit cards target a **location**
  (spawn there); Action cards target a **friendly unit** (it performs the action). Play resolves
  to a real spawn / a real unit behavior on the battlefield. Headless-test: a unit card spawns at
  a location; an action card makes its target unit act.
- [x] **Chunk 34 — Round loop: timed real-time play + tactical pause.** The card battle **starts
  PAUSED** with an opening hand; **End Turn BEGINS a round** → a **PLAY phase** (real time for
  `RoundSeconds`, default 15; units move/fight and cards are playable live) → at **timeout** it
  auto-flips to a **PAUSE phase** (battlefield frozen via `GetTree().Paused = true`, with `CardBattle`
  at `ProcessMode = Always` and the `Units` node `Pausable` so the UI/cards keep running while units
  freeze) **and redeals** (discard hand, refill energy, draw a fresh 5); End Turn plays the next round.
  Pure `RoundLoop` state machine drives it; HUD banner shows round/phase + countdown. End Turn is
  disabled during PLAY. Headless-tested: starts paused; End Turn begins a round; timeout advances the
  round counter + repauses; cards playable in both phases (`_ExitTree` lifts the global pause on the
  way out so the menu/next scene isn't frozen).
- [x] **Chunk 35 — Dev panel: live round-length control.** A toggleable in-mode dev panel (DEV button
  or F3) to adjust `RoundSeconds` live while testing (−/+ in 5 s steps, clamped 5–60) plus a manual
  pause/resume toggle for debugging. `RoundLoop.RetuneRoundSeconds` caps the live clock to the new
  length; `RoundLoop.Resume` continues the SAME round (no redeal/bump, unlike End Turn). Pure dev tool.
- [x] **Chunk 36 — HP / Str / Int stats wired in.** Add Str/Int to units; Str scales weapon
  damage + strength actions, Int scales magic actions. Headless-test: higher Str → more weapon
  damage; higher Int → stronger magic action.
- [x] **Chunk 37 — Energy from KotH points.** `scripts/Cards/EnergyPool.cs` (pure model): each round's
  energy = a base allowance + a bonus per capture point your team holds at the pause (territory = economy).
  `CardBattle` refills it from the live count of player-held `capture_points` at every pause and GATES
  plays (unaffordable cards are disabled / refused). Two `CapturePoint`s added to `CardBattle.tscn`.
  Headless-tested: holding more points grants more energy; energy gates plays.
- [x] **Chunk 38 — Run structure (rooms + rewards + events).** `scripts/Cards/RunMap.cs` (pure model):
  a fixed-shape sequence of rooms (Combat / Event / Boss) you traverse; `CompleteCurrentRoom()` marks
  the room cleared, advances the map, and returns a `RoomReward` (card choices); `TakeReward()` adds a
  chosen card to the run's growing `Collection` (the deck carried between rooms, seeded from the starter
  deck) or skips. `CardLibrary.RewardPool()` is the reward card pool. `CardBattle` is a thin view:
  room-track HUD, a paused-only "Clear Room" control that pops a reward picker, each new room reloads
  the battle deck from `Collection`, plus a run-complete banner. Headless-tested.
- [x] **Chunk 39 — Relics & potions.** Passive **relics** (run-long modifiers) + consumable
  **potions** (one-shot effects), collected through the run. Headless-test: a relic's modifier
  applies; a potion consumes and triggers its effect.

---

### ▶ PLANNED — M12.5 Endzone Auto-Battler Reshape (Chunks 40–43)

**Goal:** reshape "Slay the Eggs" into a football-pitch auto-battler — a smaller, fully
on-screen field with two **endzones**; you deploy units in YOUR endzone and they **march
toward the enemy endzone unless aggro'd**; faster 5 s turns; a unit-heavy starter deck.
Only `CardBattle` is touched — the other levels (real formations, global enemy chase) must
stay exactly as they are, so every new behavior is **off by default** and `CardBattle`
opts in.

- [x] **Chunk 40 — Football field: smaller arena + endzones + camera reframe.** Shrink the
  `CardBattle.tscn` ground from 50×50 to a smaller pitch (longer along Z than wide — march
  lanes), and add two translucent ground strips: a **player endzone** at the near end (+Z,
  toward camera) and an **enemy endzone** at the far end (−Z). Reframe `Camera3D` (raise /
  pull back / tilt — and/or FOV) so the WHOLE pitch is visible in front of it, including the
  near edge by the camera (today it clips off-screen). Move the seed swordmen/bowmen into the
  far enemy endzone. Store the player-endzone bounds on `CardBattle` for Chunks 41–42. Pure
  visual + layout; **user feel-check** that the field is fully on-screen and uncramped.
- [x] **Chunk 41 — Endzone-gated unit placement.** Unit cards may only be placed inside the
  PLAYER endzone. `TryPlayAtLocation` validates the ground-ray point against the endzone
  bounds and rejects an out-of-zone click with a prompt ("Place units in your endzone") —
  the card stays pending so the player can re-aim. Action-card targeting is unchanged. Pull
  the bounds test into a tiny pure helper (e.g. an `Endzone` struct with `Contains`, in
  `scripts/Cards/`) so it's headless-testable. **Headless-test:** a point inside the endzone
  is accepted, a point outside is rejected.
- [x] **Chunk 42 — Forward-march unit AI (advance to enemy endzone unless aggro'd).** Add a
  shared opt-in march behavior on `Unit` (e.g. `MarchMode` + a per-team `MarchDirection` and
  an `AggroRange`): when no opponent is within `AggroRange`, the unit walks toward the
  OPPOSING endzone (friendly → −Z, enemy → +Z); when one is in range it engages with its
  existing chase/attack. Wire it into `Ally`, `Enemy`, `Swordman`, `Bowman` _PhysicsProcess
  as a fallback path; default OFF so other levels keep global chase / real formations.
  `CardBattle` turns it ON for every unit — the seed enemies (in `_Ready`) and every spawned
  unit (`SpawnUnit`) — with the march direction set by team. **Headless-test:** a march-mode
  unit with no foe in range moves toward its goal direction; with a foe inside `AggroRange`
  it stops marching and engages. Feel-check the advance pacing.
- [x] **Chunk 43 — Mode tuning: 5 s turns + unit-heavy starter deck.** Default `RoundSeconds`
  to **5** (script default + explicit on `CardBattle.tscn`). Reweight `CardLibrary.StarterDeck()`
  to be **mostly Unit cards** (units the clear majority, a few actions) so the opening deck
  is about deploying a force. **Headless-test:** the starter deck is majority Unit; default
  round length is 5 s.

---

### ▶ PLANNED — M12.7 Two-Player Couch Co-op (Chunks 44–47)

**Goal:** one new *local same-screen* co-op level. **Player 1** drives a captain with
**keyboard + mouse**; **Player 2** drives a second captain with a **gamepad**. Each captain
leads **6 pikemen + 2 bowmen** in formation, and both squads fight a **shared AI enemy force**.
A single shared camera frames both captains; the match is lost only when **both** captains fall.
The first four levels are removed from the menu. This is NOT the networked M13 — no netcode,
just two input devices on one machine.

**Invariant — don't disturb the other levels.** Every behavior below is **off by default**
(`Any` control scheme, no `CaptainPath`, single camera target, single-captain lose rule); only
the new co-op scene opts in. Existing levels (real formations, blended keyboard+gamepad on one
captain, single-target camera, lose-on-player-death) must behave EXACTLY as they do today.

**New durable rules (promote into §5 as chunks land):**
- **Control schemes.** `Player.ControlScheme` ∈ {`Any`, `KeyboardMouse`, `Gamepad`}. `Any`
  (default) = today's blended read (keyboard+gamepad move, mouse aim) so single-player levels
  are untouched. `KeyboardMouse` reads only the keyboard for move + mouse for aim. `Gamepad`
  reads a specific pad (`DeviceId`) — **left stick = move, right stick = aim** (directional, no
  mouse), face buttons = attack/brace/swap/mount/zoom.
- **Squad ownership.** An `Ally` with `CaptainPath` set anchors its formation slot + facing to
  THAT captain (not `GetFirstNodeInGroup("player")`), so two squads follow two captains.
- **Co-op match state.** `GameManager` gains `RequireAllPlayersDead` (default false). When true
  (co-op scene), LOSE fires only once EVERY `player`-group captain is dead/gone; WIN is unchanged
  (all enemies cleared).

- [x] **Chunk 44 — Per-captain control schemes + controller aim.** Add `Player.ControlScheme`
  (`Any`|`KeyboardMouse`|`Gamepad`) + `DeviceId`, default `Any` = current behavior. Route move /
  aim / attack / brace / swap / mount / zoom through scheme-aware reads: `Gamepad` uses left stick
  to move, **right stick to aim** (turn toward the stick direction, rate-limited like the mouse
  aim), and that pad's buttons; `KeyboardMouse` uses keyboard + mouse only. Pull the stick-aim
  math (stick vector → desired yaw) into a tiny pure helper. **Headless-test:** right-stick vector
  resolves to the correct facing yaw; `Any` still reads the blended path.
- [x] **Chunk 45 — Squad ownership (allies bound to a specific captain).** Add `Ally.CaptainPath`
  (NodePath export); when set, the ally resolves its captain from it and anchors slot + facing to
  that captain; unset = today's first-`player`-group behavior. **Headless-test:** an ally with an
  explicit captain follows that captain's slot, not another captain's.
- [x] **Chunk 46 — Shared two-player camera.** Give `FollowCamera` an optional second target
  (`Target2`); when set, center on the **midpoint** of both captains and size the distance to keep
  both (plus crowd spread) framed, reusing the dynamic-zoom fit. Single-target path unchanged.
  **Headless-test:** with two targets the focus point is their midpoint and the distance grows as
  they separate.
- [x] **Chunk 47 — Co-op level scene + co-op lose rule + menu.** Build
  `scenes/Levels/CoopStand.tscn`: two captains (P1 `KeyboardMouse`, P2 `Gamepad` device 0), each
  leading **6 Pikemen + 2 bowmen** (friendly archers — re-team a Bowman to the player side or use
  a bow-skinned ranged `Ally`, whichever keeps squad cohesion) wired to their captain via
  `CaptainPath`, vs a shared AI enemy force (swordmen + bowmen); the shared two-captain camera;
  `GameManager` with `RequireAllPlayersDead = true`. **Remove Level 1–4** from `LevelSelect.tscn`
  and delete their scene files (git keeps history); renumber the menu and add the co-op level +
  a P1/P2 controls note. **User feel-check** (needs a gamepad to test P2).

---

### ▶ PLANNED — M7 Ally Commands (Chunks 48–49)

**Goal:** let the captain DIRECT the squad beyond the default loose leash — recall to formation,
hold a spot, or push forward — without disturbing any existing level. Every behaviour is **off by
default** (allies spawn `Follow` = today's loose-leash) and `MarchMode` is resolved before commands,
so the football auto-battler is untouched too.

- [x] **Chunk 48 — Ally command model (Follow / Hold / Attack-move).** `Ally.CommandMode`
  (`Follow` default | `Hold` | `AttackMove`) + a `_commandPoint` anchor and `HoldAt` / `AttackMoveTo`
  / `FollowCaptain` setters. **Follow** = today's behaviour (leash to the moving formation slot).
  **Hold** plants on a fixed point and engages foes within `LeashRadius` of THAT point, else settles
  back on it. **Attack-move** advances to a fixed point, engaging foes within `AggroRange` en route,
  then holds. The leash scan now anchors on `CommandAnchor()` and the arrive logic on a shared
  `ArriveVelocity(point)`. Headless-tested: default is Follow; Hold plants; Attack-move advances; a
  held ally engages a near foe.
- [x] **Chunk 49 — Captain issues squad commands.** `Player` reads scheme-aware command edges
  (Follow / Hold / Attack-move) and `IssueSquadCommand` dispatches them to the allies bound to it
  (`a.Captain == this` — its `CaptainPath` squad, or the whole player squad in single-player):
  **Hold** plants each ally where it stands, **Attack-move** sends them to a point
  `AttackMoveDistance` ahead of the captain's facing, **Follow** recalls them to formation.
  Per-captain (co-op: P1 keys F/H/G, P2 gamepad left-shoulder/d-pad) so squads take orders
  independently. New `command_follow` / `command_hold` / `command_attack` input actions + a HUD hint
  line. Headless-tested: a captain's order reaches only its own squad, with the right mode + target.

**(Later — M7 polish, unnumbered for now: a command-state HUD marker over each ally, and an
attack-move ground-target reticle.)**

---

### ▶ PLANNED — M13 Multiplayer (Chunks 50–54)

**Goal:** 2 players over the network, **server-authoritative** — the hardest, last milestone.
One player **hosts** (acts as server + plays); the other **joins** by address. The server owns
all simulation (units, AI, combat, knockback, match state); clients send input and render
replicated state. Built on **Godot 4 high-level multiplayer** — `ENetMultiplayerPeer`,
`MultiplayerSpawner`, `MultiplayerSynchronizer`, and `[Rpc]` methods — so it layers on the
existing `Unit`/`Player`/`GameManager` spine without rewriting combat.

**Invariant — don't disturb single-player / couch co-op.** All netcode is **off unless a peer
is active** (`Multiplayer.MultiplayerPeer` set / `GetTree().GetMultiplayer().HasMultiplayerPeer()`).
With no peer, every unit keeps simulating locally exactly as today (offline play, the couch-coop
scene, the card battler must be byte-identical). Authority checks short-circuit to "I am authority"
when there's no peer.

**New durable rules (promote into §5 as chunks land):**
- **Server is the sole authority.** Only the server runs AI, applies damage/knockback, spawns/frees
  units, and decides win/lose. Clients are thin: send input via RPC, display synchronized state.
- **One Player per peer.** Each connected peer owns exactly one captain; `SetMultiplayerAuthority`
  ties that captain (input) to its peer. AI units stay under server authority.
- **Net play is opt-in by scene.** A networked level sets up the peer + spawner; offline scenes
  never create a peer, so the authority short-circuit keeps them unchanged.

- [ ] **Chunk 50 — Net bootstrap: host/join lobby + ENet peer.** A `scenes/Menu/Lobby.tscn` +
  `scripts/Net/NetGame.cs` (autoload-style singleton or scene script): **Host** creates an
  `ENetMultiplayerPeer.CreateServer(port)`, **Join** does `CreateClient(address, port)`; show a
  live connected-peer list and a Start button (host only). Wire `peer_connected` /
  `peer_disconnected` / `connected_to_server` / `connection_failed` signals. No gameplay yet —
  just establish and tear down the connection cleanly. Add Lobby to `LevelSelect`. **Headless-test:**
  a server peer reports `IsServer`/peer count; the authority short-circuit returns true with no peer.
- [ ] **Chunk 51 — Networked captain spawn + ownership.** A `scenes/Levels/NetStand.tscn` with a
  `MultiplayerSpawner` that spawns one captain per peer on the server and replicates it to clients;
  `SetMultiplayerAuthority(peerId)` on each so a client only drives its own captain. `Player` reads
  input **only when it is the multiplayer authority** (and the authority check short-circuits to true
  offline, so existing scenes are untouched). **Headless-test:** authority resolves to the owning peer
  id; a non-authority Player skips its input read.
- [ ] **Chunk 52 — State replication (transforms + health).** Attach `MultiplayerSynchronizer`s (or a
  hand-rolled server→client state RPC) to units replicating position, yaw, and `Health` from the
  server. The server spawns/frees all AI + ally units via the spawner; clients never spawn. Dead units
  free server-side and the despawn replicates. **Headless-test:** a server-side health change is the
  value clients would read from the synced property; only the server mutates it.
- [ ] **Chunk 53 — Server-authoritative combat & input RPCs.** Client captain input (move dir, aim yaw,
  attack/brace/swap/command edges) goes to the server via `[Rpc(RpcMode.AnyPeer)]`; the server applies
  it to that peer's captain, resolves all `TakeDamage`/`AddKnockback`/death there, and lets replication
  carry results back. Clients never resolve damage locally. **Headless-test:** an input RPC routed to
  the server moves/acts the right captain; a client-side damage call is ignored when not authority.
- [ ] **Chunk 54 — Networked co-op level + synced match state + menu.** Make `NetStand` a full battle:
  two captains (one per peer) each leading a squad vs a shared server-run enemy force; `GameManager`
  runs **only on the server** and broadcasts win/lose to clients (drives the shared `victory`/`game_over`
  groups so `ResultMenu` works for everyone); `RequireAllPlayersDead` reused so it ends only when both
  captains fall. Listed on `LevelSelect`. **Headless-test:** server-decided match result propagates to a
  client; clients don't independently declare win/lose. **User feel-check** (needs two machines / two
  instances).

---

---

### ▶ PLANNED — M12.8 Slay-the-Spire-Feel Pass (Chunks 55–59)

**Goal:** the card mode (`scenes/Cards/CardBattle.tscn` + `scripts/Cards/CardBattle.cs`) currently
renders each hand card as a bare `Button` whose face is one multi-line text string — it reads as an
"ugly transparent block," the hand band eats ~40% of the screen, and the hand `CanvasLayer` overlaps
the near edge of the 3D pitch. This milestone makes it LOOK like Slay the Spire (and PLAY like it in
the PAUSE phase) **without** changing the model or the real-time auto-battler. **Visual reskin only.**

**Invariant — model & play untouched.** No changes to `Deck`/`Card`/`EnergyPool`/`RunMap`/`CardPlay`
or the two-click aim (click card → click ground/unit). The round loop, energy gating, endzone march,
and run structure all behave EXACTLY as today. These chunks only touch how `CardBattle` *draws* the
hand/HUD and how the `Camera3D`/layout frame the board. Each is independently feel-checkable.

**OUT of scope (don't build unless asked):** drag-to-target play, enemy intent telegraph icons, and a
full turn-based remake (dropping the real-time march). Decided against in the planning convo.

- [ ] **Chunk 55 — `CardView` card frame (keystone).** Replace the `Button`-with-text in
  `CardBattle.MakeCardButton` with a reusable composite control (a small `scenes/Cards/CardView.tscn`
  or an in-code builder): rounded `StyleBoxFlat` frame, a **cost orb** badge (circular, top-left, the
  `EnergyCost` number), a **title banner** colored by kind (Unit=blue / Action=amber), an **art panel**
  (solid tint / simple per-kind icon placeholder for now — real art later), and a **description box**
  at the bottom. Preserve today's behavior: click-to-select (`OnCardSelected`), pending-card highlight,
  and affordability dimming (`Disabled = _pendingReward != null || !_energy.CanAfford(card)`). Biggest
  single visual win; the rest build on it. Pure visual; user feel-check.
- [ ] **Chunk 56 — Compact fanned hand + hover-lift.** Stop the hand eating ~40% of the screen at rest:
  render the `CardView`s smaller and **overlapping in a slight arc** (manual fan / negative separation
  instead of the plain `HandBox` row), and on hover **enlarge + raise** the focused card above its
  neighbors (mouse enter/exit → scale + z-order). At rest the hand is a thin ribbon; hovering "opens" a
  card. Reclaims most of the bottom-screen real estate. Pure visual; feel-check.
- [ ] **Chunk 57 — Board/tray separation + camera reframe.** Tilt/raise the `Camera3D` (currently
  `y=30, z=23`, ~50° tilt) and/or drop FOV so the whole pitch sits in the upper portion and its near
  edge no longer hides behind the hand; add a dark **hand-tray band** (styled panel strip) behind the
  cards so board and hand are visually distinct (the StS shelf). Feel-check nothing important clips.
- [ ] **Chunk 58 — StS HUD chrome (energy orb + pile stacks).** Reskin existing widgets only: turn the
  centered "ENERGY x/y" `EnergyLabel` into a **crystal orb** bottom-center/left (big number in a circular
  `StyleBox`), and turn the flat `DrawPanel`/`DiscardPanel` into little **card-back stacks** with the
  count overlaid (bottom-left / bottom-right); make End Turn the big rounded bottom-right button. No
  logic change — same labels/counts, new look.
- [ ] **Chunk 59 — Play juice.** Cheap `Tween`s, no model change: cards slide up into the hand on draw,
  fly toward the discard stack on play, the energy orb pulses on refill, hovered cards tween smoothly.
  Final polish pass; feel-check.

**(Independent of M13 — these can be built whenever the user asks; they don't gate, and aren't gated by,
the netcode chunks. Build order recommendation: 55 → 57 → 58 → 56 → 59, but 55 first regardless.)**

---

### ▶ PLANNED — M14 Traversable Terrain — fighting on slopes (Chunks 60–65)

**Goal:** in the **Highlands** level, units WALK AND FIGHT up and down real terrain elevation — not just a
visual backdrop ridge. The current `scenes/Levels/Highlands.tscn` is a flat field ringed by visual-only
hills (`scripts/Scenery.cs`); this milestone makes that terrain solid and the units climb it.

**Why this is the project's highest-risk change.** Every fighter shares ONE movement pattern: build a
horizontal (X/Z) velocity, force `Y = 0`, then `MoveAndSlide()` on a flat plane — **no gravity, no floor
snapping** — and spawns sit at a fixed `y ≈ 1` (`Player.cs` "Flat ground for now", `Ally.ArriveVelocity`
zeroes Y, etc.). The terrain mesh has **no collision**. Projectiles fly dead level (`Stone.cs`/`Arrow.cs`
set `direction.Y = 0`). The camera and formation slots assume a fixed height. So slopes touch the movement
core of `Player`/`Ally`/`Enemy`/`Swordman`/`Bowman`/`Mount` at once.

**Invariant — flat levels stay byte-identical.** All grounded behaviour is **opt-in** via a `Unit.Grounded`
export, **default OFF**; only Highlands turns it on. With it OFF the `Y = 0` + flat-plane motion every other
level (Pinball / KotH / Co-op / card battler / crowd tests) relies on must be UNCHANGED. Headless-test that a
non-grounded unit moves exactly as before.

**New durable rules (promote into §5 as chunks land):**
- **Grounded movement.** When `Grounded` is on, a unit applies gravity to a Y velocity, sets
  `UpDirection = Vector3.Up` + a `FloorSnapLength` so it sticks to downhill slopes, and `FloorMaxAngle` so
  shallow hills are walkable but cliffs block. Off = today's `Y = 0` flat motion.
- **Heightmap terrain.** `Scenery` exposes its height function as solid collision via a `HeightMapShape3D`
  generated from the SAME height field that draws the mesh, so visuals and collision match exactly.
- **Ballistic projectiles.** On grounded levels stones/arrows lob in an arc (gravity) toward the target's
  actual height instead of flying level, so up/downhill shots connect.

- [x] **Chunk 60 — Terrain collision (heightmap).** Give `Scenery`'s hills a `HeightMapShape3D` (or trimesh)
  built from its height function under a `StaticBody3D`, with `FloorMaxAngle`-friendly slope; keep the flat
  centre level. Replace Highlands' separate flat ground plane with the terrain collision (keep boundary walls).
  **Headless-test:** a downward ray / a dropped body lands at the height the function predicts.
- [x] **Chunk 61 — Grounded movement on `Unit` (keystone).** Add `Unit.Grounded` (default OFF) + shared
  gravity/floor-snap helper; route every subclass's `Velocity = horizontal*scale + knockback` through it so
  Y becomes the gravity term when grounded, untouched (0) when not. Set `UpDirection`/`FloorSnapLength`/
  `FloorMaxAngle`. **Headless-test:** a grounded unit on a slope settles to the surface and can climb it; a
  non-grounded unit's motion is byte-identical to today.
- [x] **Chunk 62 — Spawn / formation / facing height.** Grounded units settle to terrain on spawn (ray-place
  or let gravity drop them); formation-slot + command points sample terrain height so allies don't steer at a
  point in the air; mounts follow the ground under the rider. **Headless-test:** a slot point on a slope
  resolves to the terrain surface height.
- [x] **Chunk 63 — Ballistic projectiles.** On grounded levels, `Stone`/`Arrow` lob in a gravity arc aimed at
  the target's real position (height included) instead of level flight; flat levels keep the straight shot.
  Shared `Ballistics.SolveArcVelocity` solver; arrows tip along the arc; lobbed shots free on terrain impact.
  **Headless-test:** an arced shot's trajectory reaches a target above/below the launch height.
- [x] **Chunk 64 — Terrain-following camera.** Damp `FollowCamera`'s focus height (and lift it) so the view
  doesn't jolt as the captain climbs/descends, reusing the dynamic-zoom fit. Single-target + flat path
  unchanged. **Headless-test:** the focus height eases toward the target's Y rather than snapping.
- [~] **Chunk 65 — Highlands redesign + tune.** Reworked `Scenery.HeightAt` from "flat field ringed by a
  26 m wall of hills" into GENTLE PLAYABLE terrain: low rolling swells across the whole field
  (`PlayAmplitude`) + a couple of crossing ridges (`Ridge()`/`RidgeHeight`/`RidgeWidth`), with only the
  distant backdrop rising for a highland horizon (`BackdropHeight`/`RampWidth`). Set `Grounded = true` on
  every Highlands unit so they walk/fight the slopes. Knockback-on-slopes tuning still open. **User feel-check.**

**(Highlands-scoped by decision: do NOT roll `Grounded` out to other levels unless asked. Build order is
60 → 61 → 62 → 63 → 64 → 65; 60 + 61 are the load-bearing pair.)**

---

Then proceed to multiplayer polish & netcode hardening as needed (lag handling, reconnection),
updating checkboxes and §8 as you go.

## 8. Quick Reference

```powershell
# Godot exe (PowerShell variable for convenience):
$godot = "C:\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"

# Run the game (windowed):
& $godot --path "C:\Users\benhu\OneDrive\Desktop\3D Eggs"

# Run headless (logic/tests, output to terminal):
& $godot --headless --path "C:\Users\benhu\OneDrive\Desktop\3D Eggs" --quit

# Build C#:
dotnet build
```

## 9. Environment State (update as we go)

- .NET SDK: **v10.0.203** (≥ 8.0 ✓)
- Godot .NET: **v4.6.3.stable.mono** ✓ — path in header above.
- Project: `project.godot`, assembly_name=`Eggs`, builds clean (`Eggs.dll`).
  Solution `Eggs.sln` / `Eggs.csproj` (Godot.NET.Sdk 4.6.3, net8.0).
- Renderer: **GL Compatibility (OpenGL3)** in `project.godot [rendering]`.
  ⚠️ Do NOT switch to Forward+/Vulkan: this machine (NVIDIA 940MX, driver 376.54 →
  Vulkan 1.0.24) crashes with `vkCreateComputePipelines` errors. Compatibility runs fine.
- Godot relaunches **detached** on Windows → launching from a tool returns exit 0
  instantly with empty stdout. Read the real per-run log at:
  `%APPDATA%\Godot\app_userdata\3D Eggs\logs\godot.log`
- Layout: scenes in `scenes/`, scripts in `scripts/`. Launch via `play.bat` or editor F5.
- Git: initialized; commit after each chunk / before risky changes.
