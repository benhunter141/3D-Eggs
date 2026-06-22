using Godot;
using System.Collections.Generic;

// Co-op Card Brawl front-end (M15, Chunks 69–70). Two weak eggs (P1 keyboard+mouse, P2 gamepad) share
// ONE hand of player-buff cards and ONE energy pool. The battle runs the M12 RoundLoop: it starts
// PAUSED so the players spend energy to arm up / spawn soldiers, then End Turn begins a 15 s survival
// WAVE — the WaveManager spawns an escalating ring of enemies and the frozen world unfreezes to fight.
// At the timeout it auto-pauses, redeals a fresh hand, refills flat energy, and queues the next (harder)
// wave. Lose only when BOTH eggs fall (GameManager.RequireAllPlayersDead + the eggs' ShowGameOverOnDeath
// = false); survival has no win (GameManager.DisableWin).
//
// Cards play ONLY in the pause, and each play is tagged with the egg that triggered it so the buff lands
// on THAT egg: P1 clicks a card with the mouse; P2 moves a cursor with the d-pad/stick and confirms with
// A. The routing core is the headless-tested BrawlHand; this node is the UI + two-device input shell.
public partial class CardBrawl : Node3D
{
	[Export] public int HandSize = 5;
	[Export] public int BaseEnergy = 5;          // flat energy granted every wave (no KotH bonus in the brawl)
	[Export] public float RoundSeconds = 15f;    // length of a survival wave before it auto-pauses

	private readonly Deck _deck = new();
	private EnergyPool _energy;
	private RoundLoop _round;
	private BrawlHand _hand;
	private WaveManager _waves;
	private Node _units;                          // container the eggs live in + spawned enemies drop into

	private readonly List<Player> _players = new();        // [0] = P1, [1] = P2 (by control scheme)
	private readonly List<ICardPlayer> _cardPlayers = new();

	// UI
	private Label _phaseLabel, _energyLabel, _promptLabel, _waveLabel, _drawCount, _discardCount;
	private HBoxContainer _handBox;
	private Button _endTurnButton;
	private readonly List<Button> _handButtons = new();

	private int _previewedWave;                   // foes-first (Chunk 72): highest wave already staged in a pause

	// P2 gamepad selection + edge-detect state (device 0).
	private int _p2Cursor;
	private bool _navLeftPrev, _navRightPrev, _confirmPrev, _endTurnPrev;
	private const JoyButton ConfirmButton = JoyButton.A;      // play the cursor card
	private const JoyButton EndTurnButton = JoyButton.Start;  // begin the wave
	private static readonly Color CursorTint = new(0.7f, 1.0f, 0.7f);

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;   // keep ticking + driving the UI while the world is frozen

		// Find the two eggs (assigned by control scheme; fall back to scene order). The first egg's
		// parent is the Units container both soldiers and wave enemies are dropped into.
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
		_cardPlayers.Add(p1); _cardPlayers.Add(p2);
		_units = p1 != null ? p1.GetParent() : (Node)this;

		_waves = GetNodeOrNull<WaveManager>("WaveManager");
		if (_waves == null) { _waves = new WaveManager { Name = "WaveManager" }; AddChild(_waves); }

		_round = new RoundLoop(RoundSeconds);
		_round.PhaseChanged += OnPhaseChanged;
		_energy = new EnergyPool(BaseEnergy, 0);
		_deck.LoadStarter(CardLibrary.BrawlDeck());
		_hand = new BrawlHand(_deck, _energy);

		BuildUi();

