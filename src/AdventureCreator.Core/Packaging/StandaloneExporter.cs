using System.Diagnostics;
using System.Text;
using AdventureCreator.Core.Model;

namespace AdventureCreator.Core.Packaging;

/// <summary>
/// Turns an adventure into a standalone, double-clickable game.
/// <list type="bullet">
/// <item><b>Payload append</b>: a prebuilt single-file player executable with the game package appended
/// (console player, any OS; no SDK needed).</item>
/// <item><b>App template</b>: a prebuilt graphical player (macOS .app / Windows folder) copied with the game placed
/// where the player looks for it (no SDK needed).</item>
/// <item><b>Build</b>: <c>dotnet publish</c> of the Player project with the game embedded as an app asset — required for
/// Android (.apk) and iOS/iPadOS (.ipa), and gives signed, store-ready desktop builds.</item>
/// </list>
/// </summary>
public static class StandaloneExporter
{
    /// <summary>Package file name the players look for.</summary>
    public const string GameFileName = "game.adventure";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("ADVPAYLD");

    // ------------------------------------------------------------------ payload in executable

    public static void AppendPayload(string playerExecutable, Adventure adventure, string outputPath)
    {
        var package = AdventurePackage.SaveToBytes(adventure);
        var exe = File.ReadAllBytes(playerExecutable);
        // Strip an existing payload so exported games can be re-exported.
        int baseLength = PayloadOffset(exe) ?? exe.Length;
        using (var fs = File.Create(outputPath))
        {
            fs.Write(exe, 0, baseLength);
            fs.Write(package);
            fs.Write(BitConverter.GetBytes((long)package.Length));
            fs.Write(Magic);
        }
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(outputPath, File.GetUnixFileMode(outputPath) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch { /* best effort */ }
        }
    }

    private static int? PayloadOffset(byte[] data)
    {
        if (data.Length < 16) return null;
        if (!data.AsSpan(data.Length - 8).SequenceEqual(Magic)) return null;
        long len = BitConverter.ToInt64(data, data.Length - 16);
        long start = data.Length - 16 - len;
        return start >= 0 ? (int)start : null;
    }

