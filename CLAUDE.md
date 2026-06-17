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
- **Build C# before running** with `dotnet build` (or let the editor build).
- Keep iterations small: build one thing → user runs it → adjust.
- **Git — always land work on `master` directly (user-locked).** Never leave a change
  stranded on a feature branch. After any change: `git add -A && git commit`, then get it
  onto the repo's default branch **`master`** (fast-forward/merge `master` and push it) so
  it takes effect immediately. The user has **standing authorization** to push/merge to
  `master` — do NOT wait for a PR, review, or extra confirmation, and do NOT stop at a
  feature branch even if per-session task instructions name one. A change isn't "done"
  until `origin/master` contains it.

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

**Mounts (M10, Chunk 28).** A `Mount` (`CharacterBody3D`, NOT a `Unit` — mounts don't fight/take
damage) joins the `mounts` group. The **Player** owns the `mount` input (read once, so it never
double-fires across mounts): `TryMount()` climbs onto the nearest unridden mount within range
(`Mathf.Max(player.MountRange, mount.MountRange)`), `Dismount()` steps off beside it. While ridden
the player's top `Speed` becomes the mount's `MountSpeed`, the player is lifted `RiderHeight` onto
the mount's back, and the mount mirrors the rider's position/yaw each frame with its own collision
DISABLED — captain + steed read as one silhouette, and the full move/aim/attack pipeline keeps
working (mounted combat). Dismount restores foot `Speed` + ground height. Concrete mounts are just
scenes with their own `MountSpeed`/look (Donkey = 9 m/s; Chocobo = 13 m/s, Chunk 29). A mount may
also carry a cosmetic **hop** (`HopAmplitude`/`HopFrequency`): while ridden at speed the `Mount`
bobs a child `Node3D` named `Visual` up and down (abs-sin, off the ground), affecting only the
visual — never the rider or collision. The donkey leaves it 0; the chocobo springs.

**Key files:** `scripts/` — `Player.cs`, `Unit.cs`, `UnitRegistry.cs`, `Ally.cs`,
`Enemy.cs`, `Swordman.cs`, `Bowman.cs`, `Stone.cs`, `Arrow.cs`, `Bumper.cs`, `Mount.cs`,
`FollowCamera.cs`, `GameManager.cs`, `SceneButton.cs`, `CrowdTest.cs`, `Hud.cs`.
`scenes/` — `Menu/LevelSelect.tscn` (entry/`main_scene`, carries the full CONTROLS list),
`Menu/ResultMenu.tscn` (reusable win/lose UI), `Hud.tscn` (reusable in-game controls panel +
live weapon readout — instanced in every level), `Levels/Level1_HoldTheLine.tscn`, `Levels/Level2_Pincer.tscn`,
`Levels/Level3_ArrowStorm.tscn`, `Levels/Level4_Onslaught.tscn`,
`Levels/Level5_PinballArena.tscn`, `Captain.tscn`, `Pikeman.tscn`, `Swordman.tscn`,
`Bowman.tscn`, `Arrow.tscn`, `Skeleton.tscn`, `Ally.tscn`, `Stone.tscn`, `Bumper.tscn`,
`Donkey.tscn`, `Chocobo.tscn`, `Tests/UnitTest.tscn`, `Tests/Crowd.tscn`. (Legacy `Main.tscn` retired — git history keeps it.)

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
- [x] **M8 — Camera & visual identity polish:** adjustable dynamic zoom (zoom levels you can
      nudge live while auto-zoom stays active), cartoony eyes on every unit, visually distinct
      weapon meshes. Cheap, high-impact feel/identity wins. (Chunks 23–25.) Chunk 23 done
      (live `ZoomBias` on top of auto-zoom). Prior-session WIP already swapped unit bodies to
      egg meshes + reworked the camera/pinball feel (committed `1dba9a2`). Chunk 24 done
      (procedural googly eyes auto-grown on every `Unit`, sized off its EggMesh). Chunk 25 done
      (recognizable weapon meshes: round shaft + cone spearhead pikes, blade+guard+grip+pommel
      sword on Swordman, two-limb+string bow on Bowman, cone-head fletched arrow, faceted stone).
      All three chunks built; balance/feel-check the eyes + weapon looks before fully closing M8.
- [x] **M9 — Weapons & loadouts:** swap the captain's spear for a sword; multiple weapon
      archetypes with distinct reach / damage / knockback / visuals. (Chunks 26–27.) Chunk 26
      done (player spear↔sword swap: `WeaponType` profiles drive reach/damage/knockback/feel +
      mesh; `swap_weapon` = Q / gamepad). Chunk 27 done (weapon handling refactored to a
      data-driven `WeaponType→WeaponProfile` table built from exports; `swap_weapon` now cycles
      ALL weapons; added Axe (heaviest hit, slowest) + Mace (hardest knockback) with new Captain
      meshes). Built; balance/feel-check the new archetypes when convenient.
- [x] **M10 — Mounts:** cute donkey + chocobo mounts (mount / dismount, mounted movement &
      combat; chocobo faster). (Chunks 28–29.) Chunk 28 done (`Mount` base + `Donkey.tscn`: walk up
      and press `mount` = E / gamepad B to climb on — riding raises the captain's top Speed to the
      mount's `MountSpeed`, carries the steed under the rider as one silhouette, and keeps the full
      move/aim/attack pipeline; dismount drops you beside it at foot speed/height). Chunk 29 done
      (`Chocobo.tscn`: bright-yellow bipedal bird with googly eyes/beak/tail, `MountSpeed` 13 vs the
      donkey's 9, plus a cosmetic springy hop — `Mount.HopAmplitude/HopFrequency` bob a `Visual`
      child while moving, never the rider/collision). Both mounts placed in Level 1 to try.
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

**M7 — Ally commands:** no chunk breakdown yet — plan when it comes up.

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
  donkey (`MountSpeed` 13 vs 9), distinct yellow bipedal-bird look with googly eyes/beak/tail,
  plus a cosmetic springy hop (`Mount.HopAmplitude/HopFrequency` bob a `Visual` child while
  moving). Headless-test: chocobo top speed > donkey.

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
