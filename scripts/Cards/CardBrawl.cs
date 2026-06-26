using Godot;
using System;
using System.Collections.Generic;

// Co-op Card Brawl front-end (M15, rebuilt). Two weak eggs (P1 keyboard+mouse, P2 gamepad) share ONE
// hand of cards but each spends their OWN energy. The battle runs the M12 RoundLoop: it starts PAUSED so
// the players spend energy to arm up / spawn soldiers, then a 3-2-1 countdown leads into a 15 s survival
// WAVE — the WaveManager's foes (staged inert during the pause so they can be seen + countered) spring to
// life. At the timeout it auto-pauses, redeals a fresh hand, refills each player's energy, and queues the
// next (harder) wave. Lose only when BOTH eggs fall (GameManager.RequireAllPlayersDead + DisableWin).
//
// PLAY FLOW (the redesign): a card is never resolved blind. You pick a card, THEN choose WHERE it lands —
//   · a weapon / ability card attaches to one of YOUR eggs (your hero or a soldier you control);
//   · a soldier card attaches to a free cell in one of the four cardinal directions next to an egg you
//     control (a SquadGrid placement — you build a little tile-squad, and it can't overlap a taken space).
// Confirming spawns/equips immediately (so the squad visibly forms while frozen), marks the card SELECTED
// (it stays in hand, the energy is reserved) and is reversible: click a selected card to UNSELECT it
// (refund + undo) any time before End Turn. End Turn locks the selections in and runs the countdown.
//
// Targeting input mirrors the two devices: P1 clicks cards + on-field markers with the mouse (right-click
// cancels); P2 moves a cursor with the d-pad/stick over the hand or the markers and confirms with A
// (B cancels), Start ends the turn.
public partial class CardBrawl : Node3D
{
	[Export] public int HandSize = 5;
	[Export] public int BaseEnergy = 5;          // flat energy granted to EACH player every wave
	[Export] public float RoundSeconds = 15f;    // length of a survival wave before it auto-pauses
	[Export] public float CountdownSeconds = 3f;  // 3-2-1 before the wave starts
	[Export] public float SpawnHeight = 1.0f;     // ground height soldiers/markers sit at
	[Export] public string SoldierScenePath = "res://scenes/Captain.tscn";   // a subordinate egg (AI-driven)

	private readonly Deck _deck = new();
	private RoundLoop _round;
	private WaveManager _waves;
	private Node _units;                          // Pausable container the eggs + spawned enemies live in

	private readonly List<Player> _players = new();             // [0] = P1, [1] = P2 (by control scheme)
	private readonly EnergyPool[] _energies = new EnergyPool[2];  // per-player energy (the redesign)
	private readonly List<Player>[] _subordinates =              // soldier eggs each player has placed
		{ new List<Player>(), new List<Player>() };

	private PackedScene _soldierScene;

	// ── Selection / targeting state ────────────────────────────────────────────────────────────────────
	// A resolved-but-reversible play: the card stays in hand marked SELECTED until End Turn.
	private class Selection { public int Player; public Card Card; public Action Undo; }
	private readonly Dictionary<int, Selection> _selections = new();   // keyed by hand index (stable in a pause)

	// A candidate target while a card is armed: where it would land + the change to make (returns its undo).
	private struct Candidate { public Vector3 World; public Func<Action> Apply; }

	private class Armed
	{
		public int Player;
		public int HandIndex;
		public Card Card;
		public List<Candidate> Candidates = new();
		public List<Button> Markers = new();
	}
	private readonly Armed[] _armed = new Armed[2];   // one in-progress targeting per player

	// ── Countdown ──────────────────────────────────────────────────────────────────────────────────────
	private bool _countingDown;
	private float _countdownLeft;

	private int _previewedWave;                   // foes-first: highest wave already staged in a pause

	// ── UI ───────────────────────────────────────────────────────────────────────────────────────────
	private Control _root;
	private Label _phaseLabel, _waveLabel, _promptLabel, _countdownLabel;
	private Label _energyP1, _energyP2, _drawCount, _discardCount;
	private HBoxContainer _handBox;
	private Button _endTurnButton;
	private readonly List<Button> _handButtons = new();
	private HBoxContainer _abilityBarP1, _abilityBarP2;   // each hero's granted-ability bar (hotkey + cooldown)

	// Ability-slot hotkey glyphs per player (P1 keyboard number keys; P2 gamepad buttons — see Player).
	private static readonly string[] P1Keys = { "1", "2", "3", "4" };
	private static readonly string[] P2Keys = { "A", "Y", "R1", "↑" };

	// P2 gamepad edge-detect state (device 0).
	private int _p2Cursor;
	private bool _navLeftPrev, _navRightPrev, _confirmPrev, _cancelPrev, _endTurnPrev;
	private bool _rmbPrev;                          // P1 right-click cancel edge
	private const JoyButton ConfirmButton = JoyButton.A;
	private const JoyButton CancelButton  = JoyButton.B;
	private const JoyButton EndTurnButton = JoyButton.Start;

