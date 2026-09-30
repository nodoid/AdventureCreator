using AdventureSystem.Core.Packaging;
using AdventureSystem.Importers.Common;
using AdventureSystem.Studio.Controls;
using AdventureSystem.Studio.Services;
#if IOS
using UIKit;
#endif

namespace AdventureSystem.Studio.Pages;

/// <summary>
/// The iPad layout of the Studio: a split view (sidebar, list, editor) under a navigation bar that holds the
/// commands, which becomes one column at a time, with Back, when the window is narrow (Split View, Slide Over).
/// Documents are opened from the system document browser and save themselves; there's no menu bar or status bar.
/// </summary>
public sealed partial class StudioPage
{
    private enum Pane { Sidebar, List, Detail }

    private const double CompactWidth = 700, RoomyWidth = 1100, WatchWidth = 300;

    private readonly ColumnDefinition sidebarColumn = new(new GridLength(280));
    private readonly ColumnDefinition detailColumn = new(GridLength.Star);
    private View? sidebarPane;
    private bool compact;
    private Pane pane = Pane.Sidebar;
    private Grid? testPlayGrid;
    private bool watchShown;
    private Action? releaseDocument;
    private IDispatcherTimer? autosaveTimer;

    /// <summary>The Studio on screen (the iPad has one window), for the menu bar's commands.</summary>
    public static StudioPage? Current { get; private set; }

    /// <summary>A section's name for menus, e.g. "Items &amp; People".</summary>
    public static string SectionName(Section s) => SectionTitle(s)[3..].Trim();

    /// <summary>Runs a menu bar command (see AppDelegate.BuildMenu on iOS).</summary>
    public void RunCommand(string id)
    {
        var (verb, arg) = id.Contains(':') ? (id[..id.IndexOf(':')], id[(id.IndexOf(':') + 1)..]) : (id, "");
        Section Parse() => Enum.Parse<Section>(arg);
        switch (verb)
        {
            case "import": _ = ImportGameAsync(); break;
            case "export": _ = GameFileExport.ChooseAndExportAsync(this, document.Adventure); break;
            case "send": _ = SendCopyAsync(); break;
            case "new": AddNewIn(Parse()); break;
            case "duplicate": Duplicate(); break;
            case "delete": _ = DeleteSelectedAsync(); break;
            case "section" or "show": ShowSection(Parse()); SectionPicked(); break;
            case "validate": _ = ValidateAsync(); break;
            case "previous": testPlayer?.RecallPrevious(); break;
            case "next": testPlayer?.RecallNext(); break;
            case "guide": _ = ShowUserGuideAsync(); break;
        }
    }

    private View BuildTabletLayout(View sidebarScroll)
    {
        Current = this;
        sidebarPane = sidebarScroll;
        var grid = new Grid { ColumnDefinitions = { sidebarColumn, listColumn, detailColumn } };
        grid.Add(sidebarScroll, 0);
        grid.Add(listPane, 1);
        grid.Add(detail, 2);
        SizeChanged += (_, _) => ApplyTabletLayout();
        return grid;
    }

    private void ApplyTabletLayout()
    {
        if (sidebarPane == null) return;
        bool wasCompact = compact;
        compact = Width > 0 && Width < CompactWidth;
        bool hasList = HasList(section);
        if (compact)
        {
            if (!wasCompact) pane = Pane.Detail;
            if (pane == Pane.List && !hasList) pane = Pane.Detail;
            sidebarColumn.Width = pane == Pane.Sidebar ? GridLength.Star : new GridLength(0);
            listColumn.Width = pane == Pane.List ? GridLength.Star : new GridLength(0);
            detailColumn.Width = pane == Pane.Detail ? GridLength.Star : new GridLength(0);
            sidebarPane.IsVisible = pane == Pane.Sidebar;
            listPane.IsVisible = pane == Pane.List;
            detail.IsVisible = pane == Pane.Detail;
        }
        else
        {
            // The sidebar is always there (narrower columns leave the editor room in portrait).
            bool roomy = Width >= RoomyWidth;
            sidebarColumn.Width = new GridLength(roomy ? 280 : 230);
            sidebarPane.IsVisible = true;
            listColumn.Width = new GridLength(hasList ? (roomy ? 320 : 280) : 0);
            listPane.IsVisible = hasList;
            detailColumn.Width = GridLength.Star;
            detail.IsVisible = true;
        }
        LayOutTestPlay();
        if (wasCompact != compact) UpdateNavBar();
    }

