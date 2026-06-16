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

**Near-term target — a vertical slice:** player (sword) + 4 allies (2 fists, 2 stones)
vs 5 skeletons, **5v5**. Allies stay near the player in formation. See §7 for the plan.

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
- **Build C# before running** with `dotnet build` (or let the editor build).
- Keep iterations small: build one thing → user runs it → adjust.

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

**Match state:** `GameManager` (node in `Main.tscn`) is the single authority — each
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
- **Squad:** 2 fists (Ally1/2) + 2 stones (Ally3/4) in `Main.tscn`.
- Direct player-issued ally commands come later (M7).

**Key files:** `scripts/` — `Player.cs`, `Unit.cs`, `UnitRegistry.cs`, `Ally.cs`,
`Enemy.cs`, `Swordman.cs`, `Bowman.cs`, `Stone.cs`, `Arrow.cs`, `FollowCamera.cs`,
`GameManager.cs`, `SceneButton.cs`, `CrowdTest.cs` (M5 stress harness).
`scenes/` — `Menu/LevelSelect.tscn` (entry/`main_scene`), `Menu/ResultMenu.tscn` (reusable
win/lose UI), `Levels/Level1_HoldTheLine.tscn` (captain + pike wall vs swordmen/bowmen),
`Levels/Level2_Pincer.tscn` (two-flank swordman charge), `Levels/Level3_ArrowStorm.tscn`
(advance under massed bowman fire), `Levels/Level4_Onslaught.tscn` (crowd battle —
phalanx vs ~33-unit host), `Captain.tscn`, `Pikeman.tscn`, `Swordman.tscn`,
`Bowman.tscn`, `Arrow.tscn`,
`Skeleton.tscn`, `Ally.tscn`, `Stone.tscn`, `Tests/UnitTest.tscn`,
`Tests/Crowd.tscn` (M5 stress sweep). (Legacy `Main.tscn`
retired in Chunk 15 — git history keeps it.)

## 6. Roadmap (single-player fun first, multiplayer LAST)

Each milestone must be playable before moving on. **Don't start multiplayer until
M1–M5 feel great** — networking many physics bodies is the hardest part.

- [x] **M0 — Setup:** env verified, project builds, ground + player capsule visible.
- [~] **M1 — Twin-stick melee feel ⭐:** WASD move, mouse aim, click-swing sword.
      Chunks 1–2 built; Chunk 2 swing still pending the user's feel-check. Tune til fun.
- [x] **M2 — Skeletons:** chase, take damage, die; sword knockback; player can die.
- [x] **M3 — Allies in formation:** loose-leash followers that fight (fists + stones)
      and re-form.
- [~] **M4 — 5v5 vertical slice:** player + 4 allies vs 5 skeletons; win/lose; juice.
      Chunk 9 done (5v5 encounter + win/lose/restart). Standalone juice (Chunk 10) is
      **deferred** — it now folds into the per-level tuning passes under M4.5.
- [x] **M4.5 — Level select + phalanx battles ⭐:** front-end menu; the player twin-sticks
      a **pikeman captain** leading a **braceable pike wall** across hand-designed
      medieval levels vs **swordmen + bowmen**. Replaces the single 5v5 sandbox.
      Chunks 11–16 done (Level Select shell; Pike + Pikeman + Brace; Swordman; Bowman + Arrow;
      Level 1 "Hold the Line"; Levels 2 "Pincer" & 3 "Arrow Storm" + objective labels).
- [x] **M5 — Crowds:** scale to 50–100 units smoothly. Chunks 17–19 done (`UnitRegistry`
      killed the per-frame O(n²) group scans; `Crowd.tscn` stress harness + staggered target
      re-scan; `Level4_Onslaught.tscn` crowd battle, ~49 units). 50 AND 100 units sit within
      the 60 FPS budget (median physics ~2.9 / ~4.4 ms throttled). Onslaught balance still
      needs a user feel-check.
