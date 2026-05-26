# 3D Eggs — Project Guide

Godot exe path: C:\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe

> This file is the single source of truth for the project. The conversation that
> created it will be cleared, so everything needed to continue lives here. When the
> user says **"go"**, build the **next unchecked chunk** in §7 — follow the WORKFLOW
> box there. Assume prior chunks already work; the user reports problems if any.

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
vs 5 skeletons, **5v5**. Allies stay near the player in formation. See §7 for the
chunk-by-chunk plan.

Theme: the folder is called "3D Eggs" 🥚 but the game is **medieval fantasy** — the egg
theme is dropped for now. Use medieval/neutral names (`Player`, `Ally`, `Enemy` /
`Skeleton`).

## 2. Locked-In Decisions

| Decision | Choice | Why |
|---|---|---|
| Engine | **Godot 4.5.x — the .NET / C# build** | Free & open source; text-based scene files let Claude build & edit scenes directly; first-class headless mode for testing. |
| Language | **C#** | User has prior C#/Unity experience — it transfers directly. |
| .NET SDK | **8.0 or newer** (required by Godot 4.4+ for C#) | Godot C# packages target `net8.0`. |
| Target platform | **Desktop download** (Windows first) | Simplest path; best physics/multiplayer performance. C# desktop export is fully supported. |
| Controls | **Twin-stick:** WASD = move, mouse = aim, left-click = attack (melee). Gamepad sticks also supported. | Classic desktop twin-stick feel. |

## 3. Developer Profile

- Comfortable with **C#** and has used **Unity** before — but **new to Godot**.
- So: explain Godot-specific concepts (nodes, scenes, signals, the editor) but don't
  over-explain general programming or C#.
- Wants Claude to be **hands-on** — build large chunks (scenes, scripts, tests), not
  just hand over code to paste.

## 4. How Claude Works On This Project

- **Claude CANNOT see the running game.** Visual "feel" (movement, bounce, fun) can
  only be judged by the user playing. After building something visual, ask the user to
  run it and describe what they see. Always give exact run instructions.
- **What Claude CAN test:** logic/numbers (damage, spawn counts, collision events via
  `GD.Print`), C# compilation (`dotnet build`), and project/scene structure.
- **Godot files are text** — Claude reads & edits `.tscn` (scenes), `project.godot`,
  and `.cs` scripts directly.
- **Headless run/test:** `godot --headless --path . --script res://path/to/test.gd`
  (or run a scene headless). `GD.Print(...)` output goes to stdout for Claude to read.
- **Build C# before running:** Godot generates a `.sln` / `.csproj`; compile with
  `dotnet build` (or let the editor build). The Godot executable path is recorded in
  §6 once installed.
- Keep iterations small: build one thing → user runs it → adjust.

## 5. Roadmap (build in layers — single-player fun first, multiplayer LAST)

Each milestone must be playable before moving on. **Do not start multiplayer until
1–5 feel great** — networking many physics bodies is the hardest part and kills
momentum if attempted early.

- [x] **M0 — Setup:** .NET SDK + Godot .NET installed & verified; project created;
      flat ground + a player capsule visible. ✅ DONE
- [~] **M1 — Twin-stick melee feel ⭐ (top priority):** WASD move, mouse aim, click to
      swing a sword. Tune until it *feels good*. (§7 Chunks 1–2)
      Progress: WASD + mouse aim + follow camera (Chunk 1 ✓); sword swing built (Chunk 2,
      pending user feel-check).
- [ ] **M2 — Skeletons:** chase the player, take damage, die; sword knockback; player
      can be hit and die. (§7 Chunks 3–5)
- [ ] **M3 — Allies in formation:** loose-leash followers that fight (fists + thrown
      stones) and return to formation. (§7 Chunks 6–8)
- [ ] **M4 — 5v5 vertical slice:** player + 4 allies vs 5 skeletons; win/lose; juice &
      tuning. (§7 Chunks 9–10)
- [ ] **M5 — Crowds:** scale to 50–100 units smoothly.
- [ ] **M6 — Deeper pinball physics:** bumpers, bouncier impacts — the chaotic soul.
- [ ] **M7 — Ally commands:** player directs allies (hold / follow / attack-move).
- [ ] **M8 — Multiplayer:** start with 2 players, server-authoritative. Hardest, last.

## 6. Environment State (update as we go)

- .NET SDK installed: **YES — v10.0.203** (≥ 8.0 ✓)
- Godot .NET installed: **YES — v4.6.3.stable.mono** ✓
- Godot executable path: `C:\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe`
- Project created: **YES** — `project.godot`, assembly_name=`Eggs`, C# builds clean (`Eggs.dll`)
- C# solution: `Eggs.sln` / `Eggs.csproj` (Godot.NET.Sdk 4.6.3, net8.0)
- Renderer: **GL Compatibility (OpenGL3)** — set in `project.godot [rendering]`.
  ⚠️ Do NOT switch back to Forward+/Vulkan: this machine (NVIDIA 940MX, old driver
  376.54 → Vulkan 1.0.24) crashes with hundreds of `vkCreateComputePipelines` errors.
  Compatibility runs fine and suits the hardware.
- Godot relaunches **detached** on Windows, so launching from a tool returns exit 0
  instantly with an empty stdout log. To see real output, read the per-run log at:
  `%APPDATA%\Godot\app_userdata\3D Eggs\logs\godot.log`
- Project layout: `scenes/Main.tscn` (main scene), `scripts/Player.cs`. Put new
  scenes in `scenes/`, scripts in `scripts/`. Launch via `play.bat` or editor F5.
- Git: initialized; commit after each milestone / before risky changes (roll-back net).

## 7. Getting Started Walkthrough  ← start here when user says "go"

Setup (old Phases 0–1, M0) is **DONE**: env verified, project builds clean, and the
base scene exists — ground + player capsule (`CharacterBody3D`) + angled camera + light
(§6).

> **WORKFLOW — what "go" means.** Find the **first unchecked `[ ]` chunk** in the list
> below and build it **completely** (scenes + scripts), assuming all earlier chunks
> already work. Then:
> 1. Run `dotnet build` and confirm it compiles (0 errors).
> 2. **Commit** the chunk (`git add -A && git commit`), so each chunk is a roll-back point.
> 3. Tick the chunk's box `[x]` here and update the M-progress line in §5.
> 4. Reply with a one-line summary + the exact run command (§8). Do **not** wait for
>    confirmation before continuing — the user will say if something's off, else "go".
>
> Keep chunks self-contained and small. Headless-test logic where possible (§4).

Architecture spine: a `Unit` base (`CharacterBody3D` with `Health`, `Team`,
`TakeDamage`, knockback-decay) that `Player`, `Ally`, and `Enemy` all extend, so combat
works the same for everyone.

**Locked design choices (from the user):**
- **Allies = loose leash:** stay near their formation slot; chase enemies only within a
  radius, then return. (Direct player-issued ally commands come later — M7.)
- **Only the player's sword knocks back.** Fists, thrown stones, and skeleton hits deal
  damage with **no** knockback for now.
- **Slice team:** player + 4 allies (2 fists, 2 stones) vs 5 skeletons.

### Phase A — Player melee core (M1)
- [x] **Chunk 1 — Mouse aim + follow camera.** `FollowCamera.cs` on `Camera3D` tracks
  the player's position at a fixed offset (no rotation inherited). `Player.AimAtMouse()`
  raycasts camera→player-plane at the cursor and `LookAt`s it; red `FacingMarker` nub
  shows facing. **DONE & confirmed.**
- [x] **Chunk 2 — Sword swing.** Sword mesh on the player; left-click swings a ~0.2s arc
  with a cooldown; an `Area3D` arc hitbox is live during the swing and prints overlaps.
  Add an `attack` input action. → User confirms the swing feels responsive.

### Phase B — Skeletons & combat (M2)
- [ ] **Chunk 3 — `Unit`/health foundation + damage dummy.** Build `Unit` (`Health`,
  `Team`, `TakeDamage`, `Die`); refactor `Player` onto it. Drop in one stationary
  skeleton the sword kills. → Headless test prints damage/death; user confirms the
  dummy dies after N hits.
- [ ] **Chunk 4 — Sword knockback.** Sword hits apply a knockback impulse along the hit
  direction; `Unit` decays it each frame. → User confirms skeletons fly back.
- [ ] **Chunk 5 — Skeleton AI + player can die.** Skeletons chase the nearest enemy-team
  unit and melee on contact (damage, no knockback). Player has health + a game-over
  state. → User confirms a skeleton chases, hits, and can kill the player.

### Phase C — Allies (M3)
- [ ] **Chunk 6 — Allies + formation (movement only).** `Ally : Unit`; 4 allies steer to
  formation slots (offsets rotated with the player) with arrive-slowdown. No combat
  yet. → User confirms they hold formation while moving and turning.
- [ ] **Chunk 7 — Ally combat (loose leash) + fists.** Allies attack skeletons within a
  leash radius of their slot, then return when none are near. Fists = damage, no
  knockback. → User confirms they engage nearby skeletons without scattering.
- [ ] **Chunk 8 — Stone-throwing allies.** A `Stone` projectile thrown at targets in leash
  range on a cooldown. Squad = 2 fists + 2 stones. → User confirms ranged allies hit.

### Phase D — Vertical slice (M4)
- [ ] **Chunk 9 — 5v5 encounter + win/lose.** A `GameManager` spawns player + 4 allies vs
  5 skeletons; win when all skeletons die, lose when the player dies; result label +
  restart key. → User plays a full 5v5.
- [ ] **Chunk 10 — Juice & tuning.** Health bars / hit flashes, swing + impact effects, and
  a balance pass (damage, knockback, speeds, cooldowns, formation radius). → Iterate on
  feel with the user.

Then proceed to M5+ (§5), updating the checkboxes and §6 as you go.

## 8. Quick Reference (fill in once known)

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
