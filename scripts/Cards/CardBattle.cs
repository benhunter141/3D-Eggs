using Godot;

// Card battler front-end (M12, Chunk 32): the StS-style draw / hand / discard UI over a Deck.
// Shows the draw-pile count (bottom-left), the live hand as card panels (centre), and the
// discard-pile count (bottom-right), plus Draw and End Turn controls so the cycle — including
// the auto-reshuffle when the draw pile empties — is visible. Card PLAY (Unit→location vs
// Action→friendly-unit targeting) lands in Chunk 33 and energy gating in Chunk 35; here a click
// just discards the card so the pile cycle reads at a glance, and Energy is shown but not spent.
public partial class CardBattle : CanvasLayer
{
	[Export] public int HandSize = 5;
	[Export] public int StartingEnergy = 3;

	private readonly Deck _deck = new();
	private int _energy;

	private Label _drawCount;
	private Label _discardCount;
	private Label _energyLabel;
	private HBoxContainer _handBox;

	public override void _Ready()
	{
		_energyLabel = GetNode<Label>("Root/EnergyLabel");
		_handBox = GetNode<HBoxContainer>("Root/HandBox");
		_drawCount = GetNode<Label>("Root/DrawPanel/DrawCount");
		_discardCount = GetNode<Label>("Root/DiscardPanel/DiscardCount");
		GetNode<Button>("Root/Buttons/DrawButton").Pressed += OnDrawOne;
		GetNode<Button>("Root/Buttons/EndTurnButton").Pressed += OnEndTurn;

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
		_deck.DiscardHand();
		_energy = StartingEnergy;
		_deck.Draw(HandSize);
		Refresh();
	}

	// Chunk 32: "playing" a card just sends it to the discard pile so the cycle is visible.
	// (Unit/Action targeting comes in Chunk 33, energy cost in Chunk 35.)
	private void OnCardPlayed(Card card)
	{
		if (_deck.Discard(card))
			Refresh();
	}

	// Rebuild the hand row and refresh the three counters from the deck's current piles.
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

	// A single hand card rendered as a clickable panel-button.
	private Button MakeCardButton(Card card)
	{
		var b = new Button
		{
			CustomMinimumSize = new Vector2(156, 210),
			Text = $"{card.Title}\n\n[ {card.Kind} ]\nCost {card.EnergyCost}\n\n{card.Description}",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			ClipText = false,
		};
		b.AddThemeFontSizeOverride("font_size", 15);
		b.AddThemeColorOverride("font_color",
			card.Kind == Card.CardKind.Unit
				? new Color(0.6f, 0.8f, 1.0f)        // Unit cards read blue
				: new Color(1.0f, 0.85f, 0.55f));    // Action cards read amber
		b.Pressed += () => OnCardPlayed(card);
		return b;
	}
}
