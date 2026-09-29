using AdventureCreator.Core.Model;
using AdventureCreator.Core.Packaging;
using CommunityToolkit.Maui.Storage;

namespace AdventureCreator.Studio.Pages;

/// <summary>Export dialog: game package, standalone console executable, desktop app from a template, or native builds via the .NET SDK.</summary>
public sealed class ExportPage : ContentPage
{
    private readonly Adventure adventure;
    private readonly Editor log = new() { IsReadOnly = true, FontFamily = "Menlo", FontSize = 11, HeightRequest = 260 };
    private readonly Entry playerProject = new() { Placeholder = "…/src/AdventureCreator.Player/AdventureCreator.Player.csproj" };
    private readonly Entry consoleTemplate = new() { Placeholder = "Published single-file console player (see build/build-templates.sh)" };
    private readonly Entry appTemplate = new() { Placeholder = "Prebuilt player: Adventure Player.app (macOS) or its folder (Windows)" };
    private readonly Picker target = new() { ItemsSource = Enum.GetNames<StandaloneExporter.BuildTarget>(), SelectedIndex = OperatingSystem.IsWindows() ? 3 : 2 };
    private CancellationTokenSource? cts;

    private static bool CanRunProcesses => (OperatingSystem.IsMacCatalyst() || OperatingSystem.IsWindows()) && Environment.GetEnvironmentVariable("APP_SANDBOX_CONTAINER_ID") == null;

