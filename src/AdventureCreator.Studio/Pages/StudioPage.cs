using AdventureCreator.Core.Audio;
using System.Collections;
using System.Collections.ObjectModel;
using AdventureCreator.Core.Engine;
using AdventureCreator.Core.Model;
using AdventureCreator.Core.Packaging;
using AdventureCreator.Core.Samples;
using AdventureCreator.Importers.Common;
using AdventureCreator.Maui;
using Theme = AdventureCreator.Maui.Theme;
using AdventureCreator.Studio.Controls;
using AdventureCreator.Studio.Services;
using CommunityToolkit.Maui.Storage;

namespace AdventureCreator.Studio.Pages;

public enum Section { Game, Map, Rooms, Items, Puzzles, Triggers, Events, Variables, Commands, Vocabulary, Pictures, Sounds, Messages, TestPlay }

/// <summary>
/// The Studio's main window: a desktop-style source list on the left, a master list, and a detail editor,
/// with a full menu bar and keyboard shortcuts.
/// </summary>
public sealed class StudioPage : ContentPage
{
    private StudioDocument document;
    private EditorContext ctx;
    private Section section = Section.Game;
    private object? selected;

    private readonly VerticalStackLayout sidebar = new() { Spacing = 2, Padding = new Thickness(8, 12) };
    private readonly Dictionary<Section, (Border Box, Label Label)> sectionButtons = new();
    private readonly Grid listPane;
    private readonly CollectionView list;
    private readonly Entry search = new() { Placeholder = "Filter", ClearButtonVisibility = ClearButtonVisibility.WhileEditing };
    private readonly Label listTitle = new() { FontAttributes = FontAttributes.Bold, FontSize = 15, VerticalOptions = LayoutOptions.Center };
    private readonly ObservableCollection<ListRow> rows = new();
    private readonly ContentView detail = new();
    private readonly Label statusLeft = new() { FontSize = 12, VerticalOptions = LayoutOptions.Center };
    private readonly Label statusRight = new() { FontSize = 12, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
    private readonly ColumnDefinition listColumn = new(new GridLength(290));
    private GamePlayerView? testPlayer;
    private bool refreshQueued;

    private static readonly Color SidebarBg = Theme.Sidebar;
    private static readonly Color PaneBg = Theme.Pane;
    private static readonly Color Accent = Theme.Accent;

    public sealed class ListRow
    {
        public required object Item { get; init; }
        public required string Title { get; init; }
        public string Subtitle { get; init; } = "";
    }

    public StudioPage(StudioDocument doc)
    {
        document = doc;
        ctx = new EditorContext(doc, () => this);
        BackgroundColor = Theme.Window;

        // ---------------- sidebar
        foreach (var s in Enum.GetValues<Section>())
        {
            // Source-list row (left aligned, highlight on selection and hover) rather than push buttons.
            var label = new Label { Text = SectionTitle(s), TextColor = Theme.Text, FontSize = 14, VerticalOptions = LayoutOptions.Center };
            var box = new Border
            {
                Content = label,
                Padding = new Thickness(10, 6),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                BackgroundColor = Colors.Transparent,
            };
            var section0 = s;
            box.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => ShowSection(section0)) });
            var hover = new PointerGestureRecognizer();
            hover.PointerEntered += (_, _) => { if (section != section0) box.BackgroundColor = Theme.Hover; };
            hover.PointerExited += (_, _) => { if (section != section0) box.BackgroundColor = Colors.Transparent; };
            box.GestureRecognizers.Add(hover);
            sectionButtons[s] = (box, label);
            if (s is Section.Rooms or Section.Pictures or Section.TestPlay)
                sidebar.Children.Add(new BoxView { HeightRequest = 1, Color = Colors.Gray.WithAlpha(0.25f), Margin = new Thickness(4, 6) });
            sidebar.Children.Add(box);
        }

        // ---------------- list pane
        list = new CollectionView
        {
            ItemsSource = rows,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() =>
            {
                var title = new Label { FontSize = 14, TextColor = Theme.Text, LineBreakMode = LineBreakMode.TailTruncation };
                title.SetBinding(Label.TextProperty, nameof(ListRow.Title));
                var sub = new Label { FontSize = 11, TextColor = Theme.SecondaryText, LineBreakMode = LineBreakMode.TailTruncation };
                sub.SetBinding(Label.TextProperty, nameof(ListRow.Subtitle));
                var stack = new VerticalStackLayout { Padding = new Thickness(10, 6), Children = { title, sub } };
                var states = new VisualStateGroup { Name = "CommonStates" };
                states.States.Add(new VisualState { Name = "Normal" });
                var sel = new VisualState { Name = "Selected" };
                sel.Setters.Add(new Setter { Property = BackgroundColorProperty, Value = Theme.Selected });
                states.States.Add(sel);
                VisualStateManager.GetVisualStateGroups(stack).Add(states);
                return stack;
            }),
        };
        list.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is ListRow row && !ReferenceEquals(row.Item, selected)) Select(row.Item);
        };
        search.TextChanged += (_, _) => RefreshList();

        var add = new Button { Text = "+", WidthRequest = 36, FontSize = 18, Padding = 0 };
        ToolTipProperties.SetText(add, "Add new");
        add.Clicked += (_, _) => AddNew();
        var dup = new Button { Text = "⧉", WidthRequest = 36, Padding = 0 };
        ToolTipProperties.SetText(dup, "Duplicate");
        dup.Clicked += (_, _) => Duplicate();
        var del = new Button { Text = "−", WidthRequest = 36, FontSize = 18, Padding = 0 };
        ToolTipProperties.SetText(del, "Delete");
        del.Clicked += async (_, _) => await DeleteSelectedAsync();

        var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 4, Padding = new Thickness(10, 8) };
        header.Add(listTitle, 0);
        header.Add(add, 1);
        header.Add(dup, 2);
        header.Add(del, 3);
        listPane = new Grid
        {
            BackgroundColor = PaneBg,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) },
        };
        listPane.Add(header, 0, 0);
        listPane.Add(new ContentView { Content = search, Padding = new Thickness(10, 0, 10, 6) }, 0, 1);
        listPane.Add(list, 0, 2);

        // ---------------- layout
        var content = new Grid
        {
            ColumnDefinitions = { new(new GridLength(200)), listColumn, new(GridLength.Star) },
        };
        content.Add(new ScrollView { Content = sidebar, BackgroundColor = SidebarBg }, 0);
        content.Add(listPane, 1);
        content.Add(detail, 2);

        var statusBar = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(12, 4), BackgroundColor = SidebarBg };
        statusBar.Add(statusLeft, 0);
        statusBar.Add(statusRight, 1);

        Content = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Children = { content } };
        ((Grid)Content).Add(statusBar, 0, 1);

        BuildMenus();
        // Windows: the navigation bar only hosts the menus; the window's own title bar already shows the title.
        if (DeviceInfo.Platform == DevicePlatform.WinUI) NavigationPage.SetTitleView(this, new ContentView());
        AttachDocument(doc);
        ShowSection(Section.Game);
