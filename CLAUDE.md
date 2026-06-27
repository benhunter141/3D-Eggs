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
- [ ] **M12.8 — Slay-the-Spire-feel pass (SHELVED — revisit only when asked):** card-mode visual reskin
      (card frames, fanned hover-lift hand, board/tray + camera reframe, StS HUD chrome, play juice;
      Chunks 55–59). Visual only — two-click play + auto-battler stay.
- [ ] **M13 — Multiplayer (SHELVED — revisit only when asked):** 2 players over the network,
      server-authoritative (Chunks 50–54). The hardest, last milestone.
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
- [~] **M15 — Co-op Card Brawl (Slay the Eggs reborn) ⭐:** a *local 2-player* card-driven survival mode.
      Each player drives a **basic egg** (weak punch only) — P1 keyboard+mouse, P2 gamepad — and both draw
      from ONE shared hand. During the between-rounds **pause** they spend shared energy to play cards that
      **buff whoever played them** (`sword` = equip a weapon, `fireball` = grant a castable ability) or
      **spawn subordinates** (`soldier`); End Turn starts a **15 s real-time survival wave**, then it pauses
      and redeals — repeat, surviving escalating waves. Chunk 66 done: weak unarmed `Punch` loadout +
      `StartUnarmed` + runtime `EquipWeapon` on `Player` (M9 plumbing; Punch excluded from Q-swap). Chunk 67
      done: `Player` ability system — `AbilityType` (None default | Fireball), `GrantAbility(kind)` +
      scheme-aware `cast_ability` (C / gamepad A), and `Fireball` (a straight/ballistic magic projectile on a
      cooldown whose damage is baked in via `ScaledMagicDamage`/Int at cast). None granted by default, so every
      other captain is byte-identical. Chunks 68–70 done: `CardKind.PlayerBuff` (target = Self) + `BuffKind`
      (Weapon/Ability/Soldier) + `CardPlay.Play`'s 5-arg overload route a card to the triggering `ICardPlayer`
      (`Player.ApplyCard` → EquipWeapon / GrantAbility / SpawnSoldier); `CardLibrary.BrawlDeck()`/`BrawlPool()`;
      the pure `BrawlHand` routing core (shared deck + energy, per-play player index) under the `CardBrawl.cs`
      shell (code-built shared-hand UI, P1 mouse + P2 gamepad cursor); `WaveManager` (escalating ring per round,
      `CountForWave`); `GameManager.DisableWin` (survival has no win); `scenes/Levels/CoopCardBrawl.tscn` (two
      basic eggs in a Pausable `Units` node, shared camera, `RequireAllPlayersDead` + `DisableWin`), battle 6 on
      LevelSelect. All headless-tested. Lose only when BOTH eggs fall. Reuses the M12 card model + `RoundLoop`,
      the M12.7 co-op control schemes / shared camera / `RequireAllPlayersDead`, and the M9 weapon plumbing
      (Chunks 66–71). Decided: buffs apply to whoever played the card; cards play in the PAUSE only; opponent =
      escalating survival waves. **Chunk 71 balance feel-check pending (needs a gamepad for P2).** Chunk 72 done:
      the loop is now foes-first — `CardBrawl.SpawnPreviewWave` stages each wave during the pause (inert in the
      Pausable Units node while frozen) so both eggs see the threat and counter it with cards; End Turn just
      unfreezes the staged foes. Chunk 73 done: card-UI reskin — compact tinted-border card frames (`CardFrame`)
      with a coloured header bar, `⚡ cost` badge + wrapped desc, cards 150×200→116×156 and the hand footprint
      shrunk so 5 cards stay fully on-screen; clickable `Button` root preserved (P1 click, P2 cursor tint).
      **All M15 chunks (66–73) built; Chunk 71 balance feel-check still pending (needs a gamepad for P2).**
- [~] **M16 — Visual overhaul (toon/cel look) ⭐ ACTIVE:** the game "looks terrible" — flat-shaded
      primitives, drab solid-colour ground, grey sky, no outlines/AO/particles/health-bars. Fix it GLOBALLY
      with a cohesive **toon / cel-shaded** style + the requested juice/health-bars/UI/environment scope
      (Chunks 75–81). Shared environment+sun kit, a toon `.gdshader` + inverted-hull outline on every unit,
      stylized ground/arena dressing, floating health bars + hit-flash, combat particles, and a unified
      toon UI/HUD/card theme. **GL-Compatibility-renderer-safe only** (no Vulkan features) and tuned for the
      940MX. Visual/material/scene-decoration changes; headless logic tests stay green. Each chunk is
      individually testable by running a level. Chunk 75 done (shared toon environment + sun kit). Chunk 76
      done: `materials/toon.gdshader` (banded cel `light()` + fresnel rim, per-instance `base_color`) now the
      `material_override` on all 7 unit egg bodies; `Unit.cs` drives the hit-flash via the shader's `flash`
      uniform and keeps the EggCracks `next_pass` on either body-material type — then RETUNED after a feel-check
      (ambient was washing the bands flat): dropped `ToonEnvironment` ambient + switched to a hard 3-step ramp
      with cooled shadow band + stronger rim. Chunk 77 done: `materials/outline.gdshader` inverted-hull outline
      folded into `EggMesh` (runtime child, default on) so every egg has a crisp dark cartoon silhouette.
      Chunk 78 done: shared `materials/ground.gdshader` (world-space checkerboard + grid + 2-band cel light)
      replaces the flat solid-colour grounds in KotH / Pinball / CoopCardBrawl / CoopStand (per-level tint);
      Pinball walls warmed to matte stone, CoopCardBrawl fences given wood, corner posts added to KotH + Brawl.
      Chunk 79 done: billboarded `HealthBar3D` on every `Unit` (dark bg + team-coloured fill draining right→left),
      grown sized-off-the-egg in `_Ready`, hidden until hurt and on death; crowd-cheap (shared bg mesh + 3 unshaded
      billboard mats, only the fill quad per-instance). Chunk 80 done: `scripts/Particles.cs` — low-count one-shot
      `GpuParticles3D` bursts that self-free on `Finished`: `HitSpark` off any non-lethal `TakeDamage`
      (sword/fist/stone/fireball), `DeathPoof` in the unit's colour from `Die()` (parented to the container so it
      outlives the corpse), `BounceDust` when `ResolveKnockbackBounce` bounces; procedural, shared draw mats/meshes,
      headless-safe (skips when `DisplayServer.GetName()=="headless"`). Feel-check pending. Chunk 81 done: shared
      `scenes/Shared/ToonTheme.tres` Theme (cream-on-dark, rounded Button styleboxes + Label/Panel defaults) applied
      to LevelSelect / ResultMenu / PauseMenu / Hud and loaded onto CardBrawl's UI root so the whole front-end
      coheres with the toon look. Chunk 82 done: `Die()` now fires `Particles.EggBurst` — the egg shell SHATTERS
      violently (tumbling shell shards + a yolk splat + a sharp pop, biased/scaled by the killing shove) instead of
      the soft death poof. **All M16 chunks (75–82) built; feel-checks pending.**