- [~] **M6 — Deeper pinball physics:** bumpers, bouncier impacts — the chaotic soul.
      Chunks 20–22 done (knockback now BOUNCES + transfers on impact — a shoved unit hands part
      of its momentum to whatever it rams and rebounds, so one sword-fling chains through a
      crowd; `Bumper.tscn` static posts KICK touching units back out faster than they came in;
      `Level5_PinballArena.tscn` is a walled bumper arena that revels in it). All three chunks
      built; balance (restitution / transfer / min-bounce / bumper strength / arena counts)
      needs a user feel-check before M6 closes.
- [ ] **M7 — Ally commands:** player directs allies (hold / follow / attack-move).
- [~] **M8 — Camera & visual identity polish:** adjustable dynamic zoom (zoom levels you can
      nudge live while auto-zoom stays active), cartoony eyes on every unit, visually distinct
      weapon meshes. Cheap, high-impact feel/identity wins. (Chunks 23–25.) Chunk 23 done
      (live `ZoomBias` on top of auto-zoom). Prior-session WIP already swapped unit bodies to
      egg meshes + reworked the camera/pinball feel (committed `1dba9a2`); Chunks 24–25 pending.
- [ ] **M9 — Weapons & loadouts:** swap the captain's spear for a sword; multiple weapon
      archetypes with distinct reach / damage / knockback / visuals. (Chunks 26–27.)
- [ ] **M10 — Mounts:** cute donkey + chocobo mounts (mount / dismount, mounted movement &
      combat; chocobo faster). (Chunks 28–29.)
- [ ] **M11 — King of the Hill mode:** capture zones score their holder at the end of each
      period (15 s for now); HUD for scores / timer / contest. Feeds M12's energy. (Chunks 30–31.)
- [ ] **M12 — Slay the Eggs (card battler mode):** Slay-the-Spire-style PvE — visible
      draw / hand / discard piles; Unit cards played on a location, Action cards played on a
      friendly unit who then performs the action; units have HP / Str / Int (Str → weapon &
      strength actions, Int → magic); beat a series of rooms collecting cards / relics /
      potions with events; **energy comes from holding KotH points (M11) at period end.**
      (Chunks 32–37.)
- [ ] **M13 — Multiplayer:** 2 players, server-authoritative. Hardest, last.

## 7. Build Plan (chunks)  ← start here when user says "go"

> **WORKFLOW — what "go" means.** Find the **first unchecked `[ ]` chunk** below and
> build it **completely** (scenes + scripts), assuming all earlier chunks work. Then:
> 1. `dotnet build` — confirm 0 errors.
> 2. **Commit** the chunk (`git add -A && git commit`) as a roll-back point.
> 3. Tick its box `[x]` and update the M-line in §6.
> 4. Reply with a one-line summary + the exact run command (§8). Don't wait for
>    confirmation — the user says if something's off, else "go".
>
> Keep chunks self-contained and small. Headless-test logic where possible (§4).

**Done (detail in code + git log):**
- [x] **Chunk 1** — Mouse aim + follow camera.
- [x] **Chunk 2** — Sword swing (arc hitbox + cooldown). *Pending user feel-check.*
- [x] **Chunk 3** — `Unit`/health foundation + damage dummy.
- [x] **Chunk 4** — Sword knockback.
- [x] **Chunk 5** — Skeleton AI (chase + melee) + player can die.
- [x] **Chunk 6** — Allies + formation (movement only).
- [x] **Chunk 7** — Ally combat (loose leash) + fists.
- [x] **Chunk 8** — Stone-throwing allies.
- [x] **Chunk 9** — 5v5 encounter + win/lose. `GameManager` owns match state (LOSE if
  player dies, WIN once all skeletons cleared; lose-first so a late ally kill can't show
  VICTORY over GAME OVER). 5 skeletons in `Main.tscn`; VICTORY/GAME OVER labels; Restart
  button + `restart` key (R / gamepad).