#if DEBUG
        // Development aid for screenshots: AC_STUDIO_DEMO=genesis|lighthouse[:Section[:item[:command]]].
        if (Environment.GetEnvironmentVariable("AC_STUDIO_DEMO") is { Length: > 0 } demo) OpenDemo(demo);
        // AC_STUDIO_DEMO_FILE=<path>[|Section]: import a game file (e.g. a Z-code story) and open a section.
        if (Environment.GetEnvironmentVariable("AC_STUDIO_DEMO_FILE") is { Length: > 0 } demoFile)
        {
            var bits = demoFile.Split('|');
            var data = File.ReadAllBytes(bits[0]);
            if (ImporterRegistry.Detect(data, bits[0]) is { } importer)
            {
                SetDocument(new StudioDocument(importer.Import(data, bits[0]).Adventure));
                if (bits.Length > 1 && Enum.TryParse<Section>(bits[1], true, out var sec)) ShowSection(sec);
            }
        }
#endif
    }

#if DEBUG
    private void OpenDemo(string demo)
    {
        var parts = demo.Split(':');
        SetDocument(new StudioDocument(parts[0].Equals("genesis", StringComparison.OrdinalIgnoreCase) ? ExampleAdventures.Genesis() : ExampleAdventures.Lighthouse()));
        if (parts.Length < 2 || !Enum.TryParse<Section>(parts[1], true, out var s)) return;
        ShowSection(s);
        if (parts.Length < 3 || !int.TryParse(parts[2], out var index) || index < 0 || index >= rows.Count) return;
        Select(rows[index].Item);
        if (FindPictureEditor(detail) is { } editor)
        {
            if (parts.Length >= 4 && int.TryParse(parts[3], out var command)) editor.SelectCommand(command);
            if (parts.Contains("play")) editor.PlayAnimations();
        }
        if (parts.Contains("sfx")) NewSoundEffect();
        if (parts.Contains("newsound")) _ = NewLibrarySoundAsync();
    }

    private static PictureEditorView? FindPictureEditor(IView? view) => view switch
    {
        PictureEditorView e => e,
        ContentView cv => FindPictureEditor(cv.Content),
        _ => null,
    };
