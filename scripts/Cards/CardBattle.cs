using Godot;

// Card battler front-end (M12). Chunk 32 built the StS-style draw/hand/discard UI over a Deck;
// Chunk 33 adds REAL play onto a small 3D battlefield. The scene is now a Node3D world (ground +
// fixed top-down camera + a line of seed enemies) with the card UI on a CanvasLayer above it.
//
// Playing a card is now a two-step aim, routed through CardPlay:
//   • Click a UNIT card  → it becomes "pending"; the next click on the GROUND spawns that unit there.
//   • Click an ACTION card → it becomes "pending"; the next click on a FRIENDLY UNIT makes it act.
// A right-click (or clicking nothing valid) cancels the pending card. On a successful play the card
// goes to discard and its energy is spent (shown, not yet GATED — that's Chunk 35).
//
// Round loop (Chunk 34): the battle STARTS PAUSED with an opening hand. End Turn begins the round —
// the battlefield runs in real time for RoundSeconds while you keep playing cards live. When the
// clock runs out it auto-pauses AND redeals (discard the hand, refill energy, draw a fresh 5); you
// set up again and hit End Turn to play the next round.
public partial class CardBattle : Node3D, ICardField
{
	[Export] public int HandSize = 5;
	[Export] public int StartingEnergy = 3;
	[Export] public float RoundSeconds = 15f;   // length of a PLAY phase before it auto-pauses (Chunk 34)

	private readonly Deck _deck = new();
	private RoundLoop _round;        // PLAY/PAUSE state machine (Chunk 34)
	private int _energy;
	private Card _pending;          // card awaiting a target click (null = nothing selected)

	private Label _energyLabel;
	private Label _phaseLabel;
	private Label _promptLabel;
	private Label _drawCount;
	private Label _discardCount;
	private HBoxContainer _handBox;
	private Button _endTurnButton;

	private Camera3D _camera;
	private Node3D _units;           // parent for every spawned/seed unit

	// Chunk 35 dev panel — built in code so it never ships in the scene. Tune RoundSeconds live and
	// pause/resume the battlefield for debugging. Toggle with the DEV button or F3.
	private const float DevMinRound = 5f, DevMaxRound = 60f, DevRoundStep = 5f;
	private Control _devPanel;
	private Label _devRoundLabel;
	private Button _devPauseButton;

	public override void _Ready()
	{
		_energyLabel = GetNode<Label>("Ui/Root/EnergyLabel");
		_phaseLabel = GetNode<Label>("Ui/Root/PhaseLabel");
		_promptLabel = GetNode<Label>("Ui/Root/PromptLabel");
		_handBox = GetNode<HBoxContainer>("Ui/Root/HandBox");
		_drawCount = GetNode<Label>("Ui/Root/DrawPanel/DrawCount");
		_discardCount = GetNode<Label>("Ui/Root/DiscardPanel/DiscardCount");
		GetNode<Button>("Ui/Root/Buttons/DrawButton").Pressed += OnDrawOne;
		_endTurnButton = GetNode<Button>("Ui/Root/Buttons/EndTurnButton");
		_endTurnButton.Pressed += OnEndTurn;

		_camera = GetNode<Camera3D>("Camera3D");
		_units = GetNode<Node3D>("Units");

		_round = new RoundLoop(RoundSeconds);
		_round.PhaseChanged += OnPhaseChanged;
		BuildDevPanel();

		_deck.LoadStarter(CardLibrary.StarterDeck());
		_energy = StartingEnergy;
		_deck.Draw(HandSize);
		OnPhaseChanged(_round.Current);   // sync the opening PAUSED state (freeze + button + HUD)
		Refresh();
	}

	// GetTree().Paused is global and survives scene changes, so always lift it on the way out (the
	// Menu button can be hit mid-pause) — otherwise the menu / next level would load frozen.
	public override void _ExitTree() => GetTree().Paused = false;

