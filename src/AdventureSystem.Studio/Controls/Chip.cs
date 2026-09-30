namespace AdventureSystem.Studio.Controls;

/// <summary>
/// A small toggle "chip" (label in a rounded box). Used instead of Button where the selected state must be visible:
/// native macOS push buttons ignore background colours.
/// </summary>
public sealed class Chip : Border
{
    private static readonly Color Normal = Maui.Theme.Chip;
    private static readonly Color Hover = Maui.Theme.ChipHover;
    private static readonly Color Active = Maui.Theme.Accent;
    private readonly Label label;
    private bool selected;
    private bool hovering;
    private bool pressed;

    public event EventHandler? Clicked;

    public Chip(string text, string? tooltip = null)
    {
        label = new Label { Text = text, FontSize = TouchMetrics.Pick(12, 15), TextColor = Maui.Theme.Text, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
        Content = label;
        Padding = TouchMetrics.IsTouch ? new Thickness(14, 8) : new Thickness(9, 4);
        if (TouchMetrics.IsTouch) MinimumHeightRequest = MinimumWidthRequest = TouchMetrics.MinTarget;
        Margin = new Thickness(0, 0, 4, 4);
        StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 };
        if (tooltip != null) ToolTipProperties.SetText(this, tooltip);
        // No tooltips on touch: VoiceOver reads the tip instead, and names glyph-only chips (▶, ✕) by it.
        if (TouchMetrics.IsTouch && tooltip != null)
        {
            if (!text.Any(char.IsLetter)) SemanticProperties.SetDescription(this, tooltip);
            else SemanticProperties.SetHint(this, tooltip);
        }
        GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { if (TouchMetrics.IsTouch) Flash(); Clicked?.Invoke(this, EventArgs.Empty); }) });
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => { hovering = true; Update(); };
        pointer.PointerExited += (_, _) => { hovering = false; Update(); };
        GestureRecognizers.Add(pointer);
        Update();
    }

    public string Text
    {
        get => label.Text;
        set => label.Text = value;
    }

    public bool IsSelected
    {
        get => selected;
        set { selected = value; Update(); }
    }

    /// <summary>Touch has no hover, so show the tap briefly instead.</summary>
    private void Flash()
    {
        pressed = true;
        Update();
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(150), () => { pressed = false; Update(); return false; });
    }

    private void Update()
    {
        BackgroundColor = selected ? Active : hovering || pressed ? Hover : Normal;
        Stroke = selected ? Active : Maui.Theme.ChipBorder;
        label.TextColor = selected ? Colors.White : Maui.Theme.Text;
    }
}
