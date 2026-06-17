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
//
// Energy from KotH (Chunk 37): each pause refills energy from the capture points your team HOLDS
// (EnergyPool: a base allowance + a bonus per held point) — territory is your economy. Plays are
// GATED: a card can only be played if you can afford its cost (unaffordable cards are disabled).
public partial class CardBattle : Node3D, ICardField
{
	[Export] public int HandSize = 5;
	[Export] public int BaseEnergy = 3;         // energy granted each round before counting held ground
	[Export] public int EnergyPerPoint = 1;     // extra energy per capture point held at the pause
	[Export] public float RoundSeconds = 5f;    // length of a PLAY phase before it auto-pauses (Chunk 34; 5 s for the endzone auto-battler, Chunk 43)

	// Football-pitch layout (M12.5, Chunk 40). The pitch is 28 wide (X, ±FieldHalfWidth) × 44 long (Z),
	// with a player endzone at the NEAR (+Z) end and an enemy endzone at the FAR (−Z) end. These bounds
	// define the player endzone rectangle — x ∈ [−FieldHalfWidth, FieldHalfWidth], z ∈ [EndzoneFarZ,
	// EndzoneNearZ] — and back the endzone-gated placement (Chunk 41) and forward-march goals (Chunk 42).
	[Export] public float FieldHalfWidth = 14f;        // X extent of the pitch (±)
	[Export] public float PlayerEndzoneNearZ = 22f;    // +Z edge of the player endzone (near the camera)
	[Export] public float PlayerEndzoneFarZ = 14f;     // inner (−Z) edge of the player endzone

	private readonly Deck _deck = new();
	private RoundLoop _round;        // PLAY/PAUSE state machine (Chunk 34)
	private EnergyPool _energy;      // KotH-fed energy economy + play gate (Chunk 37)
	private RunMap _run;             // room map + run deck + rewards (Chunk 38)
	private RunMap.RoomReward _pendingReward;   // reward awaiting a pick (non-null = reward screen open)
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

	// Chunk 38 run UI — built in code: a room track, a Clear-Room control, and a reward picker.
	private Label _runTrackLabel;
	private Button _clearRoomButton;
	private Control _rewardPanel;
	private Label _rewardPrompt;
	private VBoxContainer _rewardChoices;

	// Chunk 39 inventory UI — built in code (left edge): a relics readout + a button per held potion.
	private Label _relicLabel;
	private VBoxContainer _potionBox;

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

		// Football-pitch auto-battler (M12.5, Chunk 42): every unit on the pitch MARCHES toward the
		// opposing endzone unless a foe comes within AggroRange. Enable it on the seed enemies here;
		// each card-spawned unit is enabled in SpawnUnit. Only this mode opts in — other levels keep
		// their real formations / global chase untouched.
		foreach (Node n in _units.GetChildren())
			if (n is Unit seed)
				EnableMarch(seed);

		_round = new RoundLoop(RoundSeconds);
		_round.PhaseChanged += OnPhaseChanged;
		BuildDevPanel();
		BuildRunUi();
		BuildInventoryUi();

		_run = new RunMap();                       // the run: rooms + the deck we carry between them
		_deck.LoadStarter(_run.Collection);        // first battle's deck = the run's starting collection
		_energy = new EnergyPool(BaseEnergy, EnergyPerPoint);
		RefillEnergy();                            // opening allowance (no ground held yet -> base + relics)
		_deck.Draw(EffectiveHandSize());
		OnPhaseChanged(_round.Current);   // sync the opening PAUSED state (freeze + button + HUD)
		UpdateRunHud();
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

	// The PLAY clock ran out: discard the spent hand, refill energy from the ground we now hold, and
	// deal a fresh 5 for the new round. The phase already flipped to PAUSE (OnPhaseChanged froze the
	// battlefield); the player now sets up the new hand and hits End Turn to play on.
	private void OnRoundTimeout()
	{
		_pending = null;
		_deck.DiscardHand();
		RefillEnergy();                            // KotH points held + relic bonus -> this round's energy
		_deck.Draw(EffectiveHandSize());
		UpdatePrompt();
		Refresh();
	}

	// Refill energy at a pause folding in the run's relic energy bonus (Chunk 39) on top of held ground.
	private void RefillEnergy()
	{
		if (_run != null)
			_energy.BonusEnergy = _run.Inventory.BonusEnergy;
		_energy.Refill(CountPlayerHeldPoints());
	}

	// Cards drawn each round = base hand size + the run's relic hand-size bonus (Chunk 39).
	private int EffectiveHandSize() => HandSize + (_run != null ? _run.Inventory.BonusHandSize : 0);