	// PLAY runs the clock down in real time; the loop flips itself to PAUSE when the round times out.
	// This node is ProcessMode = Always (scene) so _Process keeps running while the tree is frozen.
	public override void _Process(double delta)
	{
		RoundLoop.Phase before = _round.Current;
		_round.Tick((float)delta);   // counts down only during PLAY (no-op while paused)
		if (before == RoundLoop.Phase.Play && _round.Current == RoundLoop.Phase.Pause)
			OnRoundTimeout();        // the round just ran out -> redeal a fresh hand
		UpdatePhaseHud();
	}

	// The PLAY clock ran out: discard the spent hand, refill energy, and deal a fresh 5 for the new
	// round. The phase already flipped to PAUSE (OnPhaseChanged froze the battlefield); the player now
	// sets up the new hand and hits End Turn to play on.
	private void OnRoundTimeout()
	{
		_pending = null;
		_deck.DiscardHand();
		_energy = StartingEnergy;
		_deck.Draw(HandSize);
		UpdatePrompt();
		Refresh();
	}

	// Freeze the battlefield while paused, run it while playing. The Units node is ProcessMode =
	// Pausable (scene) so it stops on GetTree().Paused; this node + the UI stay Always, so cards
	// remain playable in both phases. End Turn is only live while paused (it BEGINS the round).
	private void OnPhaseChanged(RoundLoop.Phase phase)
	{
		bool paused = phase == RoundLoop.Phase.Pause;
		GetTree().Paused = paused;
		_endTurnButton.Disabled = !paused;
		_endTurnButton.Text = paused ? "End Turn  ▶" : "Round in play…";
		UpdateDevPauseLabel();
		UpdatePhaseHud();
	}