**Deferred (do NOT build next — superseded by the pivot below):**
- [~] **Chunk 10 — Juice & tuning.** Health bars / hit flashes, swing + impact effects,
  balance pass. Rolled into the per-level tuning of Chunks 15–16; revisit only if asked.

---

### ▶ ACTIVE PLAN — Level Select + Phalanx Battles (Chunks 11–16)

**Direction change (locked with the user 2026-06-14):** retire the single 5v5 sandbox in
favour of a **level-select front-end + hand-designed phalanx battles**.
- **Control = Captain + phalanx.** You twin-stick **one pikeman captain** (WASD + mouse,
  click = pike thrust — the existing `Player`). Your pikemen hold a **tight wall** in
  player-relative slots that rotate with your facing (existing `Ally` formation).
  **Right-click / Space = BRACE:** the whole line plants, faces your yaw, presents pikes.
  **Lose = captain dies; Win = all enemies cleared** — `GameManager` logic is unchanged.
- **Levels replace the 5v5.** Level Select is the new entry scene; old `Main.tscn` is
  retired (git history keeps it). All levels are fresh medieval scenarios.

**New durable rules (design law — promote into §5 as the chunks land):**
- **Pike reach + brace-repel.** Pikemen attack at long range (~3 m). The rule "only the
  captain's weapon knocks back" still holds, with **one exception:** a **braced** pike
  *repels* (small shove) whatever charges its front — the phalanx's whole point, and it
  feeds the pinball feel.
- **Brace is polled, not plumbed.** Every Pikeman reads the `brace` input itself each
  frame (no captain→squad messaging), keeping the stance decoupled.
- **Enemy archetypes never knock back.** **Swordman** = charging/flanking melee (curls
  around the wall to dodge braced pikes); **Bowman** = kiting ranged (holds a range band,
  flees melee, lobs arrows).

**Unit reference (build each as a reusable sub-scene):**
- `Captain` (= `Player`, pike skin, longer thrust hitbox; the `FollowCamera` target).
- `Pikeman` (= `Ally` + `WeaponType.Pike`): reach ~3 m, moderate dmg; **brace** = hold
  slot, face captain yaw, damage + small repel of enemies in the pike-front.
- `Swordman` (enemy): chase nearest player-team unit, **charge burst** on acquire,
  flank-offset approach, melee on contact.
- `Bowman` (enemy): keep ~10–14 m, flee if a melee unit < ~5 m, fire an `Arrow` on cooldown.
- `Arrow` (projectile): mirrors `Stone` (straight flight, proximity hit on the *opposite*
  team, damage no knockback, lifetime). *A later cleanup may unify Stone + Arrow.*

**Level designs (the "few levels"):**
1. **Hold the Line** — phalanx vs a screen of **swordmen** backed by **bowmen**. Brace to
   impale charges; push forward between volleys to reach and rout the archers.
2. **Pincer** — swordmen charge from **two flanks** (few bowmen); pivot the wall (slot
   rotation) to face each push. Teaches facing.
3. **Arrow Storm** — many **bowmen** on the far side behind a thin swordman screen; advance
   the braced phalanx across open ground **under fire** to close. Endurance.

**Chunks (the WORKFLOW box above still applies — build the first `[ ]`):**
- [x] **Chunk 11 — Level Select shell + scene routing.** `scenes/Menu/LevelSelect.tscn`
  (title + one button per level; unbuilt ones disabled) becomes the new `run/main_scene`.
  `scripts/SceneButton.cs` (exported `ScenePath` → `GetTree().ChangeSceneToFile`). Replace
  the single Restart button with a reusable **Result menu** (Retry = reload scene, Level
  Select = back to menu) usable by every level. Temporarily route button 1 → old
  `Main.tscn` so routing is testable end-to-end (repointed in Chunk 15).
