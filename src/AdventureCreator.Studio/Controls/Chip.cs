namespace AdventureCreator.Studio.Controls;

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

    public event EventHandler? Clicked;

    public Chip(string text, string? tooltip = null)
    {
        label = new Label { Text = text, FontSize = 12, TextColor = Maui.Theme.Text, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
        Content = label;
        Padding = new Thickness(9, 4);
        Margin = new Thickness(0, 0, 4, 4);
        StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 };
        if (tooltip != null) ToolTipProperties.SetText(this, tooltip);
        GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Clicked?.Invoke(this, EventArgs.Empty)) });
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

    private void Update()
    {
        BackgroundColor = selected ? Active : hovering ? Hover : Normal;
        Stroke = selected ? Active : Maui.Theme.ChipBorder;
        label.TextColor = selected ? Colors.White : Maui.Theme.Text;
    }
}