	private static readonly Color CursorTint  = new(0.7f, 1.0f, 0.7f);
	private static readonly Color P1Color     = new(0.55f, 0.78f, 1.0f);   // mouse player
	private static readonly Color P2Color     = new(1.0f, 0.78f, 0.5f);    // gamepad player

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;   // keep driving the UI while the world is frozen

		// Find the two hero eggs (assigned by control scheme; fall back to scene order). Their parent is
		// the Pausable Units container soldiers + wave enemies drop into.
		Player p1 = null, p2 = null;
		var ordered = new List<Player>();
		foreach (Node n in GetTree().GetNodesInGroup("player"))
			if (n is Player p)
			{
				ordered.Add(p);
				if (p.Control == Player.ControlScheme.Gamepad) p2 ??= p;
				else p1 ??= p;
			}
		if (p1 == null && ordered.Count > 0) p1 = ordered[0];
		if (p2 == null) foreach (Player p in ordered) if (p != p1) { p2 = p; break; }
		_players.Add(p1); _players.Add(p2);
		_units = p1 != null ? p1.GetParent() : (Node)this;

		_soldierScene = GD.Load<PackedScene>(SoldierScenePath);

		_waves = GetNodeOrNull<WaveManager>("WaveManager");
		if (_waves == null) { _waves = new WaveManager { Name = "WaveManager" }; AddChild(_waves); }
		BuildWaveTable();

		_round = new RoundLoop(RoundSeconds);
		_round.PhaseChanged += OnPhaseChanged;
		_energies[0] = new EnergyPool(BaseEnergy, 0);
		_energies[1] = new EnergyPool(BaseEnergy, 0);
		_deck.LoadStarter(CardLibrary.BrawlDeck());

		BuildUi();