- [x] **Chunk 12 — Pike + Pikeman + Brace.** Add `WeaponType.Pike` to `Ally` (long reach;
  brace stance = hold slot, face captain yaw, damage + small repel of enemies in the
  pike-front). `scenes/Pikeman.tscn`, `scenes/Captain.tscn` (pike-skinned `Player`, longer
  hitbox). Add the `brace` input action (RMB + Space + gamepad) to `project.godot`.
  Headless-test in `UnitTest.tscn`: reach gate (hits at ~2.8 m, misses at ~3.5 m) +
  braced pikeman holds slot and damages/repels a unit placed in front.
- [x] **Chunk 13 — Swordman.** `scripts/Swordman.cs` (charge burst on acquire +
  flank-offset approach + melee, no knockback) + `scenes/Swordman.tscn` (reskin Skeleton).
  Headless-test: charge closes a set distance in T s and lands a hit.
- [x] **Chunk 14 — Bowman + Arrow.** `scripts/Arrow.cs` + `scenes/Arrow.tscn` (Stone-like,
  hits the opposite team). `scripts/Bowman.cs` (range-band kite, flee melee, fire on
  cooldown) + `scenes/Bowman.tscn`. Headless-test: arrow damages a player-team unit;
  bowman retreats when a unit closes and holds range otherwise.
- [x] **Chunk 15 — Level 1 "Hold the Line" (assemble + tune).** `scenes/Levels/
  Level1_HoldTheLine.tscn` (ground/light/`FollowCamera`/`GameManager`/Result UI + captain
  + pikeman wall vs swordmen + bowmen). Tune counts/ranges/charge/brace-repel until it's
  winnable AND brace clearly matters. Repoint LevelSelect button 1 → Level 1; delete the
  retired `Main.tscn`. Headless smoke (scene loads; counts correct).
- [x] **Chunk 16 — Levels 2 & 3 + finalize menu.** Built **Pincer**
  (`scenes/Levels/Level2_Pincer.tscn` — 6 swordmen from two flanks + 2 bowmen; captain +
  6-pikeman wall in center) and **Arrow Storm** (`scenes/Levels/Level3_ArrowStorm.tscn` —
  6 bowmen far behind a 3-swordman screen; advance the braced phalanx under fire). Enabled
  their LevelSelect buttons; added a top-center objective `CanvasLayer` label to all three
  levels. Headless smoke: both scenes load clean (no errors); Level 2 flank bowmen fire on
  the wall immediately, Level 3 archers stay quiet (out of range until you close).

---

### ▶ ACTIVE PLAN — M5 Crowds (Chunks 17–19)

**Goal:** make 50–100 units run smoothly so battles can scale up. Attack the per-frame
cost first, then add a stress scene to measure, then ship a big battle.

- [x] **Chunk 17 — `UnitRegistry` (kill the per-frame group scans).** New static
  `scripts/UnitRegistry.cs` buckets living units by team; `Unit._Ready`/`_ExitTree`
  register/unregister. Replaced every `GetNodesInGroup("units")` target scan
  (Enemy/Swordman/Bowman + Ally leash & brace + Stone/Arrow) with
  `UnitRegistry.FindNearestOpponent` / `Opponents`. Pure perf refactor — all 11 headless
  tests still pass, incl. a new registry test (buckets, nearest, max-range, skip-dead,
  unregister-on-free). See §5 "Target scanning".
