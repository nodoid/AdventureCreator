using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;

namespace AdventureCreator.Maui;

/// <summary>
/// A complete, self-contained adventure player: status bar, picture, scrolling transcript, command entry with history,
/// optional touch shortcuts, and audio. Used by the standalone Player app and by the Studio's test window.
/// </summary>
public sealed class GamePlayerView : ContentView
{
    private const int MaxParagraphs = 400;

    private readonly Label statusRoom = new() { FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
    private readonly Label statusScore = new() { HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
    private readonly Image picture = new() { Aspect = Aspect.AspectFit, IsVisible = false };
    private readonly VerticalStackLayout transcript = new() { Spacing = 6, Padding = new Thickness(14, 10) };
    private readonly ScrollView scroller = new();
    private readonly Entry input = new() { Placeholder = "What now?", ReturnType = ReturnType.Send, IsSpellCheckEnabled = false, IsTextPredictionEnabled = false, Keyboard = Keyboard.Plain };
    private readonly Grid shortcuts = new() { ColumnSpacing = 4, IsVisible = false };
    private readonly Grid root;
    private readonly List<string> history = new();
    private int historyIndex;
    private readonly RowDefinition pictureRow = new(GridLength.Auto);

    private GameEngine? engine;
    private Color textColor = Colors.WhiteSmoke;
    private Color accent = Color.FromArgb("#E0B050");
    private Label? currentLine;
    private bool busy;

    public AudioService Audio { get; } = new();
    public Adventure? Adventure => engine?.Adventure;
    public GameEngine? Engine => engine;

    /// <summary>Raised when the game asks to quit (the Player app closes, the Studio closes its test window).</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Show direction/look/inventory buttons (useful on phones).</summary>
    public bool ShowShortcuts
    {
        get => shortcuts.IsVisible;
        set => shortcuts.IsVisible = value;
    }

    /// <summary>Maximum picture height as a fraction of the view height (0..1).</summary>
    public double PictureHeightFraction { get; set; } = 0.42;

    public GamePlayerView()
    {
        scroller.Content = transcript;
        input.Completed += async (_, _) => await SubmitAsync();

        var send = new Button { Text = "↵", WidthRequest = 48, Padding = 0 };
        send.Clicked += async (_, _) => await SubmitAsync();
        SemanticProperties.SetDescription(send, "Send command");

        var inputRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6, Padding = new Thickness(10, 6) };
        inputRow.Add(input, 0);
        inputRow.Add(send, 1);

        BuildShortcuts();

        var status = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(14, 6) };
        status.Add(statusRoom, 0);
        status.Add(statusScore, 1);

        root = new Grid
        {
            RowDefinitions = { new(GridLength.Auto), pictureRow, new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) },
        };
        root.Add(status, 0, 0);
        root.Add(picture, 0, 1);
        root.Add(scroller, 0, 2);
        root.Add(shortcuts, 0, 3);
        root.Add(inputRow, 0, 4);
        Content = root;

        SizeChanged += (_, _) => UpdatePictureSize();
        ApplyTheme(new GameSettings());

