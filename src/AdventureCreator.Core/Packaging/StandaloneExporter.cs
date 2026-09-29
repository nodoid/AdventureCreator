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
            var text = File.ReadAllText(plist);
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

    public enum BuildTarget { Android, iOS, MacCatalyst, Windows }

    /// <summary>
    /// Arguments for <c>dotnet publish</c> of the Player project with the game embedded. The Player project includes
    /// <c>$(EmbeddedGame)</c> as the <c>game.adventure</c> asset and takes its display name and id from
    /// <c>$(GameTitle)</c> / <c>$(GameId)</c>.
    /// </summary>
    public static string BuildArguments(string playerProject, string packagePath, string outputDirectory, BuildTarget target, Adventure adventure)
    {
        var framework = target switch
        {
            BuildTarget.Android => "net10.0-android",
            BuildTarget.iOS => "net10.0-ios",
            BuildTarget.MacCatalyst => "net10.0-maccatalyst",
            _ => "net10.0-windows10.0.19041.0",
        };
        var id = "com.adventurecreator.game." + new string(SafeFileName(adventure.Title).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (id.EndsWith('.')) id += "untitled";
        var args = new StringBuilder();
        args.Append($"publish \"{playerProject}\" -f {framework} -c Release -o \"{outputDirectory}\"");
        args.Append($" -p:EmbeddedGame=\"{packagePath}\" -p:GameTitle=\"{adventure.Title.Replace("\"", "'")}\" -p:GameId={id}");
        if (target == BuildTarget.Android) args.Append(" -p:AndroidPackageFormat=apk");
        if (target == BuildTarget.Windows) args.Append(" -p:WindowsPackageType=None -p:SelfContained=true -p:WindowsAppSDKSelfContained=true");
        if (target == BuildTarget.MacCatalyst) args.Append(" -p:CreatePackage=false");
        return args.ToString();
    }

    /// <summary>Runs <c>dotnet publish</c> and streams its output. Returns the exit code.</summary>
    public static async Task<int> BuildAsync(string playerProject, Adventure adventure, string outputDirectory, BuildTarget target,
        Action<string> log, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var package = Path.Combine(outputDirectory, GameFileName);
        AdventurePackage.Save(adventure, package);
        var psi = new ProcessStartInfo("dotnet", BuildArguments(playerProject, package, outputDirectory, target, adventure))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        log("> dotnet " + psi.Arguments);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start dotnet. Is the .NET SDK installed?");
        p.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(cancellationToken);
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