- [x] **Chunk 18 — Stress scene + frame budget.** `scenes/Tests/Crowd.tscn` + `CrowdTest.cs`
  spawn two armies (Swordman/Bowman vs Pikemen) at a parametric crowd size and report the
  MEDIAN physics-step ms over a sample window (median, not mean — C# GC pauses inflate the
  mean at this scale; the min/p90 frame the GC tail). Cold-process A/B (`++ --count=N
  --rescan=K`) is the trustworthy comparison — within one process the medians drift with run
  order. Added the staggered target re-scan (`Unit.ShouldRescanTarget()`/`CachedTarget`,
  `TargetRescanInterval`=6) across Enemy/Swordman/Bowman/Ally. **Numbers (cold-process
  median physics step, 60 FPS budget = 16.67 ms):** N=50 scan-every-frame 5.7 ms → throttled
  2.9 ms; N=100 9.4 → 4.4 ms — the throttle ~halves it and both sizes are well within budget
  (even unthrottled 100 floors ~6 ms; `MoveAndSlide` physics, not the registry scan, is now
  the dominant term). All 11 headless `UnitTest` checks still pass. `FaceTowards`-at-rest
  skip wasn't needed — budget already met.
- [x] **Chunk 19 — A crowd battle level.** `scenes/Levels/Level4_Onslaught.tscn` —
  captain + 15-pikeman 3×5 block vs a 33-unit host (20 swordmen in 3 waves + 12 bowmen
  behind), ~49 units total on a wider 70×70 ground with a pulled-back camera (offset
  0,18,16) + objective label. Added the LevelSelect "4. Onslaught" button. Headless smoke:
  loads clean, no premature win/lose. **Counts/balance unplayed — user feel-check pending;
  tune enemy counts / charge / brace-repel if it's un-winnable or trivial.**

---

### ▶ ACTIVE PLAN — M6 Pinball Physics (Chunks 20–22)

**Goal:** make collisions *chaotic and bouncy* — the game's stated soul. Build the response
first (bounce + momentum transfer), then place bouncy obstacles, then a level that revels in it.

- [x] **Chunk 20 — Bouncy knockback + impact transfer (chain reactions).** Pure script in
  `Unit.cs`: a shoved unit (> `MinBounceSpeed`) hands part of its momentum to whatever it rams
  (`KnockbackTransfer`, no damage) and bounces the rest back (`KnockbackBounce`); all shove
  injection routes through a new public `Unit.AddKnockback` (clamped to `MaxKnockback`), and
  each AI subclass calls `ResolveKnockbackBounce()` after its `MoveAndSlide`. So one sword-fling
  chains through a crowd. Uses the knockback travel direction (not the unreliable capsule
  contact normal/positions) as the impact axis for body-vs-body; surface normal only for walls.
  See §5 "Pinball collision response". Headless `UnitTest`: a cue unit flung into a stationary
  pin hands it a forward shove and rebounds (12/12 checks pass). **Balance + feel unplayed —
  user feel-check pending (tune `KnockbackBounce`/`Transfer`/`MinBounceSpeed`).**
- [x] **Chunk 21 — Bumpers (static bouncy obstacles).** `scenes/Bumper.tscn` (`StaticBody3D`
  solid core + child `Area3D` detection ring) + `scripts/Bumper.cs`: on `Area3D.BodyEntered`,
  KICK the unit straight out from the bumper centre — outward speed = `max(BumperStrength,
  incoming * SpeedAmplify)` so even a slow walker is flung and a fast pinball impact leaves
  faster. Routed through `Unit.AddKnockback` (cancel current shove, then add outward*target),
  so it's clamped to `MaxKnockback`, decays, and chains like any shove (no damage). Chose the
  `Area3D` path over the `ResolveKnockbackBounce` wall path because that path only fires for a
  unit already carrying a shove (> `MinBounceSpeed`); the area kicks anyone who wanders in.
  Headless `UnitTest`: a skeleton shoved at 6 m/s into a bumper leaves at 12 m/s pointing away
  (13/13 checks pass). **Balance/feel unplayed — folds into Chunk 22's arena tuning.**