		RefillEnergy();
		_deck.Draw(HandSize);
		OnPhaseChanged(_round.Current);   // sync the opening PAUSED state
		Refresh();
	}

	// GetTree().Paused is global and survives scene changes — always lift it on the way out.
	public override void _ExitTree() => GetTree().Paused = false;

	public override void _Process(double delta)
	{
		RoundLoop.Phase before = _round.Current;
		_round.Tick((float)delta);
		if (before == RoundLoop.Phase.Play && _round.Current == RoundLoop.Phase.Pause)
			OnRoundTimeout();
		UpdatePhaseHud();

		PollGamepad();
	}

	// P2's two-device hand control (device 0): d-pad / left-stick move the cursor, A confirms, Start ends
	// the turn. Only the cursor moves + confirms in the pause (cards play between waves).
	private void PollGamepad()
	{
		float x = Input.GetJoyAxis(0, JoyAxis.LeftX);
		bool left  = Input.IsJoyButtonPressed(0, JoyButton.DpadLeft)  || x < -0.5f;
		bool right = Input.IsJoyButtonPressed(0, JoyButton.DpadRight) || x >  0.5f;
		bool confirm = Input.IsJoyButtonPressed(0, ConfirmButton);
		bool endTurn = Input.IsJoyButtonPressed(0, EndTurnButton);

		if (_round.Current == RoundLoop.Phase.Pause && _handButtons.Count > 0)
		{
			if (left && !_navLeftPrev)   MoveCursor(-1);
			if (right && !_navRightPrev) MoveCursor(+1);
			if (confirm && !_confirmPrev) PlayHandCard(_p2Cursor, 1);
		}
		if (endTurn && !_endTurnPrev) OnEndTurn();

		_navLeftPrev = left; _navRightPrev = right; _confirmPrev = confirm; _endTurnPrev = endTurn;
	}

	private void MoveCursor(int step)
	{
		if (_handButtons.Count == 0)
			return;
		_p2Cursor = Mathf.PosMod(_p2Cursor + step, _handButtons.Count);
		ApplyCursorTint();
	}

	// Play hand[handIndex] on player[playerIndex] (0 = P1, 1 = P2). Cards play only in the pause; the buff
	// lands on the triggering egg. Public so the headless test can drive routing without faking devices.
	public bool PlayHandCard(int handIndex, int playerIndex)
	{
		if (_round.Current != RoundLoop.Phase.Pause)
		{
			if (_promptLabel != null) _promptLabel.Text = "Play cards between waves (during the pause).";
			return false;
		}
		bool ok = _hand.Play(handIndex, playerIndex, _cardPlayers);
		if (!ok && _promptLabel != null)
			_promptLabel.Text = "Can't play that (not enough energy?).";
		if (ok)
			Refresh();
		return ok;
	}

	private void OnCardClicked(int index) => PlayHandCard(index, 0);   // P1 = mouse

	// End Turn BEGINS the wave: the foes were already STAGED during the pause (foes-first, Chunk 72), so
	// this just unfreezes the world — the previewed enemies spring to life and the 15 s clock starts.
	private void OnEndTurn()
	{
		if (_round.Current != RoundLoop.Phase.Pause)
			return;
		_round.EndTurn();                       // -> PLAY (OnPhaseChanged unfreezes the staged foes)
		Refresh();
	}

	// Foes-first (Chunk 72): stage the coming wave DURING the pause so both eggs can SEE the threat
	// (count + composition) and spend energy to counter it before committing. The foes drop into the
	// Pausable Units node while the world is frozen, so they stand inert — no move/attack, can't trip the
	// lose check — until End Turn unfreezes them. Spawns at most once per wave number (idempotent across
	// repeated pause edges).
	private void SpawnPreviewWave()
	{
		int wave = _round.RoundNumber;
		if (_waves == null || _units == null || wave == _previewedWave)
			return;
		_previewedWave = wave;
		_waves.SpawnWave(wave, _units, ArenaCenter());
	}

	// A wave survived (clock ran out): discard the spent hand, refill flat energy, deal a fresh hand. The
	// next (harder) wave is queued for the next End Turn.
	private void OnRoundTimeout()
	{
		_deck.DiscardHand();
		RefillEnergy();
		_deck.Draw(HandSize);
		Refresh();
	}

	private void RefillEnergy() => _energy.Refill(0);   // flat BaseEnergy each wave (no held ground)

	// Midpoint of the eggs (the wave rings around them); origin if no eggs.
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

	// Freeze the world while paused (eggs/soldiers/enemies are in the Pausable Units node), run it while
	// playing. This node + the UI are Always, so cards stay clickable in the pause. End Turn is live only
	// while paused (it begins the wave).
	private void OnPhaseChanged(RoundLoop.Phase phase)
	{
		bool paused = phase == RoundLoop.Phase.Pause;
		GetTree().Paused = paused;
		if (paused)
			SpawnPreviewWave();   // freeze FIRST, then stage the coming wave so it's inert from frame 0
		if (_endTurnButton != null)
		{
			_endTurnButton.Disabled = !paused;
			_endTurnButton.Text = paused ? "End Turn — start wave  ▶" : "Wave in progress…";
		}
		UpdatePhaseHud();
		Refresh();
	}

	// ── UI (built in code) ───────────────────────────────────────────────────────────────────────────
	private void BuildUi()
	{
		var ui = new CanvasLayer { Name = "Ui" };
		AddChild(ui);
		var root = new Control();
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.MouseFilter = Control.MouseFilterEnum.Ignore;
		ui.AddChild(root);

		_phaseLabel = MakeLabel(root, Control.LayoutPreset.TopWide, 16, 56, 26, new Color(1f, 0.85f, 0.55f));
		_phaseLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_waveLabel = MakeLabel(root, Control.LayoutPreset.TopWide, 60, 92, 20, new Color(0.92f, 0.88f, 0.72f));
		_waveLabel.HorizontalAlignment = HorizontalAlignment.Center;

		_energyLabel = MakeLabel(root, Control.LayoutPreset.TopLeft, 20, 48, 22, new Color(0.7f, 1f, 0.8f));
		_energyLabel.OffsetLeft = 24; _energyLabel.OffsetRight = 460;
		_promptLabel = MakeLabel(root, Control.LayoutPreset.TopLeft, 52, 80, 16, new Color(0.78f, 0.8f, 0.86f));
		_promptLabel.OffsetLeft = 24; _promptLabel.OffsetRight = 640;

		_drawCount = MakeLabel(root, Control.LayoutPreset.BottomLeft, -64, -24, 16, new Color(0.7f, 0.8f, 1f));
		_drawCount.OffsetLeft = 24; _drawCount.OffsetRight = 200;
		_discardCount = MakeLabel(root, Control.LayoutPreset.BottomRight, -64, -24, 16, new Color(0.7f, 0.8f, 1f));
		_discardCount.OffsetLeft = -200; _discardCount.OffsetRight = -24;
		_discardCount.HorizontalAlignment = HorizontalAlignment.Right;

		_handBox = new HBoxContainer();
		_handBox.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_handBox.OffsetLeft = -560; _handBox.OffsetRight = 560;
		_handBox.OffsetTop = -250; _handBox.OffsetBottom = -70;
		_handBox.AddThemeConstantOverride("separation", 10);
		_handBox.Alignment = BoxContainer.AlignmentMode.Center;
		root.AddChild(_handBox);

		_endTurnButton = new Button
		{
			CustomMinimumSize = new Vector2(300, 52),
			Text = "End Turn — start wave  ▶",
		};
		_endTurnButton.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_endTurnButton.OffsetLeft = -150; _endTurnButton.OffsetRight = 150;
		_endTurnButton.OffsetTop = -60; _endTurnButton.OffsetBottom = -8;
		_endTurnButton.AddThemeFontSizeOverride("font_size", 20);
		_endTurnButton.Pressed += OnEndTurn;
		root.AddChild(_endTurnButton);
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

		foreach (Node c in _handBox.GetChildren())
			c.QueueFree();
		_handButtons.Clear();

		for (int i = 0; i < _deck.Hand.Count; i++)
			_handButtons.Add(MakeCardButton(_deck.Hand[i], i));

		if (_handButtons.Count == 0) _p2Cursor = 0;
		else _p2Cursor = Mathf.Clamp(_p2Cursor, 0, _handButtons.Count - 1);
		ApplyCursorTint();

		if (_drawCount != null) _drawCount.Text = $"DRAW  {_deck.DrawPile.Count}";
		if (_discardCount != null) _discardCount.Text = $"DISCARD  {_deck.DiscardPile.Count}";
		if (_energyLabel != null) _energyLabel.Text = $"ENERGY  {_energy.Energy} / {_energy.Granted}";
		if (_promptLabel != null && _round.Current == RoundLoop.Phase.Pause && _promptLabel.Text == "")
			_promptLabel.Text = "P1: click a card    P2: d-pad + A    then End Turn.";
	}

	private Button MakeCardButton(Card card, int index)
	{
		var b = new Button
		{
			CustomMinimumSize = new Vector2(150, 200),
			Text = $"{card.Title}\n\nCost {card.EnergyCost}\n\n{card.Description}",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			ClipText = false,
			Disabled = _round.Current != RoundLoop.Phase.Pause || !_energy.CanAfford(card),
		};
		b.AddThemeFontSizeOverride("font_size", 14);
		b.AddThemeColorOverride("font_color", BuffColor(card));
		b.Pressed += () => OnCardClicked(index);
		_handBox.AddChild(b);
		return b;
	}

	// Soldier = blue, weapon = amber, ability = violet (so the shared hand reads at a glance).
	private static Color BuffColor(Card card) => card.Buff switch
	{
		Card.BuffKind.Weapon  => new Color(1.0f, 0.85f, 0.55f),
		Card.BuffKind.Ability => new Color(0.8f, 0.65f, 1.0f),
		_                     => new Color(0.6f, 0.8f, 1.0f),
	};

	// Tint P2's cursor card so the gamepad player can see their selection in the shared hand.
	private void ApplyCursorTint()
	{
		for (int i = 0; i < _handButtons.Count; i++)
			_handButtons[i].Modulate = i == _p2Cursor ? CursorTint : Colors.White;
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
			if (_waveLabel != null)
				_waveLabel.Text = $"{waveCount} foes on the field — survive!";
		}
		else
		{
			_phaseLabel.Text = $"WAVE {_round.RoundNumber} INCOMING — counter the foes, then End Turn";
			_phaseLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.55f));
			if (_waveLabel != null)
				_waveLabel.Text = $"{waveCount} foes staged ahead — spend energy to counter them";
		}
	}
}