#endif

    // =========================================================== document

    private void AttachDocument(StudioDocument doc)
    {
        document = doc;
        ctx = new EditorContext(doc, () => this);
        doc.Changed += _ => QueueRefresh();
        doc.DirtyChanged += UpdateTitle;
        UpdateTitle();
        UpdateStatus();
    }

    private void SetDocument(StudioDocument doc)
    {
        testPlayer?.Stop();
        testPlayer = null;
        AttachDocument(doc);
        selected = null;
        ShowSection(Section.Game);
    }

    private void UpdateTitle()
    {
        var title = $"{document.DisplayName}{(document.Dirty ? " — Edited" : "")} — Adventure Creator Studio";
        Title = title;
        if (Window != null) Window.Title = title;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var a = document.Adventure;
        statusLeft.Text = document.Path ?? "Not saved yet";
        statusRight.Text = $"{a.Rooms.Count} rooms · {a.Items.Count} items · {a.Triggers.Count} triggers · {a.Puzzles.Count} puzzles · max score {a.ComputeMaxScore()}";
        foreach (var (s, b) in sectionButtons)
        {
            int? count = s switch
            {
                Section.Rooms => a.Rooms.Count, Section.Items => a.Items.Count, Section.Puzzles => a.Puzzles.Count,
                Section.Triggers => a.Triggers.Count, Section.Events => a.RandomEvents.Count, Section.Variables => a.Variables.Count, Section.Commands => a.Vocabulary.Verbs.Count,
                Section.Pictures => a.Pictures.Count, Section.Sounds => a.Sounds.Count, _ => null,
            };
            b.Label.Text = SectionTitle(s) + (count.HasValue ? $"  ({count})" : "");
        }
    }

    private void QueueRefresh()
    {
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
        {
            refreshQueued = false;
            RefreshList(keepSelection: true);
            UpdateStatus();
        });
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!document.Dirty) return true;
        var choice = await DisplayActionSheetAsync($"Save changes to “{document.DisplayName}”?", "Cancel", "Don't Save", "Save");
        if (choice == "Save") return await SaveAsync(false);
        return choice == "Don't Save";
    }

    // =========================================================== sections

    private static string SectionTitle(Section s) => s switch
    {
        Section.Game => "⚙  Game",
        Section.Map => "🗺  Map",
        Section.Rooms => "🚪  Rooms",
        Section.Items => "🗝  Items & People",
        Section.Puzzles => "🧩  Puzzles",
        Section.Triggers => "⚡  Triggers",
        Section.Events => "🎲  Random Events",
        Section.Variables => "🔢  Variables",
        Section.Commands => "💬  Commands",
        Section.Vocabulary => "📖  Vocabulary",
        Section.Pictures => "🎨  Pictures",
        Section.Sounds => "🔊  Sounds",
        Section.Messages => "✉  Messages",
        Section.TestPlay => "▶  Test Play",
        _ => s.ToString(),
    };

    private static bool HasList(Section s) => s is Section.Rooms or Section.Items or Section.Puzzles or Section.Triggers or Section.Events or Section.Variables
        or Section.Commands or Section.Pictures or Section.Sounds;

    public void ShowSection(Section s)
    {
        if (s != Section.TestPlay) testPlayer?.Stop();
        section = s;
        foreach (var (k, b) in sectionButtons)
        {
            b.Box.BackgroundColor = k == s ? Theme.Selected : Colors.Transparent;
            b.Label.TextColor = k == s ? Accent : Theme.Text;
        }
        bool hasList = HasList(s);
        listPane.IsVisible = hasList;
        listColumn.Width = hasList ? new GridLength(290) : new GridLength(0);
        listTitle.Text = SectionTitle(s)[3..].Trim();
        search.Text = "";
        selected = null;
        RefreshList();
        if (hasList)
        {
            if (rows.Count > 0) Select(rows[0].Item);
            else ShowDetail(new Label { Text = "Nothing here yet. Click + to add one.", Opacity = 0.6, Margin = 30 });
        }
        else ShowSingleEditor(s);
    }

    private IList? CurrentList => section switch
    {
        Section.Rooms => document.Adventure.Rooms,
        Section.Items => document.Adventure.Items,
        Section.Puzzles => document.Adventure.Puzzles,
        Section.Triggers => document.Adventure.Triggers,
        Section.Events => document.Adventure.RandomEvents,
        Section.Variables => document.Adventure.Variables,
        Section.Commands => document.Adventure.Vocabulary.Verbs,
        Section.Pictures => document.Adventure.Pictures,
        Section.Sounds => document.Adventure.Sounds,
        _ => null,
    };

    private ListRow MakeRow(object o) => o switch
    {
        Room r => new ListRow { Item = r, Title = string.IsNullOrWhiteSpace(r.Name) ? r.Id : r.Name, Subtitle = $"{r.Id} · {r.Exits.Count} exits{(r.IsDark ? " · dark" : "")}" },
        Item i => new ListRow { Item = i, Title = (i.Npc != null ? "🧍 " : i.IsCharacter ? "👤 " : "") + (string.IsNullOrWhiteSpace(i.Name) ? i.Id : i.Name),
            Subtitle = $"{i.Id} · {DescribeLocation(i.Location)}{(i.Npc != null ? " · " + i.Npc.Movement.ToString().ToLowerInvariant() + (i.Npc.Hostile ? " · hostile" : "") : "")}" },
        RandomEvent ev => new ListRow { Item = ev, Title = string.IsNullOrWhiteSpace(ev.Name) ? ev.Id : ev.Name,
            Subtitle = $"{ev.Chance}%{(ev.ChancePerThousand > 0 ? $" +{ev.ChancePerThousand}‰" : "")} · {ev.Where}{(ev.Enabled ? "" : " · disabled")}" },
        Puzzle p => new ListRow { Item = p, Title = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name, Subtitle = $"{p.Points} points · {p.Hints.Count} hints" },
        Trigger t => new ListRow { Item = t, Title = string.IsNullOrWhiteSpace(t.Name) ? t.Id : t.Name, Subtitle = $"{t.Event}{(t.Verb != null ? " · " + t.Verb : "")}{(t.Noun1 != null ? " " + t.Noun1 : "")}{(t.Enabled ? "" : " · disabled")}" },
        Variable v => new ListRow { Item = v, Title = v.Name, Subtitle = $"starts at {v.InitialValue}" },
        VerbDefinition v => new ListRow { Item = v, Title = v.Id, Subtitle = string.Join(", ", v.Words) },
        Picture p => new ListRow { Item = p, Title = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name, Subtitle = $"{p.Id} · {p.Width}×{p.Height} · {p.Commands.Count} commands" },
        SoundAsset s => new ListRow
        {
            Item = s, Title = (s.Effect != null ? "🎛 " : "") + (string.IsNullOrWhiteSpace(s.Name) ? s.Id : s.Name),
            Subtitle = $"{s.Id} · {(s.Effect != null ? "sound effect" : Path.GetFileName(s.AssetName))} · volume {s.Volume * 10:0.#}" +
                       (s.Repeat ? " · repeats" : s.CutOffSeconds > 0 ? $" · cut off {s.CutOffSeconds:0.0} s" : ""),
        },
        _ => new ListRow { Item = o, Title = o.ToString() ?? "" },
    };

    private string DescribeLocation(string? loc) => loc switch
    {
        null or "" => "nowhere",
        Locations.Carried => "carried",
        Locations.Worn => "worn",
        _ => document.Adventure.FindRoom(loc)?.Name ?? (document.Adventure.FindItem(loc) is { } c ? "in " + c.Name : loc),
    };

    private void RefreshList(bool keepSelection = false)
    {
        var source = CurrentList;
        var keep = selected;
        rows.Clear();
        if (source == null) return;
        var filter = search.Text?.Trim() ?? "";
        foreach (var o in source)
        {
            var row = MakeRow(o!);
            if (filter.Length > 0 && !(row.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) || row.Subtitle.Contains(filter, StringComparison.OrdinalIgnoreCase))) continue;
            rows.Add(row);
        }
        if (keepSelection && keep != null)
            list.SelectedItem = rows.FirstOrDefault(r => ReferenceEquals(r.Item, keep));
    }

    private void Select(object item)
    {
        selected = item;
        list.SelectedItem = rows.FirstOrDefault(r => ReferenceEquals(r.Item, item));
        ShowDetail(EditorFor(item));
    }

    private void ShowDetail(View view)
    {
        detail.Content = view is ScrollView or GamePlayerView or PictureEditorView or Grid { ClassId: "full" } ? view
            : new ScrollView { Content = new ContentView { Content = view, Padding = new Thickness(24, 18), MaximumWidthRequest = 1100 } };
    }

    private View Heading(string title, string subtitle) => new VerticalStackLayout
    {
        Spacing = 2,
        Margin = new Thickness(0, 0, 0, 12),
        Children =
        {
            new Label { Text = title, FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Accent },
            new Label { Text = subtitle, FontSize = 13, Opacity = 0.7 },
        },
    };

    private View EditorFor(object item)
    {
        switch (item)
        {
            case Room room:
            {
                var stack = new VerticalStackLayout { Spacing = 10, Children = { Heading(room.Name, "Room") } };
                if (room.PictureId != null && document.Adventure.FindPicture(room.PictureId) is { } pic)
                {
                    var preview = new PictureView { HeightRequest = 240, WidthRequest = 240.0 * pic.Width / Math.Max(1, pic.Height), HorizontalOptions = LayoutOptions.Start };
                    preview.Show(pic, document.Adventure);
                    stack.Children.Add(preview);
                }
                stack.Children.Add(new ObjectEditor(room, ctx));
                var here = document.Adventure.Items.Where(i => string.Equals(i.Location, room.Id, StringComparison.OrdinalIgnoreCase)).Select(i => i.Name).ToList();
                stack.Children.Add(new SectionView("Items here", new Label { Text = here.Count == 0 ? "(none)" : string.Join(", ", here) }));
                var back = new Button { Text = "Add return exits from destination rooms", HorizontalOptions = LayoutOptions.Start };
                back.Clicked += (_, _) => { AddReturnExits(room); Select(room); };
                stack.Children.Add(back);
                return stack;
            }
            case Item it:
                return new VerticalStackLayout { Spacing = 10, Children = { Heading(it.Name, it.IsCharacter ? "Character" : "Item"), new ObjectEditor(it, ctx) } };
            case Puzzle p:
                return new VerticalStackLayout { Spacing = 10, Children = { Heading(p.Name, "Puzzle — solved automatically when all 'Solved When' conditions are true at the end of a turn"), new ObjectEditor(p, ctx) } };
            case Trigger t:
                return new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        Heading(string.IsNullOrWhiteSpace(t.Name) ? t.Id : t.Name, "Trigger — when the event happens and the pattern and all conditions match, the actions run"),
                        new Label { Text = "Patterns: * = anything, - = must be empty, alternatives with | (e.g. take|get). Nouns may be item ids or words.", FontSize = 12, Opacity = 0.7 },
                        new ObjectEditor(t, ctx),
                    },
                };
            case Variable v:
                return new VerticalStackLayout { Children = { Heading(v.Name, "Variable (a number that triggers can test and change)"), new ObjectEditor(v, ctx) } };
            case RandomEvent ev:
                return new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        Heading(string.IsNullOrWhiteSpace(ev.Name) ? ev.Id : ev.Name, "Random event — may happen by chance each turn, even where the player isn't"),
                        new Label { FontSize = 12, Opacity = 0.7, Text = "In conditions and actions, @eventroom is the room the event happens in; $randomitem is a random portable item there. " +
                            "Witness Message is shown if the player is there (or for Global events); Distant Message if they're elsewhere." },
                        new ObjectEditor(ev, ctx),
                    },
                };
            case VerbDefinition verb:
                return CommandEditor(verb);
            case Picture pic2:
                return new PictureEditorView(pic2, ctx);
            case SoundAsset s:
                return SoundEditor(s);
        }
        return new ObjectEditor(item, ctx);
    }

    private void AddReturnExits(Room room)
    {
        var opposite = new Dictionary<string, string>
        {
            ["north"] = "south", ["south"] = "north", ["east"] = "west", ["west"] = "east", ["up"] = "down", ["down"] = "up", ["in"] = "out", ["out"] = "in",
            ["northeast"] = "southwest", ["southwest"] = "northeast", ["northwest"] = "southeast", ["southeast"] = "northwest",
        };
        foreach (var exit in room.Exits)
        {
            var target = document.Adventure.FindRoom(exit.TargetRoomId);
            if (target == null || !opposite.TryGetValue(exit.Direction, out var back)) continue;
            if (target.Exits.Any(e => e.Direction == back)) continue;
            target.Exits.Add(new Exit { Direction = back, TargetRoomId = room.Id, DoorItemId = exit.DoorItemId });
        }
        ctx.Changed(room);
    }

    private View CommandEditor(VerbDefinition verb)
    {
        var tryEntry = new Entry { Placeholder = "Type a sentence to see how the parser understands it, e.g. \"carefully polish the lens with the cloth\"" };
        var result = new Label { FontFamily = "Menlo", FontSize = 12 };
        tryEntry.TextChanged += (_, e) =>
        {
            try
            {
                var engine = new GameEngine(document.Adventure);
                var outcome = engine.Parser.Parse(e.NewTextValue ?? "");
                result.Text = string.Join('\n', outcome.Commands.Select(c => c + (c.Lenient ? $"   (loose: {c.GrammarError})" : $"   [grammar: {c.Grammar}]")))
                              + (outcome.Error != null ? $"\nerror: {(outcome.Error == "UNKNOWN" ? "unknown word " + outcome.UnknownWord : outcome.Error)}" : "");
            }
            catch (Exception ex)
            {
                result.Text = ex.Message;
            }
        };
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Heading(verb.Id, "Command — a new verb, or extra words/grammar for a built-in verb with the same id"),
                new Label
                {
                    FontSize = 12, Opacity = 0.75,
                    Text = "Grammar lines: * is the verb; slots {noun} {held} {multi} {multiheld} {person} {noun2} {held2} {direction} {number} {topic} {text}; " +
                           "alternatives with | (in|into). \"=> action\" sends a line to another verb. Triggers use the command id as their Verb.",
                },
                new ObjectEditor(verb, ctx),
                new SectionView("Parser lab", new VerticalStackLayout { Spacing = 6, Children = { tryEntry, result } }),
            },
        };
    }

    private View SoundEditor(SoundAsset s) => new SoundEditorView(s, ctx, testPlayer?.Audio ?? previewAudio);

    private readonly AudioService previewAudio = new();

    private void ShowSingleEditor(Section s)
    {
        var a = document.Adventure;
        switch (s)
        {
            case Section.Game:
            {
                var stack = new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        Heading(a.Title, "Game settings"),
                        new ObjectEditor(a, ctx, exclude: new[] { "Rooms", "Items", "Puzzles", "Triggers", "RandomEvents", "Variables", "Pictures", "Sounds", "Vocabulary", "Messages", "Notes" }),
                    },
                };
                if (a.Notes.Count > 0)
                    stack.Children.Add(new SectionView("Notes (e.g. from importing)", new Label { Text = string.Join("\n\n", a.Notes), FontSize = 12 }));
                ShowDetail(stack);
                break;
            }
            case Section.Map:
            {
                var map = new MapView(a);
                map.RoomClicked += room => { ShowSection(Section.Rooms); Select(room); };
                ShowDetail(new VerticalStackLayout
                {
                    Children =
                    {
                        Heading("Map", "Laid out automatically from the exits. Gold border = start room, orange = door, blue dot = one-way, dashed = hidden. Click a room to edit it."),
                        map,
                    },
                });
                break;
            }
            case Section.Vocabulary:
                ShowDetail(new VerticalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        Heading("Vocabulary", "Words added to the built-in English dictionary"),
                        new Label { Text = "Item nouns and adjectives are learned automatically from the items. Add new commands in the Commands section.", FontSize = 12, Opacity = 0.7 },
                        new ObjectEditor(a.Vocabulary, ctx, exclude: new[] { "Verbs" }),
                    },
                });
                break;
            case Section.Messages:
                ShowDetail(MessagesEditor());
                break;
            case Section.TestPlay:
                ShowTestPlay();
                break;
        }
    }

    private View MessagesEditor()
    {
        var a = document.Adventure;
        var grid = new Grid { ColumnDefinitions = { new(new GridLength(170)), new(GridLength.Star) }, ColumnSpacing = 12, RowSpacing = 6 };
        int row = 0;
        foreach (var (key, def) in Msg.Defaults.OrderBy(k => k.Key))
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(new Label { Text = key, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center, FontSize = 13 }, 0, row);
            var entry = new Entry { Text = a.Messages.TryGetValue(key, out var v) ? v : null, Placeholder = def };
            entry.TextChanged += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.NewTextValue)) a.Messages.Remove(key); else a.Messages[key] = e.NewTextValue;
                ctx.Changed(a.Messages);
            };
            grid.Add(entry, 1, row++);
        }
        var custom = a.Messages.Keys.Where(k => !Msg.Defaults.ContainsKey(k)).ToList();
        return new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Heading("Messages", "The engine's built-in responses. Leave a box empty to use the default shown in grey."),
                grid,
                new Label { Text = custom.Count == 0 ? "" : $"{custom.Count} additional messages (e.g. imported system messages) are kept with the game.", FontSize = 12, Opacity = 0.7 },
            },
        };
    }

    private void ShowTestPlay()
    {
        testPlayer?.Stop();
        var clone = AdventurePackage.Clone(document.Adventure);
        testPlayer = new GamePlayerView { AutosaveEnabled = false };
        testPlayer.QuitRequested += (_, _) => testPlayer?.ShowQuitScreen();
        var watch = new Label { FontFamily = "Menlo", FontSize = 11, TextColor = Theme.SecondaryText };
        var restart = new Button { Text = "↻ Restart with latest edits" };
        restart.Clicked += (_, _) => ShowTestPlay();
        var saveBtn = new Button { Text = "Save position…" };
        saveBtn.Clicked += async (_, _) => { if (testPlayer != null) await testPlayer.ShowSaveDialogAsync(); };
        var loadBtn = new Button { Text = "Load position…" };
        loadBtn.Clicked += async (_, _) => { if (testPlayer != null) await testPlayer.ShowLoadDialogAsync(); };
        var walkthrough = new Button { Text = "Run commands…" };
        walkthrough.Clicked += async (_, _) =>
        {
            var text = await DisplayPromptAsync("Run commands", "Commands separated by semicolons (a quick walkthrough test):", maxLength: 4000);
            if (string.IsNullOrWhiteSpace(text) || testPlayer == null) return;
            foreach (var c in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                await testPlayer.SubmitAsync(c);
        };
        void UpdateWatch()
        {
            var e = testPlayer?.Engine;
            if (e == null) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Room:   {e.State.CurrentRoomId}");
            sb.AppendLine($"Score:  {e.State.Score}/{e.Adventure.ComputeMaxScore()}");
            sb.AppendLine($"Turns:  {e.State.Turns}");
            sb.AppendLine($"Dark:   {e.IsDark()}");
            if (e.Adventure.Settings.PlayerHealth > 0) sb.AppendLine($"Health: {e.State.Health}/{e.Adventure.Settings.PlayerHealth}");
            if (e.State.Traps.Count > 0) sb.AppendLine($"Traps:  {string.Join(", ", e.State.Traps.Keys)}");
            var flooded = e.State.RoomFlags.Where(kv => kv.Value.Contains("flooded")).Select(kv => kv.Key).ToList();
            if (flooded.Count > 0) sb.AppendLine($"Flooded: {string.Join(", ", flooded)}");
            var npcs = e.Npcs().ToList();
            if (npcs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("NPCs:");
                foreach (var n in npcs)
                {
                    var ns = e.NpcStateOf(n);
                    var flags = (ns.Defeated ? " defeated" : "") + (ns.Following ? " following" : "") + (e.NpcHostile(n) ? " hostile" : "") + (ns.Destination != null ? " → " + ns.Destination : "");
                    sb.AppendLine($"  {n.Id} @ {e.Loc(n)}{flags}");
                }
            }
            if (e.State.EventCounts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Random events:");
                foreach (var (id, count) in e.State.EventCounts) sb.AppendLine($"  {id} ×{count}");
            }
            sb.AppendLine();
            sb.AppendLine("Carried:");
            foreach (var i in e.Carried()) sb.AppendLine($"  {i.Id}{(e.IsWorn(i) ? " (worn)" : "")}");
            sb.AppendLine();
            sb.AppendLine("Variables:");
            foreach (var (k, v) in e.State.Variables.OrderBy(k => k.Key)) sb.AppendLine($"  {k} = {v}");
            sb.AppendLine();
            sb.AppendLine("Puzzles solved:");
            foreach (var p in e.State.SolvedPuzzles) sb.AppendLine($"  {p}");
            sb.AppendLine();
            sb.AppendLine("Triggers fired:");
            foreach (var t in e.State.FiredTriggers.Take(60)) sb.AppendLine($"  {t}");
            watch.Text = sb.ToString();
        }
        var grid = new Grid { ClassId = "full", ColumnDefinitions = { new(GridLength.Star), new(new GridLength(260)) } };
        grid.Add(testPlayer, 0);
        var side = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = 10,
            Children = { restart, walkthrough, saveBtn, loadBtn, new Label { Text = "Watch", FontAttributes = FontAttributes.Bold }, watch },
        };
        grid.Add(new ScrollView { Content = side, BackgroundColor = PaneBg }, 1);
        ShowDetail(grid);
        testPlayer.Load(clone, new FileSaveStorage(Path.Combine(FileSystem.AppDataDirectory, "TestSaves", StandaloneExporter.SafeFileName(clone.Title))));
        testPlayer.TurnCompleted += (_, _) => UpdateWatch();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), UpdateWatch);
    }

    // =========================================================== add / duplicate / delete

    private void AddNew()
    {
        var a = document.Adventure;
        object? item = section switch
        {
            Section.Rooms => new Room { Id = a.NewId("room"), Name = "New Room", Description = "" },
            Section.Items => new Item { Id = a.NewId("item"), Name = "new item", Location = (selected as Room)?.Id ?? a.StartRoomId },
            Section.Puzzles => new Puzzle { Id = a.NewId("puzzle"), Name = "New puzzle", Points = 10 },
            Section.Triggers => new Trigger { Id = a.NewId("trigger"), Name = "New trigger", Verb = "", Actions = { GameAction.Say("Something happens.") } },
            Section.Events => new RandomEvent { Id = a.NewId("event"), Name = "New event", Chance = 5, DistantMessage = "You hear something in the distance." },
            Section.Variables => new Variable { Name = UniqueVariableName(a) },
            Section.Commands => new VerbDefinition { Id = "newverb", Words = { "newverb" }, Grammar = { "*", "* {noun}" }, DefaultResponse = "Nothing happens." },
            Section.Pictures => new Picture { Id = a.NewId("pic"), Name = "New picture", Palette = Palettes.Extended.ToList(), InitialPaper = 15, InitialInk = 0 },
            Section.Sounds => null,
            _ => null,
        };
        if (section == Section.Sounds)
        {
            _ = AddSoundAsync();
            return;
        }
        if (item == null || CurrentList is not { } l) return;
        l.Add(item);
        ctx.Changed(item);
        RefreshList();
        Select(item);
    }

    private static string UniqueVariableName(Adventure a)
    {
        for (int n = 1; ; n++)
            if (a.FindVariable($"var{n}") == null) return $"var{n}";
    }

    private void Duplicate()
    {
        if (selected == null || CurrentList is not { } l) return;
        var json = System.Text.Json.JsonSerializer.Serialize(selected, selected.GetType(), AdventurePackage.JsonOptions);
        var copy = System.Text.Json.JsonSerializer.Deserialize(json, selected.GetType(), AdventurePackage.JsonOptions)!;
        var a = document.Adventure;
        switch (copy)
        {
            case Room r: r.Id = a.NewId(r.Id + "_"); r.Name += " (copy)"; break;
            case Item i: i.Id = a.NewId(i.Id + "_"); break;
            case Puzzle p: p.Id = a.NewId(p.Id + "_"); break;
            case Trigger t: t.Id = a.NewId(t.Id + "_"); break;
            case RandomEvent ev: ev.Id = a.NewId(ev.Id + "_"); break;
            case Picture p: p.Id = a.NewId(p.Id + "_"); p.Name += " (copy)"; break;
            case Variable v: v.Name = UniqueVariableName(a); break;
            case VerbDefinition v: v.Id += "2"; break;
            case SoundAsset s: s.Id = a.NewId(s.Id + "_"); break;
        }
        l.Insert(l.IndexOf(selected) + 1, copy);
        ctx.Changed(copy);
        RefreshList();
        Select(copy);
    }

    private async Task DeleteSelectedAsync()
    {
        if (selected == null || CurrentList is not { } l) return;
        var name = MakeRow(selected).Title;
        if (!await DisplayAlertAsync("Delete", $"Delete “{name}”? References to it elsewhere will be reported by Validate.", "Delete", "Cancel")) return;
        int index = l.IndexOf(selected);
        l.Remove(selected);
        ctx.Changed(null);
        selected = null;
        RefreshList();
        if (rows.Count > 0) Select(rows[Math.Clamp(index, 0, rows.Count - 1)].Item);
        else ShowDetail(new Label { Text = "Nothing here yet. Click + to add one.", Opacity = 0.6, Margin = 30 });
    }

    private async Task AddSoundAsync()
    {
        var choice = await DisplayActionSheetAsync("Add a sound", "Cancel", null, "From the sound library", "Import audio file…", "Retro synth effect");
        if (choice == "From the sound library") await NewLibrarySoundAsync();
        else if (choice == "Import audio file…") await ImportSoundAsync(null);
        else if (choice == "Retro synth effect") NewSoundEffect();
    }

    /// <summary>A new sound that starts as the library's first recording; its editor opens on the library to choose another.</summary>
    private async Task NewLibrarySoundAsync()
    {
        byte[] bytes;
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("sounds/door_open.wav");
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            bytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Sound library", "The sound library couldn't be opened: " + ex.Message, "OK");
            return;
        }
        var a = document.Adventure;
        var sound = new SoundAsset { Id = a.NewId("snd"), Name = SoundEditorView.DefaultNames[0] };
        sound.AssetName = $"sounds/{sound.Id}.wav";
        a.Assets[sound.AssetName] = bytes;
        AddSound(sound);
    }

    private void NewSoundEffect()
    {
        var a = document.Adventure;
        var fx = SfxPresets.Make("Pickup");
        var sound = new SoundAsset { Id = a.NewId("sfx"), Name = SoundEditorView.DefaultNames[1], Effect = fx };
        sound.AssetName = $"sounds/{sound.Id}.wav";
        a.Assets[sound.AssetName] = SfxSynth.RenderWav(fx);
        AddSound(sound);
    }

    private void AddSound(SoundAsset sound)
    {
        var a = document.Adventure;
        a.Sounds.Add(sound);
        ctx.Changed(sound);
        if (section != Section.Sounds) ShowSection(Section.Sounds);
        RefreshList();
        Select(sound);
    }

    private async Task ImportSoundAsync(SoundAsset? existing)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose an audio file", FileTypes = AudioFileTypes });
            if (file == null) return;
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var a = document.Adventure;
            var asset = "sounds/" + file.FileName;
            a.Assets[asset] = ms.ToArray();
            var sound = existing ?? new SoundAsset { Id = a.NewId("snd"), Name = Path.GetFileNameWithoutExtension(file.FileName) };
            sound.AssetName = asset;
            if (existing == null) a.Sounds.Add(sound);
            ctx.Changed(sound);
            RefreshList();
            Select(sound);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Import failed", ex.Message, "OK");
        }
    }

    private static readonly FilePickerFileType AudioFileTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.MacCatalyst] = new[] { "public.audio" },
        [DevicePlatform.iOS] = new[] { "public.audio" },
        [DevicePlatform.WinUI] = new[] { ".wav", ".mp3", ".m4a", ".aac", ".wma", ".ogg" },
        [DevicePlatform.Android] = new[] { "audio/*" },
    });

    private static readonly FilePickerFileType AnyFile = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.MacCatalyst] = new[] { "public.data", "public.item" },
        [DevicePlatform.iOS] = new[] { "public.data", "public.item" },
        [DevicePlatform.WinUI] = new[] { "*" },
        [DevicePlatform.Android] = new[] { "*/*" },
    });

    // =========================================================== file commands

    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        SetDocument(StudioDocument.CreateNew());
    }

    private async Task OpenAsync(string? path = null)
    {
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            if (path == null)
            {
                var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Open adventure (.adventure or .json)", FileTypes = AnyFile });
                if (file == null) return;
                path = file.FullPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    // Sandboxed platforms may only give a stream: copy it locally.
                    await using var s = await file.OpenReadAsync();
                    path = Path.Combine(FileSystem.CacheDirectory, file.FileName);
                    await using var fs = File.Create(path);
                    await s.CopyToAsync(fs);
                }
            }
            SetDocument(StudioDocument.Open(path));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Could not open", ex.Message, "OK");
        }
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        try
        {
            if (!saveAs && document.Path != null && document.Path.EndsWith(AdventurePackage.Extension, StringComparison.OrdinalIgnoreCase))
            {
                document.Save(document.Path);
                UpdateTitle();
                return true;
            }
            var bytes = AdventurePackage.SaveToBytes(document.Adventure);
            var name = StandaloneExporter.SafeFileName(document.Adventure.Title) + AdventurePackage.Extension;
            var result = await FileSaver.Default.SaveAsync(name, new MemoryStream(bytes), CancellationToken.None);
            if (!result.IsSuccessful || result.FilePath == null)
            {
                if (result.Exception is not null and not TaskCanceledException and not OperationCanceledException)
                    await DisplayAlertAsync("Save failed", result.Exception.Message, "OK");
                return false;
            }
            document.Save(result.FilePath);
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Save failed", ex.Message, "OK");
            return false;
        }
    }

    private async Task ImportLegacyAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Import a game: PAWS, Quill, GAC (.sna, .z80, .tap, .tzx…) or Z-code (.z3, .z5, .z8, .zblorb…)", FileTypes = AnyFile });
            if (file == null) return;
            await using var s = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            var data = ms.ToArray();
            var importer = ImporterRegistry.Detect(data, file.FileName);
            if (importer == null)
            {
                await DisplayAlertAsync("Not recognised", "This file doesn't contain a PAWS, Quill (with or without Illustrator graphics), Graphic Adventure Creator or Z-code game that I can find.", "OK");
                return;
            }
            var result = await Task.Run(() => importer.Import(data, file.FileName));
            SetDocument(new StudioDocument(result.Adventure));
            document.MarkChanged();
            var report = $"Imported with: {importer.Name}\nFormat: {result.DetectedFormat}\n\n" +
                         $"{result.Adventure.Rooms.Count} rooms, {result.Adventure.Items.Count} items, {result.Adventure.Triggers.Count} triggers, {result.Adventure.Pictures.Count} pictures.\n\n" +
                         (result.Warnings.Count > 0 ? "Warnings:\n• " + string.Join("\n• ", result.Warnings) : "No warnings.");
            await Navigation.PushModalAsync(new ReportPage("Import report", report));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Import failed", ex.Message, "OK");
        }
    }

    private async Task ValidateAsync()
    {
        var issues = AdventureValidator.Validate(document.Adventure);
        var text = issues.Count == 0
            ? "No problems found. 🎉"
            : string.Join("\n", issues.OrderBy(i => i.Severity).Select(i => $"{(i.Severity == IssueSeverity.Error ? "⛔" : i.Severity == IssueSeverity.Warning ? "⚠️" : "ℹ️")} {i.Where}: {i.Message}"));
        await Navigation.PushModalAsync(new ReportPage($"Validation — {issues.Count(i => i.Severity == IssueSeverity.Error)} errors, {issues.Count(i => i.Severity == IssueSeverity.Warning)} warnings", text));
    }

    private async Task ExportAsync()
    {
        var errors = AdventureValidator.Validate(document.Adventure).Where(i => i.Severity == IssueSeverity.Error).ToList();
        if (errors.Count > 0 && !await DisplayAlertAsync("There are errors", $"Validation found {errors.Count} errors (use Adventure › Validate to see them). Export anyway?", "Export", "Cancel"))
            return;
        await Navigation.PushModalAsync(new ExportPage(document.Adventure));
    }

    // =========================================================== menus

    private void BuildMenus()
    {
        const KeyboardAcceleratorModifiers Cmd = KeyboardAcceleratorModifiers.Cmd;
        const KeyboardAcceleratorModifiers CmdShift = KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Shift;
        const KeyboardAcceleratorModifiers CmdAlt = KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Alt;
        const KeyboardAcceleratorModifiers CmdAltShift = CmdAlt | KeyboardAcceleratorModifiers.Shift;
        // Note: shortcuts must not collide with macOS system menu items (⇧⌘S Duplicate, ⌘B Bold…), or UIKit drops the menu.

        MenuFlyoutItem Item(string text, Func<Task> action, string? key = null, KeyboardAcceleratorModifiers mods = Cmd)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Clicked += async (_, _) =>
            {
                try { await action(); }
                catch (Exception ex) { await DisplayAlertAsync("Error", ex.Message, "OK"); }
            };
            if (key != null) item.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = key, Modifiers = mods });
            return item;
        }
        MenuFlyoutItem Sync(string text, Action action, string? key = null, KeyboardAcceleratorModifiers mods = Cmd) =>
            Item(text, () => { action(); return Task.CompletedTask; }, key, mods);

        var file = new MenuBarItem { Text = "File" };
        file.Add(Item("New Adventure", NewAsync, "N"));
        file.Add(Item("Open…", () => OpenAsync(), "O"));
        var recent = new MenuFlyoutSubItem { Text = "Open Recent" };
        foreach (var r in RecentFiles.All) recent.Add(Item(Path.GetFileName(r), () => OpenAsync(r)));
        if (recent.Count == 0) recent.Add(new MenuFlyoutItem { Text = "(none)", IsEnabled = false });
        file.Add(recent);