        // Command history with the arrow keys on desktop (Windows and Mac Catalyst).
        input.HandlerChanged += (_, _) => KeyboardSupport.AttachHistory(input, Previous, Next);
    }

    private void BuildShortcuts()
    {
        string[] commands = { "N", "S", "E", "W", "U", "D", "Look", "Inv" };
        for (int i = 0; i < commands.Length; i++)
        {
            shortcuts.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var cmd = commands[i];
            var b = new Button { Text = cmd, Padding = new Thickness(0, 4), FontSize = 13, MinimumHeightRequest = 34 };
            b.Clicked += async (_, _) =>
            {
                input.Text = cmd.ToLowerInvariant() == "inv" ? "inventory" : cmd.ToLowerInvariant();
                await SubmitAsync();
            };
            shortcuts.Add(b, i);
        }
        shortcuts.Padding = new Thickness(10, 0);
    }

    private void ApplyTheme(GameSettings s)
    {
        var bg = PictureImages.ParseColor(s.BackgroundColor, Color.FromArgb("#101018"));
        textColor = PictureImages.ParseColor(s.TextColor, Colors.WhiteSmoke);
        BackgroundColor = bg;
        root.BackgroundColor = bg;
        statusRoom.TextColor = textColor;
        statusScore.TextColor = textColor.WithAlpha(0.8f);
        input.TextColor = textColor;
        input.PlaceholderColor = textColor.WithAlpha(0.45f);
        input.BackgroundColor = bg.AddLuminosity(0.06f);
        root.Children.OfType<Grid>().First().BackgroundColor = bg.AddLuminosity(0.04f);
        foreach (var b in shortcuts.Children.OfType<Button>())
        {
            b.BackgroundColor = bg.AddLuminosity(0.1f);
            b.TextColor = textColor;
        }
    }

    // ------------------------------------------------------------------ public API

    public void Load(Adventure adventure, ISaveStorage? saves = null)
    {
        Audio.StopAll();
        engine = new GameEngine(adventure);
        if (saves != null) engine.SaveStorage = saves;
        ApplyTheme(adventure.Settings);
        transcript.Children.Clear();
        currentLine = null;
        picture.IsVisible = false;
        AddParagraph(adventure.Title, TextStyle.RoomTitle, 1.35);
        if (!string.IsNullOrWhiteSpace(adventure.Author)) AddParagraph("by " + adventure.Author, TextStyle.System);
        _ = RenderAsync(engine.Start());
        input.Focus();
    }

    public async Task SubmitAsync(string? command = null)
    {
        if (engine == null || busy) return;
        var text = command ?? input.Text ?? "";
        input.Text = "";
        if (text.Trim().Length > 0)
        {
            history.Add(text);
            historyIndex = history.Count;
        }
        AddParagraph((engine.Adventure.Settings.Prompt ?? "> ") + text, TextStyle.Echo);
        await RenderAsync(engine.Submit(text));
        input.Focus();
    }

    public void Stop() => Audio.StopAll();

    /// <summary>Puts the previous command from the history into the input box (menu shortcut ⌘↑ / Ctrl+↑).</summary>
    public void RecallPrevious()
    {
        if (Previous() is { } t) { input.Text = t; input.CursorPosition = t.Length; }
    }

    /// <summary>Puts the next command from the history into the input box (menu shortcut ⌘↓ / Ctrl+↓).</summary>
    public void RecallNext()
    {
        if (Next() is { } t) { input.Text = t; input.CursorPosition = t.Length; }
    }

    public void FocusInput() => input.Focus();

    private string? Previous()
    {
        if (history.Count == 0) return null;
        historyIndex = Math.Max(0, historyIndex - 1);
        return history[historyIndex];
    }

    private string? Next()
    {
        if (history.Count == 0) return null;
        historyIndex = Math.Min(history.Count, historyIndex + 1);
        return historyIndex >= history.Count ? "" : history[historyIndex];
    }

    // ------------------------------------------------------------------ output

    private async Task RenderAsync(TurnResult result)
    {
        busy = true;
        try
        {
            foreach (var e in result.Events)
            {
                switch (e.Kind)
                {
                    case OutputKind.Text:
                        AppendText(e.Text ?? "", e.Style);
                        break;
                    case OutputKind.Picture:
                        ShowPicture(e.Id);
                        break;
                    case OutputKind.PlaySound:
                        Audio.Play(engine!.Adventure, e.Id, e.Loop);
                        break;
                    case OutputKind.StopSound:
                        Audio.StopAll();
                        break;
                    case OutputKind.ClearScreen:
                        transcript.Children.Clear();
                        currentLine = null;
                        break;
                    case OutputKind.Pause:
                        await Task.Delay(Math.Clamp(e.Milliseconds <= 0 ? 1200 : e.Milliseconds, 0, 5000));
                        break;
                    case OutputKind.Beep:
                        break;
                    case OutputKind.Status:
                        UpdateStatus(e.Text);
                        break;
                    case OutputKind.GameOver:
                        AddParagraph("Type RESTART, RESTORE, UNDO or QUIT.", TextStyle.System);
                        break;
                    case OutputKind.Quit:
                        Audio.StopAll();
                        QuitRequested?.Invoke(this, EventArgs.Empty);
                        break;
                }
            }
        }
        finally
        {
            busy = false;
        }
        TrimTranscript();
        await Task.Yield();
        await scroller.ScrollToAsync(0, Math.Max(0, transcript.Height), false);
    }

    private void UpdateStatus(string? status)
    {
        var parts = (status ?? "").Split('|');
        statusRoom.Text = parts.Length > 0 ? parts[0] : "";
        statusScore.Text = parts.Length >= 4 ? $"Score {parts[1]}/{parts[2]}   Turns {parts[3]}" : "";
        if (engine != null && statusRoom.Parent is View bar) bar.IsVisible = engine.Adventure.Settings.ShowStatusBar;
    }

    private void ShowPicture(string? id)
    {
        if (engine == null) return;
        var source = PictureImages.Source(engine.Adventure, id);
        picture.Source = source;
        picture.IsVisible = source != null;
        UpdatePictureSize();
    }

    private void UpdatePictureSize()
    {
        if (Height <= 0) return;
        picture.HeightRequest = picture.IsVisible ? Math.Max(120, Height * PictureHeightFraction) : 0;
    }

    private void AppendText(string text, TextStyle style)
    {
        // Text arrives as paragraphs terminated by \n, or fragments to be joined (PAWS "MES").
        bool endsParagraph = text.EndsWith('\n');
        var body = endsParagraph ? text[..^1] : text;
        if (currentLine != null && style == TextStyle.Normal)
        {
            currentLine.Text += body;
            if (endsParagraph) currentLine = null;
            return;
        }
        var label = AddParagraph(body, style);
        currentLine = endsParagraph || style != TextStyle.Normal ? null : label;
    }

    private Label AddParagraph(string text, TextStyle style, double scale = 1)
    {
        var label = new Label
        {
            Text = text,
            TextColor = style switch
            {
                TextStyle.RoomTitle => accent,
                TextStyle.System => textColor.WithAlpha(0.7f),
                TextStyle.Echo => textColor.WithAlpha(0.6f),
                TextStyle.Error => Colors.IndianRed,
                TextStyle.Emphasis => accent,
                _ => textColor,
            },
            FontAttributes = style switch
            {
                TextStyle.RoomTitle or TextStyle.Emphasis => FontAttributes.Bold,
                TextStyle.System or TextStyle.Echo => FontAttributes.Italic,
                _ => FontAttributes.None,
            },
            FontSize = 16 * scale * (style == TextStyle.RoomTitle ? 1.1 : 1),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        if (!string.IsNullOrWhiteSpace(engine?.Adventure.Settings.FontFamily)) label.FontFamily = engine.Adventure.Settings.FontFamily;
        if (style == TextStyle.RoomTitle) label.Margin = new Thickness(0, 8, 0, 0);
        transcript.Children.Add(label);
        return label;
    }

    private void TrimTranscript()
    {
        while (transcript.Children.Count > MaxParagraphs) transcript.Children.RemoveAt(0);
    }
}