- [x] **Chunk 22 — A pinball arena level.** `scenes/Levels/Level5_PinballArena.tscn` — a
  walled 44×44 arena (four solid perimeter `StaticBody3D` walls) with a cluster of 8 `Bumper`
  posts in the midfield. Captain + 4-pikeman squad vs a 10-swordman charge (+2 corner bowmen),
  so sword-knockback flings chargers into bumpers/walls and chain-bounces them around. Added
  the "5. Pinball Arena" LevelSelect button + objective label. Headless smoke: loads clean, no
  errors, bumpers fling charging swordmen, no premature win/lose. **Counts/balance unplayed —
  user feel-check pending.**

M7 (Ally commands, §6) has no chunk breakdown yet — its chunks get planned when it comes up.
The active build queue continues with M8 below.

---

### ▶ PLANNED — M8 Camera & Visual Identity Polish (Chunks 23–25)

**Goal:** sharpen look & feel with cheap, high-impact wins before the bigger systems land.

- [x] **Chunk 23 — Adjustable dynamic zoom.** `FollowCamera` now carries a live, persistent
  `ZoomBias` (metres) the player nudges with the mouse wheel / `zoom_in` / `zoom_out` (added to
  `project.godot`: wheel up/down + `=`/`-` keys + D-pad up/down). The bias is added on top of the
  base distance and re-clamped each frame, so DynamicZoom keeps auto-framing both armies — just
  shifted by the player's preference — and the fixed-camera levels (1–4) get the same nudge hung
  off their authored Offset, floored at `MinFixedDistance` so a hard zoom-in can't pass through
  the captain. The bias core is pure (`StepZoom`/`DesiredDistance`), so a new headless `UnitTest`
  drives it without a tree: stepping shifts the target distance and stays clamped at both ends in
  both modes (14/14 checks pass). **Feel (step size / clamp range) unplayed — user feel-check
  pending.**
- [ ] **Chunk 24 — Cartoony eyes on units.** Give every `Unit` a pair of simple
  camera-/forward-facing eye visuals (white + pupil), sized per archetype, as a child node.
  Pure visual; no logic. User feel-check for cuteness.
- [ ] **Chunk 25 — Visually distinct weapon meshes.** Replace placeholder weapon visuals with
  recognizable meshes per weapon (spear/pike shaft+tip, sword blade+guard, bow, stone, arrow)
  so a unit's weapon reads at a glance. Pure visual; reused across the matching scenes.

---

### ▶ PLANNED — M9 Weapons & Loadouts (Chunks 26–27)

**Goal:** make the weapon a real choice — the captain can wield a sword instead of the pike,
and weapons differ in reach / damage / knockback / look.

- [ ] **Chunk 26 — Player weapon swap (spear ↔ sword).** Give `Player` a `WeaponType`
  (Spear | Sword) driving hitbox reach, damage, knockback, and thrust/swing feel + the
  Chunk-25 mesh. Add a `swap_weapon` input to toggle in-game (and/or per-level default).
  Sword = short reach + knockback (existing sword rules); spear = long reach, no knockback.
  Headless-test: each weapon's reach + knockback match its profile.
- [ ] **Chunk 27 — More weapon archetypes.** Add a couple more weapons (e.g. axe = heavy/slow/
  high-damage, mace = knockback, bow = ranged) as `WeaponType` entries reusing the Chunk-26
  plumbing + Chunk-25 visuals, so allies/enemies can be skinned with them too. Headless-test:
  each archetype's stats resolve correctly.

---

### ▶ PLANNED — M10 Mounts (Chunks 28–29)

**Goal:** rideable mounts for speed & charm — a cute donkey and a chocobo.

- [ ] **Chunk 28 — Mount base + Donkey.** `scripts/Mount.cs` + `scenes/Donkey.tscn`:
  mount/dismount (proximity + `mount` input), mounted state raises move speed & changes the
  player silhouette (rider on mount), mounted combat still works; dismount drops you beside the
  mount. Headless-test: mounting raises speed, dismount restores it.
