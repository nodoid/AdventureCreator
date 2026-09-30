using AdventureSystem.Core.Model;
using AdventureSystem.Core.Packaging;
using CommunityToolkit.Maui.Storage;

namespace AdventureSystem.Studio.Pages;

/// <summary>Export dialog: game file (original, Creator or another format), standalone console executable, desktop app from a template, or native builds via the .NET SDK.</summary>
public sealed class ExportPage : ContentPage
{
    private readonly Adventure adventure;
    private readonly Editor log = new() { IsReadOnly = true, FontFamily = "Menlo", FontSize = 11, HeightRequest = 260 };
    private readonly Entry playerProject = new() { Placeholder = "…/src/AdventureSystem.Player/AdventureSystem.Player.csproj" };
    private readonly Entry consoleTemplate = new() { Placeholder = "Published single-file console player (see build/build-templates.sh)" };
    private readonly Entry appTemplate = new() { Placeholder = "Prebuilt player: Adventure Player.app (macOS) or its folder (Windows)" };
    private readonly Picker target = new()
    {
        ItemsSource = Enum.GetNames<StandaloneExporter.BuildTarget>(),
        SelectedItem = OperatingSystem.IsWindows() ? nameof(StandaloneExporter.BuildTarget.Windows) : nameof(StandaloneExporter.BuildTarget.MacCatalyst),
    };
    private readonly Entry bundleId = new() { Placeholder = "Bundle id (default com.adventuresystem.game.<title>)" };
    private readonly Entry signingIdentity = new() { Placeholder = "Signing identity, e.g. Apple Development: Name (TEAMID) – iOS devices" };
    private readonly Entry provisioningProfile = new() { Placeholder = "Provisioning profile name – iOS devices" };
    private readonly Button runInSimulator = new() { Text = "▶ Run in iOS Simulator", IsVisible = false };
    private string? lastSimulatorApp;
    private CancellationTokenSource? cts;

    private static bool CanRunProcesses => (OperatingSystem.IsMacCatalyst() || OperatingSystem.IsWindows()) && Environment.GetEnvironmentVariable("APP_SANDBOX_CONTAINER_ID") == null;

    public ExportPage(Adventure adventure)
    {
        this.adventure = adventure;
        Title = "Export";
        BackgroundColor = AdventureSystem.Maui.Theme.Window;
        playerProject.Text = Preferences.Default.Get("export.playerProject", FindPlayerProject() ?? "");
        consoleTemplate.Text = Preferences.Default.Get("export.consoleTemplate", "");
        appTemplate.Text = Preferences.Default.Get("export.appTemplate", "");

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
                    new Label { Text = $"Export “{adventure.Title}”", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = AdventureSystem.Maui.Theme.Accent },
                    Section("1. Game file", "Saves the game as a file: in the format it was imported from (only what changed is rebuilt, and anything that format can't hold is listed in a report), " +
                        "as an Adventure System .adventure package with pictures and sounds (opens in the Adventure Player on any platform), or in another system's format.",
                        FormatButtons()),
                    Section("2. Native app (all platforms)", "Builds the Adventure Player with this game built in, using the .NET SDK: Android (.apk), iOS/iPadOS, macOS (.app) or Windows (.exe). Requires the .NET SDK with the MAUI workload, and signing identities for Apple platforms." + desktopOnly,
                        new Label { Text = "Player project", FontSize = 12 }, Row(playerProject, BrowseFile(playerProject, "export.playerProject")),
                        new HorizontalStackLayout { Spacing = 8, Children = { new Label { Text = "Target", VerticalOptions = LayoutOptions.Center }, target, build, cancel, runInSimulator } },
                        new Label { Text = "Optional – for iOS devices and store builds (the profile must match the bundle id):", FontSize = 12, Opacity = 0.7 },
                        bundleId, signingIdentity, provisioningProfile),
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
        bundleId.Text = Preferences.Default.Get("export.bundleId", "");
        signingIdentity.Text = Preferences.Default.Get("export.signingIdentity", "");
        provisioningProfile.Text = Preferences.Default.Get("export.provisioningProfile", "");
        bundleId.TextChanged += (_, e) => Preferences.Default.Set("export.bundleId", e.NewTextValue ?? "");
        signingIdentity.TextChanged += (_, e) => Preferences.Default.Set("export.signingIdentity", e.NewTextValue ?? "");
        provisioningProfile.TextChanged += (_, e) => Preferences.Default.Set("export.provisioningProfile", e.NewTextValue ?? "");
        runInSimulator.Clicked += async (_, _) =>
        {
            if (lastSimulatorApp == null) return;
            Log("Starting the iOS Simulator…");
            var ok = await StandaloneExporter.RunInSimulatorAsync(lastSimulatorApp, Log);
            Log(ok ? "✔ Running in the simulator (see the DeviceHub / Simulator window)." : "✖ Could not start it in the simulator.");
        };
    }

    private View FormatButtons()
    {
        var stack = new VerticalStackLayout { Spacing = 6 };
        foreach (var (label, exporter) in GameFileExport.Choices(adventure))
        {
            var button = new Button { Text = $"Save as {label}…", HorizontalOptions = LayoutOptions.Start };
            button.Clicked += async (_, _) => await GameFileExport.ExportAsync(this, adventure, exporter);
            stack.Children.Add(button);
        }
        return stack;
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
            var candidate = Path.Combine(dir.FullName, "src", "AdventureSystem.Player", "AdventureSystem.Player.csproj");
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
        if (!File.Exists(playerProject.Text)) { Log("Player project not found. Point it at src/AdventureSystem.Player/AdventureSystem.Player.csproj."); return; }
        var folder = await PickFolderAsync();
        if (folder == null) return;
        var t = Enum.Parse<StandaloneExporter.BuildTarget>((string)target.SelectedItem);
        cts = new CancellationTokenSource();
        var output = Path.Combine(folder, StandaloneExporter.SafeFileName(adventure.Title) + "-" + t);
        try
        {
            var options = new StandaloneExporter.BuildOptions
            {
                BundleId = string.IsNullOrWhiteSpace(bundleId.Text) ? null : bundleId.Text.Trim(),
                CodesignKey = t == StandaloneExporter.BuildTarget.iOSSimulator || string.IsNullOrWhiteSpace(signingIdentity.Text) ? null : signingIdentity.Text.Trim(),
                CodesignProvision = t == StandaloneExporter.BuildTarget.iOSSimulator || string.IsNullOrWhiteSpace(provisioningProfile.Text) ? null : provisioningProfile.Text.Trim(),
            };
            var result = await StandaloneExporter.BuildAsync(playerProject.Text!, adventure, output, t, Log, cts.Token, options);
            Log(result.Succeeded ? $"✔ Build succeeded: {result.ProductPath}" : $"✖ Build failed (exit code {result.ExitCode}).");
            if (result.Succeeded && t == StandaloneExporter.BuildTarget.iOSSimulator)
            {
                lastSimulatorApp = result.ProductPath;
                runInSimulator.IsVisible = true;
            }
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