    /// <summary>A section was chosen in the sidebar: in one-column mode, go on to its list (or its editor).</summary>
    private void SectionPicked()
    {
        if (!TouchMetrics.IsTouch || !compact) return;
        pane = HasList(section) ? Pane.List : Pane.Detail;
        ApplyTabletLayout();
        UpdateNavBar();
    }

    /// <summary>Something was chosen to edit: in one-column mode, show its editor.</summary>
    private void ItemPicked()
    {
        if (!TouchMetrics.IsTouch || !compact) return;
        pane = Pane.Detail;
        ApplyTabletLayout();
        UpdateNavBar();
    }

    private void GoBack()
    {
        pane = pane == Pane.Detail && HasList(section) ? Pane.List : Pane.Sidebar;
        ApplyTabletLayout();
        UpdateNavBar();
    }

    // ------------------------------------------------------------------ list rows

    /// <summary>Long-press menu on a list row: the iPad's way to duplicate or delete.</summary>
    private void AddRowMenu(View row)
    {
        var dup = new MenuFlyoutItem { Text = "Duplicate" };
        dup.Clicked += (_, _) => { if (row.BindingContext is ListRow r) { Select(r.Item); Duplicate(); } };
        var del = new MenuFlyoutItem { Text = "Delete…" };
        del.Clicked += async (_, _) => { if (row.BindingContext is ListRow r) { Select(r.Item); await DeleteSelectedAsync(); } };
        FlyoutBase.SetContextFlyout(row, new MenuFlyout { dup, del });
    }

    // ------------------------------------------------------------------ test play

    private void SetUpTabletTestPlay(Grid grid, VerticalStackLayout side)
    {
        testPlayGrid = grid;
        foreach (var b in side.Children.OfType<Button>()) b.MinimumHeightRequest = TouchMetrics.MinTarget;
        LayOutTestPlay();
    }

    /// <summary>The Watch panel is an inspector beside the game, shown with the navigation bar button (in place of the game when narrow).</summary>
    private void LayOutTestPlay()
    {
        if (testPlayGrid == null || testPlayGrid.ColumnDefinitions.Count < 2) return;
        var (game, watch) = compact ? (watchShown ? (0.0, -1.0) : (-1.0, 0.0)) : (-1.0, watchShown ? WatchWidth : 0);
        testPlayGrid.ColumnDefinitions[0].Width = game < 0 ? GridLength.Star : new GridLength(game);
        testPlayGrid.ColumnDefinitions[1].Width = watch < 0 ? GridLength.Star : new GridLength(watch);
        ((View)testPlayGrid.Children[0]).IsVisible = game != 0;
        ((View)testPlayGrid.Children[1]).IsVisible = watch != 0;
    }

    private void ToggleWatch()
    {
        watchShown = !watchShown;
        LayOutTestPlay();
    }

    // ------------------------------------------------------------------ documents

    /// <summary>Opens an adventure that saves itself (the iPad). <paramref name="released"/> runs when it is closed.</summary>
    public void OpenDocument(StudioDocument doc, Action released)
    {
        ReleaseDocument();
        releaseDocument = released;
        testPlayGrid = null;
        SetDocument(doc);
        doc.Changed += _ => ScheduleAutosave();
        pane = Pane.Sidebar;
        ApplyTabletLayout();
        UpdateNavBar();
    }

#if DEBUG
    /// <summary>Development aid (AC_STUDIO_EDIT): an edit made without a finger, to check that saving in place works.</summary>
    public void DebugRetitle(string title)
    {
        document.Adventure.Title = title;
        ctx.Changed(document.Adventure);
    }
#endif

    private void ScheduleAutosave()
    {
        if (autosaveTimer == null)
        {
            autosaveTimer = Dispatcher.CreateTimer();
            autosaveTimer.Interval = TimeSpan.FromSeconds(1.5);
            autosaveTimer.IsRepeating = false;
            autosaveTimer.Tick += (_, _) => SaveNow();
        }
        autosaveTimer.Stop();
        autosaveTimer.Start();
    }

    /// <summary>Writes unsaved changes now (also when the app goes into the background).</summary>
    public void SaveNow()
    {
        autosaveTimer?.Stop();
        try { document.SaveInPlace(); }
        catch (Exception ex) { _ = DisplayAlertAsync("Couldn't save", ex.Message, "OK"); }
    }

    private void ReleaseDocument()
    {
        SaveNow();
        var release = releaseDocument;
        releaseDocument = null;
        release?.Invoke();
    }