    /// <summary>Reads a game appended to an executable (or any file), or null if there is none.</summary>
    public static Adventure? TryReadPayload(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 16) return null;
            fs.Seek(-16, SeekOrigin.End);
            var footer = new byte[16];
            fs.ReadExactly(footer);
            if (!footer.AsSpan(8).SequenceEqual(Magic)) return null;
            long len = BitConverter.ToInt64(footer, 0);
            if (len <= 0 || len > fs.Length - 16) return null;
            fs.Seek(-16 - len, SeekOrigin.End);
            var data = new byte[len];
            fs.ReadExactly(data);
            return AdventurePackage.Load(data);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the game a standalone player should run: appended to the executable, next to it, or in the app bundle's
    /// resources. Returns null if none is found (the player then offers to open a file).
    /// </summary>
    public static Adventure? LocateEmbeddedGame()
    {
        var exe = Environment.ProcessPath;
        if (exe != null && TryReadPayload(exe) is { } appended) return appended;

        var baseDir = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDir, GameFileName),
            Path.Combine(baseDir, "..", "Resources", GameFileName),              // macOS / Mac Catalyst bundle
            Path.Combine(baseDir, "Resources", GameFileName),
        };
        if (exe != null) candidates.Add(Path.Combine(Path.GetDirectoryName(exe) ?? baseDir, GameFileName));
        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(c)) return AdventurePackage.Load(c);
            }
            catch { /* try next */ }
        }
        return null;
    }

    // ------------------------------------------------------------------ app templates

    /// <summary>
    /// Copies a prebuilt player app (a macOS/Mac Catalyst .app bundle or a Windows output folder) and places the game
    /// inside it. Returns the path of the created app. On macOS the bundle is re-signed ad hoc.
    /// </summary>
    public static string ExportFromTemplate(string templatePath, Adventure adventure, string outputDirectory, string? appName = null)
    {
        appName ??= SafeFileName(adventure.Title);
        Directory.CreateDirectory(outputDirectory);
        bool isBundle = templatePath.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(templatePath);
        string target;
        if (isBundle)
        {
            target = Path.Combine(outputDirectory, appName + ".app");
            if (Directory.Exists(target)) Directory.Delete(target, true);
            CopyDirectory(templatePath, target);
            var resources = Path.Combine(target, "Contents", "Resources");
            Directory.CreateDirectory(resources);
            AdventurePackage.Save(adventure, Path.Combine(resources, GameFileName));
            TrySetBundleName(Path.Combine(target, "Contents", "Info.plist"), adventure.Title);
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
                RunQuiet("codesign", $"--force --deep --sign - \"{target}\"");
        }
        else if (Directory.Exists(templatePath))
        {
            target = Path.Combine(outputDirectory, appName);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            CopyDirectory(templatePath, target);
            AdventurePackage.Save(adventure, Path.Combine(target, GameFileName));
            // Rename the main executable after the game.
            var exe = Directory.GetFiles(target, "AdventureCreator.Player.exe").FirstOrDefault();
            if (exe != null)
            {
                var renamed = Path.Combine(target, appName + ".exe");
                if (!File.Exists(renamed)) File.Copy(exe, renamed);
            }
        }
        else
        {
            // Single executable template: append.
            target = Path.Combine(outputDirectory, appName + (templatePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ".exe" : ""));
            AppendPayload(templatePath, adventure, target);
        }
        return target;
    }

    private static void TrySetBundleName(string plist, string title)
    {
        try
        {
            if (!File.Exists(plist)) return;
            // Built bundles usually contain binary plists: convert to XML first (macOS only).
            if (!File.ReadAllText(plist).TrimStart().StartsWith("<?xml", StringComparison.Ordinal))
            {
                if (!(OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())) return;
                RunQuiet("plutil", $"-convert xml1 \"{plist}\"");
            }
            var text = File.ReadAllText(plist);
            if (!text.TrimStart().StartsWith("<?xml", StringComparison.Ordinal)) return;
            text = ReplacePlistValue(text, "CFBundleDisplayName", title);
            text = ReplacePlistValue(text, "CFBundleName", title.Length > 15 ? title[..15] : title);
            File.WriteAllText(plist, text);
        }
        catch { /* binary plist or unexpected format: keep the template's name */ }
    }

    private static string ReplacePlistValue(string xml, string key, string value)
    {
        var keyTag = $"<key>{key}</key>";
        int k = xml.IndexOf(keyTag, StringComparison.Ordinal);
        var escaped = System.Security.SecurityElement.Escape(value);
        if (k < 0)
        {
            int dict = xml.LastIndexOf("</dict>", StringComparison.Ordinal);
            return dict < 0 ? xml : xml.Insert(dict, $"\t{keyTag}\n\t<string>{escaped}</string>\n");
        }
        int s = xml.IndexOf("<string>", k, StringComparison.Ordinal);
        int e = xml.IndexOf("</string>", s, StringComparison.Ordinal);
        if (s < 0 || e < 0) return xml;
        return xml[..(s + 8)] + escaped + xml[e..];
    }

    // ------------------------------------------------------------------ building with the SDK

    public enum BuildTarget { Android, iOS, iOSSimulator, MacCatalyst, Windows }

    /// <summary>Optional identity and signing for native builds.</summary>
    public sealed class BuildOptions
    {
        /// <summary>Bundle / application id. Default: com.adventurecreator.game.&lt;title&gt;.</summary>
        public string? BundleId { get; set; }
        /// <summary>Apple signing identity, e.g. "Apple Development: Jane Doe (TEAMID)" (iOS devices, macOS distribution).</summary>
        public string? CodesignKey { get; set; }
        /// <summary>Name of the provisioning profile to use (iOS devices, App Store builds).</summary>
        public string? CodesignProvision { get; set; }
        public string Configuration { get; set; } = "Release";
    }

    public sealed record BuildResult(int ExitCode, string? ProductPath)
    {
        public bool Succeeded => ExitCode == 0 && ProductPath != null;
    }

    /// <summary>File name of the generated launch screen (MAUI resource names must be lower case letters/digits).</summary>
    public const string SplashFileName = "gamesplash.svg";

    private static string SplashColour(Adventure adventure) =>
        "#" + (Graphics.SplashRenderer.ParseColour(adventure.Settings.BackgroundColor, 0xFFFBFAF6) & 0xFFFFFF).ToString("X6");

    public static string DefaultBundleId(Adventure adventure)
    {
        var name = new string(SafeFileName(adventure.Title).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return "com.adventurecreator.game." + (name.Length == 0 ? "untitled" : char.IsDigit(name[0]) ? "g" + name : name);
    }

    private static bool IsApple(BuildTarget t) => t is BuildTarget.iOS or BuildTarget.iOSSimulator or BuildTarget.MacCatalyst;

    private static string FrameworkFor(BuildTarget t) => t switch
    {
        BuildTarget.Android => "net10.0-android",
        BuildTarget.iOS or BuildTarget.iOSSimulator => "net10.0-ios",
        BuildTarget.MacCatalyst => "net10.0-maccatalyst",
        _ => "net10.0-windows10.0.19041.0",
    };

    private static string SimulatorRid => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
        ? "iossimulator-arm64" : "iossimulator-x64";

    /// <summary>
    /// Arguments for building the Player project with the game embedded. The Player project includes
    /// <c>$(EmbeddedGame)</c> as the <c>game.adventure</c> asset and takes its display name and id from
    /// <c>$(GameTitle)</c> / <c>$(GameId)</c>. Each export builds in its own folder (<c>--artifacts-path</c>) so exports
    /// never share intermediate files.
    /// </summary>
    public static string BuildArguments(string playerProject, string packagePath, string outputDirectory, BuildTarget target, Adventure adventure, BuildOptions? options = null)
    {
        options ??= new BuildOptions();
        var framework = target switch
        {
            BuildTarget.Android => "net10.0-android",
            BuildTarget.iOS or BuildTarget.iOSSimulator => "net10.0-ios",
            BuildTarget.MacCatalyst => "net10.0-maccatalyst",
            _ => "net10.0-windows10.0.19041.0",
        };
        var id = string.IsNullOrWhiteSpace(options.BundleId) ? DefaultBundleId(adventure) : options.BundleId.Trim();
        var args = new StringBuilder();
        // Simulator apps are built, not published (publishing produces a signed device archive).
        args.Append(target == BuildTarget.iOSSimulator ? "build" : "publish");
        args.Append($" \"{playerProject}\" -f {framework} -c {options.Configuration}");
        // Android/Windows build in a private folder per export. The Apple SDK mis-places the app executable when
        // --artifacts-path is used, so Apple targets build in the project's own bin/obj with a full (non-incremental)
        // build, which guarantees the new game, title and bundle id are used.
        if (target == BuildTarget.iOSSimulator) args.Append(" --no-incremental");   // publish doesn't accept it: BuildAsync cleans first instead
        else if (!IsApple(target)) args.Append($" --artifacts-path \"{Path.Combine(outputDirectory, ".build")}\"");
        args.Append($" -p:EmbeddedGame=\"{packagePath}\" -p:GameTitle=\"{adventure.Title.Replace("\"", "'")}\" -p:GameId={id}");
        // Launch screen showing the game's picture and title (written by BuildAsync next to the package).
        var splash = Path.Combine(Path.GetDirectoryName(packagePath) ?? outputDirectory, SplashFileName);
        args.Append($" -p:GameSplash=\"{splash}\" -p:GameSplashColor=\"{SplashColour(adventure)}\"");
        switch (target)
        {
            case BuildTarget.Android: args.Append(" -p:AndroidPackageFormat=apk"); break;
            case BuildTarget.Windows: args.Append(" -p:WindowsPackageType=None -p:SelfContained=true -p:WindowsAppSDKSelfContained=true"); break;
            case BuildTarget.MacCatalyst: args.Append(" -p:CreatePackage=false"); break;
            case BuildTarget.iOSSimulator: args.Append($" -r {SimulatorRid}"); break;
            case BuildTarget.iOS: args.Append(" -r ios-arm64 -p:ArchiveOnBuild=true"); break;
        }
        if (!string.IsNullOrWhiteSpace(options.CodesignKey)) args.Append($" -p:CodesignKey=\"{options.CodesignKey}\"");
        if (!string.IsNullOrWhiteSpace(options.CodesignProvision)) args.Append($" -p:CodesignProvision=\"{options.CodesignProvision}\"");
        if (target == BuildTarget.MacCatalyst && !string.IsNullOrWhiteSpace(options.CodesignKey)) args.Append(" -p:EnableCodeSigning=true");
        return args.ToString();
    }

    /// <summary>Builds the Player with the game embedded, then copies the finished app into <paramref name="outputDirectory"/>.</summary>
    public static async Task<BuildResult> BuildAsync(string playerProject, Adventure adventure, string outputDirectory, BuildTarget target,
        Action<string> log, CancellationToken cancellationToken = default, BuildOptions? options = null)
    {
        Directory.CreateDirectory(outputDirectory);
        var package = Path.Combine(outputDirectory, GameFileName);
        AdventurePackage.Save(adventure, package);
        File.WriteAllText(Path.Combine(outputDirectory, SplashFileName), Graphics.SplashRenderer.RenderSvg(adventure));
        var started = DateTime.UtcNow;
        var privateBuild = Path.Combine(outputDirectory, ".build");
        if (!IsApple(target) && Directory.Exists(privateBuild)) Directory.Delete(privateBuild, true); // always a fresh build
        if (IsApple(target) && target != BuildTarget.iOSSimulator)
        {
            // Apple builds share the project's bin/obj; clean so a previous export's game, title or id can't leak in.
            var rid = target == BuildTarget.iOS ? " -r ios-arm64" : "";
            await RunAsync("dotnet", $"clean \"{playerProject}\" -f {FrameworkFor(target)} -c {(options ?? new BuildOptions()).Configuration}{rid}", _ => { }, cancellationToken);
        }
        int code = await RunAsync("dotnet", BuildArguments(playerProject, package, outputDirectory, target, adventure, options), log, cancellationToken);
        if (code != 0) return new BuildResult(code, null);

        var searchRoot = IsApple(target)
            ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(playerProject))!, "bin", (options ?? new BuildOptions()).Configuration, FrameworkFor(target))
            : Path.Combine(outputDirectory, ".build");
        var expectedId = string.IsNullOrWhiteSpace(options?.BundleId) ? DefaultBundleId(adventure) : options!.BundleId!.Trim();
        var product = FindProduct(searchRoot, target, started, expectedId);
        if (product == null)
        {
            log("Build succeeded but the app could not be found in " + searchRoot);
            return new BuildResult(code, null);
        }
        var name = SafeFileName(adventure.Title);
        string destination = target switch
        {
            BuildTarget.Android => Path.Combine(outputDirectory, name + ".apk"),
            BuildTarget.iOS => Path.Combine(outputDirectory, name + ".ipa"),
            BuildTarget.Windows => Path.Combine(outputDirectory, name),
            _ => Path.Combine(outputDirectory, name + ".app"),
        };
        if (Directory.Exists(destination)) Directory.Delete(destination, true);
        if (File.Exists(destination)) File.Delete(destination);
        if (Directory.Exists(product)) CopyDirectory(product, destination); else File.Copy(product, destination);
        log("Created " + destination);
        return new BuildResult(code, destination);
    }

    /// <summary>Locates the app a build produced (newest .app / .ipa / signed .apk / Windows publish folder).</summary>
    public static string? FindProduct(string root, BuildTarget target, DateTime notBefore, string? bundleId = null)
    {
        if (!Directory.Exists(root)) return null;
        var appRoot = IsApple(target) ? root : Path.Combine(root, "bin");
        if (target is BuildTarget.MacCatalyst or BuildTarget.iOSSimulator && !Directory.Exists(appRoot)) return null;
        IEnumerable<string> candidates = target switch
        {
            BuildTarget.Android => Directory.GetFiles(root, "*-Signed.apk", SearchOption.AllDirectories),
            BuildTarget.iOS => Directory.GetFiles(root, "*.ipa", SearchOption.AllDirectories).Where(f => File.GetLastWriteTimeUtc(f) >= notBefore.AddMinutes(-1)),
            BuildTarget.Windows => Directory.GetFiles(root, "AdventureCreator.Player.exe", SearchOption.AllDirectories)
                .Where(f => f.Contains("publish", StringComparison.OrdinalIgnoreCase)).Select(f => Path.GetDirectoryName(f)!),
            // Finished bundles live under bin/ and contain their executable (obj/ holds incomplete intermediate copies).
            _ => Directory.GetDirectories(appRoot, "*.app", SearchOption.AllDirectories)
                .Where(d => !d.Contains(".app/", StringComparison.Ordinal))
                .Where(d => target != BuildTarget.iOSSimulator || d.Contains("iossimulator", StringComparison.Ordinal))
                .Where(d => target == BuildTarget.MacCatalyst
                    ? File.Exists(Path.Combine(d, "Contents", "MacOS", "AdventureCreator.Player"))
                    : File.Exists(Path.Combine(d, "AdventureCreator.Player")))
                // Only the app just built for this game: the project's bin folder may hold apps from other exports.
                .Where(d => bundleId == null || BundleHasId(d, bundleId)),
        };
        return candidates
            .Select(p => (Path: p, Time: Directory.Exists(p) ? Directory.GetLastWriteTimeUtc(p) : File.GetLastWriteTimeUtc(p)))
            // Prefer universal (non-RID-specific) Mac apps, then the newest.
            .OrderBy(x => target == BuildTarget.MacCatalyst && x.Path.Contains("maccatalyst-", StringComparison.Ordinal) ? 1 : 0)
            .ThenByDescending(x => x.Time)
            .Select(x => x.Path).FirstOrDefault();
    }

    private static bool BundleHasId(string app, string bundleId)
    {
        var plist = File.Exists(Path.Combine(app, "Contents", "Info.plist")) ? Path.Combine(app, "Contents", "Info.plist") : Path.Combine(app, "Info.plist");
        if (!File.Exists(plist)) return false;
        // Works for XML and binary plists (strings are stored as plain ASCII).
        var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(plist));
        return text.Contains(bundleId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Installs and launches an iOS Simulator build (macOS only): boots a simulator (an already booted one, or the first
    /// available iPhone), opens the Simulator app, installs and starts the game.
    /// </summary>
    public static async Task<bool> RunInSimulatorAsync(string appPath, Action<string> log, string? deviceName = null, CancellationToken cancellationToken = default)
    {
        var bundleId = ReadBundleId(Path.Combine(appPath, "Info.plist"));
        if (bundleId == null) { log("Could not read the app's bundle id."); return false; }

        var device = await PickSimulatorAsync(deviceName, log, cancellationToken);
        if (device == null) { log("No iOS simulator is available. Install one in Xcode › Settings › Components."); return false; }
        await RunAsync("xcrun", $"simctl boot {device}", _ => { }, cancellationToken); // fails harmlessly if already booted
        await RunAsync("xcrun", $"simctl bootstatus {device} -b", _ => { }, cancellationToken);
        // Show the Simulator window if this Xcode has the Simulator app (the simulator also runs without it).
        // Xcode 27 replaced Simulator.app with DeviceHub.app.
        var developerDir = Environment.GetEnvironmentVariable("DEVELOPER_DIR") is { Length: > 0 } dd ? dd : "/Applications/Xcode.app/Contents/Developer";
        var xcodeApps = Path.GetFullPath(Path.Combine(developerDir, "..", "Applications"));
        var simulatorApp = new[]
        {
            Path.Combine(xcodeApps, "DeviceHub.app"), Path.Combine(developerDir, "Applications", "Simulator.app"), "/Applications/Simulator.app",
        }.FirstOrDefault(Directory.Exists);
        if (simulatorApp != null) await RunAsync("open", $"\"{simulatorApp}\"", log, cancellationToken);
        else log("(Neither DeviceHub nor Simulator was found – the simulator is running without a window.)");
        if (await RunAsync("xcrun", $"simctl install {device} \"{appPath}\"", log, cancellationToken) != 0) return false;
        LastSimulator = device;
        return await RunAsync("xcrun", $"simctl launch {device} {bundleId}", log, cancellationToken) == 0;
    }

    /// <summary>UDID of the simulator used by the last <see cref="RunInSimulatorAsync"/>.</summary>
    public static string? LastSimulator { get; private set; }

    private static async Task<string?> PickSimulatorAsync(string? name, Action<string> log, CancellationToken ct)
    {
        var lines = new List<string>();
        await RunAsync("xcrun", "simctl list devices available", l => lines.Add(l), ct);
        (string Name, string Udid, bool Booted)? Parse(string l)
        {
            var m = System.Text.RegularExpressions.Regex.Match(l, @"^\s+(.+?) \(([0-9A-F-]{36})\) \((Booted|Shutdown)\)");
            return m.Success ? (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value == "Booted") : null;
        }
        var devices = lines.Select(Parse).Where(d => d != null).Select(d => d!.Value).ToList();
        var chosen = name != null ? devices.FirstOrDefault(d => d.Name == name)
            : devices.FirstOrDefault(d => d.Booted && d.Name.StartsWith("iPhone"));
        if (chosen == default) chosen = devices.FirstOrDefault(d => d.Name.StartsWith("iPhone"));
        if (chosen == default) return null;
        log($"Using simulator {chosen.Name} ({chosen.Udid})");
        return chosen.Udid;
    }

    private static string? ReadBundleId(string plist)
    {
        if (!File.Exists(plist)) return null;
        var lines = new List<string>();
        RunAsync("/usr/libexec/PlistBuddy", $"-c \"Print :CFBundleIdentifier\" \"{plist}\"", l => lines.Add(l), CancellationToken.None).GetAwaiter().GetResult();
        return lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
    }

    private static async Task<int> RunAsync(string file, string arguments, Action<string> log, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (file == "dotnet") log("> dotnet " + arguments);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {file}.");
        p.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try
        {
            await p.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        return p.ExitCode;
    }

    // ------------------------------------------------------------------ helpers

    public static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(title.Where(c => !invalid.Contains(c) && c != '/' && c != ':').ToArray()).Trim();
        return s.Length == 0 ? "Adventure" : s;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, destination));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = file.Replace(source, destination);
            var info = new FileInfo(file);
            if (info.LinkTarget != null)
            {
                File.CreateSymbolicLink(dest, info.LinkTarget);
                continue;
            }
            File.Copy(file, dest, true);
            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(dest, File.GetUnixFileMode(file)); } catch { }
            }
        }
    }

    private static void RunQuiet(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true });
            p?.WaitForExit(60000);
        }
        catch { /* codesign not available */ }
    }
}