    public ExportPage(Adventure adventure)
    {
        this.adventure = adventure;
        Title = "Export";
        BackgroundColor = AdventureCreator.Maui.Theme.Window;
        playerProject.Text = Preferences.Default.Get("export.playerProject", FindPlayerProject() ?? "");
        consoleTemplate.Text = Preferences.Default.Get("export.consoleTemplate", "");
        appTemplate.Text = Preferences.Default.Get("export.appTemplate", "");

        var package = new Button { Text = "Save game package (.adventure)…" };
        package.Clicked += async (_, _) => await SavePackageAsync();

        var console = new Button { Text = "Export console executable…" };
        console.Clicked += async (_, _) => await ExportConsoleAsync();

        var fromTemplate = new Button { Text = "Export desktop app…" };
        fromTemplate.Clicked += async (_, _) => await ExportTemplateAsync();

        var build = new Button { Text = "Build with .NET SDK…" };
        build.Clicked += async (_, _) => await BuildAsync();
        var cancel = new Button { Text = "Cancel build" };
        cancel.Clicked += (_, _) => cts?.Cancel();

        var close = new Button { Text = "Close", HorizontalOptions = LayoutOptions.End, WidthRequest = 100 };
        close.Clicked += async (_, _) => { cts?.Cancel(); await Navigation.PopModalAsync(); };

        View Section(string title, string text, params View[] controls)
        {
            var stack = new VerticalStackLayout { Spacing = 6 };
            stack.Children.Add(new Label { Text = title, FontAttributes = FontAttributes.Bold, FontSize = 15 });
            stack.Children.Add(new Label { Text = text, FontSize = 12, Opacity = 0.75 });
            foreach (var c in controls) stack.Children.Add(c);
            return new Border { Padding = 12, Stroke = Colors.Gray.WithAlpha(0.3f), Content = stack };
        }

        var desktopOnly = CanRunProcesses ? "" : " (Not available here: needs the desktop Studio outside the App Store sandbox.)";
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 14,
                Children =
                {
                    new Label { Text = $"Export “{adventure.Title}”", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = AdventureCreator.Maui.Theme.Accent },
                    Section("1. Game package", "A single .adventure file containing the game, pictures and sounds. Opens in the Adventure Player on any platform.", package),
                    Section("2. Native app (all platforms)", "Builds the Adventure Player with this game built in, using the .NET SDK: Android (.apk), iOS/iPadOS, macOS (.app) or Windows (.exe). Requires the .NET SDK with the MAUI workload, and signing identities for Apple platforms." + desktopOnly,
                        new Label { Text = "Player project", FontSize = 12 }, Row(playerProject, BrowseFile(playerProject, "export.playerProject")),
                        new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Target", VerticalOptions = LayoutOptions.Center }, target, build, cancel } }),
                    Section("3. Desktop app from a template", "Fast, no SDK needed: copies a prebuilt Adventure Player and puts the game inside it (macOS .app is re-signed ad hoc)." + desktopOnly,
                        Row(appTemplate, BrowseFile(appTemplate, "export.appTemplate")), fromTemplate),
                    Section("4. Console executable", "A single self-contained terminal program (text only) with the game appended. Build the template once for each OS with build/build-templates.sh." + desktopOnly,
                        Row(consoleTemplate, BrowseFile(consoleTemplate, "export.consoleTemplate")), console),
                    new Label { Text = "Log", FontAttributes = FontAttributes.Bold },
                    log,
                    close,
                },
            },
        };
        foreach (var b in new[] { console, fromTemplate, build, cancel }) b.IsEnabled = CanRunProcesses;
    }

    private static View Row(View main, View button)
    {
        var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6 };
        g.Add(main, 0);
        g.Add(button, 1);
        return g;
    }

    private Button BrowseFile(Entry entry, string key)
    {
        var b = new Button { Text = "Browse…" };
        b.Clicked += async (_, _) =>
        {
            var file = await FilePicker.Default.PickAsync();
            if (file?.FullPath is { } p)
            {
                // .app bundles are directories: allow picking a file inside and walk up.
                var app = p.Contains(".app/") ? p[..(p.IndexOf(".app/", StringComparison.Ordinal) + 4)] : p;
                entry.Text = app;
                Preferences.Default.Set(key, app);
            }
        };
        entry.TextChanged += (_, e) => Preferences.Default.Set(key, e.NewTextValue ?? "");
        return b;
    }

    private static string? FindPlayerProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "AdventureCreator.Player", "AdventureCreator.Player.csproj");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private void Log(string line) => MainThread.BeginInvokeOnMainThread(() =>
    {
        log.Text = (log.Text?.Length > 60000 ? log.Text[^40000..] : log.Text) + line + "\n";
    });

    private async Task<string?> PickFolderAsync()
    {
        var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
        return result.IsSuccessful ? result.Folder?.Path : null;
    }

    private async Task SavePackageAsync()
    {
        var bytes = AdventurePackage.SaveToBytes(adventure);
        var result = await FileSaver.Default.SaveAsync(StandaloneExporter.SafeFileName(adventure.Title) + AdventurePackage.Extension, new MemoryStream(bytes), CancellationToken.None);
        Log(result.IsSuccessful ? $"Saved {result.FilePath}" : $"Not saved {result.Exception?.Message}");
    }

    private async Task ExportConsoleAsync()
    {
        if (!File.Exists(consoleTemplate.Text)) { Log("Choose the console player template first."); return; }
        var folder = await PickFolderAsync();
        if (folder == null) return;
        var name = StandaloneExporter.SafeFileName(adventure.Title) + (consoleTemplate.Text!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ".exe" : "");
        var output = Path.Combine(folder, name);
        await Task.Run(() => StandaloneExporter.AppendPayload(consoleTemplate.Text!, adventure, output));
        Log($"Created {output}");
    }

    private async Task ExportTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(appTemplate.Text) || !(Directory.Exists(appTemplate.Text) || File.Exists(appTemplate.Text)))
        {
            Log("Choose a prebuilt player template first (build/build-templates.sh creates them).");
            return;
        }
        var folder = await PickFolderAsync();
        if (folder == null) return;
        try
        {
            var result = await Task.Run(() => StandaloneExporter.ExportFromTemplate(appTemplate.Text!, adventure, folder));
            Log($"Created {result}");
        }
        catch (Exception ex)
        {
            Log("Failed: " + ex.Message);
        }
    }

    private async Task BuildAsync()
    {
        if (!File.Exists(playerProject.Text)) { Log("Player project not found. Point it at src/AdventureCreator.Player/AdventureCreator.Player.csproj."); return; }
        var folder = await PickFolderAsync();
        if (folder == null) return;
        var t = Enum.Parse<StandaloneExporter.BuildTarget>((string)target.SelectedItem);
        cts = new CancellationTokenSource();
        var output = Path.Combine(folder, StandaloneExporter.SafeFileName(adventure.Title) + "-" + t);
        try
        {
            int code = await StandaloneExporter.BuildAsync(playerProject.Text!, adventure, output, t, Log, cts.Token);
            Log(code == 0 ? $"✔ Build succeeded: {output}" : $"✖ Build failed (exit code {code}).");
        }
        catch (OperationCanceledException)
        {
            Log("Cancelled.");
        }
        catch (Exception ex)
        {
            Log("Failed: " + ex.Message);
        }
    }
}
