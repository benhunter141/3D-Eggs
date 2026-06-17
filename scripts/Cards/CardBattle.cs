using Godot;

// Card battler front-end (M12). Chunk 32 built the StS-style draw/hand/discard UI over a Deck;
// Chunk 33 adds REAL play onto a small 3D battlefield. The scene is now a Node3D world (ground +
// fixed top-down camera + a line of seed enemies) with the card UI on a CanvasLayer above it.
//
// Playing a card is now a two-step aim, routed through CardPlay:
//   • Click a UNIT card  → it becomes "pending"; the next click on the GROUND spawns that unit there.
//   • Click an ACTION card → it becomes "pending"; the next click on a FRIENDLY UNIT makes it act.
// A right-click (or clicking nothing valid) cancels the pending card. On a successful play the card
// goes to discard and its energy is spent (shown, not yet GATED — that's Chunk 35). End Turn dumps
// the hand, refills energy, and deals a fresh hand; Draw pulls one more so the pile cycle still reads.
public partial class CardBattle : Node3D, ICardField
{
	[Export] public int HandSize = 5;
	[Export] public int StartingEnergy = 3;

	private readonly Deck _deck = new();
	private int _energy;
	private Card _pending;          // card awaiting a target click (null = nothing selected)

	private Label _energyLabel;
	private Label _promptLabel;
	private Label _drawCount;
	private Label _discardCount;
	private HBoxContainer _handBox;

	private Camera3D _camera;
	private Node3D _units;           // parent for every spawned/seed unit

	public override void _Ready()
	{
		_energyLabel = GetNode<Label>("Ui/Root/EnergyLabel");
		_promptLabel = GetNode<Label>("Ui/Root/PromptLabel");
		_handBox = GetNode<HBoxContainer>("Ui/Root/HandBox");
		_drawCount = GetNode<Label>("Ui/Root/DrawPanel/DrawCount");
		_discardCount = GetNode<Label>("Ui/Root/DiscardPanel/DiscardCount");
		GetNode<Button>("Ui/Root/Buttons/DrawButton").Pressed += OnDrawOne;
		GetNode<Button>("Ui/Root/Buttons/EndTurnButton").Pressed += OnEndTurn;

		_camera = GetNode<Camera3D>("Camera3D");
		_units = GetNode<Node3D>("Units");

		_deck.LoadStarter(CardLibrary.StarterDeck());
		_energy = StartingEnergy;
		_deck.Draw(HandSize);
		Refresh();
	}

	// World clicks resolve a pending card. Card-button clicks are handled by the buttons
	// themselves (they consume the input), so a left-click that reaches here is a battlefield aim.
	public override void _UnhandledInput(InputEvent @event)
	{
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

	// End of turn: dump the hand to discard, refill energy, and deal a fresh hand.
	private void OnEndTurn()
	{
		_pending = null;
		_deck.DiscardHand();
		_energy = StartingEnergy;
		_deck.Draw(HandSize);
		UpdatePrompt();
		Refresh();
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
