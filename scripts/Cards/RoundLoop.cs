using System;

// Round loop state machine for Slay the Eggs (M12, Chunk 34). The card battle alternates between a
// PLAY phase (real time: units move and fight, RoundSeconds counting down) and a PAUSE phase (the
// battlefield is frozen so cards can be played unhurried). The clock only ticks during PLAY; when it
// reaches zero the loop flips PLAY -> PAUSE. End Turn is the PAUSE -> PLAY control: it resumes real-
// time play and resets the clock to a full round. Cards are playable in BOTH phases.
//
// Pure model — no Godot types — so the whole machine is deterministic and headless-testable. The
// CardBattle view drives Tick() from _Process, freezes the scene tree on the PhaseChanged->Pause
// edge (GetTree().Paused), and lifts the freeze on the ->Play edge. Chunk 35 adds a live dev knob to
// RoundSeconds; this model already takes a changed RoundSeconds on the next round.
public class RoundLoop
{
	public enum Phase { Play, Pause }

	public Phase Current { get; private set; } = Phase.Play;
	// Length of a PLAY phase. Mutable so the Chunk-35 dev panel can retune it live (applies next round).
	public float RoundSeconds { get; set; }
	// Seconds left in the current PLAY phase (clamped at 0; frozen while paused).
	public float TimeLeft { get; private set; }
	// 1-based count of PLAY phases entered — bumps each time End Turn starts a new round.
	public int RoundNumber { get; private set; } = 1;

	// Raised on every phase change. The view freezes / unfreezes the battlefield on this edge.
	public event Action<Phase> PhaseChanged;

	public RoundLoop(float roundSeconds = 15f)
	{
		RoundSeconds = roundSeconds;
		TimeLeft = roundSeconds;
	}

	// Cards may be played in either phase — live during PLAY, unhurried during PAUSE.
	public bool CardsPlayable => true;

	// Advance the play clock by dt seconds. Only PLAY consumes time; when it runs out the loop flips
	// to PAUSE. A no-op while paused (the battlefield is frozen, so no time passes).
	public void Tick(float dt)
	{
		if (Current != Phase.Play)
			return;
		TimeLeft -= dt;
		if (TimeLeft <= 0f)
		{
			TimeLeft = 0f;
			SetPhase(Phase.Pause);
		}
	}

	// End the current PLAY phase early ("pause now to play cards"), flipping straight to PAUSE.
	// No-op if already paused.
	public void EndPlayPhase()
	{
		if (Current == Phase.Play)
			SetPhase(Phase.Pause);
	}

	// End Turn: resume real-time play and reset the clock to a full round. No-op unless paused —
	// this is the PAUSE -> PLAY control only.
	public void EndTurn()
	{
		if (Current != Phase.Pause)
			return;
		RoundNumber++;
		TimeLeft = RoundSeconds;
		SetPhase(Phase.Play);
	}

	private void SetPhase(Phase p)
	{
		if (Current == p)
			return;
		Current = p;
		PhaseChanged?.Invoke(p);
	}
}