	// World clicks resolve a pending card. Card-button clicks are handled by the buttons
	// themselves (they consume the input), so a left-click that reaches here is a battlefield aim.
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F3)
		{
			ToggleDevPanel();
			return;
		}

		if (@event is not InputEventMouseButton mb || !mb.Pressed)
			return;

		if (mb.ButtonIndex == MouseButton.Right)
		{
			CancelPending();
			return;
		}
		if (mb.ButtonIndex != MouseButton.Left || _pending == null)
			return;

		if (_pending.Target == Card.TargetKind.Location)
			TryPlayAtLocation(mb.Position);
		else
			TryPlayOnUnit(mb.Position);
	}

	// Unit card: drop the unit where the mouse ray meets the ground plane (y = 0).
	private void TryPlayAtLocation(Vector2 mousePos)
	{
		Vector3 from = _camera.ProjectRayOrigin(mousePos);
		Vector3 dir = _camera.ProjectRayNormal(mousePos);
		if (Mathf.Abs(dir.Y) < 0.0001f)
			return;                                  // ray parallel to the ground — no hit
		float t = -from.Y / dir.Y;
		if (t <= 0f)
			return;                                  // ground is behind the camera
		Vector3 point = from + dir * t;
		ResolvePlay(_camera, point, null);
	}

	// Action card: physics-pick whatever unit is under the cursor; CardPlay rejects non-friendlies.
	private void TryPlayOnUnit(Vector2 mousePos)
	{
		Vector3 from = _camera.ProjectRayOrigin(mousePos);
		Vector3 to = from + _camera.ProjectRayNormal(mousePos) * 200f;
		var query = PhysicsRayQueryParameters3D.Create(from, to);
		Godot.Collections.Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
		ICardUnit unit = hit.Count > 0 ? hit["collider"].As<GodotObject>() as ICardUnit : null;
		ResolvePlay(_camera, Vector3.Zero, unit);
	}

	// Common tail: hand the pending card to CardPlay; on success discard it, spend energy, refresh.
	// `_` (unused Camera3D) keeps both callers symmetric; the real args are location + unitTarget.
	private void ResolvePlay(Camera3D _, Vector3 location, ICardUnit unitTarget)
	{
		Card card = _pending;
		if (card == null)
			return;
		if (CardPlay.Play(card, this, location, unitTarget))
		{
			_deck.Discard(card);
			_energy -= card.EnergyCost;             // spent (not gated until Chunk 35)
			CancelPending();
		}
		// On a miss (e.g. clicked an enemy with an Action card) the card stays pending to retry.
	}

	// ICardField: instance the Unit card's scene at `location`, drop it into the Units node.
	public ICardUnit SpawnUnit(Card card, Vector3 location)
	{
		if (string.IsNullOrEmpty(card.SpawnPath))
			return null;
		var scene = GD.Load<PackedScene>(card.SpawnPath);
		if (scene == null || scene.Instantiate() is not Node3D node)
			return null;
		_units.AddChild(node);
		node.GlobalPosition = location + Vector3.Up;   // lift onto the ground like the level scenes
		return node as ICardUnit;
	}

	private void OnCardSelected(Card card)
	{
		_pending = card;
		UpdatePrompt();
	}

	private void CancelPending()
	{
		_pending = null;
		UpdatePrompt();
		Refresh();
	}

	// Draw one more card (watch the draw count fall, and the discard reshuffle in when it hits 0).
	private void OnDrawOne()
	{
		_deck.Draw(1);
		Refresh();
	}

	// End Turn BEGINS the round (Chunk 34): unfreeze the battlefield and start the clock. Only live
	// while paused; the hand carries into play (cards stay playable live), and the timeout redeal
	// (OnRoundTimeout) is what cycles the hand. The button is disabled during play, so this only
	// fires from PAUSE — the guard is belt-and-braces.
	private void OnEndTurn()
	{
		if (_round.Current != RoundLoop.Phase.Pause)
			return;
		_pending = null;
		_round.EndTurn();                       // -> PLAY (OnPhaseChanged unfreezes + starts the clock)
		UpdatePrompt();
		Refresh();
	}

	// Top banner: which round we're in, the phase, and (during PLAY) the countdown to the next pause.
	private void UpdatePhaseHud()
	{
		if (_round.Current == RoundLoop.Phase.Play)
		{
			_phaseLabel.Text = $"ROUND {_round.RoundNumber}   •   PLAY   {_round.TimeLeft:0.0}s";
			_phaseLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.95f, 0.6f));
		}
		else
		{
			_phaseLabel.Text = $"ROUND {_round.RoundNumber}   •   PAUSED — play cards, then End Turn";
			_phaseLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.55f));
		}
	}

	// Rebuild the hand row and refresh the counters/energy from the deck's current piles.
	private void Refresh()
	{
		foreach (Node c in _handBox.GetChildren())
			c.QueueFree();

		foreach (Card card in _deck.Hand)
			_handBox.AddChild(MakeCardButton(card));

		_drawCount.Text = $"DRAW\n{_deck.DrawPile.Count}";
		_discardCount.Text = $"DISCARD\n{_deck.DiscardPile.Count}";
		_energyLabel.Text = $"ENERGY   {_energy} / {StartingEnergy}";
	}

	// The aim hint under the energy readout: what the currently selected card wants targeted.
	private void UpdatePrompt()
	{
		if (_pending == null)
		{
			_promptLabel.Text = "Click a card to play it.";
			return;
		}
		_promptLabel.Text = _pending.Target == Card.TargetKind.Location
			? $"{_pending.Title} — click the GROUND to place  (right-click cancels)"
			: $"{_pending.Title} — click a FRIENDLY unit  (right-click cancels)";
	}

	// ── Chunk 35 dev panel ───────────────────────────────────────────────────────────────────────
	// A small in-code overlay (top-right, hidden by default) to retune round length live and to
	// pause/resume the battlefield for debugging. ProcessMode is inherited from this node (Always),
	// so its buttons keep working while the battlefield is frozen.
	private void BuildDevPanel()
	{
		Control root = GetNode<Control>("Ui/Root");

		var toggle = new Button
		{
			Text = "DEV",
			CustomMinimumSize = new Vector2(72, 32),
			ToggleMode = true,
		};
		toggle.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		toggle.OffsetLeft = -96; toggle.OffsetRight = -24;
		toggle.OffsetTop = 20; toggle.OffsetBottom = 52;
		toggle.AddThemeFontSizeOverride("font_size", 16);
		toggle.Pressed += ToggleDevPanel;
		root.AddChild(toggle);

		var panel = new PanelContainer { Visible = false };
		panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		panel.OffsetLeft = -256; panel.OffsetRight = -24;
		panel.OffsetTop = 60; panel.OffsetBottom = 196;
		root.AddChild(panel);
		_devPanel = panel;

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		panel.AddChild(box);

		var header = new Label { Text = "DEV PANEL  (F3)", HorizontalAlignment = HorizontalAlignment.Center };
		header.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.72f));
		box.AddChild(header);

		// Round-length row: [ − ]  Round 15s  [ + ]
		var roundRow = new HBoxContainer();
		roundRow.AddThemeConstantOverride("separation", 8);
		roundRow.Alignment = BoxContainer.AlignmentMode.Center;
		box.AddChild(roundRow);

		var minus = new Button { Text = "−", CustomMinimumSize = new Vector2(40, 36) };
		minus.Pressed += () => StepRoundSeconds(-DevRoundStep);
		roundRow.AddChild(minus);

		_devRoundLabel = new Label
		{
			CustomMinimumSize = new Vector2(120, 0),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		_devRoundLabel.AddThemeFontSizeOverride("font_size", 18);
		roundRow.AddChild(_devRoundLabel);

		var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(40, 36) };
		plus.Pressed += () => StepRoundSeconds(DevRoundStep);
		roundRow.AddChild(plus);

		// Manual pause / resume toggle — freezes the battlefield without the turn bookkeeping.
		_devPauseButton = new Button { CustomMinimumSize = new Vector2(0, 36) };
		_devPauseButton.Pressed += OnDevPauseToggle;
		box.AddChild(_devPauseButton);

		UpdateDevRoundLabel();
		UpdateDevPauseLabel();
	}

	private void ToggleDevPanel()
	{
		if (_devPanel != null)
			_devPanel.Visible = !_devPanel.Visible;
	}

	private void StepRoundSeconds(float delta)
	{
		float seconds = Mathf.Clamp(_round.RoundSeconds + delta, DevMinRound, DevMaxRound);
		_round.RetuneRoundSeconds(seconds);
		UpdateDevRoundLabel();
		UpdatePhaseHud();
	}

	// Pause/resume for debugging: flip the round phase without redealing or refilling. Resume continues
	// the SAME round (RoundLoop.Resume), unlike End Turn which starts a fresh one.
	private void OnDevPauseToggle()
	{
		if (_round.Current == RoundLoop.Phase.Play)
			_round.EndPlayPhase();
		else
			_round.Resume();
	}

	private void UpdateDevRoundLabel()
	{
		if (_devRoundLabel != null)
			_devRoundLabel.Text = $"Round {_round.RoundSeconds:0}s";
	}

	private void UpdateDevPauseLabel()
	{
		if (_devPauseButton != null)
			_devPauseButton.Text = _round.Current == RoundLoop.Phase.Play ? "⏸  Pause" : "▶  Resume";
	}

	// A single hand card rendered as a clickable panel-button.
	private Button MakeCardButton(Card card)
	{
		var b = new Button
		{
			CustomMinimumSize = new Vector2(156, 210),
			Text = $"{card.Title}\n\n[ {card.Kind} ]\nCost {card.EnergyCost}\n\n{card.Description}",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			ClipText = false,
			ToggleMode = true,
			ButtonPressed = card == _pending,        // the pending card stays visibly selected
		};
		b.AddThemeFontSizeOverride("font_size", 15);
		b.AddThemeColorOverride("font_color",
			card.Kind == Card.CardKind.Unit
				? new Color(0.6f, 0.8f, 1.0f)        // Unit cards read blue
				: new Color(1.0f, 0.85f, 0.55f));    // Action cards read amber
		b.Pressed += () => OnCardSelected(card);
		return b;
	}
}
