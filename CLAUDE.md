# 3D Eggs — Project Guide

Godot exe path: C:\Godot\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe

> This file is the single source of truth for the project. The conversation that
> created it will be cleared, so everything needed to continue lives here. When the
> user says **"go"**, start at **§7 Getting Started Walkthrough** and walk them
> through it one step at a time.

---

## 1. The Vision

A **3D twin-stick shooter** with:
- Large numbers of allies and enemies on screen (~50–100 units).
- **Pinball-like physics** — bouncy knockback, impacts, chaotic collisions.
- **Multiplayer** (play with friends).
- Built entirely with **free** tools.

Theme: the folder is called "3D Eggs" 🥚 — the egg theme is **not yet decided**
(are the units eggs? bouncy egg-shaped characters?). Ask the user early if it matters
for naming; otherwise use neutral names (`Player`, `Enemy`, `Ally`) and rename later.

## 2. Locked-In Decisions

| Decision | Choice | Why |
|---|---|---|
| Engine | **Godot 4.5.x — the .NET / C# build** | Free & open source; text-based scene files let Claude build & edit scenes directly; first-class headless mode for testing. |
| Language | **C#** | User has prior C#/Unity experience — it transfers directly. |
| .NET SDK | **8.0 or newer** (required by Godot 4.4+ for C#) | Godot C# packages target `net8.0`. |
| Target platform | **Desktop download** (Windows first) | Simplest path; best physics/multiplayer performance. C# desktop export is fully supported. |
| Controls | **Twin-stick:** WASD = move, mouse = aim & shoot. Gamepad sticks also supported. | Classic desktop twin-stick feel. |

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
- [ ] **M1 — Twin-stick feel ⭐ (user's top priority):** WASD movement, aim at mouse,
      shoot projectiles. Tune until it *feels good*.
      Progress: WASD movement works & confirmed by user. TODO: mouse aim, shooting, tuning.
- [ ] **M2 — One enemy:** chases the player, takes damage, dies.
- [ ] **M3 — Crowds:** spawn dozens of enemies; keep it smooth at 50–100 units.
- [ ] **M4 — Allies:** friendly units that fight alongside the player.
- [ ] **M5 — Pinball physics:** bouncy knockback, impacts, bumpers — the chaotic soul.
- [ ] **M6 — Multiplayer:** start with 2 players, server-authoritative. Hardest layer.

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

Walk through ONE step at a time. Confirm each works before the next. Adapt versions to
whatever is actually installed.

### Phase 0 — Environment (M0)
1. **Check .NET SDK:** run `dotnet --version`. If missing or < 8.0, direct the user to
   install the **.NET SDK 8.0+** from https://dotnet.microsoft.com/download (tell them
   they can open it with `! start <url>`). Re-verify.
2. **Check/Install Godot .NET:** look for an existing Godot exe; if none, direct the
   user to download **Godot 4.5.x — the ".NET" version** (NOT standard) from
   https://godotengine.org/download/windows/ . It's a zip — unzip anywhere, no
   installer. Record the exe path in §6.
3. **Verify Godot runs headless:** `& "<godot-exe>" --version`.

### Phase 1 — Create the project (M0)
4. Create a new Godot project **in this folder** (`C:\Users\benhu\OneDrive\Desktop\3D Eggs`).
   Generate `project.godot` and confirm it opens.
5. Add a `.gitignore` for Godot+C# (ignore `.godot/`, `.mono/`, `bin/`, `obj/`,
   `*.translation`). Offer to `git init` (good practice; optional).
6. Confirm the C# build works: open in editor once so Godot generates the `.sln`, then
   `dotnet build` succeeds.

### Phase 2 — Movement & the twin-stick core (M1 — the priority)
7. Build the first scene (text-edit the `.tscn` directly):
   - Ground: `StaticBody3D` + large flat mesh + collision.
   - Player: `CharacterBody3D` + capsule mesh + collision shape.
   - Camera: positioned above & angled down (top-down-ish twin-stick view).
   - A `DirectionalLight3D` so things are visible.
8. Write `Player.cs`: WASD → move the `CharacterBody3D`. Set up input actions in
   `project.godot`. → **User runs it, confirms they can move.**
9. Add mouse aiming: raycast from camera through mouse to the ground plane; rotate the
   player to face that point. → User confirms.
10. Add shooting: a `Projectile` scene spawned toward the aim direction, with a fire
    rate. → User confirms it feels responsive.
11. **Tune the feel** with the user (move speed, accel/friction, fire rate, projectile
    speed). This is the whole point of M1 — iterate here.

Then proceed down §5 (M2 onward), updating the checkboxes and §6 as you go.

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