		RefillEnergy();
		_deck.Draw(HandSize);
		OnPhaseChanged(_round.Current);   // sync the opening PAUSED state
		Refresh();
	}

	public override void _ExitTree() => GetTree().Paused = false;

	public override void _Process(double delta)
	{
		UpdateAbilityBars();   // keep both heroes' ability bars (hotkey + cooldown) live in every phase

		if (_countingDown)
		{
			_countdownLeft -= (float)delta;
			if (_countdownLabel != null)
				_countdownLabel.Text = _countdownLeft > 0f ? Mathf.CeilToInt(_countdownLeft).ToString() : "FIGHT!";
			if (_countdownLeft <= 0f)
				StartWave();
			PollGamepad();
			return;
		}

		RoundLoop.Phase before = _round.Current;
		_round.Tick((float)delta);
		if (before == RoundLoop.Phase.Play && _round.Current == RoundLoop.Phase.Pause)
			OnRoundTimeout();
		UpdatePhaseHud();

		PositionMarkers();
		PollMouseCancel();
		PollGamepad();
	}

	// ── Per-player gamepad hand/target control (device 0) ──────────────────────────────────────────────
	private void PollGamepad()
	{
		float x = Input.GetJoyAxis(0, JoyAxis.LeftX);
		bool left    = Input.IsJoyButtonPressed(0, JoyButton.DpadLeft)  || x < -0.5f;
		bool right   = Input.IsJoyButtonPressed(0, JoyButton.DpadRight) || x >  0.5f;
		bool confirm = Input.IsJoyButtonPressed(0, ConfirmButton);
		bool cancel  = Input.IsJoyButtonPressed(0, CancelButton);
		bool endTurn = Input.IsJoyButtonPressed(0, EndTurnButton);

		if (!_countingDown && _round.Current == RoundLoop.Phase.Pause)
		{
			Armed a = _armed[1];
			if (a != null)   // P2 is choosing a target
			{
				if (a.Markers.Count > 0)
				{
					if (left  && !_navLeftPrev)  MoveP2Cursor(-1, a.Markers.Count);
					if (right && !_navRightPrev) MoveP2Cursor(+1, a.Markers.Count);
					if (confirm && !_confirmPrev) ConfirmCandidate(a, _p2Cursor);
				}
				if (cancel && !_cancelPrev) DisarmPlayer(1);
			}
			else             // P2 is browsing the hand
			{
				if (_handButtons.Count > 0)
				{
					if (left  && !_navLeftPrev)  MoveP2Cursor(-1, _handButtons.Count);
					if (right && !_navRightPrev) MoveP2Cursor(+1, _handButtons.Count);
					if (confirm && !_confirmPrev) ArmOrToggle(1, _p2Cursor);
				}
			}
			if (endTurn && !_endTurnPrev) RequestEndTurn();
		}

		_navLeftPrev = left; _navRightPrev = right; _confirmPrev = confirm;
		_cancelPrev = cancel; _endTurnPrev = endTurn;
	}

	private void MoveP2Cursor(int step, int count)
	{
		_p2Cursor = Mathf.PosMod(_p2Cursor + step, Mathf.Max(1, count));
		ApplyCursorTint();
	}

	// P1 right-click cancels an in-progress targeting.
	private void PollMouseCancel()
	{
		bool rmb = Input.IsMouseButtonPressed(MouseButton.Right);
		if (rmb && !_rmbPrev && _armed[0] != null)
			DisarmPlayer(0);
		_rmbPrev = rmb;
	}

	// ── Card selection / targeting ─────────────────────────────────────────────────────────────────────

	// P1 clicks a hand card with the mouse.
	private void OnHandPressed(int handIndex)
	{
		if (_countingDown || _round.Current != RoundLoop.Phase.Pause)
			return;
		if (_armed[0] != null) DisarmPlayer(0);   // clicking the hand abandons an in-progress aim
		ArmOrToggle(0, handIndex);
	}

	// Arm a fresh card for targeting, or — if it's already SELECTED — unselect it (refund + undo).
	private void ArmOrToggle(int player, int handIndex)
	{
		if (handIndex < 0 || handIndex >= _deck.Hand.Count)
			return;
		if (_selections.ContainsKey(handIndex))
		{
			Unselect(handIndex);
			return;
		}

		Card card = _deck.Hand[handIndex];
		if (!_energies[player].CanAfford(card))
		{
			SetPrompt($"P{player + 1}: not enough energy for {card.Title}.");
			return;
		}

		List<Candidate> candidates = BuildCandidates(player, card);
		if (candidates.Count == 0)
		{
			SetPrompt(card.Buff == Card.BuffKind.Soldier
				? $"P{player + 1}: no free space to place a soldier (need a controlled egg)."
				: $"P{player + 1}: no egg to attach {card.Title} to.");
			return;
		}

		DisarmPlayer(player);
		var armed = new Armed { Player = player, HandIndex = handIndex, Card = card, Candidates = candidates };
		_armed[player] = armed;
		BuildMarkers(armed);
		SetPrompt(card.Buff == Card.BuffKind.Soldier
			? $"P{player + 1}: choose a spot to place {card.Title}."
			: $"P{player + 1}: choose an egg for {card.Title}.");
		Refresh();
	}

	private void Unselect(int handIndex)
	{
		Selection sel = _selections[handIndex];
		sel.Undo?.Invoke();
		_energies[sel.Player].Add(sel.Card.EnergyCost);   // refund the reserved energy
		_selections.Remove(handIndex);
		SetPrompt($"P{sel.Player + 1}: unselected {sel.Card.Title}.");
		Refresh();
	}

	// Resolve the armed card on the chosen candidate: perform the change, reserve energy, mark SELECTED.
	private void ConfirmCandidate(Armed a, int index)
	{
		if (index < 0 || index >= a.Candidates.Count)
			return;
		Action undo = a.Candidates[index].Apply();
		_energies[a.Player].Spend(a.Card);
		_selections[a.HandIndex] = new Selection { Player = a.Player, Card = a.Card, Undo = undo };
		SetPrompt($"P{a.Player + 1}: selected {a.Card.Title} (click it to unselect).");
		DisarmPlayer(a.Player);
		Refresh();
	}

	private void OnMarkerClicked(int player, int index)
	{
		if (_armed[player] != null)
			ConfirmCandidate(_armed[player], index);
	}

	private void DisarmPlayer(int player)
	{
		Armed a = _armed[player];
		if (a == null)
			return;
		foreach (Button m in a.Markers)
			if (IsInstanceValid(m)) m.QueueFree();
		_armed[player] = null;
		if (player == 1) { _p2Cursor = 0; ApplyCursorTint(); }
	}

	// Candidate targets for `card` played by `player`: weapon/ability -> the player's controlled eggs;
	// soldier -> the valid 4-adjacent placement cells around them.
	private List<Candidate> BuildCandidates(int player, Card card)
	{
		var list = new List<Candidate>();
		if (card.Buff == Card.BuffKind.Soldier)
		{
			var anchors = new List<Vector2I>();
			foreach (Player egg in ControlledEggs(player))
				anchors.Add(SquadGrid.WorldToCell(egg.GlobalPosition));
			HashSet<Vector2I> occupied = AllOccupiedCells();
			foreach (Vector2I cell in SquadGrid.ValidPlacements(anchors, occupied))
			{
				Vector2I c = cell;   // capture
				Vector3 world = SquadGrid.CellToWorld(c, SpawnHeight);
				list.Add(new Candidate { World = world + Vector3.Up * 0.4f, Apply = () => SpawnSoldier(player, c, card) });
			}
		}
		else   // Weapon / Ability
		{
			foreach (Player egg in ControlledEggs(player))
			{
				Player e = egg;   // capture
				list.Add(new Candidate { World = e.GlobalPosition + Vector3.Up * 1.5f, Apply = () => ApplyBuff(e, card) });
			}
		}
		return list;
	}

	// Equip a weapon / grant an ability on `egg`, returning the undo that restores its prior loadout.
	private Action ApplyBuff(Player egg, Card card)
	{
		if (card.Buff == Card.BuffKind.Ability)
		{
			Player.AbilityType prev = egg.CurrentAbility;
			egg.GrantAbility(card.BuffAbility);
			return () => { if (IsInstanceValid(egg)) egg.GrantAbility(prev); };
		}
		Player.WeaponType prevW = egg.CurrentWeapon;
		egg.EquipWeapon(card.BuffWeapon);
		return () => { if (IsInstanceValid(egg)) egg.EquipWeapon(prevW); };
	}

	// Spawn a subordinate egg (AI-driven) at the chosen grid cell on the player's team; undo frees it.
	private Action SpawnSoldier(int player, Vector2I cell, Card card)
	{
		Player hero = _players[player];
		var egg = _soldierScene.Instantiate<Player>();   // a subordinate egg (AI-driven)
		egg.Control = Player.ControlScheme.Ai;
		egg.StartUnarmed = true;
		egg.ShowGameOverOnDeath = false;
		egg.Team = hero != null ? hero.Team : Unit.TeamId.Player;
		_units.AddChild(egg);
		egg.GlobalPosition = SquadGrid.CellToWorld(cell, SpawnHeight);
		_subordinates[player].Add(egg);
		return () =>
		{
			_subordinates[player].Remove(egg);
			if (IsInstanceValid(egg)) egg.QueueFree();
		};
	}

	// The eggs a player controls right now: their hero (if alive) + their living soldiers.
	private IEnumerable<Player> ControlledEggs(int player)
	{
		Player hero = _players[player];
		if (hero != null && IsInstanceValid(hero) && !hero.IsDead)
			yield return hero;
		// Prune dead/freed soldiers as we go.
		List<Player> subs = _subordinates[player];
		for (int i = subs.Count - 1; i >= 0; i--)
		{
			Player s = subs[i];
			if (s == null || !IsInstanceValid(s) || s.IsDead)
				subs.RemoveAt(i);
			else
				yield return s;
		}
	}

	// Every cell currently occupied by a standing unit (either team) — a soldier can't be placed onto one.
	private HashSet<Vector2I> AllOccupiedCells()
	{
		var set = new HashSet<Vector2I>();
		foreach (Node n in GetTree().GetNodesInGroup("units"))
			if (n is Unit u && IsInstanceValid(u) && !u.IsDead)
				set.Add(SquadGrid.WorldToCell(u.GlobalPosition));
		return set;
	}

	// ── Round flow ─────────────────────────────────────────────────────────────────────────────────────

	// End Turn: cancel any in-progress aim, lock the selections, run the 3-2-1, then start the wave.
	private void RequestEndTurn()
	{
		if (_countingDown || _round.Current != RoundLoop.Phase.Pause)
			return;
		DisarmPlayer(0);
		DisarmPlayer(1);
		_countingDown = true;
		_countdownLeft = Mathf.Max(0.1f, CountdownSeconds);
		if (_countdownLabel != null) { _countdownLabel.Visible = true; _countdownLabel.Text = Mathf.CeilToInt(_countdownLeft).ToString(); }
		if (_endTurnButton != null) { _endTurnButton.Disabled = true; _endTurnButton.Text = "Starting…"; }
		Refresh();
	}

	// Countdown finished: selections lock in (undo discarded), the wave begins (foes unfreeze).
	private void StartWave()
	{
		_countingDown = false;
		_selections.Clear();
		if (_countdownLabel != null) _countdownLabel.Visible = false;
		_round.EndTurn();   // -> PLAY (OnPhaseChanged unfreezes the staged foes)
		Refresh();
	}

	private void OnEndTurnButtonPressed() => RequestEndTurn();

	// Foes-first: stage the coming wave DURING the pause so both eggs can SEE the threat and counter it.
	// Author the brawl's per-wave bestiary (M17). Populated incrementally across Chunks 84–93 as each
	// foe type lands; Chunk 93 finalizes the full difficulty ramp. For now the opener is a slow Zombie
	// horde (Chunk 84), stiffened with a couple of Skeletons as it escalates. Waves past the table clamp
	// to the last (hardest) row, so the run keeps pressuring until the ramp is authored.
	private void BuildWaveTable()
	{
		if (_waves == null) return;
		var zombie = GD.Load<PackedScene>("res://scenes/Zombie.tscn");
		var dog = GD.Load<PackedScene>("res://scenes/Dog.tscn");
		var skeleton = GD.Load<PackedScene>("res://scenes/Skeleton.tscn");
		var goblin = GD.Load<PackedScene>("res://scenes/Goblin.tscn");

		_waves.WaveTable.Clear();
		// Wave 1 — a pure slow zombie horde: the gentle opener.
		_waves.WaveTable.Add(new WaveManager.WaveComposition(WaveManager.Formation.Spread).Add(zombie, 5));
		// Wave 2 — a fast War Dog pack rushes in among the shamblers (frail but quick — close the gap).
		_waves.WaveTable.Add(new WaveManager.WaveComposition(WaveManager.Formation.Spread).Add(zombie, 4).Add(dog, 3));
		// Wave 3 — a bigger horde, a larger dog pack, and a couple of tougher Skeletons mixed in.
		_waves.WaveTable.Add(new WaveManager.WaveComposition(WaveManager.Formation.Spread).Add(zombie, 6).Add(dog, 4).Add(skeleton, 3));
		// Wave 4 — tier-2 arrives: darting Goblin Cutters with crude blades flank the horde, Skeletons anchoring.
		_waves.WaveTable.Add(new WaveManager.WaveComposition(WaveManager.Formation.Spread).Add(goblin, 4).Add(skeleton, 4).Add(zombie, 4));
	}

	private void SpawnPreviewWave()
	{
		int wave = _round.RoundNumber;
		if (_waves == null || _units == null || wave == _previewedWave)
			return;
		_previewedWave = wave;
		_waves.SpawnWave(wave, _units, ArenaCenter());
	}

	// A wave survived: discard the spent hand, refill both players' energy, deal a fresh hand. Selections
	// were already locked at StartWave, so clear the per-pause targeting state for the new hand. Abilities
	// are granted PER TURN — wipe each controlled egg's bar so a spell must be replayed for the next wave
	// (weapons + soldiers persist).
	private void OnRoundTimeout()
	{
		for (int p = 0; p < 2; p++)
			foreach (Player egg in ControlledEggs(p))
				egg.ClearAbilities();

		_deck.DiscardHand();
		RefillEnergy();
		_deck.Draw(HandSize);
		_selections.Clear();
		DisarmPlayer(0);
		DisarmPlayer(1);
		Refresh();
	}

	private void RefillEnergy()
	{
		_energies[0].Refill(0);
		_energies[1].Refill(0);
	}

	private Vector3 ArenaCenter()
	{
		Vector3 sum = Vector3.Zero;
		int n = 0;
		foreach (Player p in _players)
			if (p != null && IsInstanceValid(p)) { sum += p.GlobalPosition; n++; }
		Vector3 c = n > 0 ? sum / n : Vector3.Zero;
		c.Y = 0f;
		return c;
	}

	// Freeze while paused, run while playing. This node + the UI are Always, so cards stay interactive.
	private void OnPhaseChanged(RoundLoop.Phase phase)
	{
		bool paused = phase == RoundLoop.Phase.Pause;
		GetTree().Paused = paused;
		if (paused)
			SpawnPreviewWave();   // freeze FIRST, then stage the coming wave so it's inert from frame 0
		if (_endTurnButton != null)
		{
			_endTurnButton.Disabled = !paused || _countingDown;
			_endTurnButton.Text = paused ? "End Turn  ▶" : "Wave in progress…";
		}
		UpdatePhaseHud();
		Refresh();
	}

	// ── UI (built in code) ───────────────────────────────────────────────────────────────────────────
	private void BuildUi()
	{
		var ui = new CanvasLayer { Name = "Ui" };
		AddChild(ui);
		_root = new Control();
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.MouseFilter = Control.MouseFilterEnum.Ignore;
		// Shared toon UI theme (Chunk 81): the End-Turn button + any un-overridden Control inherit the
		// cream-on-dark cel look. Cards / markers / ability slots set their own styleboxes (those win).
		Theme toonTheme = GD.Load<Theme>("res://scenes/Shared/ToonTheme.tres");
		if (toonTheme != null) _root.Theme = toonTheme;
		ui.AddChild(_root);

		_phaseLabel = MakeLabel(_root, Control.LayoutPreset.TopWide, 16, 56, 26, new Color(1f, 0.85f, 0.55f));
		_phaseLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_waveLabel = MakeLabel(_root, Control.LayoutPreset.TopWide, 60, 92, 18, new Color(0.92f, 0.88f, 0.72f));
		_waveLabel.HorizontalAlignment = HorizontalAlignment.Center;

		_energyP1 = MakeLabel(_root, Control.LayoutPreset.TopLeft, 20, 48, 22, P1Color);
		_energyP1.OffsetLeft = 24; _energyP1.OffsetRight = 360;
		_energyP2 = MakeLabel(_root, Control.LayoutPreset.TopRight, 20, 48, 22, P2Color);
		_energyP2.OffsetLeft = -360; _energyP2.OffsetRight = -24;
		_energyP2.HorizontalAlignment = HorizontalAlignment.Right;

		_promptLabel = MakeLabel(_root, Control.LayoutPreset.TopWide, 96, 122, 16, new Color(0.82f, 0.85f, 0.92f));
		_promptLabel.HorizontalAlignment = HorizontalAlignment.Center;

		_drawCount = MakeLabel(_root, Control.LayoutPreset.BottomLeft, -64, -24, 16, new Color(0.7f, 0.8f, 1f));
		_drawCount.OffsetLeft = 24; _drawCount.OffsetRight = 200;
		_discardCount = MakeLabel(_root, Control.LayoutPreset.BottomRight, -64, -24, 16, new Color(0.7f, 0.8f, 1f));
		_discardCount.OffsetLeft = -200; _discardCount.OffsetRight = -24;
		_discardCount.HorizontalAlignment = HorizontalAlignment.Right;

		_countdownLabel = MakeLabel(_root, Control.LayoutPreset.Center, -80, 80, 110, new Color(1f, 0.9f, 0.4f));
		_countdownLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_countdownLabel.VerticalAlignment = VerticalAlignment.Center;
		_countdownLabel.SetAnchorsPreset(Control.LayoutPreset.Center);
		_countdownLabel.OffsetLeft = -200; _countdownLabel.OffsetRight = 200;
		_countdownLabel.OffsetTop = -100; _countdownLabel.OffsetBottom = 100;
		_countdownLabel.Visible = false;

		_handBox = new HBoxContainer();
		_handBox.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_handBox.OffsetLeft = -380; _handBox.OffsetRight = 380;
		_handBox.OffsetTop = -226; _handBox.OffsetBottom = -68;
		_handBox.AddThemeConstantOverride("separation", 8);
		_handBox.Alignment = BoxContainer.AlignmentMode.Center;
		_root.AddChild(_handBox);

		_endTurnButton = new Button { CustomMinimumSize = new Vector2(280, 50), Text = "End Turn  ▶" };
		_endTurnButton.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_endTurnButton.OffsetLeft = -140; _endTurnButton.OffsetRight = 140;
		_endTurnButton.OffsetTop = -58; _endTurnButton.OffsetBottom = -8;
		_endTurnButton.AddThemeFontSizeOverride("font_size", 20);
		_endTurnButton.Pressed += OnEndTurnButtonPressed;
		_root.AddChild(_endTurnButton);

		// Ability bars sit in the bottom corners (P1 left, P2 right), visible during the wave so each player
		// can see their hotkeys + cooldowns while casting. Built empty; UpdateAbilityBars fills them per frame.
		_abilityBarP1 = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_abilityBarP1.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		_abilityBarP1.OffsetLeft = 24; _abilityBarP1.OffsetRight = 360;
		_abilityBarP1.OffsetTop = -120; _abilityBarP1.OffsetBottom = -72;
		_abilityBarP1.AddThemeConstantOverride("separation", 6);
		_root.AddChild(_abilityBarP1);

		_abilityBarP2 = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		_abilityBarP2.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		_abilityBarP2.OffsetLeft = -360; _abilityBarP2.OffsetRight = -24;
		_abilityBarP2.OffsetTop = -120; _abilityBarP2.OffsetBottom = -72;
		_abilityBarP2.Alignment = BoxContainer.AlignmentMode.End;
		_abilityBarP2.AddThemeConstantOverride("separation", 6);
		_root.AddChild(_abilityBarP2);
	}

	// Sync both ability bars to the heroes' current bars each frame (cheap — at most 4 slots each).
	private void UpdateAbilityBars()
	{
		SyncAbilityBar(_abilityBarP1, 0);
		SyncAbilityBar(_abilityBarP2, 1);
	}

	private void SyncAbilityBar(HBoxContainer bar, int player)
	{
		if (bar == null)
			return;
		// Rebuild from scratch (immediate Free, so no mid-frame duplicates) — the counts are tiny.
		foreach (Node c in bar.GetChildren()) { bar.RemoveChild(c); c.Free(); }

		Player egg = _players[player];
		if (egg == null || !IsInstanceValid(egg))
			return;
		Color tint = player == 0 ? P1Color : P2Color;
		string[] keys = player == 0 ? P1Keys : P2Keys;
		for (int i = 0; i < egg.AbilityCount; i++)
		{
			string hot = i < keys.Length ? keys[i] : "•";
			bar.AddChild(MakeAbilitySlot(hot, egg.AbilityTypeAt(i).ToString(),
				egg.AbilityReadyAt(i), egg.AbilityCooldownLeft(i), tint));
		}
	}

	// One ability-bar tile: "[1] Fireball" over READY / "2.3s", border tinted by player and dimmed on cooldown.
	private static Control MakeAbilitySlot(string hotkey, string name, bool ready, float cdLeft, Color tint)
	{
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(74, 44), MouseFilter = Control.MouseFilterEnum.Ignore };
		var sb = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.14f, ready ? 0.95f : 0.6f) };
		sb.SetBorderWidthAll(2);
		sb.BorderColor = ready ? tint : new Color(tint.R, tint.G, tint.B, 0.4f);
		sb.SetCornerRadiusAll(5);
		sb.SetContentMarginAll(3);
		panel.AddThemeStyleboxOverride("panel", sb);

		var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		v.AddThemeConstantOverride("separation", 0);
		panel.AddChild(v);

		var top = new Label { Text = $"[{hotkey}] {name}", MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
		top.AddThemeFontSizeOverride("font_size", 11);
		top.AddThemeColorOverride("font_color", ready ? new Color(0.95f, 0.95f, 0.98f) : new Color(0.62f, 0.62f, 0.68f));
		v.AddChild(top);

		var bottom = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
		bottom.AddThemeFontSizeOverride("font_size", 12);
		if (ready) { bottom.Text = "READY"; bottom.AddThemeColorOverride("font_color", tint); }
		else { bottom.Text = $"{cdLeft:0.0}s"; bottom.AddThemeColorOverride("font_color", new Color(0.88f, 0.62f, 0.4f)); }
		v.AddChild(bottom);
		return panel;
	}

	private static Label MakeLabel(Control parent, Control.LayoutPreset preset, float top, float bottom,
		int fontSize, Color color)
	{
		var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
		l.SetAnchorsPreset(preset);
		l.OffsetTop = top; l.OffsetBottom = bottom;
		l.AddThemeFontSizeOverride("font_size", fontSize);
		l.AddThemeColorOverride("font_color", color);
		parent.AddChild(l);
		return l;
	}

	private void Refresh()
	{
		if (_handBox == null)
			return;

		// Hide the hand + End-Turn during the live wave so their (disabled) buttons can't swallow P1's
		// attack clicks; they reappear at the next pause.
		bool pause = _round.Current == RoundLoop.Phase.Pause;
		_handBox.Visible = pause;
		if (_endTurnButton != null) _endTurnButton.Visible = pause;

		foreach (Node c in _handBox.GetChildren())
			c.QueueFree();
		_handButtons.Clear();

		for (int i = 0; i < _deck.Hand.Count; i++)
			_handButtons.Add(MakeCardButton(_deck.Hand[i], i));

		if (_handButtons.Count == 0) _p2Cursor = 0;
		else if (_armed[1] == null) _p2Cursor = Mathf.Clamp(_p2Cursor, 0, _handButtons.Count - 1);
		ApplyCursorTint();

		if (_drawCount != null) _drawCount.Text = $"DRAW  {_deck.DrawPile.Count}";
		if (_discardCount != null) _discardCount.Text = $"DISCARD  {_deck.DiscardPile.Count}";
		if (_energyP1 != null) _energyP1.Text = $"P1  ⚡ {_energies[0].Energy} / {_energies[0].Granted}";
		if (_energyP2 != null) _energyP2.Text = $"P2  ⚡ {_energies[1].Energy} / {_energies[1].Granted}";
	}

	private void SetPrompt(string text)
	{
		if (_promptLabel != null) _promptLabel.Text = text;
	}

	// Compact card frame. A selected card shows its owner's colour + a ✔; an armed card pulses brighter.
	private Button MakeCardButton(Card card, int index)
	{
		Color accent = BuffColor(card);
		bool selected = _selections.TryGetValue(index, out Selection sel);
		bool armed = (_armed[0] != null && _armed[0].HandIndex == index) || (_armed[1] != null && _armed[1].HandIndex == index);
		bool affordable = _energies[0].CanAfford(card) || _energies[1].CanAfford(card);

		var b = new Button
		{
			CustomMinimumSize = new Vector2(116, 156),
			ClipText = true,
			// Selected cards stay clickable (to unselect); others need affordable energy + the pause.
			Disabled = _countingDown || _round.Current != RoundLoop.Phase.Pause || (!selected && !affordable),
		};
		float mix = armed ? 0.34f : (selected ? 0.26f : 0.13f);
		b.AddThemeStyleboxOverride("normal",   CardFrame(accent, mix, selected ? sel.Player : -1));
		b.AddThemeStyleboxOverride("hover",    CardFrame(accent, mix + 0.07f, selected ? sel.Player : -1));
		b.AddThemeStyleboxOverride("pressed",  CardFrame(accent, mix + 0.07f, selected ? sel.Player : -1));
		b.AddThemeStyleboxOverride("disabled", CardFrame(accent, 0.07f, selected ? sel.Player : -1));
		b.Pressed += () => OnHandPressed(index);

		var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		col.AddThemeConstantOverride("separation", 4);
		b.AddChild(col);

		string title = selected ? $"✔ {card.Title}" : card.Title;
		var header = new Label
		{
			Text = title,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			CustomMinimumSize = new Vector2(0, 26),
			ClipText = true,
		};
		var bar = new StyleBoxFlat { BgColor = accent };
		bar.SetContentMarginAll(2);
		bar.CornerRadiusTopLeft = bar.CornerRadiusTopRight = 5;
		header.AddThemeStyleboxOverride("normal", bar);
		header.AddThemeFontSizeOverride("font_size", 13);
		header.AddThemeColorOverride("font_color", new Color(0.1f, 0.1f, 0.13f));
		col.AddChild(header);

		var cost = new Label
		{
			Text = selected ? $"P{sel.Player + 1}  ⚡ {card.EnergyCost}" : $"⚡ {card.EnergyCost}",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		cost.AddThemeFontSizeOverride("font_size", 12);
		cost.AddThemeColorOverride("font_color", accent);
		col.AddChild(cost);

		var desc = new Label
		{
			Text = card.Description,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			HorizontalAlignment = HorizontalAlignment.Center,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
		};
		desc.AddThemeFontSizeOverride("font_size", 10);
		desc.AddThemeColorOverride("font_color", new Color(0.82f, 0.84f, 0.9f));
		desc.OffsetLeft = 4; desc.OffsetRight = -4;
		col.AddChild(desc);

		_handBox.AddChild(b);
		return b;
	}

	// Dark card body with a tinted border. A selected card's border takes its owner's colour.
	private static StyleBoxFlat CardFrame(Color accent, float bgMix, int ownerPlayer)
	{
		var s = new StyleBoxFlat { BgColor = new Color(0.11f, 0.11f, 0.15f).Lerp(accent, bgMix) };
		s.SetBorderWidthAll(ownerPlayer >= 0 ? 3 : 2);
		s.BorderColor = ownerPlayer == 0 ? P1Color : ownerPlayer == 1 ? P2Color : accent;
		s.SetCornerRadiusAll(6);
		s.SetContentMarginAll(0);
		return s;
	}

	private static Color BuffColor(Card card) => card.Buff switch
	{
		Card.BuffKind.Weapon  => new Color(0.95f, 0.72f, 0.35f),
		Card.BuffKind.Ability => new Color(0.72f, 0.52f, 0.97f),
		_                     => new Color(0.45f, 0.66f, 0.96f),
	};

	// Tint P2's cursor — over the markers while targeting, else over the hand.
	private void ApplyCursorTint()
	{
		if (_armed[1] != null)
		{
			List<Button> markers = _armed[1].Markers;
			for (int i = 0; i < markers.Count; i++)
				if (IsInstanceValid(markers[i]))
					markers[i].Modulate = i == _p2Cursor ? CursorTint : Colors.White;
		}
		else
		{
			for (int i = 0; i < _handButtons.Count; i++)
				_handButtons[i].Modulate = i == _p2Cursor ? CursorTint : Colors.White;
		}
	}

	// ── On-field target markers ────────────────────────────────────────────────────────────────────────
	private void BuildMarkers(Armed a)
	{
		bool soldier = a.Card.Buff == Card.BuffKind.Soldier;
		Color tint = a.Player == 0 ? P1Color : P2Color;
		for (int i = 0; i < a.Candidates.Count; i++)
		{
			int idx = i;
			var m = new Button
			{
				CustomMinimumSize = new Vector2(40, 40),
				Text = soldier ? "+" : "▲",
				// P2 confirms with A (cursor), so its markers ignore the mouse; P1's are mouse-clickable.
				MouseFilter = a.Player == 0 ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore,
			};
			var box = new StyleBoxFlat { BgColor = new Color(tint.R, tint.G, tint.B, 0.35f) };
			box.SetBorderWidthAll(2);
			box.BorderColor = tint;
			box.SetCornerRadiusAll(soldier ? 8 : 20);
			m.AddThemeStyleboxOverride("normal", box);
			m.AddThemeStyleboxOverride("hover", box);
			m.AddThemeStyleboxOverride("pressed", box);
			m.AddThemeFontSizeOverride("font_size", 22);
			if (a.Player == 0)
				m.Pressed += () => OnMarkerClicked(0, idx);
			_root.AddChild(m);
			a.Markers.Add(m);
		}
		ApplyCursorTint();
	}

	// Keep each marker pinned over its world point (the camera eases even while frozen).
	private void PositionMarkers()
	{
		Camera3D cam = GetViewport().GetCamera3D();
		for (int p = 0; p < 2; p++)
		{
			Armed a = _armed[p];
			if (a == null)
				continue;
			for (int i = 0; i < a.Markers.Count; i++)
			{
				Button m = a.Markers[i];
				if (!IsInstanceValid(m))
					continue;
				if (cam == null || cam.IsPositionBehind(a.Candidates[i].World))
				{
					m.Visible = false;
					continue;
				}
				m.Visible = true;
				Vector2 screen = cam.UnprojectPosition(a.Candidates[i].World);
				m.Position = screen - m.Size / 2f;
			}
		}
	}

	private void UpdatePhaseHud()
	{
		if (_phaseLabel == null)
			return;
		int waveCount = _waves != null ? _waves.CountForWave(_round.RoundNumber) : 0;
		if (_round.Current == RoundLoop.Phase.Play)
		{
			_phaseLabel.Text = $"WAVE {_round.RoundNumber}   •   SURVIVE   {_round.TimeLeft:0.0}s";
			_phaseLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.95f, 0.6f));
			if (_waveLabel != null) _waveLabel.Text = $"{waveCount} foes on the field — survive!";
		}
		else
		{
			_phaseLabel.Text = $"WAVE {_round.RoundNumber} INCOMING — build your squad, then End Turn";
			_phaseLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.55f));
			if (_waveLabel != null) _waveLabel.Text = $"{waveCount} foes staged — arm up, place soldiers, grant abilities";
		}
	}
}
