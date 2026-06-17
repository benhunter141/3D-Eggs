using Godot;

// Card battler front-end (M12). The scene is now a real little BATTLEFIELD (a Node3D world with
// ground + a few enemies) under the StS-style card HUD, so cards resolve to actual play:
//
//   • Chunk 32 built the deck model + the draw / hand / discard HUD (draw count bottom-left, the
//     live hand centre, discard count bottom-right, Draw / End-Turn controls + an Energy readout).
//   • Chunk 33 wires PLAY TARGETING: click a hand card to PICK it up, then click the battlefield —
//       - a UNIT card spawns its friendly unit where you clicked (CardResolver.SpawnUnit);
//       - an ACTION card makes the friendly unit nearest your click perform its action
//         (CardResolver.PerformAction).
//     A successful play discards the card; right-click (or re-clicking the held card) cancels.
//
// Energy is shown but NOT yet spent — gating arrives in Chunk 35. The deck/pile bookkeeping stays
// in the pure Deck model; this is just the view + the input plumbing over it.
public partial class CardBattle : Node3D
{
	[Export] public int HandSize = 5;
	[Export] public int StartingEnergy = 3;

	private readonly Deck _deck = new();
	private int _energy;

	private Label _drawCount;
	private Label _discardCount;
	private Label _energyLabel;
	private Label _prompt;
	private HBoxContainer _handBox;
	private Node _battlefield;

	private Card _selected;   // the card picked up and awaiting a battlefield target (null = none)

	public override void _Ready()
	{
		_energyLabel  = GetNode<Label>("Hud/Root/EnergyLabel");
		_handBox      = GetNode<HBoxContainer>("Hud/Root/HandBox");
		_drawCount    = GetNode<Label>("Hud/Root/DrawPanel/DrawCount");
		_discardCount = GetNode<Label>("Hud/Root/DiscardPanel/DiscardCount");
		_prompt       = GetNode<Label>("Hud/Root/PromptLabel");
		_battlefield  = GetNode<Node>("Battlefield");
		GetNode<Button>("Hud/Root/Buttons/DrawButton").Pressed += OnDrawOne;
		GetNode<Button>("Hud/Root/Buttons/EndTurnButton").Pressed += OnEndTurn;

		_deck.LoadStarter(CardLibrary.StarterDeck());
		_energy = StartingEnergy;
		_deck.Draw(HandSize);
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
		CancelSelection();
		_deck.DiscardHand();
		_energy = StartingEnergy;
		_deck.Draw(HandSize);
		Refresh();
	}

	// Clicking a hand card PICKS IT UP for targeting (clicking the held one again cancels).
	private void OnCardClicked(Card card)
	{
		_selected = _selected == card ? null : card;
		Refresh();
	}

	// Battlefield clicks (reach here only when not consumed by the card HUD): left-click resolves
	// the held card at the clicked ground point, right-click cancels the pickup.
	public override void _UnhandledInput(InputEvent @event)
	{
		if (_selected == null || @event is not InputEventMouseButton mb || !mb.Pressed)
			return;

		if (mb.ButtonIndex == MouseButton.Right)
		{
			CancelSelection();
			Refresh();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (mb.ButtonIndex == MouseButton.Left && TryGroundPoint(mb.Position, out Vector3 point))
		{
			ResolveSelectedAt(point);
			GetViewport().SetInputAsHandled();
		}
	}

	// Resolve the held card at a battlefield point: spawn (Unit) or act on the nearest friendly
	// (Action). Only discard the card if the play actually happened.
	private void ResolveSelectedAt(Vector3 point)
	{
		Card card = _selected;
		bool played;

		if (card.Kind == Card.CardKind.Unit)
		{
			played = CardResolver.SpawnUnit(card, point, _battlefield) != null;
		}
		else
		{
			Unit target = NearestFriendly(point);
			played = target != null && CardResolver.PerformAction(card, target);
		}

		if (played)
			_deck.Discard(card);
		else
			GD.Print($"[CardBattle] '{card.Title}' had no valid target there — try again");

		CancelSelection();
		Refresh();
	}

	private void CancelSelection() => _selected = null;

	// Nearest living player-team unit to a battlefield point (the target an Action card acts on).
	private Unit NearestFriendly(Vector3 point)
	{
		Unit best = null;
		float bestSq = float.MaxValue;
		System.Collections.Generic.IReadOnlyList<Unit> friends = UnitRegistry.OnTeam(Unit.TeamId.Player);
		for (int i = 0; i < friends.Count; i++)
		{
			Unit u = friends[i];
			if (u == null || !IsInstanceValid(u) || u.IsDead)
				continue;
			float d = point.DistanceSquaredTo(u.GlobalPosition);
			if (d < bestSq)
			{
				bestSq = d;
				best = u;
			}
		}
		return best;
	}

	// Cast the mouse ray onto the battlefield ground plane (Y = 0). Returns false if it can't hit.
	private bool TryGroundPoint(Vector2 screenPos, out Vector3 point)
	{
		point = Vector3.Zero;
		Camera3D cam = GetViewport().GetCamera3D();
		if (cam == null)
			return false;

		Vector3 from = cam.ProjectRayOrigin(screenPos);
		Vector3 dir = cam.ProjectRayNormal(screenPos);
		if (Mathf.IsZeroApprox(dir.Y))
			return false;
		float t = (0f - from.Y) / dir.Y;
		if (t < 0f)
			return false;
		point = from + dir * t;
		return true;
	}

	// Rebuild the hand row and refresh the counters / prompt from the deck's current state.
	private void Refresh()
	{
		foreach (Node c in _handBox.GetChildren())
			c.QueueFree();

		foreach (Card card in _deck.Hand)
			_handBox.AddChild(MakeCardButton(card));

		_drawCount.Text = $"DRAW\n{_deck.DrawPile.Count}";
		_discardCount.Text = $"DISCARD\n{_deck.DiscardPile.Count}";
		_energyLabel.Text = $"ENERGY   {_energy} / {StartingEnergy}";

		_prompt.Visible = _selected != null;
		if (_selected != null)
			_prompt.Text = _selected.Kind == Card.CardKind.Unit
				? $"Click the battlefield to deploy {_selected.Title}   (right-click to cancel)"
				: $"Click a friendly unit to {_selected.Title}   (right-click to cancel)";
	}

	// A single hand card rendered as a clickable panel-button; the held card is highlighted.
	private Button MakeCardButton(Card card)
	{
		var b = new Button
		{
			CustomMinimumSize = new Vector2(156, 210),
			Text = $"{card.Title}\n\n[ {card.Kind} ]\nCost {card.EnergyCost}\n\n{card.Description}",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			ClipText = false,
			ToggleMode = true,
			ButtonPressed = card == _selected,   // show the picked-up card as pressed
		};
		b.AddThemeFontSizeOverride("font_size", 15);
		b.AddThemeColorOverride("font_color",
			card.Kind == Card.CardKind.Unit
				? new Color(0.6f, 0.8f, 1.0f)        // Unit cards read blue
				: new Color(1.0f, 0.85f, 0.55f));    // Action cards read amber
		b.Pressed += () => OnCardClicked(card);
		return b;
	}
}