- [ ] **Chunk 29 — Chocobo mount.** `scenes/Chocobo.tscn` reusing `Mount.cs` — faster than the
  donkey, distinct look (Chunk-24 eyes), maybe a small hop/dash trait. Headless-test: chocobo
  top speed > donkey.

---

### ▶ PLANNED — M11 King of the Hill Mode (Chunks 30–31)

**Goal:** a scoring mode where holding ground matters — and the foundation Slay the Eggs (M12)
draws energy from.

- [ ] **Chunk 30 — Capture zone + period scoring.** `scripts/CapturePoint.cs` +
  `scenes/CapturePoint.tscn`: an `Area3D` zone tracking which team has units inside; every
  **period (15 s for now)** it awards a point to the team holding it at period end (contested =
  no award). Emits a signal / exposes state for HUD + energy hooks. Headless-test: holder at
  period end scores; contested scores nobody.
- [ ] **Chunk 31 — KotH mode level + HUD.** `scenes/Levels/KingOfTheHill.tscn` (arena + one or
  more `CapturePoint`s + squads) + a `CanvasLayer` HUD showing per-team score, period countdown,
  and contest state; win at a score threshold. Add the LevelSelect button. Headless smoke:
  loads clean, points tick at period boundaries.

---

### ▶ PLANNED — M12 Slay the Eggs — Card Battler Mode (Chunks 32–37)

**Goal:** a Slay-the-Spire-style PvE mode layered on the battlefield — build a deck and spend
energy (earned by holding KotH points, M11) to deploy units and trigger actions across a run of
rooms.

**New durable rules (promote into §5 as chunks land):**
- **Cards = Units or Actions.** A **Unit card** is played onto a **location** (a battlefield
  zone / `CapturePoint`-style slot) to spawn that unit for your team. An **Action card** is
  played onto a **friendly unit**, which then performs the action (move / attack / buff / spell).
- **Unit stats: HP / Str / Int.** **Str** scales weapon attack power + strength-based actions;
  **Int** scales magic-based actions. Stats live on `Unit` (or a card-mode component).
- **Energy from holding ground.** Card energy each period = KotH points your team holds at
  period end (M11) — territory *is* your economy.
- **Run = rooms.** PvE like StS: a series of rooms (combat / event), collecting **cards**,
  **relics**, and **potions** between them.

- [ ] **Chunk 32 — Card model + piles + hand UI.** `scripts/Cards/` data model (Card, Deck) +
  draw / hand / discard piles with reshuffle; on-screen UI showing all three piles (StS-style:
  draw count, hand, discard). Headless-test: draw/discard/reshuffle cycle conserves the deck.
- [ ] **Chunk 33 — Unit & Action cards (play targeting).** Unit cards target a **location**
  (spawn there); Action cards target a **friendly unit** (it performs the action). Play resolves
  to a real spawn / a real unit behavior on the battlefield. Headless-test: a unit card spawns at
  a location; an action card makes its target unit act.
- [ ] **Chunk 34 — HP / Str / Int stats wired in.** Add Str/Int to units; Str scales weapon
  damage + strength actions, Int scales magic actions. Headless-test: higher Str → more weapon
  damage; higher Int → stronger magic action.
- [ ] **Chunk 35 — Energy from KotH points.** Tie M11 capture-point holdings to per-period card
  **energy**; you can only play cards your held points afford. Headless-test: holding more points
  grants more energy; energy gates plays.
- [ ] **Chunk 36 — Run structure (rooms + rewards + events).** A room map you traverse (combat /
  event rooms), with post-room rewards (pick a card; find relics / potions) and a few event
  rooms. Headless-test: completing a room advances the map and offers a reward.
- [ ] **Chunk 37 — Relics & potions.** Passive **relics** (run-long modifiers) + consumable
  **potions** (one-shot effects), collected through the run. Headless-test: a relic's modifier
  applies; a potion consumes and triggers its effect.

---

Then proceed to **M13 — Multiplayer** (§6), updating checkboxes and §8 as you go.

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
