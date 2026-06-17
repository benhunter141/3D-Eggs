using System;
using System.Collections.Generic;

// Run structure for Slay the Eggs (M12, Chunk 38). A run is a fixed-shape SEQUENCE of rooms you
// traverse one at a time — combat rooms, a couple of event rooms, and a boss finale. CLEARING the
// current room (CompleteCurrentRoom) marks it cleared, hands back a RoomReward to offer, and
// ADVANCES the map to the next room; the player then TakeReward()s a card into the run's growing
// Collection (the deck carried between rooms, seeded from the starter deck) — or skips it.
//
// Pure model (plain C# + System.Random, no Godot types) so the whole traversal + reward cycle is
// deterministic and headless-testable. CardBattle is a thin view: it loads each room's battle deck
// from Collection, calls CompleteCurrentRoom when a room is won, and shows the reward as card
// buttons. Relics / potions layer on in Chunk 39.
public class RunMap
{
	public enum RoomType { Combat, Event, Boss }

	// One stop on the run. Type drives the reward it yields and how the view renders it.
	public class Room
	{
		public RoomType Type;
		public string Title;
		public string Description;
		public bool Cleared;

		public Room(RoomType type, string title, string description)
		{
			Type = type;
			Title = title;
			Description = description;
		}
	}

	// What clearing a room offers: a prompt plus a few card choices. The player picks ONE (added to
	// the run deck) or skips (null). Resolved once chosen/skipped so a reward can't be taken twice.
	public class RoomReward
	{
		public string Prompt;
		public List<Card> Choices;
		public bool Resolved;
		public Card Chosen;       // null once Resolved = the reward was skipped

		public RoomReward(string prompt, List<Card> choices)
		{
			Prompt = prompt;
			Choices = choices;
		}
	}

	public List<Room> Rooms { get; } = new();
	// The run's growing deck — every battle this run is built from it; rewards add to it.
	public List<Card> Collection { get; } = new();

	public int CurrentIndex { get; private set; }
	public Room Current => IsComplete ? null : Rooms[CurrentIndex];
	public bool IsComplete => CurrentIndex >= Rooms.Count;
	// 1-based "room X of N" for the HUD; reads N+1 once the run is finished, which the view guards.
	public int RoomNumber => CurrentIndex + 1;
	public int RoomCount => Rooms.Count;

	private readonly Random _rng;
	private const int CombatChoices = 3;   // cards offered after a combat/boss win
	private const int EventChoices = 2;    // a smaller boon from an event room

	// Flavour pools — drawn from at random so two runs read differently. Shape stays fixed for tests.
	private static readonly string[] CombatTitles =
		{ "Skirmish at the Coop", "The Broken Fence", "Roost Raiders", "Hatchery Hold", "The Sunken Nest" };
	private static readonly string[] EventTitles =
		{ "A Wandering Hen", "The Abandoned Roost", "An Old Captain's Tale" };
	private static readonly string[] EventPrompts =
	{
		"A wandering hen blesses your flock — take a card.",
		"You raid an abandoned roost and find supplies — take a card.",
		"A retired captain shares a battle trick — take a card.",
	};

	// Pass a seed for deterministic runs (tests); omit for a fresh random run. The starter deck
	// seeds the Collection so the first battle has the opening hand earlier chunks were tuned around.
	public RunMap(int? seed = null)
	{
		_rng = seed.HasValue ? new Random(seed.Value) : new Random();
		foreach (Card c in CardLibrary.StarterDeck())
			Collection.Add(c.Clone());
		BuildRooms();
	}

	// A fixed-shape run: combat-heavy with a couple of events and a boss finale. The SHAPE is fixed
	// (so traversal is predictable for tests); only the flavour titles are randomised.
	private void BuildRooms()
	{
		RoomType[] shape =
		{
			RoomType.Combat, RoomType.Combat, RoomType.Event,
			RoomType.Combat, RoomType.Event, RoomType.Combat, RoomType.Boss,
		};
		foreach (RoomType t in shape)
			Rooms.Add(MakeRoom(t));
	}

	private Room MakeRoom(RoomType type) => type switch
	{
		RoomType.Combat => new Room(type, Pick(CombatTitles), "A battle. Clear the field to advance."),
		RoomType.Event => new Room(type, Pick(EventTitles), "An encounter on the road."),
		_ => new Room(type, "The Egg-Tyrant", "The final battle. Best it to win the run."),
	};

	// Clear the current room: mark it cleared, build the reward to offer, and ADVANCE to the next
	// room. Returns the reward (always offers card choices). A no-op returning null once the run is
	// already complete.
	public RoomReward CompleteCurrentRoom()
	{
		if (IsComplete)
			return null;
		Room room = Rooms[CurrentIndex];
		room.Cleared = true;
		RoomReward reward = BuildReward(room);
		CurrentIndex++;
		return reward;
	}

	// Apply the player's pick: add the chosen card (a clone, so the pool isn't aliased) to the run
	// deck, or skip when chosen is null. Idempotent — a resolved reward can't be taken again.
	public void TakeReward(RoomReward reward, Card chosen)
	{
		if (reward == null || reward.Resolved)
			return;
		reward.Resolved = true;
		reward.Chosen = chosen;
		if (chosen != null)
			Collection.Add(chosen.Clone());
	}

	private RoomReward BuildReward(Room room)
	{
		switch (room.Type)
		{
			case RoomType.Event:
				return new RoomReward(Pick(EventPrompts), SampleRewards(EventChoices));
			case RoomType.Boss:
				return new RoomReward("The Egg-Tyrant falls! Claim a powerful card.", SampleRewards(CombatChoices));
			default:
				return new RoomReward("Victory! Choose a card to add to your deck.", SampleRewards(CombatChoices));
		}
	}

	// Draw `count` distinct cards from the reward pool without replacement (clones, so picks are
	// independent of the pool and of one another).
	private List<Card> SampleRewards(int count)
	{
		List<Card> pool = CardLibrary.RewardPool();
		// Partial Fisher–Yates: shuffle the first `count` slots, take them.
		int take = Math.Min(count, pool.Count);
		for (int i = 0; i < take; i++)
		{
			int j = i + _rng.Next(pool.Count - i);
			(pool[i], pool[j]) = (pool[j], pool[i]);
		}
		var picks = new List<Card>(take);
		for (int i = 0; i < take; i++)
			picks.Add(pool[i].Clone());
		return picks;
	}

	private string Pick(string[] options) => options[_rng.Next(options.Length)];
}