    /// <summary>"Documents": saves and closes the adventure and goes back to the document browser.</summary>
    private void CloseToBrowser()
    {
        testPlayer?.Stop();
        ReleaseDocument();
        SetDocument(StudioDocument.CreateNew());
#if IOS
        DocumentBrowser.Show(this, true);
#endif
    }

    /// <summary>Shows a report once the adventure it belongs to is on screen.</summary>
    public void ShowReportWhenReady(string title, string text) =>
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(800), () => _ = Navigation.PushModalAsync(new ReportPage(title, text)));

    /// <summary>
    /// Imports a game from another system into a new adventure in the app's own folder (On My iPad › Adventure
    /// System), then opens it. (Games opened from the document browser are imported next to themselves instead.)
    /// </summary>
    private async Task ImportGameAsync()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Import a game: PAWS, Quill, GAC, Scott Adams, Quest, Twine or Z-code", FileTypes = ImportFileTypes });
            if (file == null) return;
            await using var s = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            var data = ms.ToArray();
            var path = string.IsNullOrEmpty(file.FullPath) ? file.FileName : file.FullPath;
            var importer = ImporterRegistry.Detect(data, path);
            if (importer == null)
            {
                await DisplayAlertAsync("Not recognised", "This file isn't a game I can import. I can read PAWS, The Quill (with Illustrator pictures), GAC and Scott Adams games, Quest 5 (.aslx, .quest), Twine (published .html or .twee) and Z-code stories (.z3–.z8, .zblorb).", "OK");
                return;
            }
            var result = await Task.Run(() => ImporterRegistry.Run(importer, data, path));
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var name = Path.GetFileNameWithoutExtension(file.FileName);
            var target = Path.Combine(folder, name + AdventurePackage.Extension);
            for (int n = 2; File.Exists(target); n++) target = Path.Combine(folder, $"{name} {n}{AdventurePackage.Extension}");
            AdventurePackage.Save(result.Adventure, target);
#if IOS
            DocumentBrowser.OpenFile(this, target);