	// Count the capture points the player's team HOLDS right now (sole occupant). Read at the pause,
	// this is the territory that funds the next round (Chunk 37). Capture points are tagged into the
	// "capture_points" group in the scene; their State reflects the last PLAY frame while frozen.
	private int CountPlayerHeldPoints()
	{
		int held = 0;
		foreach (Node n in GetTree().GetNodesInGroup("capture_points"))
			if (n is CapturePoint cp && cp.State == CapturePoint.ZoneState.PlayerHeld)
				held++;
		return held;
	}

	// Freeze the battlefield while paused, run it while playing. The Units node is ProcessMode =
	// Pausable (scene) so it stops on GetTree().Paused; this node + the UI stay Always, so cards
	// remain playable in both phases. End Turn is only live while paused (it BEGINS the round).
	private void OnPhaseChanged(RoundLoop.Phase phase)
	{
		bool paused = phase == RoundLoop.Phase.Pause;
		GetTree().Paused = paused;
		_endTurnButton.Disabled = !paused || _pendingReward != null;
		_endTurnButton.Text = paused ? "End Turn  ▶" : "Round in play…";
		if (_clearRoomButton != null)   // clearing a room is a between-rounds (paused) action
			_clearRoomButton.Disabled = !paused || _pendingReward != null || (_run != null && _run.IsComplete);
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

		if (_pendingReward != null)   // reward screen open: ignore battlefield aim clicks
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

	// The player endzone rectangle (Chunk 41) — the only place Unit cards may be deployed.
	private Endzone PlayerEndzone => new(FieldHalfWidth, PlayerEndzoneFarZ, PlayerEndzoneNearZ);

	// Unit card: drop the unit where the mouse ray meets the ground plane (y = 0), but only inside the
	// player endzone (Chunk 41). An out-of-zone click is rejected with a prompt and the card stays
	// pending so the player can re-aim.
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
		if (!PlayerEndzone.Contains(point))
		{
			_promptLabel.Text = "Place units in your endzone (the near strip).";
			return;                                  // card stays pending — re-aim inside the zone
		}
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
		if (!_energy.CanAfford(card))               // gate: can't play what your held ground can't pay for
		{
			_promptLabel.Text = $"Not enough energy for {card.Title}  ({card.EnergyCost} needed, {_energy.Energy} left)";
			return;                                 // card stays pending so the player can pick another target/card
		}
		if (CardPlay.Play(card, this, location, unitTarget))
		{
			_deck.Discard(card);
			_energy.Spend(card);                    // deduct the cost (gated above)
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
		if (node is Unit unit)
		{
			// Chunk 39: SpawnStrength relics make every deployed unit hit harder.
			if (_run != null)
				unit.Strength += _run.Inventory.SpawnStrengthBonus;
			EnableMarch(unit);                         // Chunk 42: march toward the enemy endzone
		}
		return node as ICardUnit;
	}

	// Turn on forward-march for one pitch unit (Chunk 42), aiming it at the OPPOSING endzone: enemies
	// march toward the near player endzone (+Z = Vector3.Back), friendlies toward the far enemy endzone
	// (−Z = Vector3.Forward). CapturePoints and other non-Unit children are left alone by the caller.
	private static void EnableMarch(Unit unit)
	{
		unit.MarchMode = true;
		unit.MarchGoalDirection = unit.Team == Unit.TeamId.Enemy ? Vector3.Back : Vector3.Forward;
	}

	private void OnCardSelected(Card card)
	{
		if (_pendingReward != null)   // reward screen open: no card play
			return;
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
		if (_pendingReward != null)   // reward screen open: lock the deck
			return;
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
		// Show the held ground that funded this round next to the spend tally (territory = economy).
		int held = CountPlayerHeldPoints();
		_energyLabel.Text = held > 0
			? $"ENERGY   {_energy.Energy} / {_energy.Granted}   (holding {held})"
			: $"ENERGY   {_energy.Energy} / {_energy.Granted}";
		UpdateInventoryUi();
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

	// ── Chunk 38 run structure ─────────────────────────────────────────────────────────────────────
	// Built in code (like the dev panel) so the scene stays simple: a room-track readout under the
	// prompt, a Clear-Room control next to End Turn, and a hidden reward picker that pops when a room
	// is cleared. The run model (RunMap) is the authority; this is a thin view over it.
	private void BuildRunUi()
	{
		Control root = GetNode<Control>("Ui/Root");

		// Room track — sits just under the aim-prompt line, top-centre.
		_runTrackLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_runTrackLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		_runTrackLabel.OffsetTop = 166; _runTrackLabel.OffsetBottom = 196;
		_runTrackLabel.AddThemeFontSizeOverride("font_size", 18);
		_runTrackLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.72f));
		root.AddChild(_runTrackLabel);

		// Clear-Room control — appended to the existing button row (Draw / End Turn / Clear Room).
		_clearRoomButton = new Button
		{
			CustomMinimumSize = new Vector2(200, 48),
			Text = "Clear Room  ▶",
		};
		_clearRoomButton.AddThemeFontSizeOverride("font_size", 22);
		_clearRoomButton.Pressed += OnClearRoom;
		GetNode<HBoxContainer>("Ui/Root/Buttons").AddChild(_clearRoomButton);

		// Reward picker — centred overlay, hidden until a room is cleared.
		var panel = new PanelContainer { Visible = false };
		panel.SetAnchorsPreset(Control.LayoutPreset.Center);
		panel.OffsetLeft = -330; panel.OffsetRight = 330;
		panel.OffsetTop = -210; panel.OffsetBottom = 210;
		root.AddChild(panel);
		_rewardPanel = panel;

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 14);
		panel.AddChild(box);

