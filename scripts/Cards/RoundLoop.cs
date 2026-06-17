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

	// Games START PAUSED: the player sets up with their opening hand, then End Turn begins round 1.
	public Phase Current { get; private set; } = Phase.Pause;
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

	// Advance the play clock by dt seconds. Only PLAY consumes time; when it runs out the round ends:
	// the counter advances and the loop flips to PAUSE (the view redeals the hand on this edge). A
	// no-op while paused (the battlefield is frozen, so no time passes).
	public void Tick(float dt)
	{
		if (Current != Phase.Play)
			return;
		TimeLeft -= dt;
		if (TimeLeft <= 0f)
		{
			TimeLeft = 0f;
			RoundNumber++;
			SetPhase(Phase.Pause);
		}
	}

	// Dev only (Chunk 35): end the current PLAY phase early -> PAUSE WITHOUT ending the round (no
	// counter bump, no redeal — Resume continues the same round). No-op if already paused.
	public void EndPlayPhase()
	{
		if (Current == Phase.Play)
			SetPhase(Phase.Pause);
	}

	// End Turn: BEGIN the round — resume real-time play and reset the clock to a full round. No-op
	// unless paused; this is the PAUSE -> PLAY control. The round counter advances at the timeout
	// that ENDS a round (Tick), not here, so the round you start playing keeps its number.
	public void EndTurn()
	{
		if (Current != Phase.Pause)
			return;
		TimeLeft = RoundSeconds;
		SetPhase(Phase.Play);
	}

	// Dev panel resume (Chunk 35): unfreeze and continue the CURRENT round — no redeal/refill and no
	// round-number bump (unlike EndTurn). Keeps the remaining clock; if the round had already timed out
	// it gets a fresh full clock so there's something to watch. No-op unless paused.
	public void Resume()
	{
		if (Current != Phase.Pause)
			return;
		if (TimeLeft <= 0f)
			TimeLeft = RoundSeconds;
		SetPhase(Phase.Play);
	}

	// Dev panel live retune (Chunk 35): change the round length and apply it to the CURRENT play clock
	// at once — cap any remaining time at the new length — so shortening a round is visible immediately
	// instead of only taking effect next round.
	public void RetuneRoundSeconds(float seconds)
	{
		RoundSeconds = seconds;
		if (TimeLeft > seconds)
			TimeLeft = seconds;
	}

	private void SetPhase(Phase p)
	{
		if (Current == p)
			return;
		Current = p;
		PhaseChanged?.Invoke(p);
	}
}
