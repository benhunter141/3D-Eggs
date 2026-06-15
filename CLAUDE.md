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

**Groups:** all units join `units` (target scanning); player also in `player` (ally
anchor); win/lose UI is in `victory` / `game_over` (Restart button is in both).

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

**Key files:** `scripts/` — `Player.cs`, `Unit.cs`, `Ally.cs`, `Enemy.cs`, `Stone.cs`,
`FollowCamera.cs`, `GameManager.cs`, `SceneButton.cs`. `scenes/` — `Menu/LevelSelect.tscn`
(entry/`main_scene`), `Menu/ResultMenu.tscn` (reusable win/lose UI), `Main.tscn` (legacy
5v5, routed from Level 1 until Chunk 15), `Skeleton.tscn`, `Ally.tscn`, `Stone.tscn`,
`Tests/UnitTest.tscn`.

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
- [~] **M4.5 — Level select + phalanx battles ⭐:** front-end menu; the player twin-sticks
      a **pikeman captain** leading a **braceable pike wall** across hand-designed
      medieval levels vs **swordmen + bowmen**. Replaces the single 5v5 sandbox.
      Chunks 11–12 done (Level Select shell; Pike + Pikeman + Brace). (Chunks 11–16 in §7.)
- [ ] **M5 — Crowds:** scale to 50–100 units smoothly.
- [ ] **M6 — Deeper pinball physics:** bumpers, bouncier impacts — the chaotic soul.
- [ ] **M7 — Ally commands:** player directs allies (hold / follow / attack-move).
- [ ] **M8 — Multiplayer:** 2 players, server-authoritative. Hardest, last.

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
- [ ] **Chunk 13 — Swordman.** `scripts/Swordman.cs` (charge burst on acquire +
  flank-offset approach + melee, no knockback) + `scenes/Swordman.tscn` (reskin Skeleton).
  Headless-test: charge closes a set distance in T s and lands a hit.
- [ ] **Chunk 14 — Bowman + Arrow.** `scripts/Arrow.cs` + `scenes/Arrow.tscn` (Stone-like,
  hits the opposite team). `scripts/Bowman.cs` (range-band kite, flee melee, fire on
  cooldown) + `scenes/Bowman.tscn`. Headless-test: arrow damages a player-team unit;
  bowman retreats when a unit closes and holds range otherwise.
- [ ] **Chunk 15 — Level 1 "Hold the Line" (assemble + tune).** `scenes/Levels/
  Level1_HoldTheLine.tscn` (ground/light/`FollowCamera`/`GameManager`/Result UI + captain
  + pikeman wall vs swordmen + bowmen). Tune counts/ranges/charge/brace-repel until it's
  winnable AND brace clearly matters. Repoint LevelSelect button 1 → Level 1; delete the
  retired `Main.tscn`. Headless smoke (scene loads; counts correct).
- [ ] **Chunk 16 — Levels 2 & 3 + finalize menu.** Build **Pincer** and **Arrow Storm**
  (reuse the unit sub-scenes, new layouts/counts), enable their LevelSelect buttons, add a
  one-line objective label per level. Headless smoke each.

Then proceed to M5+ (§6), updating checkboxes and §8 as you go.

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