		_rewardPrompt = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(620, 0),
		};
		_rewardPrompt.AddThemeFontSizeOverride("font_size", 22);
		_rewardPrompt.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.72f));
		box.AddChild(_rewardPrompt);

		_rewardChoices = new VBoxContainer();
		_rewardChoices.AddThemeConstantOverride("separation", 8);
		box.AddChild(_rewardChoices);
	}

	// "Room X/N — Title  (Type)", or a finished banner once the run is complete.
	private void UpdateRunHud()
	{
		if (_runTrackLabel == null || _run == null)
			return;
		if (_run.IsComplete)
		{
			_runTrackLabel.Text = "RUN COMPLETE — victory!";
			return;
		}
		RunMap.Room room = _run.Current;
		_runTrackLabel.Text = $"ROOM {_run.RoomNumber}/{_run.RoomCount}   •   {room.Title}   ({room.Type})";
	}

	// Clear the current room (only while paused, no reward open). Advances the map in the model and
	// pops the reward picker. With no room left the run is over.
	private void OnClearRoom()
	{
		if (_round.Current != RoundLoop.Phase.Pause || _pendingReward != null || _run.IsComplete)
			return;
		_pending = null;
		RunMap.RoomReward reward = _run.CompleteCurrentRoom();
		UpdateRunHud();
		OpenReward(reward);
	}

	// Show the reward picker: a button per card choice plus a Skip. Picking resolves the reward and
	// starts the next room (or ends the run). While it's open, play/turn controls are locked.
	private void OpenReward(RunMap.RoomReward reward)
	{
		_pendingReward = reward;
		foreach (Node c in _rewardChoices.GetChildren())
			c.QueueFree();

		_rewardPrompt.Text = reward.Prompt;
		foreach (Card card in reward.Choices)
		{
			Card choice = card;   // capture per-iteration for the closure
			var b = new Button
			{
				CustomMinimumSize = new Vector2(600, 56),
				Text = $"{choice.Title}   ·   {choice.Kind}   ·   Cost {choice.EnergyCost}\n{choice.Description}",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			b.AddThemeFontSizeOverride("font_size", 16);
			b.AddThemeColorOverride("font_color",
				choice.Kind == Card.CardKind.Unit
					? new Color(0.6f, 0.8f, 1.0f)
					: new Color(1.0f, 0.85f, 0.55f));
			b.Pressed += () => ChooseReward(choice);
			_rewardChoices.AddChild(b);
		}

		var skip = new Button { CustomMinimumSize = new Vector2(600, 44), Text = "Skip (take nothing)" };
		skip.AddThemeFontSizeOverride("font_size", 16);
		skip.Pressed += () => ChooseReward(null);
		_rewardChoices.AddChild(skip);

		_rewardPanel.Visible = true;
		_endTurnButton.Disabled = true;
		_clearRoomButton.Disabled = true;
		UpdatePrompt();
		Refresh();   // dim hand cards while the picker is up (energy unchanged, but keep state honest)
	}

	// Apply the pick (null = skip), close the picker, and roll into the next room — or finish the run.
	private void ChooseReward(Card chosen)
	{
		_run.TakeReward(_pendingReward, chosen);
		_pendingReward = null;
		_rewardPanel.Visible = false;
		if (_run.IsComplete)
			FinishRun();
		else
			StartRoom();
		UpdateRunHud();
	}

	// Begin the next room: rebuild the battle deck from the (now grown) run collection, refill energy
	// and deal a fresh hand, and ensure we're paused so the player sets up before End Turn.
	private void StartRoom()
	{
		if (_round.Current == RoundLoop.Phase.Play)
			_round.EndPlayPhase();              // back to PAUSE (no round bump) for setup
		_deck.LoadStarter(_run.Collection);
		RefillEnergy();
		_deck.Draw(EffectiveHandSize());
		OnPhaseChanged(_round.Current);         // re-sync button/freeze state now the reward is closed
		UpdatePrompt();
		Refresh();
	}

	// Run won: freeze setup, lock the round/clear controls, and let the prompt celebrate.
	private void FinishRun()
	{
		if (_round.Current == RoundLoop.Phase.Play)
			_round.EndPlayPhase();
		OnPhaseChanged(_round.Current);
		_endTurnButton.Disabled = true;
		_clearRoomButton.Disabled = true;
		_promptLabel.Text = "Run complete — you bested the Egg-Tyrant! (◀ Menu to leave)";
		Refresh();
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
			// gate: dim cards your held ground can't pay for, and lock the whole hand while picking a reward
			Disabled = _pendingReward != null || !_energy.CanAfford(card),
		};
		b.AddThemeFontSizeOverride("font_size", 15);
		b.AddThemeColorOverride("font_color",
			card.Kind == Card.CardKind.Unit
				? new Color(0.6f, 0.8f, 1.0f)        // Unit cards read blue
				: new Color(1.0f, 0.85f, 0.55f));    // Action cards read amber
		b.Pressed += () => OnCardSelected(card);
		return b;
	}

	// ── Chunk 39 relics & potions ────────────────────────────────────────────────────────────────
	// Built in code (like the run/dev panels): a relics readout + a column of potion buttons down the
	// left edge. Relics are passive (their modifiers fold into RefillEnergy / EffectiveHandSize /
	// SpawnUnit); potions are popped here for a one-shot effect.
	private void BuildInventoryUi()
	{
		Control root = GetNode<Control>("Ui/Root");

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
		panel.OffsetLeft = 24; panel.OffsetRight = 268;
		panel.OffsetTop = -150; panel.OffsetBottom = 150;
		root.AddChild(panel);

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		panel.AddChild(box);

		var header = new Label { Text = "RELICS & POTIONS" };
		header.AddThemeFontSizeOverride("font_size", 16);
		header.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.72f));
		box.AddChild(header);

		_relicLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(230, 0),
		};
		_relicLabel.AddThemeFontSizeOverride("font_size", 14);
		_relicLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 1.0f));
		box.AddChild(_relicLabel);

		_potionBox = new VBoxContainer();
		_potionBox.AddThemeConstantOverride("separation", 6);
		box.AddChild(_potionBox);
	}

	// Refresh the relics line and rebuild the potion buttons from the run inventory. Consumed potions
	// stay listed but disabled (clear feedback that they're spent); all lock while a reward is open.
	private void UpdateInventoryUi()
	{
		if (_relicLabel == null || _run == null)
			return;

		var relics = _run.Inventory.Relics;
		if (relics.Count == 0)
		{
			_relicLabel.Text = "Relics: none yet\n(beat the boss to earn one)";
		}
		else
		{
			string text = "Relics:";
			foreach (Relic r in relics)
				text += $"\n• {r.Title} — {r.Description}";
			_relicLabel.Text = text;
		}

		foreach (Node c in _potionBox.GetChildren())
			c.QueueFree();

		foreach (Potion potion in _run.Inventory.Potions)
		{
			Potion p = potion;   // capture per-iteration for the closure
			var b = new Button
			{
				CustomMinimumSize = new Vector2(230, 40),
				Text = p.Consumed ? $"{p.Title}  (used)" : $"Pop: {p.Title}",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				Disabled = p.Consumed || _pendingReward != null,
			};
			b.AddThemeFontSizeOverride("font_size", 14);
			b.AddThemeColorOverride("font_color", new Color(0.7f, 1.0f, 0.8f));
			b.Pressed += () => UsePotion(p);
			_potionBox.AddChild(b);
		}
	}

	// Pop a potion: fire its one-shot effect against the live energy pool / deck, then refresh. The
	// potion marks itself consumed (Apply refuses a second use), so the button disables on the rebuild.
	private void UsePotion(Potion potion)
	{
		if (_pendingReward != null)
			return;
		if (potion.Apply(_energy, _deck))
			Refresh();   // rebuilds hand (a Draw potion may have added cards) + inventory + energy
	}
}