#endif
            ShowReportWhenReady("Import report", $"Imported with: {importer.Name}\nFormat: {result.DetectedFormat}\n\n" +
                $"{result.Adventure.Rooms.Count} rooms, {result.Adventure.Items.Count} items, {result.Adventure.Triggers.Count} triggers, {result.Adventure.Pictures.Count} pictures.\n\n" +
                (result.Warnings.Count > 0 ? "Warnings:\n• " + string.Join("\n• ", result.Warnings) : "No warnings.") +
                $"\n\nSaved as On My iPad › Adventure System › {Path.GetFileName(target)}.");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Import failed", ex.Message, "OK");
        }
    }

    /// <summary>Sends the adventure file itself (AirDrop, Mail, Save to Files…).</summary>
    private async Task SendCopyAsync()
    {
        SaveNow();
        var folder = Path.Combine(FileSystem.CacheDirectory, "Share");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, StandaloneExporter.SafeFileName(document.Adventure.Title) + AdventurePackage.Extension);
        AdventurePackage.Save(document.Adventure, path);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = document.Adventure.Title, File = new ShareFile(path) });
    }

    private void AddNewIn(Section s)
    {
        ShowSection(s);
        if (s == Section.Sounds) _ = AddSoundAsync();
        else AddNew();
        ItemPicked();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!TouchMetrics.IsTouch) return;
        // After the navigation bar has taken the page's own (empty) toolbar items.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), UpdateNavBar);
    }

    // ------------------------------------------------------------------ navigation bar

    /// <summary>The navigation bar's buttons and menus, for the current section and width.</summary>
    private void UpdateNavBar()
    {
#if IOS
        if (!TouchMetrics.IsTouch || Handler is not IPlatformViewHandler { ViewController: { } vc }) return;
        var top = vc.NavigationController?.TopViewController ?? vc.ParentViewController ?? vc;
        var item = top.NavigationItem;
        if (OperatingSystem.IsIOSVersionAtLeast(16)) item.Style = UINavigationItemStyle.Editor;

        static UIImage? Symbol(string name) => UIImage.GetSystemImage(name);
        static UIAction Act(string title, string? symbol, Action action, bool destructive = false)
        {
            var a = UIAction.Create(title, symbol == null ? null : Symbol(symbol), null, _ => action());
            if (destructive) a.Attributes = UIMenuElementAttributes.Destructive;
            return a;
        }
        static UIMenu Group(params UIMenuElement[] children) => UIMenu.Create("", null, UIMenuIdentifier.None, UIMenuOptions.DisplayInline, children);
        static UIBarButtonItem Button(string symbol, string label, Action action) =>
            new(Symbol(symbol), UIBarButtonItemStyle.Plain, (_, _) => action()) { AccessibilityLabel = label };
        static UIBarButtonItem MenuButton(string symbol, string label, UIMenu menu) => new() { Image = Symbol(symbol), Menu = menu, AccessibilityLabel = label };
        static UIBarButtonItem Back(string title, Action action)
        {
            var config = UIButtonConfiguration.PlainButtonConfiguration;
            config.Image = UIImage.GetSystemImage("chevron.backward", UIImageSymbolConfiguration.Create(UIFont.PreferredBody.PointSize, UIImageSymbolWeight.Semibold));
            config.Title = title;
            config.ImagePadding = 5;
            config.ContentInsets = new NSDirectionalEdgeInsets(0, 0, 0, 0);
            return new UIBarButtonItem(UIButton.GetButton(config, UIAction.Create(_ => action()))) { AccessibilityLabel = title };
        }

        var left = new List<UIBarButtonItem>();
        if (compact && pane != Pane.Sidebar) left.Add(Back(pane == Pane.Detail && HasList(section) ? listTitle.Text : "Studio", GoBack));
        else left.Add(Back("Documents", CloseToBrowser));
        item.LeftItemsSupplementBackButton = false;
        item.LeftBarButtonItems = left.ToArray();

        var add = UIMenu.Create("Add", null, UIMenuIdentifier.None, 0, new UIMenuElement[]
        {
            Act("Room", "door.left.hand.open", () => AddNewIn(Section.Rooms)),
            Act("Item or Person", "shippingbox", () => AddNewIn(Section.Items)),
            Act("Trigger", "bolt", () => AddNewIn(Section.Triggers)),
            Act("Puzzle", "puzzlepiece", () => AddNewIn(Section.Puzzles)),
            Act("Random Event", "dice", () => AddNewIn(Section.Events)),
            Act("Variable", "number", () => AddNewIn(Section.Variables)),
            Act("Command", "text.bubble", () => AddNewIn(Section.Commands)),
            Act("Picture", "paintpalette", () => AddNewIn(Section.Pictures)),
            Act("Sound…", "speaker.wave.2", () => AddNewIn(Section.Sounds)),
        });
        var share = UIMenu.Create("", null, UIMenuIdentifier.None, 0, new UIMenuElement[]
        {
            Act("Export Game File…", "square.and.arrow.up.on.square", () => _ = GameFileExport.ChooseAndExportAsync(this, document.Adventure)),
            Act("Send a Copy…", "doc", () => _ = SendCopyAsync()),
        });
        var more = new List<UIMenuElement>
        {
            Group(Act("Import Game…", "square.and.arrow.down", () => _ = ImportGameAsync()), Act("Validate", "checkmark.seal", () => _ = ValidateAsync())),
        };
        if (selected != null && CurrentList is { } l && l.Contains(selected))
            more.Add(Group(Act("Duplicate", "plus.square.on.square", Duplicate), Act("Delete…", "trash", () => _ = DeleteSelectedAsync(), destructive: true)));
        var guide = UIMenu.Create("User Guide", Symbol("book"), UIMenuIdentifier.None, 0,
            GuideChapters.Select(c => (UIMenuElement)Act(c.Title, null, () => _ = OpenGuideChapterAsync(c))).ToArray());
        more.Add(UIMenu.Create("Help", Symbol("questionmark.circle"), UIMenuIdentifier.None, 0, new UIMenuElement[]
        {
            guide,
            Act("Parser & Command Reference", null, () => _ = Navigation.PushModalAsync(new ReportPage("Parser & command reference", HelpText.ParserReference(document.Adventure)))),
            Act("Triggers, Conditions & Actions", null, () => _ = Navigation.PushModalAsync(new ReportPage("Triggers, conditions & actions", HelpText.TriggerReference()))),
            Act("Sound Library Credits", null, () => _ = ShowSoundCreditsAsync()),
            Act("About Adventure System Studio", null, () => _ = ShowAboutAsync()),
        }));

        var right = new List<UIBarButtonItem>
        {
            MenuButton("ellipsis.circle", "More", UIMenu.Create("", null, UIMenuIdentifier.None, 0, more.ToArray())),
            MenuButton("square.and.arrow.up", "Share", share),
            MenuButton("plus", "Add", add),
            Button("play.fill", "Test Play", () => { ShowSection(Section.TestPlay); ItemPicked(); }),
        };
        if (section == Section.TestPlay) right.Add(Button("sidebar.right", "Show or hide the Watch panel", ToggleWatch));
        item.RightBarButtonItems = right.ToArray();
#endif
    }
}