- [~] **M17 — Enemy bestiary (10 foes, Co-op Card Brawl) ⭐:** the brawl only ever spawns one enemy
      (Skeletons in a line). Replace that with a **varied 10-enemy roster, ordered by difficulty**, that
      feeds the escalating waves so each round reads as a distinct, recognizable threat. Roster (easy→hard):
      **Zombie** (slow horde) · **War Dog** (fast pack) · **Skeleton** (reused baseline) · **Goblin Cutter**
      (small fast blade) · **Bandit Slinger** (ranged kiter) · **Roman Legionary** (shielded block that
      marches as a Legion) · **Orc Brute** (heavy club WITH knockback) · **Necromancer** (ranged + summons
      zombies) · **Roman Centurion** (elite Legion leader w/ rally aura) · **Cave Troll** (boss — giant club,
      AOE slam shockwave). Each is **visually obvious** (silhouette + colour + held prop tell you what it does)
      and built on the shared `Unit`/`Enemy` spine + EggMesh/toon look. Includes the four requested: big troll
      w/ club, many zombies, many dogs, Roman Legion. WaveManager grows from one-type lines to a per-wave
      **composition table** so waves mix foes; bosses appear on a cadence. Co-op-Card-Brawl only — every other
      mode stays byte-identical (Chunks 83–93). Headless-test the spawn schedule; visuals are feel-checked.
      Chunk 83 done: `WaveManager` now carries a per-wave **composition table** (`WaveTable` of
      `WaveComposition` = `WaveEntry(scene, count)` list + `Formation` Spread/Block/Solo), `CompositionForWave`
      clamps past the table & falls back to the legacy Skeleton line (flat levels byte-identical), `CountForWave`
      = row total, `SpawnWave` places the mix per formation. Headless-verified (`TestWaveBestiary`). Chunk 84
      done: `Zombie` (tier-1 horde) — a hunched green toon egg with forward-thrust arms; slow (MoveSpeed 2),
      frail (50 HP), no-knockback contact melee; `CardBrawl.BuildWaveTable()` makes it the bulk early horde
      (incrementally authored, Chunk 93 finalizes the ramp). Headless-verified (`TestZombie`). Chunk 85 done:
      `WarDog` (tier-1 PACK hunter) — a small, low, four-legged brown toon egg (horizontal body + 4 legs + head
      + tail); fast (MoveSpeed 7), very frail (25 HP), short bite cooldown (0.5), no-knockback contact melee;
      added to the brawl wave table as a fast pack beside the zombie horde. Headless-verified (`TestWarDog`).
