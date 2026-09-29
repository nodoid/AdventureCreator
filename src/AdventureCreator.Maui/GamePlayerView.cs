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
    private readonly Button gameMenuButton;
    private int historyIndex;
    private readonly RowDefinition pictureRow = new(GridLength.Auto);

    private GameEngine? engine;
    private Color textColor = Color.FromArgb(Theme.GameText);
    private Color accent = Theme.Accent;
    private Label? currentLine;
    private bool busy;

    public AudioService Audio { get; } = new();
    public Adventure? Adventure => engine?.Adventure;
    public GameEngine? Engine => engine;

    /// <summary>Raised when the game asks to quit (the Player app closes, the Studio closes its test window).</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Raised after each command's output has been shown.</summary>
    public event EventHandler? TurnCompleted;

    /// <summary>Show direction/look/inventory buttons (useful on phones).</summary>
    public bool ShowShortcuts
    {
        get => shortcuts.IsVisible;
        set => shortcuts.IsVisible = value;
    }

    /// <summary>Save the position automatically after every move so players can continue later (Player app).</summary>
    public bool AutosaveEnabled { get; set; } = true;

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

        var menu = new Button { Text = "☰", WidthRequest = 40, Padding = 0, FontSize = 16, BackgroundColor = Colors.Transparent, Margin = new Thickness(8, 0, 0, 0) };
        SemanticProperties.SetDescription(menu, "Game menu: save, load, restart, undo, hint, sound");
        ToolTipProperties.SetText(menu, "Save, load, restart…");
        menu.Clicked += async (_, _) => await ShowGameMenuAsync();
        gameMenuButton = menu;
        var status = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, Padding = new Thickness(14, 6) };
        status.Add(statusRoom, 0);
        status.Add(statusScore, 1);
        status.Add(menu, 2);

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
        var bg = PictureImages.ParseColor(s.BackgroundColor, Color.FromArgb(Theme.GameBackground));
        textColor = PictureImages.ParseColor(s.TextColor, Color.FromArgb(Theme.GameText));
        // Keep headings readable on both light and dark game backgrounds.
        accent = bg.GetLuminosity() > 0.5f ? Theme.Accent : Color.FromArgb("#E0B050");
        BackgroundColor = bg;
        root.BackgroundColor = bg;
        statusRoom.TextColor = textColor;
        statusScore.TextColor = textColor.WithAlpha(0.8f);
        gameMenuButton.TextColor = textColor;
        input.TextColor = textColor;
        input.PlaceholderColor = textColor.WithAlpha(0.45f);
        input.BackgroundColor = Theme.Shade(bg, 0.04f);
        root.Children.OfType<Grid>().First().BackgroundColor = Theme.Shade(bg, 0.05f);
        foreach (var b in shortcuts.Children.OfType<Button>())
        {
            b.BackgroundColor = Theme.Shade(bg, 0.1f);
            b.TextColor = textColor;
        }
    }

    // ------------------------------------------------------------------ public API

    public void Load(Adventure adventure, ISaveStorage? saves = null)
    {
        Audio.StopAll();
        engine = new GameEngine(adventure) { HostHandlesSaveDialogs = true };
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
        if (AutosaveEnabled) engine.Autosave();
        TurnCompleted?.Invoke(this, EventArgs.Empty);
        input.Focus();
    }

    public void Stop() => Audio.StopAll();

    // ------------------------------------------------------------------ saving and loading

    private Page? HostPage
    {
        get
        {
            Element? e = this;
            while (e != null && e is not Page) e = e.Parent;
            return e as Page ?? Window?.Page;
        }
    }

    /// <summary>Asks for a name and saves the current position.</summary>
    public async Task ShowSaveDialogAsync()
    {
        if (engine == null || HostPage is not { } page) return;
        if (engine.IsGameOver) { await page.DisplayAlertAsync("Save game", "The game is over – there's nothing to save.", "OK"); return; }
        var suggestion = $"{engine.CurrentRoom?.Name} (turn {engine.State.Turns})";
        var name = await page.DisplayPromptAsync("Save game", "Name this saved position:", "Save", "Cancel", initialValue: suggestion, maxLength: 60);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Replace("\"", "'").Trim();
        if (engine.HasSave(name) && !await page.DisplayAlertAsync("Save game", $"Replace the saved game “{name}”?", "Replace", "Cancel")) return;
        try
        {
            var save = engine.SaveToSlot(name);
            AddParagraph($"Game saved as “{save.Name}”.", TextStyle.System);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Save failed", ex.Message, "OK");
        }
        await ScrollToEndAsync();
    }

    /// <summary>Lists saved positions (newest first) and restores the chosen one, or deletes saves.</summary>
    public async Task ShowLoadDialogAsync()
    {
        if (engine == null || HostPage is not { } page) return;
        var saves = engine.ListSaves();
        if (saves.Count == 0) { await page.DisplayAlertAsync("Load game", "There are no saved games yet.", "OK"); return; }
        const string deleteOption = "Delete a saved game…";
        var labels = saves.Select(s => s.Summary).ToList();
        var choice = await page.DisplayActionSheetAsync("Load game", "Cancel", null, labels.Append(deleteOption).ToArray());
        if (choice == deleteOption)
        {
            var del = await page.DisplayActionSheetAsync("Delete which saved game?", "Cancel", null, labels.ToArray());
            var victim = saves.FirstOrDefault(s => s.Summary == del);
            if (victim != null && await page.DisplayAlertAsync("Delete", $"Delete “{victim.Name}”?", "Delete", "Cancel"))
                engine.DeleteSave(victim.Name);
            return;
        }
        var chosen = saves.FirstOrDefault(s => s.Summary == choice);
        if (chosen == null) return;
        Audio.StopAll();
        await RenderAsync(engine.Submit($"restore \"{chosen.Name}\""));
        TurnCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>If an autosave exists, offers to continue from it. Returns true if the player continued.</summary>
    public async Task<bool> OfferContinueAsync()
    {
        if (engine == null || HostPage is not { } page || engine.ReadSave(GameEngine.AutosaveSlot) is not { Turns: > 0 } auto) return false;
        if (!await page.DisplayAlertAsync("Continue?", $"Continue where you left off?\n{auto.RoomName}, turn {auto.Turns}, score {auto.Score}/{auto.MaxScore}", "Continue", "New game"))
            return false;
        transcript.Children.Clear();
        currentLine = null;
        await RenderAsync(engine.Submit("restore autosave"));
        TurnCompleted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The ☰ menu: essential game commands, for touch screens.</summary>
    public async Task ShowGameMenuAsync()
    {
        if (engine == null || HostPage is not { } page) return;
        var choice = await page.DisplayActionSheetAsync(engine.Adventure.Title, "Cancel", null,
            "Save game…", "Load game…", "Undo last move", "Hint", "Restart", Audio.Muted ? "Sound on" : "Sound off");
        switch (choice)
        {
            case "Save game…": await ShowSaveDialogAsync(); break;
            case "Load game…": await ShowLoadDialogAsync(); break;
            case "Undo last move": await SubmitAsync("undo"); break;
            case "Hint": await SubmitAsync("hint"); break;
            case "Restart":
                if (await page.DisplayAlertAsync("Restart", "Start the game again from the beginning?", "Restart", "Cancel")) await SubmitAsync("restart");
                break;
            case "Sound on": Audio.Muted = false; break;
            case "Sound off": Audio.Muted = true; Audio.StopAll(); break;
        }
    }

    private async Task ScrollToEndAsync()
    {
        await Task.Yield();
        await scroller.ScrollToAsync(0, Math.Max(0, transcript.Height), false);
    }

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
                    case OutputKind.SaveRequested:
                        Dispatcher.Dispatch(async () => await ShowSaveDialogAsync());
                        break;
                    case OutputKind.RestoreRequested:
                        Dispatcher.Dispatch(async () => await ShowLoadDialogAsync());
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
        if (parts.Length >= 6) statusScore.Text = $"Health {parts[4]}/{parts[5]}   " + statusScore.Text;
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