#if DEBUG
        // Fan adventure based on BBC characters: available in development builds only, never in store releases.
        file.Add(Item("Open Example: Genesis (Doctor Who fan adventure)", async () =>
        {
            if (!await ConfirmDiscardAsync()) return;
            SetDocument(new StudioDocument(ExampleAdventures.Genesis()));
        }));
#endif
        file.Add(Item("Open Example: The Lighthouse", async () =>
        {
            if (!await ConfirmDiscardAsync()) return;
            SetDocument(new StudioDocument(ExampleAdventures.Lighthouse()));
        }));
        file.Add(new MenuFlyoutSeparator());
        file.Add(Item("Save", () => SaveAsync(false), "S"));
        file.Add(Item("Save As…", () => SaveAsync(true), "S", CmdAltShift));
        file.Add(new MenuFlyoutSeparator());
        file.Add(Item("Import Game (PAWS, Quill, GAC, Z-code)…", ImportLegacyAsync, "I", CmdShift));
        file.Add(Item("Export Standalone Game…", ExportAsync, "E", CmdShift));

        var edit = new MenuBarItem { Text = "Edit" };
        edit.Add(Sync("New Room", () => { ShowSection(Section.Rooms); AddNew(); }, "R", CmdAlt));
        edit.Add(Sync("New Item", () => { ShowSection(Section.Items); AddNew(); }, "I", CmdAlt));
        edit.Add(Sync("New Trigger", () => { ShowSection(Section.Triggers); AddNew(); }, "T", CmdAlt));
        edit.Add(Sync("New Puzzle", () => { ShowSection(Section.Puzzles); AddNew(); }, "P", CmdAlt));
        edit.Add(Sync("New Random Event", () => { ShowSection(Section.Events); AddNew(); }, "E", CmdAlt));
        edit.Add(Sync("New Picture", () => { ShowSection(Section.Pictures); AddNew(); }));
        edit.Add(Item("New Sound…", () => { ShowSection(Section.Sounds); return AddSoundAsync(); }));
        edit.Add(Sync("New Command", () => { ShowSection(Section.Commands); AddNew(); }));
        edit.Add(new MenuFlyoutSeparator());
        edit.Add(Sync("Duplicate Selected", Duplicate, "D"));
        edit.Add(Item("Delete Selected…", DeleteSelectedAsync));

        var view = new MenuBarItem { Text = "View" };
        int n = 1;
        foreach (var s in Enum.GetValues<Section>())
        {
            var sec = s;
            view.Add(Sync(SectionTitle(s)[3..].Trim(), () => ShowSection(sec), n <= 9 ? n.ToString() : null));
            n++;
        }

        var adv = new MenuBarItem { Text = "Adventure" };
        adv.Add(Sync("Test Play", () => ShowSection(Section.TestPlay), "R"));
        adv.Add(Item("Validate", ValidateAsync, "K"));
        adv.Add(Sync("Show Map", () => ShowSection(Section.Map), "M", CmdShift));
        adv.Add(new MenuFlyoutSeparator());
        adv.Add(Item("Export Standalone Game…", ExportAsync));

        var play = new MenuBarItem { Text = "Play" };
        play.Add(Sync("Previous Command", () => testPlayer?.RecallPrevious(), "Up", CmdAlt));
        play.Add(Sync("Next Command", () => testPlayer?.RecallNext(), "Down", CmdAlt));

        var help = new MenuBarItem { Text = "Help" };
        help.Add(Item("User Guide…", ShowUserGuideAsync, "?", CmdShift));
        help.Add(new MenuFlyoutSeparator());
        help.Add(Item("Parser & Command Reference", () => Navigation.PushModalAsync(new ReportPage("Parser & command reference", HelpText.ParserReference(document.Adventure)))));
        help.Add(Item("Sound Library Credits", ShowSoundCreditsAsync));
        help.Add(Item("Triggers, Conditions & Actions", () => Navigation.PushModalAsync(new ReportPage("Triggers, conditions & actions", HelpText.TriggerReference()))));
        help.Add(Item("About Adventure Creator Studio", () => DisplayAlertAsync("Adventure Creator Studio",
            $"Version {AppInfo.Current.VersionString}\nCopyright © 2026 Paul F.Johnson\n\nCreate text and graphic adventures, import PAWS, Quill (+ Illustrator) and GAC games, and export them as standalone apps.", "OK")));

        MenuBarItems.Add(file);
        MenuBarItems.Add(edit);
        MenuBarItems.Add(view);
        MenuBarItems.Add(adv);
        MenuBarItems.Add(play);
        MenuBarItems.Add(help);
    }

    private static readonly (string File, string Title)[] GuideChapters =
    {
        ("README.md", "Contents"),
        ("01-getting-started.md", "1. Getting started"),
        ("02-creating-an-adventure.md", "2. Creating an adventure (tutorial)"),
        ("03-world-reference.md", "3. Rooms, items and characters"),
        ("04-triggers.md", "4. Triggers, conditions and actions"),
        ("05-parser-and-commands.md", "5. The parser and custom commands"),
        ("06-pictures-and-sound.md", "6. Pictures and sound"),
        ("07-importing.md", "7. Importing PAWS, Quill/Illustrator and GAC"),
        ("08-exporting.md", "8. Testing, exporting and publishing"),
        ("09-file-format.md", "9. File format"),
        ("10-npcs-and-events.md", "10. NPCs, random events, traps and flooding"),
        ("11-import-walkthroughs.md", "11. Import walkthroughs (PAWS, Quill, GAC)"),
        ("12-z-code.md", "12. Z-code stories (Infocom and Inform)"),
    };

    private async Task ShowUserGuideAsync()
    {
        var choice = await DisplayActionSheetAsync("User Guide", "Cancel", null, GuideChapters.Select(c => c.Title).ToArray());
        var chapter = GuideChapters.FirstOrDefault(c => c.Title == choice);
        if (chapter.File == null) return;
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("docs/" + chapter.File);
            using var reader = new StreamReader(stream);
            await Navigation.PushModalAsync(new ReportPage(chapter.Title, HelpText.MarkdownToText(await reader.ReadToEndAsync())));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("User Guide", "The guide could not be opened: " + ex.Message, "OK");
        }
    }

    private async Task ShowSoundCreditsAsync()
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("sounds/CREDITS.md");
            using var reader = new StreamReader(stream);
            await Navigation.PushModalAsync(new ReportPage("Sound library credits and licences", HelpText.MarkdownToText(await reader.ReadToEndAsync())));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Sound library", "The credits could not be opened: " + ex.Message, "OK");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        previewAudio.StopAll();
    }
}