- [ ] **M18 — Weapon-specific attack motions ⭐:** today **every** weapon attacks with the SAME motion — a
      straight thrust (`Player.UpdateSwing` → `SetThrustOffset` slides the weapon out along -Z and back);
      only the numbers (reach/damage/knockback/timing) differ. Give each weapon its own **attack style** so it
      reads as a distinct move: **spear/pike = thrust** (keep today's poke), **sword = horizontal sweep arc**
      (multi-hit across the front), **axe = overhead chop** (slow, heavy, single big hit), **mace = wide
      circular swing** (round-house, multi-hit + strong knockback), **punch = quick jab**. Drive it from a
      per-weapon `AttackStyle` on the `WeaponProfile` table and a style-dispatching swing state machine that
      animates the weapon transform (pivot rotation + offset) AND shapes the active hitbox region (line vs arc)
      so sweeps actually hit multiple foes in their arc. Player-first; optionally extend the same style to
      ally/enemy weapon strikes. Keep it cheap (940MX) and headless-test the per-style hit logic (which foes a
      sweep vs a thrust connects with). Builds on the M9 data-driven weapon plumbing (Chunks 94–98).
- [x] **M19 — Co-op Phalanx level ⭐:** a *local 2-player* set-piece battle — each captain leads
      **two rows of 5 long-pikemen** (a pike's visual length = egg-unit height × 3) plus **2 archers beside the
      captain**, every subordinate holding formation on its captain (the CoopStand slot mechanism); the enemy
      is **one large, slow-moving slime horde** that shambles in as a single mass. New `Pikeman_Long.tscn`
      (5.1-long pike variant) + a `Slime` enemy (slow squashed green blob) + `CoopPhalanx.tscn`, added to
      LevelSelect. Win = wipe the horde; lose only when BOTH captains fall. Cloned from `CoopStand`, so every
      other mode stays byte-identical (Chunks 99–102, all built). Chunk 99: `Pikeman_Long.tscn` — a clone of
      `Pikeman.tscn` with a 5.1-long pike (4.6 shaft + 0.5 cone head) + `PikeReach` 4.5. Chunk 100: `Slime.cs`
      (thin `Enemy` subclass) + `Slime.tscn` — a wide squashed glossy-green blob (slow MoveSpeed 1.6, 60 HP,
      contact melee, no knockback); `TestSlime`-verified. Chunk 101: `scenes/Levels/CoopPhalanx.tscn` (44×54
      walled field, two captains each with two rows of 5 `Pikeman_Long` + 2 `Archer` bound to their
      `CaptainPath`, a 30-Slime block at far -Z, `GameManager RequireAllPlayersDead`); loads clean. Chunk 102:
      added the "Co-op Phalanx (2P)" `SceneButton` to LevelSelect. **Feel-check pending (needs a gamepad for P2).**

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
> (e.g. `Done: in-game controls HUD.`). **Immediately after that summary line, on its own
> final line, state the number of outstanding (unchecked `[ ]`) chunks remaining in §7**,
> of the form `Outstanding chunks: N.` — count every `[ ]` box in the Build Plan (§7).
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

- [x] **Chunks 24–25 (M8)** — googly eyes on every unit; distinct per-weapon meshes.
- [x] **Chunks 26–27 (M9)** — data-driven `WeaponType→WeaponProfile`; `swap_weapon` cycles spear/sword/axe/mace/bow.
- [x] **Chunks 28–29 (M10)** — `Mount` base + Donkey; faster Chocobo; mount/dismount + mounted combat.
- [x] **Chunks 30–31 (M11)** — `CapturePoint` period scoring; KotH level + `KothManager` HUD/authority.
- [x] **Chunks 32–39 (M12)** — card model/piles/UI, Unit+Action play, round loop, dev panel, HP/Str/Int, KotH energy, run rooms, relics/potions.
- [x] **Chunks 40–43 (M12.5)** — endzone football pitch, endzone-gated placement, forward-march AI, 5 s turns + unit-heavy deck.
- [x] **Chunks 44–47 (M12.7)** — co-op control schemes + controller aim, squad ownership, shared two-captain camera, `CoopStand` + both-fall lose; removed Levels 1–4 from menu.
- [x] **Chunks 48–49 (M7)** — `Ally` command model (Follow/Hold/AttackMove, off by default); captain `IssueSquadCommand` dispatch.
- [x] **Chunks 60–64 (M14)** — terrain heightmap collision, grounded movement (`Unit.Grounded`, default OFF), terrain spawn/formation, ballistic projectiles, terrain-following camera.
- [~] **Chunk 65 (M14)** — Highlands gentle-terrain redesign (rebuilt for cost + looks). *Open: knockback-on-slopes tuning + Highlands feel-check.*

---

### ▶ ACTIVE PLAN — M19 Co-op Phalanx level · twin phalanxes vs a slime horde (Chunks 99–102)

**The only active plan — build this on "go".** A new *local same-screen 2-player* set-piece, cloned from
`CoopStand.tscn` (P1 keyboard+mouse `Control=1`, P2 gamepad `Control=2 DeviceId=0`; shared `FollowCamera` with
`Target`/`Target2`; `GameManager RequireAllPlayersDead=true`; ResultMenu/Hud/PauseMenu/ObjectiveLabel). Each
captain commands **two rows of 5 long-pikemen + 2 flanking archers**, all bound to that captain so they hold
formation (the `Ally` `FormationOffset` + `CaptainPath` slot system — offsets are LOCAL, forward is -Z, +Z
trails). The foe is **one large slow slime horde** that advances as a mass via the stock `Enemy` chase AI.
**Hard rule:** make NEW scenes — do NOT edit `Pikeman.tscn` or any shared scene — so every other mode stays
byte-identical. Win when the horde is cleared (stock `GameManager`); lose only when BOTH captains fall.

**Grounding facts (already verified):** egg-unit height = `EggMesh.Height` = **1.7** → pike length = **5.1**.
Today's `Pikeman.tscn` pike = a 3.0 shaft + 0.5 cone head along local -Z (Pike node at `(0.28,0.45,-1.3)`),
`Ally.cs` with `Weapon=2` (Pike), `PikeReach` 3.0. `Archer.tscn` exists (used in CoopStand). CoopStand's pike
rows use `FormationOffset (x, 0, -2.5)` for x in -2.5..2.5, archers `(±1.2, 0, 2)`. `LevelSelect.tscn` currently
has ONE `SceneButton` (the Card Brawl). No Slime exists yet; `Zombie.cs`/`Zombie.tscn` is the closest pattern
(thin `Enemy` subclass + scene-root stat overrides).

- [x] **Chunk 99 — Long-pike pikeman variant.** `scenes/Pikeman_Long.tscn` — a copy of `Pikeman.tscn` whose
  pike is **5.1 long = egg height (1.7) × 3**: shaft `CylinderMesh` height ≈ 4.6 + the 0.5 cone head = ~5.1,
  with the `Pike` node / shaft / head repositioned along local -Z so the butt sits at the egg and the tip
  reaches ~5.1 ahead. NEW scene (do NOT edit `Pikeman.tscn`) so `CoopStand` + every other mode stay
  byte-identical. Bump these instances' `PikeReach` 3.0 → ~4.5 so gameplay reach matches the new visual length.
  Headless-load it clean.
- [x] **Chunk 100 — Slime enemy.** `scripts/Slime.cs` (thin `Enemy` subclass for type identity, mirroring
  `Zombie.cs`) + `scenes/Slime.tscn`: a **wide, squashed, glossy-green egg blob** (`EggMesh` large `Width` ≈ 1.4,
  short `Height` ≈ 1.0, green toon `base_color`; capsule collider sized to match), **slow** `MoveSpeed` ≈ 1.6,
  `MaxHealth` ≈ 60, `AttackDamage` ≈ 8, `AttackCooldown` ≈ 1.2, contact melee, NO knockback (inherits the
  `Enemy` chase AI). Headless-verify (a `TestSlime` in `UnitTest.cs`): Enemy-team, slower than a Skeleton,
  melee damage with zero knockback.
- [x] **Chunk 101 — Co-op Phalanx level.** `scenes/Levels/CoopPhalanx.tscn` cloned from `CoopStand.tscn`,
  larger field (~40 × 50 ground + fences + posts, camera zoom retuned for two captains). Two captains as above.
  **Each captain** leads **two rows of 5 `Pikeman_Long` (10 each, 20 total)** — `CaptainPath` bound,
  `FormationOffset` x = -2,-1,0,1,2 (1 m spacing), front row z = -2.5, back row z = -4.0 (back pikes reach
  between the front row) — **plus 2 `Archer.tscn` beside the captain** (`CaptainPath` bound, `FormationOffset`
  ≈ (±2.5, 0, 0)). Give P1's squad x-offsets around the P1 captain and P2's around the P2 captain (mirror
  CoopStand's two-captain layout). Enemy = **one large slow slime horde**: ~30–36 `Slime.tscn` in a dense block
  at far -Z that shambles in as one mass. `GameManager RequireAllPlayersDead=true` (NOT `DisableWin` — clearing
  the horde wins). ObjectiveLabel describing the fight. Headless-load clean.
- [x] **Chunk 102 — Menu + verify.** Add a `SceneButton` ("Co-op Phalanx  (2P)") to `scenes/Menu/LevelSelect.tscn`
  → `res://scenes/Levels/CoopPhalanx.tscn` (above the Card Brawl button; copy the existing button's pattern).
  Confirm `dotnet build` 0 errors + headless-load the new scene with no parse/wiring errors. Feel-check is the
  user's (needs a gamepad for P2).

**Build order 99 → 102. 99 + 100 are the two new assets (long pike + slime); 101 assembles the level from the
CoopStand template; 102 wires the menu. Keep every change in NEW files (+ the one menu button) so all existing
modes stay byte-identical.**

---

### ✅ BUILT — M16 Visual Overhaul · toon/cel look (Chunks 75–82, feel-checks pending)

**The only active plan.** The game looks like an untextured prototype: flat solid-colour egg meshes, a drab
single-colour ground plane, a grey procedural sky, box fences, one light — no outlines, AO, rim/toon shading,
particles, or health bars. This milestone fixes the LOOK **globally** with a cohesive **toon / cel-shaded**
art direction plus the requested juice / health-bars / UI / environment scope. Changes are visual/material/
scene-decoration and apply to ALL modes at once (that's the point — one pass lifts every level). **Hard
constraints:** GL-Compatibility renderer only (NO Vulkan-only features — see §9), keep it cheap on the NVIDIA
940MX, and headless logic tests must stay green (guard any viewport/particle code so `--headless` is safe).
Each chunk is self-contained and testable by running one level.

- [x] **Chunk 75 — Shared toon environment & sun kit.** `scenes/Shared/ToonEnvironment.tres` (warm gradient
  sky, depth fog, glow/bloom, ACES tonemap + exposure, coloured ambient) + `scenes/Shared/ToonSky.tscn`
  (WorldEnvironment + warm key `DirectionalLight3D`, soft PSSM shadows, max-dist 90 for Highlands). Instanced
  into all five levels, replacing each scene's inline grey sky/light (no script referenced those nodes).
  Compatibility-safe. Feel-check pending.
- [x] **Chunk 76 — Toon unit shader.** Shared `materials/toon.gdshader` (spatial, Compatibility-safe):
  a custom `light()` quantizes the sun's N·L into hard **bands** (cel look, with a `band_floor` so shadowed
  sides keep the tint) + a fragment **fresnel rim**; team tint is a per-instance `base_color` uniform. Each
  unit egg's `material_override` is now a `ShaderMaterial` instancing it (Captain, Ally, Swordman, Bowman,
  Pikeman, Skeleton, Archer). `Unit.cs` learned to drive the **hit-flash through the shader's `flash`
  uniform** (not StandardMaterial emission) and still hang the EggCracks `next_pass` off whichever body
  material it finds — so flash + shell-cracks keep working on the toon bodies. Non-egg bodies (weapons,
  mounts) stay StandardMaterial via the preserved fallback path. Headless logic tests stay green. **Tuned
  after a feel-check** ("only the ground looks different"): the toon banding was being washed out by heavy
  flat ambient, so `ToonEnvironment.tres` ambient dropped (energy 1.0→0.5, sky contribution 0.65→0.4) to let
  the sun carve form, and the shader switched to a hard 3-step ramp with a cooled/darkened shadow band +
  stronger rim (0.45→0.7) so the cel split reads.
- [x] **Chunk 77 — Outline pass (inverted hull).** Every egg now gets a crisp dark **outline** via a runtime
  child back-face hull: `materials/outline.gdshader` (`cull_front`, grow-along-normal, `unshaded`, dark) drawn
  on a second copy of the egg's own Mesh. Folded into `EggMesh` (`ShowOutline`/`OutlineWidth`, default on) —
  built runtime-only (`Engine.IsEditorHint()` guard, no duplicate-on-reload) and sharing the egg's Mesh so it's
  cheap; applies to EVERY egg (units + mounts) for one cohesive cartoon silhouette. Headless-safe (resource
  load only). Feel-check pending. *(75+76+77 = the load-bearing trio — the bulk of the "looks" jump.)*
- [x] **Chunk 78 — Stylized ground & arena dressing.** Shared `materials/ground.gdshader` (Compatibility-safe):
  a world-space two-tone **checkerboard** + soft **grid lines** + gentle 2-band cel `light()` turns the flat
  solid-colour plane into a toon surface; per-level base/alt/grid tint lives in each scene's ShaderMaterial
  (grass for KotH/Brawl, dirt for the barnyard, stone for Pinball). Applied to KotH, Pinball, CoopCardBrawl, and
  CoopStand grounds. **Recoloured/softened the arena edges:** Pinball walls bluish-metallic → warm matte stone;
  CoopCardBrawl fences default-white → wood; added **corner posts** (cylinder + warm-cap sphere) as light prop
  dressing to KotH and CoopCardBrawl (CoopStand already richly dressed; Highlands keeps its code-shaded terrain
  and the `CapturePoint` disc keeps its team-signal state colours). Headless-loads clean for all four scenes.
  Feel-check pending.
- [x] **Chunk 79 — Floating health bars + hit flash.** Reusable billboarded `HealthBar3D` on `Unit` — a dark
  bg quad + a team-coloured fill that drains right→left, grown in `_Ready` (`SetupHealthBar`) sized off the egg,
  **hidden until the unit is hurt** and again on death; `RefreshHealthBar` (from `TakeDamage`/`Heal`/`Die`)
  tracks HP. Crowd-cheap: shared static bg mesh + 3 unshaded billboard materials (depth-test-off + render-
  priority so bars read over bodies), only the fill quad per-instance (resizable via `QuadMesh.CenterOffset`).
  The paired **hit-flash** already rides `TakeDamage` via the toon shader's `flash` uniform (Chunk 76).
  Resource-only build, `--headless`-safe (logic tests stay green). Feel-check pending.
- [x] **Chunk 80 — Combat juice particles.** Shared `scripts/Particles.cs` helper — low-count one-shot
  `GpuParticles3D` bursts that self-free on the `Finished` signal: `HitSpark` (bright additive motes flung
  along the hit dir off any non-lethal `TakeDamage` — so sword/fist/stone/fireball all spark), `DeathPoof`
  (a soft puff in the unit's own colour from `Die()`, parented to the unit's CONTAINER so it outlives the
  corpse) and `BounceDust` (a pale kick-up when `ResolveKnockbackBounce` actually bounces). Procedural (no
  asset files), shared draw materials + meshes, counts 6–14 for the 940MX. **Headless-safe:** every spawn
  early-outs when `DisplayServer.GetName() == "headless"` (GPU particles need a device), so the logic tests
  stay green. Feel-check pending.
- [x] **Chunk 81 — Toon UI/HUD & card theme.** Shared `scenes/Shared/ToonTheme.tres` (a Godot `Theme`):
  one cream-on-dark palette with chunky rounded **Button** StyleBoxes (normal/hover/pressed/disabled/focus),
  default Label/Panel styles + font sizes. Applied to `LevelSelect` (root), `ResultMenu` (Buttons), `PauseMenu`
  (Overlay), `Hud` (Controls panel), and loaded onto `CardBrawl`'s `_root` in code so the End-Turn button + bars
  inherit it (cards/markers/ability slots keep their own explicit styleboxes). Compatibility-safe, headless-clean.
  Feel-check pending.
- [x] **Chunk 82 — Violent egg-break on death.** `Die()` now fires `Particles.EggBurst` (replacing the soft
  Chunk-80 `DeathPoof`): the shell SHATTERS — a fan of ~12 lit shell-shard fragments tumbles outward (spin via
  `AngularVelocityMin/Max` + gravity, per-spawn shell colour), a ~9-blob egg-yolk-yellow splat bursts from the
  core and arcs down, and a sharp bright additive pop punches at the break. The burst is biased along the
  killing shove (`KnockbackVelocity`) and scaled by its speed (`burstForce = |knockback|/MinBounceSpeed`), so a
  pinball-fling death sprays that way and explodes harder. Reuses the Chunk-80 one-shot-then-free `Spawn` helper
  (shared shard `BoxMesh` + yolk `SphereMesh` + lit shell / unshaded yolk mats), kept low-count for the 940MX and
  `Headless`-guarded so `--headless` logic tests stay green. Feel-check pending.

**Build order 75 → 82. 75 + 76 + 77 are load-bearing (environment + toon shade + outline). 78–82 layer the
requested polish (ground dressing, health bars, particles, UI, violent egg-break deaths). Keep every shader
Compatibility-safe and every change headless-test-green.**

---

### ▶ QUEUED PLAN — M17 Enemy Bestiary · 10 varied foes for the Co-op Card Brawl (Chunks 83–93)

**Queued behind M16** (build M16's remaining chunks first). Today the Co-op Card Brawl's `WaveManager` only
ever spawns ONE enemy type — Skeletons — in a marching line, so every wave looks the same. This milestone gives
the brawl a **10-enemy roster ordered by difficulty**, varied in role + silhouette, each **visually obvious**
about what it does, fed into the escalating waves via a per-wave **composition table** (so waves mix foes and
ramp). Built on the shared `Unit`/`Enemy` spine + EggMesh/toon look (Chunk 76–77). **Co-op-Card-Brawl scope
only** — `WaveManager` + the brawl's spawn schedule; the new enemy scenes opt in there and nowhere else, so
every other mode stays byte-identical. Headless-test the spawn schedule + each enemy's logic; visuals are
feel-checked by running the brawl (battle 6 on LevelSelect). Includes the four requested foes: big troll w/
club, many zombies, many dogs, a Roman Legion.

**Roster (easy → hard), with the visual tell + behaviour:**
1. **Zombie** — slow horde shambler. Low HP, contact melee, no knockback. *Look:* hunched sickly-green egg,
   arms thrust forward. Comes in big numbers.
2. **War Dog** — fast pack hunter. Very low HP, very fast chase, quick bite. *Look:* small, low-to-ground,
   four-legged brown silhouette. Comes in packs.
3. **Skeleton** — baseline melee (the existing `Enemy`/`Skeleton`, slotted into the roster as a mid filler).
   *Look:* bone-white upright egg.
4. **Goblin Cutter** — small fast skirmisher with a crude blade; jittery, slightly tougher than a zombie.
   *Look:* little green egg, dagger in hand.
5. **Bandit Slinger** — ranged kiter; lobs rocks (reuse `Stone`/`Arrow` + `Ballistics`), backs off when an
   egg closes to melee. *Look:* tan egg whirling a sling.
6. **Roman Legionary** — armored shield infantry; frontal shield reduces incoming damage; **marches as a tight
   Legion block** rather than spread. *Look:* red+steel egg, rectangular scutum shield + gladius.
7. **Orc Brute** — heavy slow bruiser; club hit lands real **knockback** (`Unit.AddKnockback`), high HP.
   *Look:* big dark-green egg hefting a club.
8. **Necromancer** — fragile caster; ranged bolt + periodically **summons ~2 Zombies** near itself (ignore it
   and the horde grows). *Look:* hooded purple egg with a staff.
9. **Roman Centurion** — elite Legion leader; tanky, strong melee, **rally aura** that buffs nearby Legionaries
   (speed/toughness). *Look:* larger egg with a red plumed crest.
10. **Cave Troll (BOSS)** — giant, slow, massive HP; periodic **club SLAM = radial knockback shockwave** around
    it. Spawns solo as a boss on a wave cadence. *Look:* huge grey-green egg with an oversized club.

- [x] **Chunk 83 — Wave bestiary framework.** `WaveManager` grew from a single `EnemyScene` line into a
  per-wave **composition table** (`WaveTable`): each (1-based) wave → a `WaveComposition` = a list of
  `WaveEntry(scene, count)` + a `Formation` hint (`Spread` line / tight `Block` / `Solo` boss).
  `CompositionForWave(wave)` pulls from the table (clamped to the last/hardest row past its end) or falls
  back to today's single-Skeleton Spread line sized by `BaseCount + (wave-1)*PerWave`; `CountForWave` now =
  the row's `TotalCount`; `SpawnWave` flattens the mix and places foes via `PlaceFoe` per formation. **No
  table set = byte-identical** to the old Skeleton line (existing brawl + tests unchanged). New enemies opt
  in from Chunk 84 on. Headless-verified (`TestWaveBestiary` in `UnitTest.cs`): the table sums counts,
  `SpawnWave(2)` drops the exact mix (3 Skeletons + 2 Swordmen, all Enemy team), waves past the table clamp,
  and an unset table still falls back.
- [x] **Chunk 84 — Zombie (horde, tier 1).** `Zombie.cs` (thin `Enemy` subclass for type identity) +
  `Zombie.tscn` — a hunched (forward-tilted egg) sickly-green toon shambler with two forward-thrust arms;
  archetype stats on the scene root (`MoveSpeed` 2, `MaxHealth` 50, `AttackDamage` 8) so it's a slow, frail,
  no-knockback contact-melee Enemy. Wired into `CardBrawl.BuildWaveTable()` as the bulk early horde (wave 1 =
  5 zombies; waves 2–3 add Skeletons), the incrementally-authored brawl bestiary table (Chunk 93 finalizes the
  ramp). Headless-verified (`TestZombie`): Enemy-team, slower + frailer than the Skeleton, melee damage with
  zero knockback.
- [x] **Chunk 85 — War Dog (pack, tier 1).** `WarDog.cs` (thin `Enemy` subclass) + `Dog.tscn` — a small,
  low-to-ground, four-legged brown silhouette (horizontal squashed egg body + 4 box legs + head + tail) with
  the fast-fragile-fast-bite stat block on the scene root (`MoveSpeed` 7, `MaxHealth` 25, `AttackDamage` 6,
  `AttackCooldown` 0.5): a swarm out-damages by closing fast and biting often, not by any one dog. Wired into
  `CardBrawl.BuildWaveTable()` as a pack (wave 2 = 4 zombies + 3 dogs; wave 3 = 6 zombies + 4 dogs + 3
  skeletons). Headless-verified (`TestWarDog`): Enemy-team, faster + frailer than the Skeleton, shorter bite
  cooldown, melee damage with zero knockback. Chunk 86 done: `Goblin` (tier-2 skirmisher) — a small vivid-green
  toon egg with a steel blade; fast (MoveSpeed 5.5, > Skeleton's 4) and tougher than a Zombie (65 HP vs 50) but
  frailer than a Skeleton (100), darting no-knockback blade melee; added a tier-2 wave row (wave 4 = 4 Goblins +
  4 Skeletons + 4 Zombies), slotting the existing Skeleton in as the tier-2 anchor. Headless-verified (`TestGoblin`).
- [x] **Chunk 86 — Goblin Cutter + Skeleton slot-in (tier 2).** `Goblin.cs` (thin `Enemy` subclass) +
  `Goblin.tscn` — a small (Width 0.8/Height 1.3) vivid-green toon egg with a crude steel blade held forward;
  the fast-skirmisher stat block on the scene root (`MoveSpeed` 5.5 > Skeleton's 4, `MaxHealth` 65 > Zombie's
  50 but < Skeleton's 100, `AttackDamage` 9, `AttackCooldown` 0.8): darts in to slash, no-knockback contact
  melee. Added a tier-2 brawl wave row (wave 4 = 4 Goblins + 4 Skeletons + 4 Zombies), slotting the existing
  `Skeleton.tscn` in as the tier-2 anchor. Headless-verified (`TestGoblin`): Enemy-team, faster than the
  Skeleton, tougher than a Zombie yet frailer than a Skeleton, melee damage with zero knockback.
- [ ] **Chunk 87 — Bandit Slinger (ranged, tier 3).** Ranged kiter reusing `Stone`/`Arrow` + `Ballistics`
  (lob at the eggs, retreat when a foe closes inside melee range). `Slinger.cs` + scene. Headless-test the
  fire/kite logic.
- [ ] **Chunk 88 — Roman Legionary + Legion block (tier 3).** `Legionary.cs` + scene: shield-front **damage
  reduction**, marches as a cohesive **block formation** (WaveManager `formation = block`: tight rows, shared
  facing) instead of an even spread. *Look:* scutum + gladius, red/steel. Headless-test the damage-reduction
  + block spawn.
- [ ] **Chunk 89 — Orc Brute (heavy club, tier 4).** Slow, high HP; melee hit routes **knockback** through
  `Unit.AddKnockback` (the only non-player foe that shoves). `OrcBrute.cs` + scene with a club mesh.
  Headless-test the knockback-on-hit.
- [ ] **Chunk 90 — Necromancer (summoner, tier 4).** Fragile ranged caster: bolt on cooldown + periodically
  **summons ~2 Zombies** adjacent to itself (capped so it can't runaway-flood). `Necromancer.cs` + scene
  (hooded, staff). Headless-test the summon cadence + cap.
- [ ] **Chunk 91 — Roman Centurion (elite leader, tier 5).** Tanky strong-melee Legion leader with a **rally
  aura**: nearby `Legionary`s within a radius get a speed/toughness buff (re-applied each frame, expires when
  out of range or the Centurion dies). `Centurion.cs` + scene (plumed crest, larger). Headless-test the aura
  apply/expire.
- [ ] **Chunk 92 — Cave Troll boss (tier 5, AOE slam).** Giant slow boss, huge HP; periodic **club SLAM** =
  radial knockback shockwave hitting every egg/soldier within `SlamRadius` (`Unit.AddKnockback` + damage).
  Spawns SOLO via the composition table's boss row. `Troll.cs` + scene (oversized egg + big club). Keep the
  slam cheap (one overlap query, no particles required for logic). Headless-test the slam radius/knockback.
- [ ] **Chunk 93 — Bestiary wave progression + balance.** Author the full **difficulty ramp**: which foes
  enter at which wave (Zombies/Dogs early → Skeletons/Goblins → Slingers/Legion blocks mid → Brute/Necromancer
  → Centurion-led Legions → **Cave Troll boss on a cadence**, e.g. every Nth wave), and tune counts / HP /
  per-round energy so the curve is fair against two basic eggs + their cards. Headless-test the composition
  schedule across a run of waves (boss cadence, no empty waves, monotonic-ish pressure). Feel-check by playing
  the brawl.

**Build order 83 → 93. Chunk 83 (composition table) is load-bearing — every later chunk just drops its enemy
scene into a wave row. 84/85 are the requested hordes (zombies, dogs); 88 is the Roman Legion; 92 is the troll
boss. Keep all changes scoped to `WaveManager` + the new enemy scenes + the brawl spawn schedule so every other
mode stays byte-identical, and keep each enemy's logic headless-test-green.**

---

### ▶ QUEUED PLAN — M18 Weapon-specific attack motions (Chunks 94–98)

**Queued behind M16 + M17.** Right now combat has ONE attack motion for every weapon: `Player.UpdateSwing`
runs a thrust state machine that slides the weapon straight out along local -Z via `SetThrustOffset` and
retracts it (a snappy poke), polling `_hitbox` overlaps during the lunge. Spear/sword/axe/mace/punch differ
only in the `WeaponProfile` numbers (Damage/Knockback/Reach/ThrustDistance/SwingDuration/SwingCooldown) — they
all *look and behave* like a jab. This milestone makes each weapon attack in its **own recognizable way**, so
the move tells you which weapon you hold. **Player-first** (the captain's swing is where attacks are most
visible); the same `AttackStyle` can later be threaded through ally/enemy weapon strikes if wanted. Builds on
the M9 data-driven `WeaponType → WeaponProfile` plumbing. Keep it cheap for the 940MX and headless-test the
per-style hit logic (which foes connect for a sweep vs a thrust vs a chop).

**Per-weapon styles (the tell):**
- **Spear / Pike — Thrust** (unchanged): straight jab out and back. The existing behaviour becomes the
  `Thrust` style so the pike/spear stay byte-identical.
- **Sword — Horizontal Sweep:** the blade arcs across the front (left→right), a wide cut that can hit
  **multiple** foes standing in the arc. Moderate knockback.
- **Axe — Overhead Chop:** a slow, heavy top-down swing that lands one big committed hit (long cooldown =
  the heavy-weapon tax). High damage, narrow.
- **Mace — Wide Circular Swing:** a round-house that sweeps a broad arc (even wider than the sword),
  multi-hit with **strong knockback** — the crowd-clearer.
- **Punch — Quick Jab:** a short, fast version of the thrust (the basic egg's weak unarmed poke).

- [ ] **Chunk 94 — Attack-style framework.** Add an `AttackStyle` enum (`Thrust | Sweep | Chop | Swing | Jab`)
  to `WeaponProfile` + one column in the weapon table (every existing weapon = `Thrust`/`Jab` so behaviour is
  byte-identical at first). Refactor `UpdateSwing` from a hard-coded thrust into a **style dispatcher**: a
  shared timed window (extend/retract `t`) feeds a per-style routine that sets the weapon pivot's transform
  (translation + rotation) and the hitbox pose. Implement `Thrust`/`Jab` here as the current slide so spear +
  punch are unchanged. Headless-test that a thrust still connects exactly as before.
- [ ] **Chunk 95 — Sword horizontal sweep.** Drive the sword pivot through a left→right yaw arc over the
  swing window (instead of sliding out), with the hitbox swept across the front so it can register
  **multiple** enemies in the arc (each hit once per swing via `_hitThisSwing`). Sword = `Sweep`. Headless-test
  that two foes flanking the front both take one hit from a single sweep.
- [ ] **Chunk 96 — Axe overhead chop.** Drive the axe pivot through a top-down pitch arc (raise → chop) over
  a slower window; single heavy hit in a narrow forward zone, long cooldown preserved. Axe = `Chop`.
  Headless-test the chop damage + that it stays single-target/narrow.
- [ ] **Chunk 97 — Mace wide circular swing.** A broad round-house yaw arc (wider than the sword), multi-hit
  with the mace's strong knockback applied along each victim's hit direction. Mace = `Swing`. Headless-test
  that a clustered group all get shoved by one mace swing.
- [ ] **Chunk 98 — Hitbox shaping + polish.** Make the active hitbox region match each style (a forward line
  for thrust/chop vs a swept arc for sweep/swing) rather than reusing the single thrust capsule, so hits read
  fairly; tune per-style timing/arc widths and add the weapon-trail/transform read-outs the HUD shows. Confirm
  all five weapons feel distinct and every headless hit test stays green. Feel-check by playing a level and
  cycling weapons with Q.

**Build order 94 → 98. Chunk 94 (the style dispatcher) is load-bearing — every later chunk just adds one
style routine + flips its weapon's `AttackStyle`. Keep `Thrust`/`Jab` byte-identical so spear/pike/punch are
untouched, and keep each style's hit logic headless-test-green.**

---

### ✅ DONE — M15 Co-op Card Brawl (Chunks 66–74)

**Built — all chunks done.** A *local same-screen 2-player* card-driven survival mode. Each player drives a
**basic egg** (weak punch only) — P1 keyboard+mouse, P2 gamepad (device 0) — and both draw from ONE shared
hand. In the between-rounds **pause** they spend shared energy on cards: `sword`/`fireball` **buff whoever
played the card**, `soldier` spawns a subordinate for that player. **End Turn** runs a **15 s real-time
survival wave**; at timeout it pauses, redeals, and queues the next (harder) wave. Lose only when **both**
eggs fall.

**Locked decisions:** buffs apply to whoever played the card (no target click); cards play in the PAUSE only;
opponent = escalating waves; flat per-round energy (`BaseEnergy`, no KotH bonus). Reuses the M12 card model +
`RoundLoop`, the M12.7 control schemes / shared camera / `RequireAllPlayersDead`, and the M9 weapon plumbing.
**Off by default** — only this new scene/captains opt in; every other mode stays byte-identical.

- [x] **Chunk 66 — Basic egg + runtime loadout.** Weak unarmed `Punch` `WeaponType` + `StartUnarmed` +
  runtime `EquipWeapon` on `Player` (Punch excluded from Q-swap). Headless-tested.
- [x] **Chunk 67 — Player ability system + Fireball.** `GrantAbility(kind)` + scheme-aware `cast_ability`
  (C / gamepad A); `AbilityType` (None default | Fireball); `Fireball` = magic projectile on a cooldown,
  damage baked in via `ScaledMagicDamage` (Int) at cast. None granted by default. Headless-tested.
- [x] **Chunk 68 — Player-buff card category.** Card model gains `CardKind.PlayerBuff` (target = Self) +
  `BuffKind` (Weapon / Ability / Soldier); `CardPlay.Play` has a 5-arg overload carrying the triggering
  `ICardPlayer` so the buff lands on THAT egg (`Player.ApplyCard`: EquipWeapon / GrantAbility / SpawnSoldier).
  `CardLibrary.BrawlDeck()` + `BrawlPool()` (Sword/Spear/Mace/Axe, Fireball, Soldier). Headless-tested.
- [x] **Chunk 69 — Shared-hand co-op card UI (two devices).** Pure `BrawlHand` routing core (deck + energy +
  per-play player index) is headless-tested; `CardBrawl.cs` is the shell — code-built shared-hand UI, P1
  mouse-click + P2 gamepad cursor (d-pad/stick + A), each play tagged with the triggering egg. Headless-tested.
- [x] **Chunk 70 — Wave/survival scene + manager + co-op lose.** `scenes/Levels/CoopCardBrawl.tscn`: two
  basic eggs (P1 `KeyboardMouse`, P2 `Gamepad` device 0) in a Pausable `Units` node, shared `FollowCamera`,
  `RoundLoop` pause→15 s→redeal, `BrawlDeck()`, flat `BaseEnergy`. `WaveManager` spawns an escalating ring
  each round (`CountForWave`); `GameManager.RequireAllPlayersDead = true` + new `DisableWin` (survival has no
  win). Added to `LevelSelect` as battle 6. Headless-tested.
- [~] **Chunk 71 — Balance + feel pass.** Starting balance set: Punch 6 dmg (≈17 hits/skeleton vs sword's 3),
  `BaseEnergy` 5 (arms both eggs + a soldier on wave 1), waves 3 → +2/round on a 17 m ring, 15 s rounds.
  **User feel-check still pending** (needs a gamepad for P2).

**Refinement chunks (72–73) — show-foes-first loop + nicer/smaller cards:**

- [x] **Chunk 72 — Foes-first wave preview ("see the threat, then counter").** The brawl loop now STAGES
  each wave during the pause: `CardBrawl.SpawnPreviewWave` (called from `OnPhaseChanged` right after
  `GetTree().Paused = true`, idempotent per wave via `_previewedWave`) drops the coming wave's ring into the
  Pausable Units node while the world is frozen — so the foes stand inert (no move/attack, can't trip the lose
  check) and both eggs can read count + composition before committing cards. **End Turn** now just unfreezes
  (no spawn there) — the staged foes spring to life for the 15 s wave. HUD reads "WAVE N INCOMING — counter the
  foes". Headless-tested (`TestBrawlPreview`): a wave enemy spawned into a Pausable node while frozen stays put,
  then chases once unpaused.
- [x] **Chunk 73 — Card visual polish (nicer + smaller).** Reskinned the `CardBrawl` shared-hand UI: each
  card's clickable root stays a `Button` (so Pressed / `Disabled` / the P2 cursor `Modulate`-tint all keep
  working) but is now a compact card frame — `CardFrame()` gives a dark body + tinted border that lightens on
  hover, with mouse-ignored child Controls drawing a coloured header bar (title, dark-on-accent), a `⚡ cost`
  badge, and a short wrapped description. Border/header tint by `BuffKind` (weapon amber / ability violet /
  soldier blue). Cards shrank 150×200 → 116×156 and the hand footprint ±560 → ±380 (sep 10 → 8) so 5 cards
  stay fully on-screen above the End Turn button. Visual/layout only — `BrawlHand` routing, two-click/cursor
  play, and buff effects unchanged. Builds clean.

**Build order 66 → 73; 66 + 68 are the load-bearing pair (weak egg + buff-the-player card category).
72 reshapes the loop (foes-first), 73 is the card-UI polish.**

- [x] **Chunk 74 — DOTA/LoL-style ability bar (targeted + instant casts).** Reshaped the single `Player`
  ability into a multi-slot BAR: `AbilityType` now `{None, Fireball, Enrage, Heal, Dash}`, and an egg holds a
  list of `AbilitySlot`s (one per granted ability, each its own cooldown), capped at 4 hotkeys. Hotkeys: P1
  keyboard keys **1–4**, P2 gamepad **A / Y / R1 / D-pad-up** (all free for the captain during a live wave;
  the brawl ignores the pad while frozen). `AbilityIsTargeted` splits them: **TARGETED** (Fireball/Dash) →
  P1 presses the hotkey to AIM (a flat ground `TorusMesh` reticle, built lazily in the egg's parent, follows
  the mouse on the y=0 plane), then LEFT-CLICK casts at that point (the click is consumed in `UpdateAiming`
  so it never also swings — `_castConfirmedThisFrame` + the `_aimingSlot` guard in `UpdateSwing`), RIGHT-CLICK
  cancels; P2/AI have no mouse so they cast toward facing (`AimDirection`). **INSTANT** (Enrage/Heal) fire on
  the hotkey, on self. Effects: Fireball = the Chunk-67 INT-scaled bolt, now aimed at the target point;
  **Enrage** = `_enrageTimer`/`EnrageFactor` (×2 for `EnrageDuration`) folded into `EffectiveAttackDamage`
  (the swing reads this); **Heal** = `Unit.Heal(amount)` (restore HP, capped, with a flash); **Dash** = blink
  toward the point capped at `DashRange` (terrain-snapped when `Grounded`). `CastSlot(slot, point?)` is the
  one cast chokepoint (`CastAbility()` = `CastSlot(0,null)` back-compat). **Abilities are PER-TURN**: granted
  by cards in the pause, wiped by `Player.ClearAbilities()` at `CardBrawl.OnRoundTimeout` (weapons + soldiers
  persist). `CardLibrary.BrawlDeck()` now grants Fireball/Enrage/Heal/Dash; `CardBrawl` draws a per-hero
  **ability bar** in each bottom corner (hotkey + name + READY/cooldown, tinted per player), refreshed every
  frame. Headless-tested (`TestAbilityBar`). Feel-check pending (needs a gamepad for P2).

---

**Shelved — not now (revisit only when asked; full detail in git history):**
- **M12.8 — Slay-the-Spire-feel pass (Chunks 55–59):** card-mode visual reskin (card frames, fanned
  hover-lift hand, board/tray + camera reframe, StS HUD chrome, play juice). Visual only.
- **M13 — Multiplayer (Chunks 50–54):** networked 2-player, server-authoritative. The hardest, last.

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
