using System;
using System.Collections.Generic;

// Run structure for Slay the Eggs (M12, Chunk 38). A RUN is a sequence of ROOMS the player traverses
// one at a time: an opening COMBAT, the odd ELITE (a tougher fight), a few EVENT rooms (non-combat
// beats), and a final BOSS. Completing a fight room (Combat / Elite / Boss) OFFERS A REWARD — a small
// set of card choices, one of which the player adds to the run deck. Event rooms advance the map
// without a card reward. Relics & potions (the non-card drops) land in Chunk 39.
//
// Pure model (no Godot types) so the whole run is deterministic (seedable) and headless-testable. The
// run-long Deck lives ON the map (reward picks go straight into it); the view glues map + deck + battle.

// The kind of room sitting at a step of the run. Combat/Elite/Boss are fights (they drop card rewards);
// Event is a non-combat beat (no card reward in Chunk 38 — richer event effects come with Chunk 39).
public enum RoomKind { Combat, Elite, Event, Boss }

// One step of the run. Immutable kind; Completed flips when the player clears it (set by RunMap).
public class Room
{
	public RoomKind Kind { get; }
	public bool Completed { get; internal set; }

	public Room(RoomKind kind) => Kind = kind;
}

// What a completed room hands back. Fight rooms offer CardChoices (pick one with RunMap.ChooseCard to
// add it to the deck); Event rooms return an empty reward (IsCardReward == false). Always non-null.
public class RoomReward
{
	public RoomKind FromKind { get; }
	public IReadOnlyList<Card> CardChoices { get; }

	// True when this reward actually offers cards to pick from (fights do, events don't).
	public bool IsCardReward => CardChoices.Count > 0;

	public RoomReward(RoomKind fromKind, IReadOnlyList<Card> choices)
	{
		FromKind = fromKind;
		CardChoices = choices ?? Array.Empty<Card>();
	}
}

public class RunMap
{
	public List<Room> Rooms { get; } = new();
	// Index of the room the player is currently on; == Rooms.Count once the run is finished.
	public int CurrentIndex { get; private set; }

	// The deck this run builds. Reward cards chosen via ChooseCard are added here.
	public Deck Deck { get; }

	private readonly Random _rng;
	private readonly int _rewardChoices;

	// length = number of rooms in the run; rewardChoices = how many cards a fight reward offers.
	// Pass a seed for a deterministic layout + reward draws (tests); omit for a fresh random run.
	public RunMap(int? seed = null, int length = 8, int rewardChoices = 3, Deck deck = null)
	{
		_rng = seed.HasValue ? new Random(seed.Value) : new Random();
		_rewardChoices = Math.Max(1, rewardChoices);
		Deck = deck ?? new Deck(seed);
		Generate(Math.Max(1, length));
	}

	// The room the player is on now, or null once the run is finished.
	public Room Current => IsComplete ? null : Rooms[CurrentIndex];
	// True once every room has been cleared (CurrentIndex walked past the end).
	public bool IsComplete => CurrentIndex >= Rooms.Count;
	public int RoomsRemaining => Math.Max(0, Rooms.Count - CurrentIndex);

	// Lay out the run: the first room is always a gentle Combat opener and the last is the Boss; the
	// middle is a shuffled mix of combats, a couple of events, and the occasional elite. Deterministic
	// under the seed so a given seed always produces the same map.
	private void Generate(int length)
	{
		Rooms.Clear();
		CurrentIndex = 0;

		if (length == 1)
		{
			Rooms.Add(new Room(RoomKind.Boss));
			return;
		}

		Rooms.Add(new Room(RoomKind.Combat));          // opener

		int middle = length - 2;                        // rooms between opener and boss
		var pool = new List<RoomKind>();
		int events = Math.Max(1, middle / 3);           // at least one event when there's a middle
		int elites = Math.Max(0, (middle - events) / 3);
		for (int i = 0; i < events; i++) pool.Add(RoomKind.Event);
		for (int i = 0; i < elites; i++) pool.Add(RoomKind.Elite);
		while (pool.Count < middle) pool.Add(RoomKind.Combat);
		for (int i = pool.Count - 1; i > 0; i--)        // shuffle the middle
		{
			int j = _rng.Next(i + 1);
			(pool[i], pool[j]) = (pool[j], pool[i]);
		}
		foreach (RoomKind k in pool) Rooms.Add(new Room(k));

		Rooms.Add(new Room(RoomKind.Boss));             // capstone
	}

	// Complete the current room: mark it cleared, build its reward, and advance to the next room.
	// Always returns a non-null reward (empty CardChoices for events). Safe past the end of the run
	// (returns an empty reward and does nothing).
	public RoomReward CompleteRoom()
	{
		if (IsComplete)
			return new RoomReward(RoomKind.Event, Array.Empty<Card>());

		Room room = Rooms[CurrentIndex];
		room.Completed = true;
		RoomReward reward = BuildReward(room.Kind);
		CurrentIndex++;
		return reward;
	}

	// Pick a few distinct cards from the reward pool for a fight room; events offer none.
	private RoomReward BuildReward(RoomKind kind)
	{
		if (kind == RoomKind.Event)
			return new RoomReward(kind, Array.Empty<Card>());

		List<Card> pool = CardLibrary.RewardPool();
		var choices = new List<Card>();
		for (int i = 0; i < _rewardChoices && pool.Count > 0; i++)
		{
			int j = _rng.Next(pool.Count);
			choices.Add(pool[j].Clone());
			pool.RemoveAt(j);                           // distinct choices (no duplicate offers)
		}
		return new RoomReward(kind, choices);
	}

	// Add a chosen reward card to the run deck. Returns false (and changes nothing) unless the card is
	// one of the reward's offered choices — so only a genuinely-offered card can enter the deck.
	public bool ChooseCard(RoomReward reward, Card card)
	{
		if (reward == null || card == null || !Contains(reward.CardChoices, card))
			return false;
		Deck.AddCard(card);
		return true;
	}

	private static bool Contains(IReadOnlyList<Card> list, Card card)
	{
		for (int i = 0; i < list.Count; i++)
			if (ReferenceEquals(list[i], card)) return true;
		return false;
	}
}
